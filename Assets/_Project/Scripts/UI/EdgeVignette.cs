using UnityEngine;
using UnityEngine.UI;

namespace ProjectCircle.UI
{
    /// <summary>
    /// A screen-edge glow with no texture: a frame whose outer edge is fully coloured and whose inner edge is
    /// transparent, so the colour fades smoothly in from the border. Its alpha comes from <see cref="Graphic.color"/>.
    /// Stretch it over the whole screen.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EdgeVignette : MaskableGraphic
    {
        [Tooltip("How far the glow reaches in from the edges, in canvas units.")]
        [SerializeField, Min(1f)] private float glowDepth = 140f;

        public float Depth
        {
            get => glowDepth;
            set { glowDepth = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float d = Mathf.Min(glowDepth, r.width * 0.5f, r.height * 0.5f);
            Color outer = color;
            Color inner = new Color(color.r, color.g, color.b, 0f);

            // 0-3: outer corners (BL, TL, TR, BR); 4-7: inner corners, inset by the depth.
            vh.AddVert(new Vector3(r.xMin, r.yMin), outer, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), outer, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), outer, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), outer, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + d, r.yMin + d), inner, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + d, r.yMax - d), inner, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax - d, r.yMax - d), inner, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax - d, r.yMin + d), inner, Vector2.zero);

            // Four trapezoids (left, top, right, bottom), two triangles each.
            for (int i = 0; i < 4; i++)
            {
                int o0 = i, o1 = (i + 1) % 4, i0 = i + 4, i1 = (i + 1) % 4 + 4;
                vh.AddTriangle(o0, o1, i1);
                vh.AddTriangle(o0, i1, i0);
            }
        }
    }
}
