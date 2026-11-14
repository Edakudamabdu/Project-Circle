using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Smooth 2D follow camera tuned for platforming:
    /// - Horizontal look-ahead in the direction of travel (keeps the last direction when stopping).
    /// - Vertical dead zone so small hops don't bob the camera; recentres once the target settles.
    /// - Extra look-down during long, fast falls so the player can see where they'll land.
    /// Runs in LateUpdate. The target's Rigidbody2D should use Interpolate to avoid jitter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Tooltip("Optional. Auto-found on the target if left empty. Used for look-ahead and fall detection.")]
        [SerializeField] private Rigidbody2D targetBody;

        [Tooltip("World-space offset from the target (e.g. raise the camera a little above the player).")]
        [SerializeField] private Vector2 offset = new Vector2(0f, 1.5f);

        [Header("Smoothing")]
        [SerializeField, Min(0f)] private float horizontalSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float verticalSmoothTime = 0.22f;

        [Header("Look Ahead")]
        [SerializeField, Min(0f)] private float lookAheadDistance = 2.2f;
        [SerializeField, Min(0f)] private float lookAheadSmoothTime = 0.45f;

        [Tooltip("Horizontal speed needed before the look-ahead direction changes.")]
        [SerializeField, Min(0f)] private float lookAheadMinSpeed = 1.5f;

        [Header("Vertical")]
        [Tooltip("Half-height of the vertical dead zone around the camera focus.")]
        [SerializeField, Min(0f)] private float verticalDeadZone = 1.2f;

        [Tooltip("Vertical speed below which the target counts as 'settled' and the camera recentres.")]
        [SerializeField, Min(0f)] private float settleSpeed = 0.1f;

        [Tooltip("How long the target must stay settled before recentring (filters out the zero-speed instant at a jump's apex).")]
        [SerializeField, Min(0f)] private float settleDelay = 0.08f;

        [Header("Fall Look-Down")]
        [Tooltip("Downward speed that triggers the look-down offset.")]
        [SerializeField, Min(0f)] private float fallLookDownSpeed = 14f;

        [SerializeField, Min(0f)] private float fallLookDownDistance = 2.5f;
        [SerializeField, Min(0f)] private float fallLookDownSmoothTime = 0.3f;

        private float focusY;
        private float lookAheadDirection = 1f;
        private float currentLookAhead;
        private float currentFallOffset;
        private float settledTime;

        private float velocityX;
        private float velocityY;
        private float lookAheadVelocity;
        private float fallOffsetVelocity;

        // Where the camera would be without shake. Shake is added on top, so it never drifts the follow.
        private Vector3 followPosition;

        // Focus override (FocusOn / ClearFocus) and zoom.
        private Camera cam;
        private bool focusActive;
        private Vector2 focusPoint;
        private float focusSmoothTime = 0.35f;
        private float defaultOrthographicSize;
        private float zoomTarget;
        private float zoomVelocity;

        // Screen shake (runs on real time, so it keeps going during a hit-stop freeze).
        private float shakeStrength;
        private float shakeDuration;
        private float shakeStartTime = float.NegativeInfinity;

        /// <summary>
        /// Shakes the camera. <paramref name="strength"/> is the maximum offset in world units; it fades out
        /// over <paramref name="duration"/> seconds of real time. A stronger shake replaces a weaker one.
        /// </summary>
        public void Shake(float strength, float duration)
        {
            float remaining = CurrentShakeStrength();
            if (strength < remaining) return;

            shakeStrength = strength;
            shakeDuration = Mathf.Max(0.01f, duration);
            shakeStartTime = Time.unscaledTime;
        }

        private float CurrentShakeStrength()
        {
            float t = (Time.unscaledTime - shakeStartTime) / Mathf.Max(shakeDuration, 0.01f);
            if (t >= 1f) return 0f;
            return shakeStrength * (1f - t) * (1f - t);
        }

        /// <summary>
        /// Temporarily glides the camera onto a fixed point and zooms to <paramref name="orthographicSize"/>
        /// (e.g. framing a Mirror). Call <see cref="ClearFocus"/> to return to following the target.
        /// </summary>
        public void FocusOn(Vector2 point, float orthographicSize, float smoothTime = 0.35f)
        {
            focusActive = true;
            focusPoint = point;
            focusSmoothTime = Mathf.Max(0.01f, smoothTime);
            zoomTarget = orthographicSize;
        }

        /// <summary>Ends <see cref="FocusOn"/>: the camera glides back to the target and its normal zoom.</summary>
        public void ClearFocus()
        {
            if (!focusActive) return;
            focusActive = false;
            zoomTarget = defaultOrthographicSize;
            // Pick up the follow from where the camera is now, so it glides back instead of jumping.
            if (target != null) focusY = target.position.y;
        }

        /// <summary>Sets a new target and snaps the camera to it.</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            targetBody = newTarget != null ? newTarget.GetComponent<Rigidbody2D>() : null;
            SnapToTarget();
        }

        /// <summary>Instantly centres the camera on the target, clearing all smoothing state.</summary>
        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            focusY = target.position.y;
            currentLookAhead = lookAheadDirection * lookAheadDistance;
            currentFallOffset = 0f;
            velocityX = velocityY = lookAheadVelocity = fallOffsetVelocity = 0f;

            followPosition = new Vector3(
                target.position.x + currentLookAhead + offset.x,
                focusY + offset.y,
                transform.position.z);
            transform.position = followPosition;
        }

        private void Awake()
        {
            followPosition = transform.position;
            cam = GetComponent<Camera>();
            defaultOrthographicSize = cam != null ? cam.orthographicSize : 6f;
            zoomTarget = defaultOrthographicSize;

            if (target != null && targetBody == null)
            {
                targetBody = target.GetComponent<Rigidbody2D>();
            }
        }

        private void Start() => SnapToTarget();

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            float dt = Time.deltaTime;

            // Zoom eases towards its target (normal size, or the size a focus asked for).
            if (cam != null && Mathf.Abs(cam.orthographicSize - zoomTarget) > 0.001f)
            {
                cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, zoomTarget, ref zoomVelocity, focusSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            }

            if (focusActive)
            {
                // Glide onto the focus point and hold there.
                followPosition.x = Mathf.SmoothDamp(followPosition.x, focusPoint.x, ref velocityX, focusSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                followPosition.y = Mathf.SmoothDamp(followPosition.y, focusPoint.y, ref velocityY, focusSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            }
            // Follow (paused while time is frozen, e.g. during a hit-stop).
            else if (dt > 0f)
            {
                Vector2 targetPos = target.position;
                Vector2 targetVelocity = targetBody != null ? targetBody.linearVelocity : Vector2.zero;

                UpdateLookAhead(targetVelocity.x, dt);
                UpdateFocusY(targetPos.y, targetVelocity.y, dt);
                UpdateFallOffset(targetVelocity.y, dt);

                float desiredX = targetPos.x + currentLookAhead + offset.x;
                float desiredY = focusY + offset.y + currentFallOffset;

                followPosition.x = Mathf.SmoothDamp(followPosition.x, desiredX, ref velocityX, horizontalSmoothTime, Mathf.Infinity, dt);
                followPosition.y = Mathf.SmoothDamp(followPosition.y, desiredY, ref velocityY, verticalSmoothTime, Mathf.Infinity, dt);
            }

            // Shake on top of the follow position.
            float strength = CurrentShakeStrength();
            Vector2 shake = strength > 0f ? Random.insideUnitCircle * strength : Vector2.zero;
            transform.position = followPosition + (Vector3)shake;
        }

        private void UpdateLookAhead(float velocityXTarget, float dt)
        {
            if (Mathf.Abs(velocityXTarget) > lookAheadMinSpeed)
            {
                lookAheadDirection = Mathf.Sign(velocityXTarget);
            }

            currentLookAhead = Mathf.SmoothDamp(
                currentLookAhead, lookAheadDirection * lookAheadDistance,
                ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity, dt);
        }

        private void UpdateFocusY(float targetY, float velocityYTarget, float dt)
        {
            settledTime = Mathf.Abs(velocityYTarget) < settleSpeed ? settledTime + dt : 0f;

            if (settledTime >= settleDelay)
            {
                // Target has come to rest (standing on a platform): recentre on it.
                focusY = targetY;
                return;
            }

            // While airborne, only drag the focus when the target leaves the dead zone.
            if (targetY > focusY + verticalDeadZone)
            {
                focusY = targetY - verticalDeadZone;
            }
            else if (targetY < focusY - verticalDeadZone)
            {
                focusY = targetY + verticalDeadZone;
            }
        }

        private void UpdateFallOffset(float velocityYTarget, float dt)
        {
            float desired = velocityYTarget < -fallLookDownSpeed ? -fallLookDownDistance : 0f;
            currentFallOffset = Mathf.SmoothDamp(
                currentFallOffset, desired, ref fallOffsetVelocity, fallLookDownSmoothTime, Mathf.Infinity, dt);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw the vertical dead zone around the current focus.
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            float x = transform.position.x;
            float y = Application.isPlaying ? focusY : (target != null ? target.position.y : transform.position.y);
            Gizmos.DrawLine(new Vector3(x - 3f, y + verticalDeadZone), new Vector3(x + 3f, y + verticalDeadZone));
            Gizmos.DrawLine(new Vector3(x - 3f, y - verticalDeadZone), new Vector3(x + 3f, y - verticalDeadZone));
        }
#endif
    }
}
