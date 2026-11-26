using ProjectCircle.Combat;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Ceiling Sentry: a triangle hanging point-down from the ceiling. When the circle is in range and in sight,
    /// an orb of light grows on its point (the vertex glows) while it turns to follow the circle. Then its point
    /// blinks (locked), and the orb shoots at the circle, bending gently after it for a moment (a small step
    /// is followed, a real dodge is not).
    ///
    /// The blade can't hurt it (it glances off). The only way to destroy it is to parry its orb: the orb flies
    /// back up into the sentry and cracks it.
    /// Setup: root at the centre of the shape, the shape a child rotated 180° so the point faces down.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class CeilingSentry : MonoBehaviour, IHitGuard
    {
        private enum Phase { Idle, Charge, Lock, Rest }

        [SerializeField] private RoundedTriangleMesh shape;

        [Header("Sensing")]
        [SerializeField, Min(1f)] private float range = 13f;
        [SerializeField] private LayerMask playerLayers = 1 << 8;
        [SerializeField] private LayerMask sightBlockers = 1 << 3;

        [Header("Aim")]
        [Tooltip("The orb grows on its point (the vertex glows) while it turns to follow the circle.")]
        [SerializeField, Min(0.1f)] private float chargeTime = 0.9f;

        [Tooltip("Then it locks on: its point blinks white-hot for this long and it stops turning. The cue to get ready.")]
        [SerializeField, Min(0.05f)] private float lockTime = 0.35f;

        [Tooltip("How quickly it turns to follow the circle while charging (higher = snappier).")]
        [SerializeField, Min(0.5f)] private float aimFollow = 6f;

        [Header("Orb")]
        [Tooltip("How fast the orb flies. The circle runs at about 8.5.")]
        [SerializeField, Min(1f)] private float orbSpeed = 9f;

        [Tooltip("How hard the orb bends towards the circle, in degrees per second, during Track Time. Strong: it chases anything done early.")]
        [SerializeField, Min(0f)] private float trackTurnRate = 120f;

        [Tooltip("Seconds it keeps bending after it fires, then it flies straight: a dodge in the last moments before it arrives beats it.")]
        [SerializeField, Min(0f)] private float trackTime = 0.35f;

        [SerializeField, Min(0.05f)] private float orbRadius = 0.4f;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0f)] private float restTime = 1.6f;
        [SerializeField] private Color orbColor = new Color(1f, 0.45f, 0.4f, 1f);

        [Tooltip("Vertex-colour material for the orb (e.g. ShapeUnlit). Uses the effects material if empty.")]
        [SerializeField] private Material orbMaterial;

        private HomingOrb forming;
        private Phase phase = Phase.Idle;
        private float phaseStart;
        private Vector2 aim = Vector2.down;
        private Transform player;

        private float PhaseTime => Time.time - phaseStart;
        private Vector2 Point => shape.transform.TransformPoint(shape.GetCornerPoint(0, shape.CornerRadius * 0.5f));

        private void Awake()
        {
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
            orbColor = TriangleKinds.Tint(orbColor, TriangleKinds.HueShiftOf(this)); // orbs in its own colour

            GetComponent<EnemyHealth>().Died += CancelOrb;
        }

        private void Start()
        {
            var movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null) player = movement.transform;
        }

        private void OnDisable() => CancelOrb();

        private void Update()
        {
            switch (phase)
            {
                case Phase.Idle:
                    if (CanSeePlayer())
                    {
                        aim = ((Vector2)player.position - Point).normalized;
                        forming = HomingOrb.Spawn(Point + aim * orbRadius * 0.6f, OrbSettings(), orbMaterial != null ? orbMaterial : Systems.Fx.Material, player);
                        forming.ReturnTo = transform; // a parried orb homes back into the sentry
                        SetPhase(Phase.Charge);
                    }
                    break;

                case Phase.Charge:
                {
                    // The orb grows on the point (the vertex glows) as it turns to follow the circle, a little behind.
                    float p = PhaseTime / chargeTime;
                    shape.SetCornerGlow(0, p * 0.8f, orbColor);
                    if (player != null)
                    {
                        Vector2 want = ((Vector2)player.position - Point).normalized;
                        aim = ((Vector2)Vector3.Slerp(aim, want, 1f - Mathf.Exp(-aimFollow * Time.deltaTime))).normalized;
                    }
                    if (forming != null) forming.SetForming(Point + aim * orbRadius * 0.6f, p * 0.85f);
                    if (p >= 1f) SetPhase(Phase.Lock);
                    break;
                }

                case Phase.Lock:
                {
                    // Locked: it stops turning and its point blinks white-hot. The orb flies where it was aimed.
                    float p = PhaseTime / lockTime;
                    bool on = Mathf.Repeat(PhaseTime * 16f, 1f) < 0.5f;
                    shape.SetCornerGlow(0, on ? 1f : 0.5f, on ? Color.white : orbColor);
                    if (forming != null) forming.SetForming(Point + aim * orbRadius * 0.6f, 0.85f + 0.15f * p);
                    if (p >= 1f) Fire();
                    break;
                }

                case Phase.Rest:
                    if (PhaseTime >= restTime) SetPhase(Phase.Idle);
                    break;
            }
        }

        private void Fire()
        {
            shape.SetCornerGlow(-1, 0f, orbColor);
            if (forming != null)
            {
                forming.Release(aim);
                forming = null;
            }
            Systems.Fx.Ring(Point, orbColor, 0.9f, 0.2f, 0.08f);
            SetPhase(Phase.Rest);
        }

        /// <summary>A fast orb: no drift, a short gentle track, then straight.</summary>
        private HomingOrb.Settings OrbSettings()
        {
            HomingOrb.Settings s = HomingOrb.Settings.Default;
            s.color = orbColor;
            s.glowRadius = orbRadius;
            s.hitRadius = orbRadius * 0.5f;
            s.damage = damage;
            s.driftSpeed = orbSpeed;
            s.driftTime = 0f;
            s.maxSpeed = orbSpeed;
            s.acceleration = 100f;
            // A gentle track: it bends towards the circle for a short while, enough to follow a small step (so it
            // can still be parried) but not a real dodge (a jump, dash or run away).
            s.turnRate = trackTurnRate;
            s.homingTime = trackTime;
            s.fullTurnFraction = 1f;
            s.lifetime = range * 1.6f / orbSpeed;
            s.playerLayers = playerLayers;
            s.groundLayers = sightBlockers;
            return s;
        }

        private void CancelOrb()
        {
            if (forming != null) forming.Cancel();
            forming = null;
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = Time.time;
        }

        private bool CanSeePlayer()
        {
            if (player == null) return false;
            Vector2 from = Point;
            Vector2 to = player.position;
            if ((to - from).sqrMagnitude > range * range) return false;
            return Physics2D.Linecast(from, to, sightBlockers).collider == null;
        }

        /// <summary>The blade glances off: only its own parried orb can break it.</summary>
        public bool TryDeflect(Vector2 hitPoint, Vector2 attackerPosition)
        {
            Systems.Fx.Sparks(hitPoint, 0f, new Color(0.8f, 0.9f, 1f, 1f), 6, 6f, 180f);
            return true;
        }
    }
}
