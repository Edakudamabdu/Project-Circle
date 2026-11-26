using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Horizontal limits for the follow camera in this scene. The camera stops so its view never shows past
    /// <see cref="MinX"/> / <see cref="MaxX"/>: at a room's exit opening the camera holds still while the circle
    /// walks on into the gap and off-screen, where the transition trigger waits — so the next room is never exposed.
    /// For shafts and holes (vertical exits) see <see cref="CameraLimitZone"/>.
    /// One per scene (usually on the Region object). Drag the limits in the Inspector; the yellow lines show them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraBounds2D : MonoBehaviour
    {
        [Tooltip("Clamp the left side of the view.")]
        [SerializeField] private bool limitLeft;

        [Tooltip("World x the left edge of the view never goes past (usually the outer face of the wall above a west opening).")]
        [SerializeField] private float minX = -12f;

        [Tooltip("Clamp the right side of the view.")]
        [SerializeField] private bool limitRight;

        [Tooltip("World x the right edge of the view never goes past.")]
        [SerializeField] private float maxX = 100f;

        /// <summary>Left limit, or negative infinity when the left side is free.</summary>
        public float MinX => limitLeft ? minX : float.NegativeInfinity;

        /// <summary>Right limit, or positive infinity when the right side is free.</summary>
        public float MaxX => limitRight ? maxX : float.PositiveInfinity;

        /// <summary>Sets (or widens) the left limit. Used by the level builder for each west opening.</summary>
        public void AddLeftLimit(float x)
        {
            minX = limitLeft ? Mathf.Min(minX, x) : x;
            limitLeft = true;
        }

        /// <summary>Sets (or widens) the right limit. Used by the level builder for each east opening.</summary>
        public void AddRightLimit(float x)
        {
            maxX = limitRight ? Mathf.Max(maxX, x) : x;
            limitRight = true;
        }

        /// <summary>
        /// Clamps a camera centre x so a view of the given half-width stays inside the limits.
        /// If the room is narrower than the view, the camera centres on the room.
        /// </summary>
        public float ClampCentreX(float x, float halfWidth)
        {
            float lo = MinX + halfWidth, hi = MaxX - halfWidth;
            if (lo > hi) return (MinX + MaxX) * 0.5f;
            return Mathf.Clamp(x, lo, hi);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            if (limitLeft) Gizmos.DrawLine(new Vector3(minX, -20f), new Vector3(minX, 60f));
            if (limitRight) Gizmos.DrawLine(new Vector3(maxX, -20f), new Vector3(maxX, 60f));        }
#endif
    }
}
