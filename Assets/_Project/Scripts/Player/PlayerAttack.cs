using System;
using System.Collections;
using System.Collections.Generic;
using ProjectCircle.Combat;
using ProjectCircle.Enemies;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>The direction of a cape attack.</summary>
    public enum AttackDirection { Side, Up, Down }

    /// <summary>
    /// The circle's attack: it whips its cape in a fast arc.
    ///
    /// - Aim: neutral swipes the way it faces; holding Up swipes overhead; holding Down in the air swipes
    ///   underneath.
    /// - Hits are decided by a fixed crescent-shaped area for the strike frames only, never by the cloth, so
    ///   they are consistent. The cape and a slash-arc smear are the visuals.
    /// - Landing a hit: brief hit-stop, camera shake and a spark. A side hit pushes the circle back a little;
    ///   a down hit bounces it up (pogo) and restores its air dash.
    ///
    /// The cape is a physical object, not vertex power, so this fits the rule that the circle has no power
    /// of its own.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // check attack hits before enemies' contact damage in the same physics step
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerInputReader))]
    public sealed class PlayerAttack : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCape cape;

        [Tooltip("Mesh used to draw the slash arc. Child of the Player.")]
        [SerializeField] private MeshFilter slashArc;

        [Tooltip("Spark shown where a hit lands.")]
        [SerializeField] private VertexGlow hitSpark;

        [Tooltip("Camera to shake on hits. Found automatically if left empty.")]
        [SerializeField] private CameraFollow2D cameraFollow;

        [Header("Attack")]
        [SerializeField, Min(1)] private int damage = 1;

        [Tooltip("Layers that can be hit.")]
        [SerializeField] private LayerMask targetLayers = 1 << 9;

        [Tooltip("Reach of the swipe from the circle's centre.")]
        [SerializeField, Min(0.1f)] private float reach = 1.9f;

        [Tooltip("Half the width of the swipe's arc, in degrees.")]
        [SerializeField, Range(10f, 120f)] private float arcHalfAngle = 70f;

        [Tooltip("Length of the whole swing.")]
        [SerializeField, Min(0.05f)] private float swingTime = 0.17f;

        [Tooltip("When during the swing the hitbox switches on and off (fractions of the swing).")]
        [SerializeField] private Vector2 activeWindow = new Vector2(0f, 1f);

        [Tooltip("Minimum time between attacks.")]
        [SerializeField, Min(0f)] private float cooldown = 0.3f;

        [Tooltip("An attack pressed this long before it is allowed still happens.")]
        [SerializeField, Min(0f)] private float bufferTime = 0.15f;

        [Tooltip("Attack and air jump both need the cape. If Jump comes this soon after an attack starts, they count as pressed together: the air jump goes first and the attack follows as soon as the cape is back. Later than this, the jump waits for the swing to finish.")]
        [SerializeField, Min(0f)] private float simultaneousWindow = 0.1f;

        [Header("On Hit")]
        [Tooltip("Upward speed when a down swipe bounces off something.")]
        [SerializeField, Min(0f)] private float pogoSpeed = 17f;

        [Tooltip("Seconds of protection from contact damage after a pogo bounce.")]
        [SerializeField, Min(0f)] private float pogoGraceTime = 0.2f;

        [Tooltip("Push-back speed after landing a side swipe.")]
        [SerializeField, Min(0f)] private float recoilSpeed = 5f;

        [SerializeField, Min(0f)] private float recoilTime = 0.08f;

        [Tooltip("Real-time seconds the game nearly freezes when a hit lands.")]
        [SerializeField, Min(0f)] private float hitStopTime = 0.045f;

        [SerializeField, Min(0f)] private float shakeStrength = 0.12f;
        [SerializeField, Min(0f)] private float shakeTime = 0.15f;

        [Header("Parried (hitting a shield)")]
        [Tooltip("Push-back speed when the blade glances off a shield.")]
        [SerializeField, Min(0f)] private float parryRecoilSpeed = 9f;

        [SerializeField, Min(0f)] private float parryRecoilTime = 0.14f;

        [Tooltip("Real-time freeze on a parry (a sharp clash).")]
        [SerializeField, Min(0f)] private float parryHitStopTime = 0.07f;

        [SerializeField, Min(0f)] private float parryShakeStrength = 0.1f;

        [Tooltip("Colour of the clash spark.")]
        [SerializeField] private Color parryColor = new Color(0.75f, 0.95f, 1f, 1f);

        [Header("Slash Arc")]
        [SerializeField] private Color slashColor = new Color(0.92f, 0.95f, 1f, 0.6f);

        [Tooltip("Inner radius of the slash arc.")]
        [SerializeField, Min(0f)] private float slashInnerRadius = 0.55f;

        [Tooltip("How much of the arc behind the leading edge is drawn, in degrees.")]
        [SerializeField, Range(20f, 220f)] private float slashTrail = 120f;

        [Tooltip("How long the arc takes to fade after the swing.")]
        [SerializeField, Min(0.01f)] private float slashFadeTime = 0.09f;

        /// <summary>Raised when an attack starts.</summary>
        public event Action<AttackDirection> Attacked;

        /// <summary>Raised when an attack hits something.</summary>
        public event Action<Collider2D> HitLanded;

        /// <summary>Raised when an attack glances off a shield. Argument: where.</summary>
        public event Action<Vector2> Parried;

        public bool IsAttacking => attacking;

        private PlayerMovement movement;
        private PlayerInputReader input;
        private PlayerHealth health;
        private PlayerParry parry;

        private bool attacking;
        private float attackStartTime = float.NegativeInfinity;
        private float bufferedUntil = float.NegativeInfinity;
        private AttackDirection direction;
        private int facing = 1;
        private float arcFrom;
        private float arcTo;
        private bool pogoUsed;
        private readonly HashSet<EnemyHealth> hitThisSwing = new HashSet<EnemyHealth>();
        private readonly List<Collider2D> overlaps = new List<Collider2D>(8);
        private ContactFilter2D targetFilter;

        private Mesh arcMesh;
        private readonly List<Vector3> arcVertices = new List<Vector3>();
        private readonly List<Color> arcColors = new List<Color>();
        private readonly List<int> arcTriangles = new List<int>();
        private float sparkTime = float.NegativeInfinity;
        private Color hitSparkColor = Color.white;

        #region Unity lifecycle

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            input = GetComponent<PlayerInputReader>();
            health = GetComponent<PlayerHealth>();
            parry = GetComponent<PlayerParry>();
            if (cameraFollow == null) cameraFollow = FindAnyObjectByType<CameraFollow2D>();
            if (hitSpark != null) hitSparkColor = hitSpark.Color;

            targetFilter = new ContactFilter2D { useLayerMask = true, layerMask = targetLayers, useTriggers = true };

            if (slashArc != null)
            {
                arcMesh = new Mesh { name = "Slash Arc" };
                arcMesh.MarkDynamic();
                slashArc.sharedMesh = arcMesh;
            }
        }

        private void OnEnable()
        {
            input.AttackPressed += HandleAttackPressed;
            movement.AirJumpGate = CanCapeTakeAirJump;
        }

        private void OnDisable()
        {
            input.AttackPressed -= HandleAttackPressed;
            if (movement.AirJumpGate == CanCapeTakeAirJump) movement.AirJumpGate = null;
        }

        private void OnDestroy()
        {
            if (arcMesh != null) Destroy(arcMesh);
        }

        private void Update()
        {
            if (attacking && Time.time - attackStartTime >= swingTime) attacking = false;

            // While the cape is out as an air-jump platform, keep a pending attack queued until it's back.
            if (Time.time <= bufferedUntil && cape != null && cape.IsFormingPlatform)
            {
                bufferedUntil = Mathf.Max(bufferedUntil, Time.time + cape.PlatformBusyTimeLeft + 0.05f);
            }

            // Same while it is wrapped for a parry: the attack comes out as soon as it unwraps.
            if (Time.time <= bufferedUntil && parry != null && parry.IsBusy)
            {
                bufferedUntil = Mathf.Max(bufferedUntil, Time.time + 0.05f);
            }

            if (Time.time <= bufferedUntil && CanAttack()) StartAttack();
        }

        private void FixedUpdate()
        {
            if (!attacking) return;

            float p = (Time.time - attackStartTime) / swingTime;
            if (p >= activeWindow.x && p <= activeWindow.y) CheckHits();
        }

        private void LateUpdate()
        {
            DrawSlashArc();

            if (hitSpark != null)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - sparkTime) / 0.18f);
                hitSpark.Intensity = 1.8f * (1f - t);
            }
        }

        #endregion

        #region Attacking

        private void HandleAttackPressed() => bufferedUntil = Time.time + bufferTime;

        private bool CanAttack()
        {
            if (attacking || Time.time < attackStartTime + cooldown) return false;
            if (movement.IsDashing || movement.IsKnockedBack) return false;
            if (cape != null && cape.IsFormingPlatform) return false; // the cape is busy being a platform
            if (parry != null && parry.IsBusy) return false;          // ...or wrapped around the circle
            return health == null || !health.IsDead;
        }

        /// <summary>Called when a parry starts: a swing in progress gives the cape up (no hit, no cooldown).</summary>
        public void InterruptForParry()
        {
            if (attacking) CancelAttack();
        }

        /// <summary>
        /// Asked by <see cref="PlayerMovement"/> right before an air jump. The cape can't be a platform and a
        /// weapon at once:
        /// - No swing in progress: the jump goes ahead.
        /// - A swing that only just started and hasn't hit anything: the two were pressed together. The swing
        ///   is cancelled and queued again, so the jump happens now and the attack right after it.
        /// - Otherwise the jump waits (it stays queued) until the swing finishes.
        /// </summary>
        private bool CanCapeTakeAirJump()
        {
            if (parry != null && parry.IsBusy) return false; // wrapped for a parry: the jump waits a moment
            if (!attacking) return true;

            bool pressedTogether = Time.time - attackStartTime <= simultaneousWindow && hitThisSwing.Count == 0;
            if (!pressedTogether) return false;

            CancelAttack();
            bufferedUntil = Time.time + bufferTime; // re-queue: fires once the cape is back
            return true;
        }

        /// <summary>Stops the current swing without it counting towards the cooldown.</summary>
        private void CancelAttack()
        {
            attacking = false;
            attackStartTime = float.NegativeInfinity; // also hides the slash arc
            if (cape != null) cape.CancelSwing();
        }

        private void StartAttack()
        {
            bufferedUntil = float.NegativeInfinity;
            attacking = true;
            attackStartTime = Time.time;
            pogoUsed = false;
            hitThisSwing.Clear();
            facing = movement.FacingDirection;

            // Aim: Up overhead; Down only in the air (on the ground it would hit the floor); otherwise sideways.
            Vector2 aim = input.Move;
            if (aim.y > 0.5f) direction = AttackDirection.Up;
            else if (aim.y < -0.5f && !movement.IsGrounded) direction = AttackDirection.Down;
            else direction = AttackDirection.Side;

            // The arc the cape sweeps (degrees, not wrapped). Right-facing values, mirrored for left.
            switch (direction)
            {
                case AttackDirection.Side: // from behind-and-above, over the top, to in front and slightly down
                    arcFrom = facing > 0 ? 140f : 40f;
                    arcTo = facing > 0 ? -35f : 215f;
                    break;
                case AttackDirection.Up: // from in front, up over the head, to behind
                    arcFrom = facing > 0 ? -10f : 190f;
                    arcTo = facing > 0 ? 190f : -10f;
                    break;
                default: // Down: from in front, down underneath, to behind
                    arcFrom = facing > 0 ? 10f : 170f;
                    arcTo = facing > 0 ? -190f : 370f;
                    break;
            }

            if (cape != null) cape.PlaySwing(arcFrom, arcTo, swingTime);
            Attacked?.Invoke(direction);

            // Hits count from the very first frame, so a swing never loses to bumping into the target.
            if (activeWindow.x <= 0f) CheckHits();
        }

        /// <summary>Hits everything inside the swipe's crescent that hasn't been hit by this swing yet.</summary>
        private void CheckHits()
        {
            Vector2 centre = transform.position;
            Vector2 aimDirection = AimVector();

            overlaps.Clear();
            int count = Physics2D.OverlapCircle(centre, reach, targetFilter, overlaps);
            for (int i = 0; i < count; i++)
            {
                Collider2D target = overlaps[i];
                Vector2 closest = target.ClosestPoint(centre);
                Vector2 toTarget = closest - centre;

                // Inside the arc (or touching the circle itself).
                bool touching = toTarget.sqrMagnitude < 0.3f * 0.3f;
                if (!touching && Vector2.Angle(aimDirection, toTarget) > arcHalfAngle) continue;

                var enemyHealth = target.GetComponentInParent<EnemyHealth>();
                if (enemyHealth == null || hitThisSwing.Contains(enemyHealth)) continue;

                hitThisSwing.Add(enemyHealth);

                // Shielded there? The blade glances off.
                var guard = target.GetComponentInParent<IHitGuard>();
                if (guard != null && guard.TryDeflect(closest, centre))
                {
                    OnParried(closest);
                    continue;
                }

                if (enemyHealth.TakeHit(damage, centre)) OnHitLanded(target, closest);
                else if (direction == AttackDirection.Down) TryPogo(); // still bounce off a target that shrugged the hit off
            }
        }

        /// <summary>Bounces the circle upwards once per down swing.</summary>
        private void TryPogo()
        {
            if (pogoUsed) return;
            pogoUsed = true;

            // The bounce wins over bumping into what was hit in the same instant.
            if (health != null) health.GrantGrace(pogoGraceTime);
            movement.Pogo(pogoSpeed);
        }

        private void OnHitLanded(Collider2D target, Vector2 point)
        {
            HitLanded?.Invoke(target);

            if (hitSpark != null)
            {
                hitSpark.Color = hitSparkColor;
                hitSpark.transform.position = new Vector3(point.x, point.y, hitSpark.transform.position.z);
                hitSpark.Burst();
                sparkTime = Time.unscaledTime;
            }

            if (cameraFollow != null) cameraFollow.Shake(shakeStrength, shakeTime);
            HitStop.Freeze(hitStopTime);

            switch (direction)
            {
                case AttackDirection.Down:
                    TryPogo();
                    break;
                case AttackDirection.Side:
                    movement.Recoil(new Vector2(-facing * recoilSpeed, 0f), recoilTime);
                    break;
            }
        }

        /// <summary>
        /// The blade struck a shield: a bright clash, a short freeze, and the circle is knocked back
        /// (a down swing still bounces, so shields can be pogoed off).
        /// </summary>
        private void OnParried(Vector2 point)
        {
            Parried?.Invoke(point);

            if (hitSpark != null)
            {
                hitSpark.Color = parryColor;
                hitSpark.transform.position = new Vector3(point.x, point.y, hitSpark.transform.position.z);
                hitSpark.Burst();
                sparkTime = Time.unscaledTime;
            }

            if (cameraFollow != null) cameraFollow.Shake(parryShakeStrength, shakeTime);
            HitStop.Freeze(parryHitStopTime);

            switch (direction)
            {
                case AttackDirection.Down:
                    TryPogo();
                    break;
                case AttackDirection.Side:
                    movement.Recoil(new Vector2(-facing * parryRecoilSpeed, 0f), parryRecoilTime);
                    break;
                default: // Up: knocked back down a little
                    movement.Recoil(new Vector2(-facing * parryRecoilSpeed * 0.5f, -2f), parryRecoilTime);
                    break;
            }
        }


        private Vector2 AimVector()
        {
            switch (direction)
            {
                case AttackDirection.Up: return Vector2.up;
                case AttackDirection.Down: return Vector2.down;
                default: return new Vector2(facing, 0f);
            }
        }

        #endregion

        #region Slash arc

        /// <summary>
        /// Draws a crescent along the path the cape's tip has swept: bright at the leading edge, fading towards
        /// the tail, then fading out completely just after the swing.
        /// </summary>
        private void DrawSlashArc()
        {
            if (arcMesh == null) return;

            float elapsed = Time.time - attackStartTime;
            float fade = elapsed <= swingTime ? 1f : 1f - (elapsed - swingTime) / slashFadeTime;
            if (fade <= 0f)
            {
                if (arcMesh.vertexCount > 0) arcMesh.Clear();
                return;
            }

            float p = Mathf.Clamp01(elapsed / swingTime);
            float eased = 1f - (1f - p) * (1f - p);
            float lead = Mathf.Lerp(arcFrom, arcTo, eased);
            float sweepSign = Mathf.Sign(arcTo - arcFrom);
            float tail = lead - sweepSign * Mathf.Min(slashTrail, Mathf.Abs(lead - arcFrom));

            arcVertices.Clear();
            arcColors.Clear();
            arcTriangles.Clear();

            const int steps = 18;
            float outer = reach * 0.95f;
            float middle = Mathf.Lerp(slashInnerRadius, outer, 0.75f);

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps; // 0 = tail, 1 = leading edge
                float angle = Mathf.Lerp(tail, lead, t) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);

                float alpha = slashColor.a * fade * t * t;
                Color clear = slashColor; clear.a = 0f;
                Color mid = slashColor; mid.a = alpha * 0.55f;
                Color edge = slashColor; edge.a = alpha;

                // Three rings: soft inner edge, mid band, bright outer edge.
                arcVertices.Add(dir * slashInnerRadius); arcColors.Add(ToRenderSpace(clear));
                arcVertices.Add(dir * middle); arcColors.Add(ToRenderSpace(mid));
                arcVertices.Add(dir * outer); arcColors.Add(ToRenderSpace(edge));
            }

            for (int s = 0; s < steps; s++)
            {
                int a = s * 3;
                int b = a + 3;
                for (int ring = 0; ring < 2; ring++)
                {
                    arcTriangles.Add(a + ring); arcTriangles.Add(b + ring); arcTriangles.Add(a + ring + 1);
                    arcTriangles.Add(a + ring + 1); arcTriangles.Add(b + ring); arcTriangles.Add(b + ring + 1);
                }
            }

            arcMesh.Clear();
            arcMesh.SetVertices(arcVertices);
            arcMesh.SetColors(arcColors);
            arcMesh.SetTriangles(arcTriangles, 0);
            arcMesh.RecalculateBounds();
        }

        private static Color ToRenderSpace(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            Color linear = c.linear;
            linear.a = c.a;
            return linear;
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // The side-swipe hitbox: a crescent of radius 'reach' around the facing direction.
            UnityEditor.Handles.color = new Color(1f, 0.6f, 0.3f, 0.6f);
            Vector3 from = Quaternion.Euler(0f, 0f, -arcHalfAngle) * Vector3.right;
            UnityEditor.Handles.DrawWireArc(transform.position, Vector3.forward, from, arcHalfAngle * 2f, reach);
        }
#endif
    }
}
