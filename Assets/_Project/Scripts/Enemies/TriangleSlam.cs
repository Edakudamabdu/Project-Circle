using ProjectCircle.Player;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// The heavy triangle's slam: it squats, its top vertex glowing, then hurls itself into the air towards
    /// the circle and crashes down. The impact hurts anything under it, shakes the camera and sends a
    /// <see cref="GroundShockwave"/> racing along the floor both ways. Jump over the waves.
    ///
    /// Sequence (timings in the Inspector):
    /// 1. Crouch  — squats and leans back; the top corner charges up. The player's cue.
    /// 2. Leap    — a high arc aimed at where the circle stood when it jumped (only onto its own floor).
    /// 3. Impact  — damage under it, camera shake, two shockwaves.
    /// 4. Recover — a long, flattened pause: the opening to hit it.
    ///
    /// Use it on a <see cref="TriangleEnemy"/> (instead of, or alongside, another attack).
    /// It never leaps where there is a ceiling in the way or no floor to land on.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after TriangleEnemy, so its state is final for the frame
    [RequireComponent(typeof(TriangleEnemy), typeof(Rigidbody2D))]
    public sealed class TriangleSlam : MonoBehaviour
    {
        private enum Phase { Ready, Crouch, Airborne, Recover }

        [Header("When")]
        [Tooltip("Slams when the circle is within this horizontal distance (and in sight).")]
        [SerializeField, Min(0.5f)] private float slamRange = 7f;

        [Tooltip("Wait after first spotting the circle before the first slam.")]
        [SerializeField, Min(0f)] private float firstSlamDelay = 0.5f;

        [Tooltip("Time between the end of one slam and the start of the next.")]
        [SerializeField, Min(0f)] private float cooldown = 2.6f;

        [Header("Timing")]
        [Tooltip("Squat before the leap. Long enough to read, short enough to punish standing still.")]
        [SerializeField, Min(0.05f)] private float crouchTime = 0.5f;

        [Tooltip("Flattened pause after landing: the window to attack it.")]
        [SerializeField, Min(0.05f)] private float recoverTime = 0.8f;

        [Header("Leap")]
        [Tooltip("Peak height of the leap (lowered automatically under a ceiling).")]
        [SerializeField, Min(0.5f)] private float leapHeight = 3.5f;

        [Tooltip("Furthest it will leap sideways.")]
        [SerializeField, Min(0f)] private float maxLeapDistance = 7f;

        [SerializeField, Min(0f)] private float crouchDepth = 0.25f;

        [Tooltip("Gravity multiplier on the way up (higher = a quicker, snappier rise).")]
        [SerializeField, Min(0.5f)] private float riseGravity = 1.6f;

        [Tooltip("Gravity multiplier on the way down (higher = it slams down hard and fast).")]
        [SerializeField, Min(0.5f)] private float fallGravity = 3.5f;

        [Tooltip("Layers it stands on and that block the leap.")]
        [SerializeField] private LayerMask groundLayers = 1 << 3;

        [Header("Impact")]
        [SerializeField, Min(0)] private int impactDamage = 1;
        [SerializeField] private LayerMask playerLayers = 1 << 8;
        [SerializeField, Min(0f)] private float shakeStrength = 0.4f;
        [SerializeField, Min(0f)] private float shakeTime = 0.4f;

        [Tooltip("Shockwaves sent along the floor on landing.")]
        [SerializeField] private GroundShockwave.Settings shockwave = GroundShockwave.Settings.Default;

        [Header("Look")]
        [Tooltip("Colour of the charging top corner.")]
        [SerializeField] private Color chargeColor = new Color(0.75f, 0.9f, 1f, 1f);

        /// <summary>True from the crouch until it has recovered from landing.</summary>
        public bool IsAttacking => phase != Phase.Ready;

        private TriangleEnemy enemy;
        private Rigidbody2D body;
        private CameraFollow2D cameraFollow;
        private Phase phase = Phase.Ready;
        private float phaseStartTime;
        private float nextSlamTime;
        private bool wasInRange;
        private int leapDirection;
        private float launchSpeedX;
        private float baseGravityScale;

        private float PhaseTime => Time.time - phaseStartTime;
        private float Gravity => Mathf.Abs(Physics2D.gravity.y) * baseGravityScale;
        private float Circumradius => enemy.Shape != null ? enemy.Shape.Circumradius : 0.8f;
        private float Inradius => Circumradius * 0.5f;
        private float SideLength => Circumradius * 1.7320508f;
        private float FloorY => body.position.y - Inradius;

        #region Unity lifecycle

        private void Awake()
        {
            enemy = GetComponent<TriangleEnemy>();
            body = GetComponent<Rigidbody2D>();
            baseGravityScale = body.gravityScale;

            // Charge glow and shockwave in its owner's colour.
            float hue = TriangleKinds.HueShiftOf(this);
            chargeColor = TriangleKinds.Tint(chargeColor, hue);
            shockwave.topColor = TriangleKinds.Tint(shockwave.topColor, hue);
            shockwave.bottomColor = TriangleKinds.Tint(shockwave.bottomColor, hue);
            shockwave.outlineColor = TriangleKinds.Tint(shockwave.outlineColor, hue);
        }

        private void Start() => cameraFollow = FindAnyObjectByType<CameraFollow2D>();

        private void OnDisable()
        {
            if (phase != Phase.Ready) Abort();
        }

        private void LateUpdate()
        {
            if (phase == Phase.Ready)
            {
                TryStartSlam();
                return;
            }

            // Something else ended the attack (e.g. an unarmored triangle was hit and stunned).
            if (!enemy.IsAttackLocked)
            {
                Abort();
                return;
            }

            switch (phase)
            {
                case Phase.Crouch: UpdateCrouch(); break;
                case Phase.Airborne: UpdateAirborne(); break;
                case Phase.Recover: UpdateRecover(); break;
            }
        }

        #endregion

        #region Phases

        private void TryStartSlam()
        {
            bool inRange = enemy.Target != null && enemy.State == TriangleState.Chase && enemy.CanSeeTarget &&
                           Mathf.Abs(enemy.Target.position.x - body.position.x) <= slamRange;

            if (inRange && !wasInRange) nextSlamTime = Mathf.Max(nextSlamTime, Time.time + firstSlamDelay);
            wasInRange = inRange;

            if (!inRange || Time.time < nextSlamTime) return;
            if (!enemy.BeginAttack()) return;

            leapDirection = enemy.Target.position.x >= body.position.x ? 1 : -1;
            SetPhase(Phase.Crouch);
        }

        private void UpdateCrouch()
        {
            float p = Mathf.Clamp01(PhaseTime / crouchTime);
            float e = Ease(p);

            // Squat, lean back from the leap, and tremble as the top corner charges.
            Vector2 tremble = Random.insideUnitCircle * (0.04f * e);
            enemy.SetAttackPose(new Vector2(0f, -crouchDepth * e) + tremble, leapDirection * 7f * e);
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(0, e, chargeColor);

            if (p < 1f) return;
            Launch();
        }

        /// <summary>Leaps on a high arc to a landing spot on its own floor, as close to the circle as it can.</summary>
        private void Launch()
        {
            float x = body.position.x;
            float targetX = enemy.Target != null ? enemy.Target.position.x : x;
            targetX = Mathf.Clamp(targetX, x - maxLeapDistance, x + maxLeapDistance);
            float landX = FindLandingX(x, targetX);

            // Lower the arc if a ceiling is in the way.
            float height = leapHeight;
            RaycastHit2D ceiling = Physics2D.BoxCast(body.position, new Vector2(SideLength * 0.8f, 0.1f), 0f, Vector2.up,
                                                     leapHeight + Circumradius, groundLayers);
            if (ceiling.collider != null) height = Mathf.Max(0.8f, ceiling.distance - Circumradius - 0.2f);

            // A quick rise, then it slams down under much heavier gravity: a snap, not a float.
            float gUp = Gravity * riseGravity;
            float gDown = Gravity * fallGravity;
            float vy = Mathf.Sqrt(2f * gUp * height);
            float flightTime = vy / gUp + Mathf.Sqrt(2f * height / gDown);
            float vx = (landX - x) / flightTime;

            enemy.AttackOwnsVelocity = true;
            body.gravityScale = baseGravityScale * riseGravity;
            body.linearVelocity = new Vector2(vx, vy);
            launchSpeedX = vx;
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(0, 1f, chargeColor);
            SetPhase(Phase.Airborne);
        }

        private void UpdateAirborne()
        {
            // Keep the sideways speed for the whole flight (it's ballistic; this also rides out any bump).
            body.linearVelocity = new Vector2(launchSpeedX, body.linearVelocity.y);

            // Past the top of the arc: drop hard.
            if (body.linearVelocity.y <= 0f) body.gravityScale = baseGravityScale * fallGravity;

            // Tip forward into the fall; the charge fades as it flies.
            float rising = Mathf.Clamp01(body.linearVelocity.y / 6f);
            enemy.SetAttackPose(Vector2.zero, -leapDirection * 10f * (1f - rising));
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(0, 0.4f + 0.6f * rising, chargeColor);

            if (PhaseTime > 0.1f && enemy.IsGrounded && body.linearVelocity.y <= 0.01f) Impact();
        }

        private void Impact()
        {
            launchSpeedX = 0f;
            body.gravityScale = baseGravityScale;
            enemy.AttackOwnsVelocity = false;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, chargeColor);
            if (cameraFollow != null) cameraFollow.Shake(shakeStrength, shakeTime);

            // Crushed underneath (the area just around its base).
            float floorY = FloorY;
            Vector2 crushCentre = new Vector2(body.position.x, floorY + 0.45f);
            Collider2D player = Physics2D.OverlapBox(crushCentre, new Vector2(SideLength * 1.05f, 0.9f), 0f, playerLayers);
            if (player != null && impactDamage > 0)
            {
                var health = player.GetComponentInParent<PlayerHealth>();
                if (health != null) health.TakeDamage(impactDamage, crushCentre);
            }

            Systems.Fx.Dust(new Vector2(body.position.x, floorY), SideLength, 1.2f);
            Systems.Fx.Crack(new Vector2(body.position.x, floorY), SideLength, new Color(0.85f, 0.92f, 1f, 1f));

            // Shockwaves from both ends of its base.
            Material material = enemy.Shape != null ? enemy.Shape.GetComponent<MeshRenderer>().sharedMaterial : null;
            float half = SideLength * 0.5f;
            GroundShockwave.Spawn(new Vector2(body.position.x - half, floorY), -1, shockwave, material);
            GroundShockwave.Spawn(new Vector2(body.position.x + half, floorY), 1, shockwave, material);

            SetPhase(Phase.Recover);
        }

        private void UpdateRecover()
        {
            // Flattened by its own impact, slowly rising back up.
            float p = Mathf.Clamp01(PhaseTime / recoverTime);
            float flatten = (1f - p) * (1f - p);
            enemy.SetAttackPose(new Vector2(0f, -crouchDepth * 0.8f * flatten), 0f);

            if (p >= 1f) Finish();
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStartTime = Time.time;
        }

        private void Finish()
        {
            phase = Phase.Ready;
            nextSlamTime = Time.time + cooldown / Mathf.Max(0.1f, enemy.EffectiveHaste); // an Overseer makes it attack more often
            enemy.EndAttack();
        }

        /// <summary>True during the flattened pause after landing (the opening to hit it).</summary>
        public bool IsRecovering => phase == Phase.Recover;

        /// <summary>
        /// Cuts the recovery short so another move can start at once (e.g. <see cref="TriangleCounterBurst"/>
        /// answering a string of hits). Only works during the recovery; returns true if it did.
        /// </summary>
        public bool InterruptRecovery()
        {
            if (phase != Phase.Recover) return false;
            phase = Phase.Ready;
            nextSlamTime = Time.time + cooldown / Mathf.Max(0.1f, enemy.EffectiveHaste);
            enemy.EndAttack();
            return true;
        }

        private void Abort()
        {
            if (enemy != null && enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, chargeColor);
            bool wasLocked = enemy != null && enemy.IsAttackLocked;
            if (body != null) body.gravityScale = baseGravityScale;
            phase = Phase.Ready;
            nextSlamTime = Time.time + cooldown * 0.5f / Mathf.Max(0.1f, enemy.EffectiveHaste);
            if (wasLocked) enemy.EndAttack();
        }

        #endregion

        #region Landing checks

        /// <summary>
        /// The landing spot nearest <paramref name="targetX"/> (searching back towards itself) where its whole
        /// base rests on floor at its current height and nothing blocks it.
        /// </summary>
        private float FindLandingX(float fromX, float targetX)
        {
            float dir = Mathf.Sign(targetX - fromX);
            for (float x = targetX; Mathf.Abs(x - fromX) > 0.25f; x -= dir * 0.25f)
            {
                if (CanLandAt(x)) return x;
            }
            return fromX; // nowhere better: slam straight down where it stands
        }

        private bool CanLandAt(float x)
        {
            float floorY = FloorY;
            float half = SideLength * 0.45f;
            foreach (float offset in new[] { -half, 0f, half })
            {
                Vector2 probe = new Vector2(x + offset, floorY + 0.5f);
                RaycastHit2D hit = Physics2D.Raycast(probe, Vector2.down, 1f, groundLayers);
                if (hit.collider == null || Mathf.Abs(hit.point.y - floorY) > 0.25f) return false;
            }

            // Its body must fit there (no wall).
            Vector2 centre = new Vector2(x, floorY + Inradius + 0.1f);
            return Physics2D.OverlapBox(centre, new Vector2(SideLength * 0.9f, Circumradius), 0f, groundLayers) == null;
        }

        #endregion

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
