using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Purely cosmetic feedback for the circle: rolls the sprite to match horizontal speed (paused during
    /// the quickstep dash) and applies a subtle squash &amp; stretch on jump, landing and dash. Physics rotation stays frozen on the Rigidbody2D;
    /// only child transforms are animated here.
    /// Expected hierarchy:  Player (Rigidbody2D) → Visual (squash pivot) → Body (roll pivot, SpriteRenderer).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class BallVisuals : MonoBehaviour
    {
        [Header("Pivots")]
        [Tooltip("Scaled for squash & stretch. Never rotated, so squash stays aligned to world up.")]
        [SerializeField] private Transform squashPivot;

        [Tooltip("Rotated to fake rolling. Child of the squash pivot.")]
        [SerializeField] private Transform rollPivot;

        [Header("Rolling")]
        [Tooltip("1 = physically correct roll for the collider radius.")]
        [SerializeField, Min(0f)] private float rollMultiplier = 1f;

        [Header("Squash & Stretch")]
        [SerializeField] private Vector2 jumpStretch = new Vector2(0.93f, 1.07f);
        [SerializeField] private Vector2 dashStretch = new Vector2(1.1f, 0.92f);

        [Tooltip("Maximum squash amount on a hard landing (0.1 = 10% flatter).")]
        [SerializeField, Range(0f, 0.6f)] private float maxLandSquash = 0.1f;

        [Tooltip("Landings slower than this produce no squash.")]
        [SerializeField, Min(0f)] private float minLandSpeed = 3f;

        [Tooltip("Landing speed that produces the maximum squash.")]
        [SerializeField, Min(0.1f)] private float maxLandSpeed = 22f;

        [Tooltip("How quickly the shape returns to round. Higher = snappier.")]
        [SerializeField, Min(0.1f)] private float recoverSpeed = 14f;

        [Header("Idle Breathing")]
        [Tooltip("How much the circle grows and shrinks while idle (0.04 = 4%). 0 = off.")]
        [SerializeField, Range(0f, 0.2f)] private float breathAmount = 0.04f;

        [Tooltip("Breaths per second.")]
        [SerializeField, Min(0f)] private float breathsPerSecond = 0.45f;

        [Tooltip("How quickly breathing fades in after stopping and out when moving again.")]
        [SerializeField, Min(0.1f)] private float breathFadeSpeed = 3f;

        private PlayerMovement movement;
        private float breathWeight;   // 0 = not breathing, 1 = full breathing
        private float breathTime;
        private Vector3 squashBasePosition;
        private float rollRadius;
        private Vector3 squashBaseScale;
        private Vector2 currentScale = Vector2.one;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();

            var circle = GetComponent<CircleCollider2D>();
            float worldScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            rollRadius = Mathf.Max(0.01f, circle != null ? circle.radius * worldScale : 0.5f);

            if (squashPivot != null)
            {
                squashBaseScale = squashPivot.localScale;
                squashBasePosition = squashPivot.localPosition;
            }
        }

        private void OnEnable()
        {
            movement.Jumped += HandleJumped;
            movement.Landed += HandleLanded;
        }

        private void OnDisable()
        {
            movement.Jumped -= HandleJumped;
            movement.Landed -= HandleLanded;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Roll: angular speed (rad/s) = linear speed / radius. Negative so moving right spins clockwise.
            // The dash is a quickstep (a slide), so the circle does not spin during it.
            if (rollPivot != null && !movement.IsDashing)
            {
                float degrees = -movement.Velocity.x / rollRadius * Mathf.Rad2Deg * rollMultiplier * dt;
                rollPivot.Rotate(0f, 0f, degrees);
            }

            if (squashPivot == null)
            {
                return;
            }

            // Hold the dash stretch while dashing; otherwise ease back to a perfect circle.
            Vector2 target = movement.IsDashing ? dashStretch : Vector2.one;
            float t = 1f - Mathf.Exp(-recoverSpeed * dt); // frame-rate independent smoothing
            currentScale = Vector2.Lerp(currentScale, target, t);

            // Idle breathing: a slow, even grow-and-shrink that fades in only while the player is Idle.
            bool idle = movement.State == PlayerState.Idle;
            breathWeight = Mathf.MoveTowards(breathWeight, idle ? 1f : 0f, breathFadeSpeed * dt);
            breathTime = breathWeight > 0f ? breathTime + dt : 0f; // restart the cycle each time we stop
            float breath = 1f + Mathf.Sin(breathTime * breathsPerSecond * Mathf.PI * 2f) * breathAmount * breathWeight;

            squashPivot.localScale = new Vector3(
                squashBaseScale.x * currentScale.x * breath,
                squashBaseScale.y * currentScale.y * breath,
                squashBaseScale.z);

            // Lift the pivot as it grows so the bottom of the circle stays on the ground while breathing.
            squashPivot.localPosition = squashBasePosition + Vector3.up * ((breath - 1f) * rollRadius);
        }

        private void HandleJumped() => currentScale = jumpStretch;

        private void HandleLanded(float impactSpeed)
        {
            if (impactSpeed < minLandSpeed)
            {
                return;
            }

            float strength = Mathf.InverseLerp(minLandSpeed, maxLandSpeed, impactSpeed);
            float squash = maxLandSquash * strength;
            currentScale = new Vector2(1f + squash, 1f - squash);
        }
    }
}
