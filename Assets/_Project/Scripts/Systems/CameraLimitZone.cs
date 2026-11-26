using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A vertical camera limit that applies only while the player is inside an area: the vertical version of
    /// <see cref="CameraBounds2D"/>, for shafts and holes. Below a shaft exit, the view stops at the
    /// <c>top limit</c> so the circle climbs off-screen before the hatch trigger fires; above a hole in the floor,
    /// the view stops at the <c>bottom limit</c> so the circle drops off-screen. The rest of the room is unaffected
    /// (a room-wide limit would cut off jumps elsewhere).
    /// Put it on the hatch itself (the level builder does): the area and limits are relative to this object, so
    /// they move with it. The cyan box is the area; the yellow line is the limit.
    /// </summary>
    public sealed class CameraLimitZone : MonoBehaviour
    {
        private static readonly List<CameraLimitZone> active = new List<CameraLimitZone>();

        [Tooltip("Centre of the area the player must be in, relative to this object.")]
        [SerializeField] private Vector2 areaOffset = new Vector2(0f, -4f);

        [Tooltip("Size of that area.")]
        [SerializeField] private Vector2 areaSize = new Vector2(7f, 10f);

        [Tooltip("Stop the top of the view at Top Limit (a shaft you climb up through).")]
        [SerializeField] private bool limitTop;

        [Tooltip("y (relative to this object) the top edge of the view never goes above.")]
        [SerializeField] private float topLimit = -1.2f;

        [Tooltip("Stop the bottom of the view at Bottom Limit (a hole you drop down through).")]
        [SerializeField] private bool limitBottom;

        [Tooltip("y (relative to this object) the bottom edge of the view never goes below.")]
        [SerializeField] private float bottomLimit = 2.2f;

        /// <summary>Sets the zone up from code (the level builder).</summary>
        public void Configure(Vector2 offset, Vector2 size, bool top, float limit)
        {
            areaOffset = offset;
            areaSize = size;
            limitTop = top;
            limitBottom = !top;
            if (top) topLimit = limit; else bottomLimit = limit;
        }

        private void OnEnable() => active.Add(this);
        private void OnDisable() => active.Remove(this);

        private bool Contains(Vector2 point)
        {
            Vector2 centre = (Vector2)transform.position + areaOffset;
            return Mathf.Abs(point.x - centre.x) <= areaSize.x * 0.5f && Mathf.Abs(point.y - centre.y) <= areaSize.y * 0.5f;
        }

        /// <summary>
        /// Clamps a camera centre y for a view of the given half-height, using every zone the target is inside.
        /// </summary>
        public static float ClampCentreY(float y, Vector2 targetPosition, float halfHeight)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var zone = active[i];
                if (!zone.Contains(targetPosition)) continue;
                float baseY = zone.transform.position.y;
                if (zone.limitTop) y = Mathf.Min(y, baseY + zone.topLimit - halfHeight);
                if (zone.limitBottom) y = Mathf.Max(y, baseY + zone.bottomLimit + halfHeight);
            }
            return y;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 centre = transform.position + (Vector3)areaOffset;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireCube(centre, areaSize);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            float half = areaSize.x * 0.5f + 2f;
            if (limitTop) Gizmos.DrawLine(new Vector3(centre.x - half, transform.position.y + topLimit), new Vector3(centre.x + half, transform.position.y + topLimit));
            if (limitBottom) Gizmos.DrawLine(new Vector3(centre.x - half, transform.position.y + bottomLimit), new Vector3(centre.x + half, transform.position.y + bottomLimit));
        }
#endif
    }
}
