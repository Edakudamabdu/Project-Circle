using ProjectCircle.Combat;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A stagger limit for heavy triangles: take too many hits in a row and it answers. It trembles for a
    /// moment, its top corner flaring, then lets out a short burst that hurts anything right next to it and
    /// throws two small shockwaves along the floor.
    ///
    /// Stops the player standing in one spot and hitting it until it dies: hit two or three times, then back
    /// off (or dash through the burst) and come in again. The wind-up is armored, so swinging through it
    /// doesn't stop it.
    ///
    /// Use it on a <see cref="TriangleEnemy"/> alongside its other attacks; they never overlap.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after TriangleEnemy, so its state is final for the frame
    [RequireComponent(typeof(TriangleEnemy), typeof(EnemyHealth), typeof(Rigidbody2D))]
    public sealed class TriangleCounterBurst : MonoBehaviour
    {
        private enum Phase { Ready, Windup, Recover }

        [Header("When")]
        [Tooltip("Hits taken in a row (each within Combo Window of the last) before it answers.")]
        [SerializeField, Min(1)] private int hitsBeforeCounter = 3;

        [Tooltip("Max seconds between hits for them to count as one string.")]
        [SerializeField, Min(0.1f)] private float comboWindow = 1.1f;

        [Tooltip("How long it keeps trying to answer after the last hit (it must be standing still and free of other attacks to start).")]
        [SerializeField, Min(0f)] private float answerWindow = 0.6f;

        [Header("Timing")]
        [Tooltip("Tremble before the burst: the player's cue to back off.")]
        [SerializeField, Min(0.05f)] private float windupTime = 0.3f;

        [SerializeField, Min(0f)] private float recoverTime = 0.4f;

        [Header("Burst")]
        [Tooltip("Reach of the burst beyond the triangle's corners.")]
        [SerializeField, Min(0f)] private float reach = 1.3f;

        [SerializeField, Min(0)] private int damage = 1;
        [SerializeField] private LayerMask playerLayers = 1 << 8;
        [SerializeField, Min(0f)] private float shakeStrength = 0.25f;

        [Tooltip("Small shockwaves sent out both ways.")]
        [SerializeField] private GroundShockwave.Settings shockwave = SmallShockwave;

        [Header("Look")]
        [SerializeField] private Color flareColor = new Color(1f, 0.92f, 0.8f, 1f);

        [SerializeField, Min(0f)] private float tremble = 0.06f;

        /// <summary>True from the wind-up until it has recovered.</summary>
        public bool IsAttacking => phase != Phase.Ready;

        private static GroundShockwave.Settings SmallShockwave
        {
            get
            {
                GroundShockwave.Settings s = GroundShockwave.Settings.Default;
                s.distance = 2.6f;
                s.height = 1f;
                s.speed = 12f;
                return s;
            }
        }

        private TriangleEnemy enemy;
        private EnemyHealth health;
        private Rigidbody2D body;
        private TriangleSlam slam; // optional: its recovery can be cut short to answer
        private CameraFollow2D cameraFollow;
        private Phase phase = Phase.Ready;
        private float phaseStartTime;
        private int hitStreak;
        private float lastHitTime = float.NegativeInfinity;

        private float PhaseTime => Time.time - phaseStartTime;
        private float Circumradius => enemy.Shape != null ? enemy.Shape.Circumradius : 0.8f;

        #region Unity lifecycle

        private void Awake()
        {
            enemy = GetComponent<TriangleEnemy>();
            health = GetComponent<EnemyHealth>();
            body = GetComponent<Rigidbody2D>();
            slam = GetComponent<TriangleSlam>();
        }

        private void Start() => cameraFollow = FindAnyObjectByType<CameraFollow2D>();

        private void OnEnable() => health.Hit += HandleHit;

        private void OnDisable()
        {
            health.Hit -= HandleHit;
            if (phase != Phase.Ready) Finish();
        }

        private void LateUpdate()
        {
            if (health.IsDead) return;

            switch (phase)
            {
                case Phase.Ready: TryAnswer(); break;
                case Phase.Windup: UpdateWindup(); break;
                case Phase.Recover: if (PhaseTime >= recoverTime) Finish(); break;
            }
        }

        #endregion

        private void HandleHit(int amount, Vector2 source)
        {
            if (phase != Phase.Ready) return;

            // Hits during its other attacks count too: if the streak completes mid-slam or mid-roll, it answers
            // the moment that attack ends (punishing a player who just keeps swinging through the recovery).
            hitStreak = Time.time - lastHitTime <= comboWindow ? hitStreak + 1 : 1;
            lastHitTime = Time.time;
        }

        private void TryAnswer()
        {
            if (hitStreak < hitsBeforeCounter) return;

            // Being hit while flattened after a slam: cut the recovery short and answer now.
            if (enemy.IsAttackLocked && slam != null && slam.IsRecovering) slam.InterruptRecovery();

            // Still busy (mid-slam or mid-roll): hold the answer until that attack is over, then burst straight away.
            if (enemy.IsAttackLocked)
            {
                lastHitTime = Mathf.Max(lastHitTime, Time.time - answerWindow * 0.5f);
                return;
            }

            if (Time.time - lastHitTime > answerWindow)
            {
                hitStreak = 0; // the player backed off in time
                return;
            }

            // Needs to be standing still (a hit's tiny stagger has to settle first).
            if (!enemy.BeginAttack()) return;

            hitStreak = 0;
            phase = Phase.Windup;
            phaseStartTime = Time.time;
        }

        private void UpdateWindup()
        {
            float p = Mathf.Clamp01(PhaseTime / windupTime);
            Vector2 shake = Random.insideUnitCircle * tremble * (0.4f + p);
            enemy.SetAttackPose(new Vector2(shake.x, -0.12f * p + shake.y), 0f);
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(0, p, flareColor);

            if (p >= 1f) Burst();
        }

        private void Burst()
        {
            // Hurt the circle if it is right next to the triangle (dashing through it dodges, as with every hit).
            Collider2D hit = Physics2D.OverlapCircle(body.position, Circumradius + reach, playerLayers);
            if (hit != null)
            {
                var playerHealth = hit.GetComponentInParent<PlayerHealth>();
                if (playerHealth != null) playerHealth.TakeDamage(damage, body.position);
            }

            // Small shockwaves both ways from its base.
            float floorY = body.position.y - Circumradius * 0.5f;
            float half = Circumradius * 0.87f;
            Material material = enemy.Shape != null ? enemy.Shape.GetComponent<MeshRenderer>().sharedMaterial : null;
            GroundShockwave.Spawn(new Vector2(body.position.x - half, floorY), -1, shockwave, material);
            GroundShockwave.Spawn(new Vector2(body.position.x + half, floorY), 1, shockwave, material);

            if (cameraFollow != null) cameraFollow.Shake(shakeStrength, 0.25f);
            enemy.SetAttackPose(Vector2.zero, 0f);
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, flareColor);

            phase = Phase.Recover;
            phaseStartTime = Time.time;
        }

        private void Finish()
        {
            phase = Phase.Ready;
            if (enemy != null && enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, flareColor);
            if (enemy != null && enemy.IsAttackLocked) enemy.EndAttack();
        }
    }
}
