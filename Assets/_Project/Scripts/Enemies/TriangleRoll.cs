using ProjectCircle.Combat;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// The heavy triangle's last stand. Once its health drops to <see cref="enrageThreshold"/> it enrages:
    /// it turns a hot crimson (its shield, if it has one, runs hot too) and it stops slamming. Before every
    /// roll it shakes with rage. Then it hurls itself across the floor, tumbling over
    /// its corners like a spiked wheel, until a wall or ledge stops it, then turns and rolls back at the
    /// circle, again and again.
    ///
    /// Dodge by dashing through it (the dash's invulnerability) or double-jumping over. Each stop is a short
    /// opening: its corners, the weak points, come to rest where they can be hit.
    ///
    /// Sequence: Rage shake → Lean back → Roll (fast, damaging) → Settle (lands flat) → Pause → repeat.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)] // after TriangleEnemy and TriangleSlam
    [RequireComponent(typeof(TriangleEnemy), typeof(Rigidbody2D), typeof(EnemyHealth))]
    public sealed class TriangleRoll : MonoBehaviour
    {
        private enum Phase { Waiting, WindUp, Rolling, Settle, Pause }

        [Header("Enrage")]
        [Tooltip("Starts rolling once health is at or below this fraction of the maximum.")]
        [SerializeField, Range(0.05f, 1f)] private float enrageThreshold = 0.3f;

        [SerializeField, Min(0f)] private float enrageShakeStrength = 0.35f;
        [SerializeField, Min(0f)] private float enrageShakeTime = 0.6f;

        [Tooltip("Colours once enraged (top and bottom of the fill gradient). Shown for the rest of the fight.")]
        [SerializeField] private Color enragedTop = new Color(0.78f, 0.32f, 0.42f, 1f);
        [SerializeField] private Color enragedBottom = new Color(0.46f, 0.12f, 0.24f, 1f);

        [Tooltip("Colour of the corner that flares while it shakes with rage.")]
        [SerializeField] private Color rageGlowColor = new Color(1f, 0.4f, 0.3f, 1f);

        [Header("Rolling")]
        [Tooltip("Speed across the floor while rolling.")]
        [SerializeField, Min(0.5f)] private float rollSpeed = 9.5f;

        [Tooltip("Furthest one roll goes before it stops on its own.")]
        [SerializeField, Min(1f)] private float maxRollDistance = 14f;

        [Tooltip("Before each roll it shakes with rage for this long (the player's cue), then leans back and goes.")]
        [SerializeField, Min(0.05f)] private float rageShakeTime = 0.55f;

        [Tooltip("How violently it shakes (world units) at the peak of the rage.")]
        [SerializeField, Min(0f)] private float rageShakeAmount = 0.14f;

        [Tooltip("The quick lean-back after the shaking, just before it rolls.")]
        [SerializeField, Min(0.05f)] private float leanTime = 0.15f;

        [Tooltip("Camera rumble while it shakes.")]
        [SerializeField, Min(0f)] private float rageRumbleStrength = 0.06f;

        [Tooltip("Rest after each roll before the next: the window to hit its corners.")]
        [SerializeField, Min(0f)] private float pauseTime = 0.55f;

        [SerializeField, Min(0.01f)] private float settleTime = 0.15f;

        [Tooltip("Shake when a roll slams into a wall.")]
        [SerializeField, Min(0f)] private float wallShakeStrength = 0.18f;

        [SerializeField] private LayerMask groundLayers = 1 << 3;

        /// <summary>True once enraged (rolling for the rest of the fight).</summary>
        public bool IsEnraged { get; private set; }

        private TriangleEnemy enemy;
        private Rigidbody2D body;
        private EnemyHealth health;
        private TriangleSlam slam;
        private TriangleShield shield;
        private CameraFollow2D cameraFollow;

        private Phase phase = Phase.Waiting;
        private float phaseStartTime;
        private int rollDirection = 1;
        private float rollAngle;       // accumulated visual turn, degrees
        private float rolled;          // distance covered this roll
        private float settleFromAngle;
        private float settleToAngle;
        private bool pendingEnrage;

        private float PhaseTime => Time.time - phaseStartTime;
        private float Circumradius => enemy.Shape != null ? enemy.Shape.Circumradius : 0.8f;
        private float Inradius => Circumradius * 0.5f;
        private float SideLength => Circumradius * 1.7320508f;
        private float FloorY => body.position.y - Inradius;

        #region Unity lifecycle

        private void Awake()
        {
            enemy = GetComponent<TriangleEnemy>();
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<EnemyHealth>();
            slam = GetComponent<TriangleSlam>();
            shield = GetComponent<TriangleShield>();
        }

        private void Start() => cameraFollow = FindAnyObjectByType<CameraFollow2D>();

        private void OnEnable() => health.Hit += HandleHit;

        private void OnDisable()
        {
            health.Hit -= HandleHit;
            if (phase != Phase.Waiting && enemy.IsAttackLocked) enemy.EndAttack();
        }

        private void HandleHit(int damage, Vector2 source)
        {
            if (!IsEnraged && !health.IsDead && health.CurrentHealth <= Mathf.CeilToInt(health.MaxHealth * enrageThreshold))
            {
                pendingEnrage = true;
            }
        }

        private void LateUpdate()
        {
            if (pendingEnrage) TryEnrage();
            if (!IsEnraged) return;

            switch (phase)
            {
                case Phase.Waiting: TryStartRoll(); break;
                case Phase.WindUp: UpdateWindUp(); break;
                case Phase.Rolling: UpdateRolling(); break;
                case Phase.Settle: UpdateSettle(); break;
                case Phase.Pause: UpdatePause(); break;
            }
        }

        #endregion

        #region Enrage

        /// <summary>Waits for any slam in progress to finish, then switches to rolling for good.</summary>
        private void TryEnrage()
        {
            if (slam != null && slam.IsAttacking) return;

            pendingEnrage = false;
            IsEnraged = true;
            if (slam != null) slam.enabled = false;
            if (shield != null) shield.SetEnraged(true);
            if (cameraFollow != null) cameraFollow.Shake(enrageShakeStrength, enrageShakeTime);
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, Color.white);
            enemy.SetColours(enragedTop, enragedBottom, enragedTop, enragedBottom);
        }

        #endregion

        #region Phases

        private void TryStartRoll()
        {
            if (enemy.Target == null || !enemy.BeginAttack()) return;

            // Roll at the circle.
            rollDirection = enemy.Target.position.x >= body.position.x ? 1 : -1;
            if (Blocked(rollDirection, 0.2f)) rollDirection = -rollDirection; // no room that way: go the other way
            rollAngle = 0f;
            rolled = 0f;
            if (cameraFollow != null) cameraFollow.Shake(rageRumbleStrength, rageShakeTime);
            SetPhase(Phase.WindUp);
        }

        /// <summary>
        /// Shakes with rage (building up, its top corner flaring red), then leans back away from the roll,
        /// digging its rear corner in, and goes.
        /// </summary>
        private void UpdateWindUp()
        {
            float t = PhaseTime;

            if (t < rageShakeTime)
            {
                float build = t / rageShakeTime;
                float amount = rageShakeAmount * (0.35f + 0.65f * build);
                // Fast, uneven jitter: two sine rates plus noise, so it reads as trembling, not swaying.
                Vector2 shake = new Vector2(
                    Mathf.Sin(t * 61f) * 0.7f + Random.Range(-0.3f, 0.3f),
                    Mathf.Abs(Mathf.Sin(t * 47f)) * 0.5f) * amount;
                float twitch = Mathf.Sin(t * 53f) * 3.5f * build;
                enemy.SetAttackPose(shake, twitch);

                float flare = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(t * 18f));
                if (enemy.Shape != null) enemy.Shape.SetCornerGlow(0, flare * build, rageGlowColor);
                return;
            }

            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, rageGlowColor);

            float p = Mathf.Clamp01((t - rageShakeTime) / leanTime);
            float e = p * p * (3f - 2f * p);
            enemy.SetAttackPose(Vector2.zero, rollDirection * 10f * e);

            if (p < 1f) return;
            enemy.AttackOwnsVelocity = true;
            rollAngle = rollDirection * 10f;
            SetPhase(Phase.Rolling);
        }

        private void UpdateRolling()
        {
            float dt = Time.deltaTime;
            float step = rollSpeed * dt;

            if (Blocked(rollDirection, step) || rolled >= maxRollDistance)
            {
                StopRolling();
                return;
            }

            body.linearVelocity = new Vector2(rollDirection * rollSpeed, body.linearVelocity.y);
            rolled += step;

            // Tumbling over its corners: one 120° turn per side length travelled. While it pivots on a corner,
            // its centre lifts from the inradius to the full circumradius and back.
            rollAngle -= rollDirection * (step / SideLength) * 120f;
            enemy.SetAttackPose(new Vector2(0f, Lift(rollAngle)), rollAngle);
        }

        private void StopRolling()
        {
            enemy.AttackOwnsVelocity = false;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (cameraFollow != null) cameraFollow.Shake(wallShakeStrength, 0.2f);

            // Settle onto the nearest flat side.
            settleFromAngle = rollAngle;
            settleToAngle = Mathf.Round(rollAngle / 120f) * 120f;
            SetPhase(Phase.Settle);
        }

        private void UpdateSettle()
        {
            float p = Mathf.Clamp01(PhaseTime / settleTime);
            float angle = Mathf.Lerp(settleFromAngle, settleToAngle, p * p);
            enemy.SetAttackPose(new Vector2(0f, Lift(angle)), angle);
            if (p >= 1f) SetPhase(Phase.Pause);
        }

        private void UpdatePause()
        {
            // Resting on a side: a full 120° turn looks exactly like no turn, so the pose can simply be cleared.
            enemy.SetAttackPose(Vector2.zero, 0f);
            if (PhaseTime < pauseTime) return;

            enemy.EndAttack();
            SetPhase(Phase.Waiting);
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStartTime = Time.time;
        }

        #endregion

        #region Helpers

        /// <summary>How far the centre is raised while pivoting on a corner partway through a 120° turn.</summary>
        private float Lift(float angle)
        {
            float within = Mathf.Repeat(Mathf.Abs(angle), 120f); // 0..120 through the current tip-over
            return Circumradius * Mathf.Sin((30f + within) * Mathf.Deg2Rad) - Inradius;
        }

        /// <summary>A wall in the way, or the floor ends (a ledge or pit) just ahead.</summary>
        private bool Blocked(int direction, float step)
        {
            float half = SideLength * 0.5f;
            Vector2 centre = body.position;

            RaycastHit2D wall = Physics2D.BoxCast(centre, new Vector2(0.1f, Circumradius * 0.8f), 0f,
                                                  Vector2.right * direction, half + step + 0.05f, groundLayers);
            if (wall.collider != null) return true;

            Vector2 ahead = new Vector2(centre.x + direction * (half + step + 0.1f), FloorY + 0.4f);
            RaycastHit2D floor = Physics2D.Raycast(ahead, Vector2.down, 0.9f, groundLayers);
            return floor.collider == null;
        }

        #endregion
    }
}
