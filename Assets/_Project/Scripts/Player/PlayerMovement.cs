using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Physics-driven platformer movement for the ball protagonist.
    /// Features: acceleration-based running with snappy turns, jump buffering, coyote time,
    /// variable jump height, apex hang, heavier falls with terminal velocity, fast-fall,
    /// a horizontal dash (one air dash per airtime) and an air jump off the cape (see <see cref="AirJumped"/>).
    /// All physics runs in FixedUpdate; input arrives through <see cref="PlayerInputReader"/> events.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(PlayerInputReader))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private PlayerMovementSettings settings;

        [Header("Ground Detection")]
        [Tooltip("Layers the ball can stand on and jump from.")]
        [SerializeField] private LayerMask groundLayers;

        [Tooltip("How far below the collider to probe for ground.")]
        [SerializeField, Min(0.005f)] private float groundCheckDistance = 0.06f;

        [Tooltip("Minimum surface normal Y to count as ground (0.6 ≈ slopes up to ~53°).")]
        [SerializeField, Range(0f, 1f)] private float minGroundNormalY = 0.6f;

        [Header("Knockback")]
        [Tooltip("Sideways speed when knocked back by a hit.")]
        [SerializeField, Min(0f)] private float knockbackSpeed = 9f;

        [Tooltip("Upward speed when knocked back by a hit.")]
        [SerializeField, Min(0f)] private float knockbackLift = 8f;

        [Tooltip("How long controls are ignored after being knocked back.")]
        [SerializeField, Min(0f)] private float knockbackTime = 0.22f;

        /// <summary>Raised when a jump leaves the ground (also raised for air jumps and pogo bounces).</summary>
        public event Action Jumped;

        /// <summary>
        /// Raised when the circle jumps in mid-air off its cape. Argument: the world point under the circle
        /// where the cape forms the platform.
        /// </summary>
        public event Action<Vector2> AirJumped;

        /// <summary>
        /// Optional gate for air jumps, asked right before one fires. Return false to hold the jump (it stays
        /// queued and fires as soon as the gate opens). Used so the cape is never asked to be a platform and a
        /// weapon at the same time.
        /// </summary>
        public Func<bool> AirJumpGate { get; set; }

        /// <summary>Raised on touchdown. Argument is the downward speed at impact (positive).</summary>
        public event Action<float> Landed;

        /// <summary>Raised when a dash starts.</summary>
        public event Action Dashed;

        /// <summary>Raised when a dash finishes.</summary>
        public event Action DashEnded;

        /// <summary>Raised when <see cref="State"/> changes. Arguments: previous state, new state.</summary>
        public event Action<PlayerState, PlayerState> StateChanged;

        /// <summary>Current high-level movement state. Updated once per physics step.</summary>
        public PlayerState State { get; private set; } = PlayerState.Idle;

        public bool IsGrounded { get; private set; }
        public bool IsDashing { get; private set; }

        /// <summary>True while being knocked back by a hit (controls are ignored).</summary>
        public bool IsKnockedBack => knockbackTimer > 0f;

        /// <summary>-1 for left, +1 for right. Keeps its last value when there is no input.</summary>
        public int FacingDirection { get; private set; } = 1;

        public Vector2 Velocity => body.linearVelocity;

        /// <summary>Air jumps left before landing.</summary>
        public int AirJumpsLeft => airJumpsLeft;

        /// <summary>World-space radius of the circle's collider.</summary>
        public float Radius => circle.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));

        /// <summary>
        /// Downward acceleration (positive, units/sec²) the player feels during a normal jump.
        /// Cosmetic physics (e.g. the cape) use this so they fall at the same rate as the player.
        /// </summary>
        public float BaseGravity => settings != null ? settings.JumpGravity : Mathf.Abs(Physics2D.gravity.y);

        /// <summary>
        /// Downward acceleration acting on the player right now (changes with jump cut, apex hang and falling).
        /// On the ground and during a dash this is <see cref="BaseGravity"/>.
        /// </summary>
        public float CurrentGravity =>
            IsGrounded || IsDashing ? BaseGravity : Mathf.Abs(Physics2D.gravity.y) * body.gravityScale;

        private Rigidbody2D body;
        private CircleCollider2D circle;
        private PlayerInputReader input;
        private PlayerAbilities abilities; // optional: the dash and air jump stay locked until unlocked

        private ContactFilter2D groundFilter;
        private readonly List<RaycastHit2D> groundHits = new List<RaycastHit2D>(8);

        // Timers count down to zero. A positive value means "still within the window".
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float dashBufferTimer;
        private float dashTimer;
        private float dashCooldownTimer;
        private float knockbackTimer;
        private float recoilTimer;

        private bool isJumping;       // true from take-off until landing
        private bool jumpCutActive;   // jump released early while rising
        private bool canAirDash;
        private int airJumpsLeft;
        private int dashDirection;
        private float peakFallSpeed;  // fastest downward speed this airtime, for landing feedback
        private Vector2 groundNormal = Vector2.up; // surface normal under the player (up when airborne)

        private const float IdleSpeedThreshold = 0.1f; // below this ground speed (with no input) the player is Idle

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            circle = GetComponent<CircleCollider2D>();
            input = GetComponent<PlayerInputReader>();
            abilities = GetComponent<PlayerAbilities>();

            groundFilter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = groundLayers,
                useTriggers = false
            };

            if (settings == null)
            {
                Debug.LogError($"{nameof(PlayerMovement)} on '{name}' has no {nameof(PlayerMovementSettings)} assigned.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            input.JumpPressed += HandleJumpPressed;
            input.JumpReleased += HandleJumpReleased;
            input.DashPressed += HandleDashPressed;
        }

        private void OnDisable()
        {
            input.JumpPressed -= HandleJumpPressed;
            input.JumpReleased -= HandleJumpReleased;
            input.DashPressed -= HandleDashPressed;
        }

        private void Update()
        {
            // Facing is read every frame so visuals/camera react immediately, even between physics steps.
            float x = GetHorizontalInput();
            if (x != 0f)
            {
                FacingDirection = x > 0f ? 1 : -1;
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            TickTimers(dt);
            UpdateGrounded();
            Simulate(dt);
            UpdateState();
        }

        /// <summary>One physics step of movement: dash, jump, run and gravity.</summary>
        private void Simulate(float dt)
        {
            if (knockbackTimer > 0f)
            {
                // Knocked back: no control, just momentum and gravity.
                knockbackTimer -= dt;
                ApplyGravity();
                ClampFallSpeed();
                return;
            }

            if (IsDashing)
            {
                TickDash(dt);
                return;
            }

            if (dashBufferTimer > 0f && CanDash())
            {
                StartDash();
                return;
            }

            if (jumpBufferTimer > 0f)
            {
                if (coyoteTimer > 0f)
                {
                    Jump();
                }
                else if (airJumpsLeft > 0 && !IsGrounded && HasAbility(PlayerAbility.AirJump))
                {
                    if (AirJumpGate == null || AirJumpGate()) AirJump();
                    else jumpBufferTimer = Mathf.Max(jumpBufferTimer, dt * 1.5f); // cape busy: hold the jump until it's free
                }
            }

            // A recoil from landing an attack briefly overrides horizontal control.
            if (recoilTimer > 0f) recoilTimer -= dt;
            else ApplyHorizontalMovement(dt);
            ApplyGravity();
            ClampFallSpeed();

            if (!IsGrounded)
            {
                peakFallSpeed = Mathf.Max(peakFallSpeed, -body.linearVelocity.y);
            }
        }

        /// <summary>Stops all motion and clears movement state. Use after teleports/respawns.</summary>
        public void ResetMotion()
        {
            body.linearVelocity = Vector2.zero;
            IsDashing = false;
            isJumping = false;
            jumpCutActive = false;
            coyoteTimer = jumpBufferTimer = dashBufferTimer = dashTimer = dashCooldownTimer = knockbackTimer = 0f;
            peakFallSpeed = 0f;
            airJumpsLeft = settings.AirJumps;
            body.gravityScale = settings.BaseGravityScale;
        }

        /// <summary>
        /// Bounce upwards off something hit from above (the down attack's pogo). Always a full-height bounce
        /// (not shortened by releasing Jump), and the air dash is restored.
        /// </summary>
        public void Pogo(float upwardSpeed)
        {
            if (IsDashing)
            {
                IsDashing = false;
                DashEnded?.Invoke();
            }

            isJumping = true;
            jumpCutActive = false;
            IsGrounded = false;
            coyoteTimer = 0f;
            canAirDash = true;
            airJumpsLeft = settings.AirJumps;
            body.gravityScale = settings.BaseGravityScale;
            body.linearVelocity = new Vector2(body.linearVelocity.x, upwardSpeed);
            Jumped?.Invoke();
        }

        /// <summary>
        /// A short push from landing an attack (pushed back off what it hit). Horizontal control is suspended
        /// for <paramref name="duration"/> seconds so the push is felt.
        /// </summary>
        public void Recoil(Vector2 velocity, float duration)
        {
            if (IsDashing) return;
            recoilTimer = duration;
            body.linearVelocity = new Vector2(velocity.x, velocity.y != 0f ? velocity.y : body.linearVelocity.y);
        }

        /// <summary>
        /// Throws the circle up and away from a hit. <paramref name="awayDirection"/> only needs the right
        /// horizontal sign; the size of the knockback comes from the Knockback settings.
        /// </summary>
        public void ApplyKnockback(Vector2 awayDirection)
        {
            if (IsDashing)
            {
                IsDashing = false;
                DashEnded?.Invoke();
            }

            float side = Mathf.Abs(awayDirection.x) > 0.01f ? Mathf.Sign(awayDirection.x) : -FacingDirection;
            knockbackTimer = knockbackTime;

            // Treated like a jump so ground detection doesn't snap it back down on the first frame.
            isJumping = true;
            jumpCutActive = false;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            IsGrounded = false;

            body.gravityScale = settings.BaseGravityScale;
            body.linearVelocity = new Vector2(side * knockbackSpeed, knockbackLift);
        }

        #region Input handlers

        private void HandleJumpPressed() => jumpBufferTimer = settings.JumpBufferTime;

        private void HandleJumpReleased()
        {
            // Releasing jump while still rising shortens the jump (variable jump height).
            if (isJumping && body.linearVelocity.y > 0f)
            {
                jumpCutActive = true;
            }
        }

        private void HandleDashPressed() => dashBufferTimer = settings.DashBufferTime;

        #endregion

        #region Ground

        private void UpdateGrounded()
        {
            bool wasGrounded = IsGrounded;

            // While rising from a jump, ignore ground contacts so the take-off frame
            // can't immediately re-ground us and refresh coyote time.
            bool risingFromJump = isJumping && body.linearVelocity.y > 0.01f;
            IsGrounded = !risingFromJump && ProbeGround();

            if (!IsGrounded)
            {
                return;
            }

            coyoteTimer = settings.CoyoteTime;
            canAirDash = true;
            airJumpsLeft = settings.AirJumps;

            if (!wasGrounded)
            {
                isJumping = false;
                jumpCutActive = false;
                Landed?.Invoke(peakFallSpeed);
                peakFallSpeed = 0f;
            }
        }

        /// <summary>Casts the circle collider a short distance down and checks for a walkable surface.</summary>
        private bool ProbeGround()
        {
            int hitCount = circle.Cast(Vector2.down, groundFilter, groundHits, groundCheckDistance);

            // Keep the flattest walkable surface; its normal is used to move along slopes.
            bool found = false;
            float bestNormalY = minGroundNormalY;
            for (int i = 0; i < hitCount; i++)
            {
                Vector2 normal = groundHits[i].normal;
                if (normal.y >= bestNormalY)
                {
                    bestNormalY = normal.y;
                    groundNormal = normal;
                    found = true;
                }
            }

            if (!found)
            {
                groundNormal = Vector2.up;
            }

            return found;
        }

        #endregion

        #region State

        /// <summary>Derives the high-level state from the physics result of this step and announces changes.</summary>
        private void UpdateState()
        {
            PlayerState next;
            if (knockbackTimer > 0f)
            {
                next = PlayerState.Hurt;
            }
            else if (IsDashing)
            {
                next = PlayerState.Dash;
            }
            else if (!IsGrounded)
            {
                next = body.linearVelocity.y > 0f ? PlayerState.Jump : PlayerState.Fall;
            }
            else
            {
                bool moving = GetHorizontalInput() != 0f || Mathf.Abs(body.linearVelocity.x) > IdleSpeedThreshold;
                next = moving ? PlayerState.Run : PlayerState.Idle;
            }

            if (next == State)
            {
                return;
            }

            PlayerState previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        #endregion

        #region Jump & gravity

        private void Jump()
        {
            // Set (not add) the vertical speed so every jump is identical regardless of slope/state.
            body.linearVelocity = new Vector2(body.linearVelocity.x, settings.JumpVelocity);

            isJumping = true;
            IsGrounded = false;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            peakFallSpeed = 0f;

            // If the button was already released while the jump sat in the buffer, make it a short hop.
            jumpCutActive = !input.JumpHeld;

            Jumped?.Invoke();
        }

        /// <summary>
        /// Jump in mid-air off the cape, which flattens into a brief platform under the circle.
        /// Like a ground jump it is shortened by releasing Jump early.
        /// </summary>
        private void AirJump()
        {
            airJumpsLeft--;
            Vector2 platformPoint = body.position + Vector2.down * Radius;

            body.linearVelocity = new Vector2(body.linearVelocity.x, settings.AirJumpVelocity);
            body.gravityScale = settings.BaseGravityScale;

            isJumping = true;
            jumpBufferTimer = 0f;
            peakFallSpeed = 0f;
            jumpCutActive = !input.JumpHeld;

            Jumped?.Invoke();
            AirJumped?.Invoke(platformPoint);
        }

        private void ApplyGravity()
        {
            float multiplier = 1f;
            float vy = body.linearVelocity.y;

            if (!IsGrounded)
            {
                if (vy < 0f)
                {
                    multiplier = settings.FallGravityMultiplier;
                    if (IsFastFalling())
                    {
                        multiplier *= settings.FastFallGravityMultiplier;
                    }
                }
                else if (jumpCutActive)
                {
                    multiplier = settings.JumpCutGravityMultiplier;
                }
                else if (IsAtApex())
                {
                    multiplier = settings.ApexGravityMultiplier;
                }
            }

            if (IsGrounded)
            {
                // On the ground, gravity presses straight into the surface instead of straight down.
                // On flat ground that is identical; on a slope it stops the (frictionless) circle sliding downhill.
                body.gravityScale = 0f;
                float weight = Mathf.Abs(Physics2D.gravity.y) * settings.BaseGravityScale * body.mass;
                body.AddForce(-groundNormal * weight);
                return;
            }

            body.gravityScale = settings.BaseGravityScale * multiplier;
        }

        private void ClampFallSpeed()
        {
            float maxFall = IsFastFalling() ? settings.FastFallMaxSpeed : settings.MaxFallSpeed;
            Vector2 v = body.linearVelocity;
            if (v.y < -maxFall)
            {
                body.linearVelocity = new Vector2(v.x, -maxFall);
            }
        }

        private bool IsAtApex() =>
            !IsGrounded && isJumping && !jumpCutActive &&
            Mathf.Abs(body.linearVelocity.y) < settings.ApexThreshold;

        private bool IsFastFalling() => settings.FastFallEnabled && !IsGrounded && input.Move.y < -0.5f;

        #endregion

        #region Horizontal

        private void ApplyHorizontalMovement(float dt)
        {
            float inputX = GetHorizontalInput();
            float targetSpeed = inputX * settings.MaxRunSpeed;

            // On the ground, speed is measured along the surface so slopes are walked at full speed.
            Vector2 velocity = body.linearVelocity;
            Vector2 groundTangent = new Vector2(groundNormal.y, -groundNormal.x);
            float vx = IsGrounded ? Vector2.Dot(velocity, groundTangent) : velocity.x;

            bool hasInput = inputX != 0f;
            bool isTurning = hasInput && Mathf.Abs(vx) > 0.01f && Mathf.Sign(targetSpeed) != Mathf.Sign(vx);

            float rate;
            if (IsGrounded)
            {
                rate = isTurning ? settings.GroundTurnAcceleration
                     : hasInput ? settings.GroundAcceleration
                     : settings.GroundDeceleration;
            }
            else
            {
                rate = isTurning ? settings.AirTurnAcceleration
                     : hasInput ? settings.AirAcceleration
                     : settings.AirDeceleration;
            }

            if (IsAtApex())
            {
                rate *= settings.ApexAccelerationMultiplier;
            }

            vx = Mathf.MoveTowards(vx, targetSpeed, rate * dt);

            if (IsGrounded)
            {
                // Move along the surface. Keep only velocity going into the ground (never away from it),
                // so the circle doesn't launch off the top of a slope or bounce down one.
                float intoGround = Mathf.Min(Vector2.Dot(velocity, groundNormal), 0f);
                body.linearVelocity = groundTangent * vx + groundNormal * intoGround;
            }
            else
            {
                body.linearVelocity = new Vector2(vx, velocity.y);
            }
        }

        /// <summary>Horizontal input with dead zone applied, snapped to a clean -1..1 range.</summary>
        private float GetHorizontalInput()
        {
            float x = input.Move.x;
            return Mathf.Abs(x) < settings.InputDeadZone ? 0f : Mathf.Clamp(x, -1f, 1f);
        }

        #endregion

        #region Dash

        private bool CanDash() => dashCooldownTimer <= 0f && (IsGrounded || canAirDash) && HasAbility(PlayerAbility.Dash);

        private bool HasAbility(PlayerAbility ability) => abilities == null || abilities.Has(ability);

        private void StartDash()
        {
            IsDashing = true;
            dashTimer = settings.DashDuration;
            dashCooldownTimer = settings.DashCooldown;
            dashBufferTimer = 0f;
            dashDirection = FacingDirection;

            if (!IsGrounded)
            {
                canAirDash = false;
            }

            // A dash cancels any jump in progress and ignores gravity for its duration.
            isJumping = false;
            jumpCutActive = false;
            body.gravityScale = 0f;
            body.linearVelocity = new Vector2(dashDirection * settings.DashSpeed, 0f);

            Dashed?.Invoke();
        }

        private void TickDash(float dt)
        {
            body.linearVelocity = new Vector2(dashDirection * settings.DashSpeed, 0f);
            dashTimer -= dt;

            if (dashTimer <= 0f)
            {
                EndDash();
            }
        }

        private void EndDash()
        {
            IsDashing = false;

            // Exit at run speed so the dash doesn't "slingshot" the player.
            body.linearVelocity = new Vector2(dashDirection * settings.MaxRunSpeed, 0f);
            body.gravityScale = settings.BaseGravityScale;

            DashEnded?.Invoke();
        }

        #endregion

        private void TickTimers(float dt)
        {
            if (coyoteTimer > 0f) coyoteTimer -= dt;
            if (jumpBufferTimer > 0f) jumpBufferTimer -= dt;
            if (dashBufferTimer > 0f) dashBufferTimer -= dt;
            if (dashCooldownTimer > 0f) dashCooldownTimer -= dt;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualise the ground probe in the Scene view.
            var col = GetComponent<CircleCollider2D>();
            if (col == null) return;

            Vector3 center = col.bounds.center;
            float radius = col.bounds.extents.x;
            Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(center + Vector3.down * groundCheckDistance, radius);
        }
#endif
    }
}
