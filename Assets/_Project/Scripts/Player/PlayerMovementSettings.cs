using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Tunable movement values for the player ball, stored as an asset so they can be tweaked
    /// in Play Mode without being lost when Play Mode ends.
    /// Jump is authored as height + time-to-apex; gravity and launch velocity are derived from them.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerMovementSettings", menuName = "Project Circle/Player Movement Settings")]
    public sealed class PlayerMovementSettings : ScriptableObject
    {
        [Header("Run")]
        [Tooltip("Top horizontal speed (units/sec).")]
        [SerializeField, Min(0f)] private float maxRunSpeed = 8.5f;

        [Tooltip("How fast the ball reaches top speed on the ground (units/sec²).")]
        [SerializeField, Min(0f)] private float groundAcceleration = 90f;

        [Tooltip("How fast the ball stops on the ground when input is released.")]
        [SerializeField, Min(0f)] private float groundDeceleration = 110f;

        [Tooltip("Acceleration used when reversing direction on the ground. Higher = snappier turns.")]
        [SerializeField, Min(0f)] private float groundTurnAcceleration = 160f;

        [SerializeField, Min(0f)] private float airAcceleration = 70f;
        [SerializeField, Min(0f)] private float airDeceleration = 50f;
        [SerializeField, Min(0f)] private float airTurnAcceleration = 110f;

        [Tooltip("Stick values below this are treated as zero.")]
        [SerializeField, Range(0f, 0.9f)] private float inputDeadZone = 0.2f;

        [Header("Jump")]
        [Tooltip("Peak height of a full (held) jump, in world units.")]
        [SerializeField, Min(0.1f)] private float jumpHeight = 4.6f;

        [Tooltip("Seconds from take-off to the peak of a full jump.")]
        [SerializeField, Min(0.05f)] private float timeToApex = 0.4f;

        [Tooltip("Grace period after walking off a ledge during which a jump is still allowed.")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.1f;

        [Tooltip("A jump pressed this long before landing will fire on touchdown.")]
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;

        [Tooltip("Gravity multiplier while rising after jump was released early (variable jump height).")]
        [SerializeField, Min(1f)] private float jumpCutGravityMultiplier = 3f;

        [Header("Air Jump (Cape Platform)")]
        [Tooltip("Jumps allowed in the air before touching the ground again. The cape flattens into a platform under the circle for each one.")]
        [SerializeField, Min(0)] private int airJumps = 1;

        [Tooltip("Peak height of a full (held) air jump, in world units.")]
        [SerializeField, Min(0.1f)] private float airJumpHeight = 3.2f;

        [Header("Apex Hang")]
        [Tooltip("Vertical speed below which the ball is considered 'at the apex' of a held jump.")]
        [SerializeField, Min(0f)] private float apexThreshold = 1.6f;

        [Tooltip("Gravity multiplier at the apex. < 1 gives a brief floaty hang for precise aiming.")]
        [SerializeField, Range(0.1f, 1f)] private float apexGravityMultiplier = 0.5f;

        [Tooltip("Horizontal acceleration multiplier at the apex, for extra air control.")]
        [SerializeField, Min(1f)] private float apexAccelerationMultiplier = 1.3f;

        [Header("Fall")]
        [Tooltip("Gravity multiplier while falling. > 1 gives weighty, non-floaty falls.")]
        [SerializeField, Min(1f)] private float fallGravityMultiplier = 1.8f;

        [Tooltip("Terminal fall speed (units/sec).")]
        [SerializeField, Min(0f)] private float maxFallSpeed = 18f;

        [Tooltip("Holding Down in the air makes the circle fall faster. Off by default: Down is also the aim for the downward (pogo) attack, and falling faster makes pogos much harder to time.")]
        [SerializeField] private bool fastFallEnabled = false;

        [Tooltip("Extra gravity multiplier while holding Down in the air.")]
        [SerializeField, Min(1f)] private float fastFallGravityMultiplier = 1.6f;

        [SerializeField, Min(0f)] private float fastFallMaxSpeed = 26f;

        [Header("Dash")]
        [SerializeField, Min(0f)] private float dashSpeed = 22f;
        [SerializeField, Min(0.01f)] private float dashDuration = 0.16f;

        [Tooltip("Measured from dash start. Should be longer than Dash Duration.")]
        [SerializeField, Min(0f)] private float dashCooldown = 0.45f;

        [Tooltip("A dash pressed this long before it becomes available will still fire.")]
        [SerializeField, Min(0f)] private float dashBufferTime = 0.1f;

        public float MaxRunSpeed => maxRunSpeed;
        public float GroundAcceleration => groundAcceleration;
        public float GroundDeceleration => groundDeceleration;
        public float GroundTurnAcceleration => groundTurnAcceleration;
        public float AirAcceleration => airAcceleration;
        public float AirDeceleration => airDeceleration;
        public float AirTurnAcceleration => airTurnAcceleration;
        public float InputDeadZone => inputDeadZone;

        public float CoyoteTime => coyoteTime;
        public float JumpBufferTime => jumpBufferTime;
        public float JumpCutGravityMultiplier => jumpCutGravityMultiplier;

        public int AirJumps => airJumps;

        /// <summary>Upward launch speed that reaches <c>airJumpHeight</c> under the normal jump gravity.</summary>
        public float AirJumpVelocity => Mathf.Sqrt(2f * JumpGravity * airJumpHeight);

        public float ApexThreshold => apexThreshold;
        public float ApexGravityMultiplier => apexGravityMultiplier;
        public float ApexAccelerationMultiplier => apexAccelerationMultiplier;

        public float FallGravityMultiplier => fallGravityMultiplier;
        public float MaxFallSpeed => maxFallSpeed;
        public bool FastFallEnabled => fastFallEnabled;
        public float FastFallGravityMultiplier => fastFallGravityMultiplier;
        public float FastFallMaxSpeed => fastFallMaxSpeed;

        public float DashSpeed => dashSpeed;
        public float DashDuration => dashDuration;
        public float DashCooldown => dashCooldown;
        public float DashBufferTime => dashBufferTime;

        /// <summary>Downward acceleration (positive number) that produces the authored jump arc.</summary>
        public float JumpGravity => 2f * jumpHeight / (timeToApex * timeToApex);

        /// <summary>Upward launch speed needed to reach <c>jumpHeight</c> in <c>timeToApex</c>.</summary>
        public float JumpVelocity => 2f * jumpHeight / timeToApex;

        /// <summary>
        /// Rigidbody2D.gravityScale that converts the project's Physics2D gravity into <see cref="JumpGravity"/>.
        /// </summary>
        public float BaseGravityScale
        {
            get
            {
                float worldGravity = Mathf.Abs(Physics2D.gravity.y);
                return worldGravity > Mathf.Epsilon ? JumpGravity / worldGravity : 1f;
            }
        }
    }
}
