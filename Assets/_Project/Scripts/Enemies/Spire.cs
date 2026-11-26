using ProjectCircle.Combat;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Spire: a tall triangle planted point-up in the ground. Its sides are armoured (the blade glances off);
    /// the only way to hurt it is from above: a down attack on its tip, which also bounces the circle (pogo).
    /// When the circle is above it, the tip trembles and then stabs upward.
    ///
    /// Teaches the pogo, and doubles as a level hazard: rows of Spires over a pit make a pogo bridge.
    /// Setup: root at the centre of the shape (base on the floor); the shape is a child "Visual" stretched tall.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class Spire : MonoBehaviour, IHitGuard
    {
        private enum Phase { Idle, Windup, Stab, Hold, Retract, Rest }

        [SerializeField] private RoundedTriangleMesh shape;

        [Header("Sensing")]
        [Tooltip("The circle must be within this horizontal distance of the tip to set it off.")]
        [SerializeField, Min(0.1f)] private float senseHalfWidth = 1.4f;

        [Tooltip("...and no higher than this above the tip.")]
        [SerializeField, Min(0.5f)] private float senseHeight = 6f;

        [SerializeField] private LayerMask playerLayers = 1 << 8;

        [Header("Stab")]
        [SerializeField, Min(0.05f)] private float windupTime = 0.35f;
        [SerializeField, Min(0.02f)] private float stabTime = 0.1f;
        [SerializeField, Min(0f)] private float holdTime = 0.35f;
        [SerializeField, Min(0.05f)] private float retractTime = 0.3f;
        [SerializeField, Min(0f)] private float restTime = 1f;

        [Tooltip("How much taller it shoots up when it stabs.")]
        [SerializeField, Min(1f)] private float stabStretch = 1.6f;

        [SerializeField, Min(1)] private int damage = 1;

        [Tooltip("After the circle pogos off it, it pulls back down and rests this long, so a pogo chain is safe.")]
        [SerializeField, Min(0f)] private float stunnedRestTime = 1.4f;

        [SerializeField, Min(0.1f)] private float tipHurtRadius = 0.35f;
        [SerializeField] private Color warnColor = new Color(1f, 0.6f, 0.35f, 1f);
        [SerializeField] private Color armourColor = new Color(0.75f, 0.9f, 1f, 1f);

        private Transform visual;
        private Vector3 restScale;
        private Phase phase = Phase.Idle;
        private float phaseStart;
        private Transform player;
        private PlayerHealth playerHealth;
        private PlayerAttack playerAttack;
        private float currentRest;
        private float retractFrom = 1f;

        private float PhaseTime => Time.time - phaseStart;

        /// <summary>World position of the tip.</summary>
        private Vector2 Tip => shape.transform.TransformPoint(shape.GetCornerPoint(0, shape.CornerRadius * 0.5f));

        private void Awake()
        {
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
            visual = shape.transform;
            restScale = visual.localScale;
            currentRest = restTime;
            GetComponent<EnemyHealth>().Hit += HandleHit;
        }

        /// <summary>Pogoed (hit from above): it gives up the stab, pulls back down and rests.</summary>
        private void HandleHit(int damageTaken, Vector2 from)
        {
            if (phase == Phase.Rest || phase == Phase.Retract) { currentRest = stunnedRestTime; return; }
            retractFrom = visual.localScale.y / restScale.y;
            currentRest = stunnedRestTime;
            visual.localPosition = new Vector3(0f, visual.localPosition.y, 0f);
            SetPhase(Phase.Retract);
        }

        private void Start()
        {
            var movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
            {
                player = movement.transform;
                playerHealth = movement.GetComponent<PlayerHealth>();
                playerAttack = movement.GetComponent<PlayerAttack>();
            }
        }

        private void Update()
        {
            switch (phase)
            {
                case Phase.Idle:
                    if (PlayerAbove()) SetPhase(Phase.Windup);
                    break;

                case Phase.Windup:
                {
                    float p = PhaseTime / windupTime;
                    visual.localPosition = (Vector3)(Random.insideUnitCircle * 0.04f * p);
                    shape.SetCornerGlow(0, p, warnColor);
                    if (p >= 1f) { visual.localPosition = Vector3.zero; retractFrom = stabStretch; SetPhase(Phase.Stab); }
                    break;
                }

                case Phase.Stab:
                {
                    float p = Mathf.Clamp01(PhaseTime / stabTime);
                    SetStretch(Mathf.Lerp(1f, stabStretch, p));
                    HurtAtTip();
                    if (p >= 1f) SetPhase(Phase.Hold);
                    break;
                }

                case Phase.Hold:
                    HurtAtTip();
                    if (PhaseTime >= holdTime) SetPhase(Phase.Retract);
                    break;

                case Phase.Retract:
                {
                    float p = Mathf.Clamp01(PhaseTime / retractTime);
                    SetStretch(Mathf.Lerp(retractFrom, 1f, p * p * (3f - 2f * p)));
                    shape.SetCornerGlow(0, 1f - p, warnColor);
                    if (p >= 1f) { shape.SetCornerGlow(-1, 0f, warnColor); SetPhase(Phase.Rest); }
                    break;
                }

                case Phase.Rest:
                    if (PhaseTime >= currentRest) { currentRest = restTime; SetPhase(Phase.Idle); }
                    break;
            }
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = Time.time;
        }

        /// <summary>Stretches the shape upward from its base (the base stays on the floor).</summary>
        private void SetStretch(float stretch)
        {
            visual.localScale = new Vector3(restScale.x, restScale.y * stretch, restScale.z);
            float baseOffset = shape.Circumradius * 0.5f * restScale.y; // centre-to-base at rest
            visual.localPosition = new Vector3(0f, baseOffset * (stretch - 1f), 0f);
        }

        private bool PlayerAbove()
        {
            if (player == null) return false;
            Vector2 tip = Tip;
            Vector2 p = player.position;
            return Mathf.Abs(p.x - tip.x) <= senseHalfWidth && p.y > tip.y - 0.3f && p.y < tip.y + senseHeight;
        }

        private void HurtAtTip()
        {
            if (playerHealth == null) return;
            // A down swing beats the tip: the circle pogos off it instead of being stabbed.
            if (playerAttack != null && playerAttack.IsDownSwinging) return;
            if (Physics2D.OverlapCircle(Tip, tipHurtRadius, playerLayers) != null) playerHealth.TakeDamage(damage, Tip + Vector2.down);
        }

        /// <summary>The sides are armoured: only a blow from above the tip (a down attack) gets through.</summary>
        public bool TryDeflect(Vector2 hitPoint, Vector2 attackerPosition)
        {
            Vector2 tip = Tip;
            bool fromAbove = attackerPosition.y > tip.y - 0.2f && Mathf.Abs(attackerPosition.x - tip.x) < 1.8f;
            if (fromAbove) return false;

            Systems.Fx.Sparks(hitPoint, Mathf.Atan2(attackerPosition.y - hitPoint.y, attackerPosition.x - hitPoint.x) * Mathf.Rad2Deg, armourColor, 8, 7f, 40f);
            return true;
        }
    }
}
