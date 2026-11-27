using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Horizontal limits for the follow camera. At a room's exit opening the camera holds still while the circle
    /// walks on into the gap and off-screen, where the transition trigger waits, so the next room is never shown.
    ///
    /// Most rooms need nothing here: every <see cref="SceneDoor"/> set as a room edge (its <c>Camera Stop</c> field)
    /// stops the camera at its own line, and moves with the door. This component is only for extra limits on a side
    /// with no opening (e.g. a dead end you don't want the camera to look past). One per scene, usually on the Region
    /// object. For shafts and holes (vertical exits) see <see cref="CameraLimitZone"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraBounds2D : MonoBehaviour
    {
        [Tooltip("Extra limit on the left side (in addition to any room-edge doors).")]
        [SerializeField] private bool limitLeft;

        [Tooltip("World x the left edge of the view never goes past.")]
        [SerializeField] private float minX = -12f;

        [Tooltip("Extra limit on the right side (in addition to any room-edge doors).")]
        [SerializeField] private bool limitRight;

        [Tooltip("World x the right edge of the view never goes past.")]
        [SerializeField] private float maxX = 100f;

        private static CameraBounds2D instance;

        private void OnEnable() => instance = this;
        private void OnDisable() { if (instance == this) instance = null; }

        /// <summary>
        /// The scene's horizontal camera limits: the outermost room-edge door on each side, combined with this
        /// component's extra limits if there is one. Infinite on a side with neither.
        /// </summary>
        public static void GetLimits(out float min, out float max)
        {
            min = float.NegativeInfinity;
            max = float.PositiveInfinity;
            bool hasLeft = false, hasRight = false;

            foreach (SceneDoor door in SceneDoor.Active)
            {
                if (door.CameraStop == SceneDoor.CameraStopSide.Left)
                {
                    min = hasLeft ? Mathf.Min(min, door.CameraStopX) : door.CameraStopX;
                    hasLeft = true;
                }
                else if (door.CameraStop == SceneDoor.CameraStopSide.Right)
                {
                    max = hasRight ? Mathf.Max(max, door.CameraStopX) : door.CameraStopX;
                    hasRight = true;
                }
            }

            if (instance != null)
            {
                if (instance.limitLeft) min = hasLeft ? Mathf.Min(min, instance.minX) : instance.minX;
                if (instance.limitRight) max = hasRight ? Mathf.Max(max, instance.maxX) : instance.maxX;
            }
        }

        /// <summary>
        /// Clamps a camera centre x so a view of the given half-width stays inside the scene's limits.
        /// If the room is narrower than the view, the camera centres on the room.
        /// </summary>
        public static float ClampCentreX(float x, float halfWidth)
        {
            GetLimits(out float min, out float max);
            float lo = min + halfWidth, hi = max - halfWidth;
            if (lo > hi) return (min + max) * 0.5f;
            return Mathf.Clamp(x, lo, hi);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            if (limitLeft) Gizmos.DrawLine(new Vector3(minX, -20f), new Vector3(minX, 60f));
            if (limitRight) Gizmos.DrawLine(new Vector3(maxX, -20f), new Vector3(maxX, 60f));
        }
#endif
    }
}
