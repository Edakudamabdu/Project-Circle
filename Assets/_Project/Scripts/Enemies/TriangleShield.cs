using System.Collections.Generic;
using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Armour along a triangle's sides: attacks that land on a side glance off (the player is parried back);
    /// only the three vertices are weak points.
    ///
    /// Shown to the player as:
    /// - a shimmering, pale coating laid over each side, stopping short of the corners, and
    /// - a warm, pulsing glow on each vertex: "hit here".
    /// The coating flashes white where a blow is deflected. Call <see cref="SetEnraged"/> to make it run
    /// hot (used when the triangle is badly hurt and starts rolling).
    ///
    /// Works with <see cref="TriangleEnemy"/>; follows its visual, so the weak points move as it tumbles.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TriangleEnemy))]
    public sealed class TriangleShield : MonoBehaviour, IHitGuard
    {
        private const int SegmentsPerSide = 16;

        [Header("Weak Points")]
        [Tooltip("How close to a vertex a blow must land to get through, as a fraction of the centre-to-corner distance.")]
        [SerializeField, Range(0.1f, 0.6f)] private float weakPointRadius = 0.32f;

        [SerializeField] private Color weakPointColor = new Color(1f, 0.55f, 0.28f, 1f);

        [Tooltip("Size of the glow on each vertex, as a fraction of the centre-to-corner distance.")]
        [SerializeField, Range(0.05f, 0.6f)] private float weakPointGlowSize = 0.26f;

        [Header("Coating")]
        [Tooltip("Vertex-colour material for the coating (e.g. ShapeUnlit). Uses the triangle's own if empty.")]
        [SerializeField] private Material coatingMaterial;

        [SerializeField] private Color coatingColor = new Color(0.62f, 0.9f, 1f, 0.75f);
        [SerializeField] private Color enragedCoatingColor = new Color(1f, 0.45f, 0.3f, 0.8f);

        [Tooltip("Thickness of the coating band.")]
        [SerializeField, Min(0.01f)] private float coatingWidth = 0.3f;

        [Tooltip("How far the band sits outside the triangle's surface.")]
        [SerializeField, Min(0f)] private float coatingOffset = 0.04f;

        [Tooltip("How fast the shimmer runs along the sides.")]
        [SerializeField, Min(0f)] private float shimmerSpeed = 1.2f;

        [SerializeField, Min(0.01f)] private float flashTime = 0.25f;

        private TriangleEnemy enemy;
        private Transform coatingObject;
        private Mesh coatingMesh;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private readonly VertexGlow[] weakGlows = new VertexGlow[3];
        private bool enraged;
        private float flashStartTime = float.NegativeInfinity;
        private Vector2 flashPoint;

        private float Circumradius => enemy.Shape != null ? enemy.Shape.Circumradius : 0.8f;

        /// <summary>Blows landing within this distance of a corner get through (a little slack for the blade's edge).</summary>
        private float WeakDistance => Circumradius * weakPointRadius + 0.15f;

        #region Unity lifecycle

        private void Awake() => enemy = GetComponent<TriangleEnemy>();

        private void Start()
        {
            if (enemy.Visual == null || enemy.Shape == null) return;

            Material material = coatingMaterial != null ? coatingMaterial : enemy.Shape.GetComponent<MeshRenderer>().sharedMaterial;
            int shapeOrder = enemy.Shape.GetComponent<MeshRenderer>().sortingOrder;

            // Coating: a band mesh on the visual, so it turns and squashes with the triangle.
            var coating = new GameObject("Shield Coating");
            coating.transform.SetParent(enemy.Visual, false);
            coating.layer = gameObject.layer;
            coatingMesh = new Mesh { name = "Shield Coating" };
            coatingMesh.MarkDynamic();
            coating.AddComponent<MeshFilter>().sharedMesh = coatingMesh;
            var coatingRenderer = coating.AddComponent<MeshRenderer>();
            coatingRenderer.sharedMaterial = material;
            coatingRenderer.sortingOrder = shapeOrder + 1;
            coatingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coatingRenderer.receiveShadows = false;
            coatingObject = coating.transform;

            // Weak-point glows on the vertices.
            for (int c = 0; c < 3; c++)
            {
                var glowObject = new GameObject("Weak Point " + c);
                glowObject.transform.SetParent(enemy.Visual, false);
                glowObject.transform.localPosition = enemy.Shape.GetCornerPoint(c, enemy.Shape.CornerRadius * 0.9f);
                glowObject.layer = gameObject.layer;
                VertexGlow glow = glowObject.AddComponent<VertexGlow>();
                glowObject.GetComponent<MeshRenderer>().sharedMaterial = material;
                glowObject.GetComponent<MeshRenderer>().sortingOrder = shapeOrder + 2;
                glow.Color = weakPointColor;
                glow.Radius = Circumradius * weakPointGlowSize;
                glow.BurstRadius = Circumradius * weakPointGlowSize * 2.5f;
                weakGlows[c] = glow;
            }
        }

        private void OnDestroy()
        {
            if (coatingMesh != null) Destroy(coatingMesh);
        }

        private void LateUpdate()
        {
            if (coatingMesh == null) return;
            BuildCoating();

            // Weak points pulse, faster when enraged.
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * (enraged ? 9f : 4f));
            for (int c = 0; c < 3; c++)
            {
                if (weakGlows[c] != null) weakGlows[c].Intensity = pulse;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Lets a blow through if it lands near a vertex; otherwise deflects it, flashing the coating there.
        /// </summary>
        public bool TryDeflect(Vector2 hitPoint, Vector2 attackerPosition)
        {
            if (enemy.Visual == null || enemy.Shape == null) return false;

            for (int c = 0; c < 3; c++)
            {
                Vector2 corner = enemy.Visual.TransformPoint(enemy.Shape.GetCornerPoint(c));
                if (Vector2.Distance(corner, hitPoint) <= WeakDistance)
                {
                    if (weakGlows[c] != null) weakGlows[c].Burst();
                    return false; // weak point: the hit goes through
                }
            }

            flashStartTime = Time.time;
            flashPoint = hitPoint;
            return true;
        }

        /// <summary>Turns the coating hot-coloured (and the weak points throb faster) while enraged.</summary>
        public void SetEnraged(bool value) => enraged = value;

        #endregion

        #region Coating mesh

        /// <summary>
        /// One band per side, laid just outside the surface and stopping short of both corners. A soft shimmer
        /// runs along it; a deflected blow flashes it white around the impact.
        /// </summary>
        private void BuildCoating()
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            float r = Circumradius;
            // Each side is left bare exactly where blows get through (the weak-point zone around each corner).
            float gap = Mathf.Clamp(WeakDistance / (r * 1.7320508f), 0.05f, 0.45f);
            Color baseColor = enraged ? enragedCoatingColor : coatingColor;
            float flash = 1f - Mathf.Clamp01((Time.time - flashStartTime) / flashTime);
            Vector2 localFlash = enemy.Visual.InverseTransformPoint(flashPoint);

            for (int side = 0; side < 3; side++)
            {
                Vector2 a = enemy.Shape.GetCornerPoint(side);
                Vector2 b = enemy.Shape.GetCornerPoint((side + 1) % 3);
                Vector2 along = b - a;
                Vector2 outward = new Vector2(along.y, -along.x).normalized;
                if (Vector2.Dot(outward, (a + b) * 0.5f) < 0f) outward = -outward;

                int first = vertices.Count;
                for (int s = 0; s <= SegmentsPerSide; s++)
                {
                    float t = Mathf.Lerp(gap, 1f - gap, (float)s / SegmentsPerSide);
                    Vector2 onSide = a + along * t;
                    Vector2 inner = onSide + outward * coatingOffset;
                    Vector2 outer = inner + outward * coatingWidth;

                    // Shimmer: a bright band travelling along the side; ends fade out towards the bare corners.
                    float shimmer = 0.65f + 0.35f * Mathf.Sin((t * 3f - Time.time * shimmerSpeed * (enraged ? 2.5f : 1f) + side) * Mathf.PI * 2f);
                    float edgeFade = Mathf.Clamp01(Mathf.Min(s, SegmentsPerSide - s) / 3f);

                    Color c = baseColor;
                    c.a *= shimmer * edgeFade;
                    float near = 1f - Mathf.Clamp01(Vector2.Distance(onSide, localFlash) / (r * 0.6f));
                    c = Color.Lerp(c, new Color(1f, 1f, 1f, 1f), flash * near);

                    vertices.Add(inner);
                    vertices.Add(outer);
                    colors.Add(ToRenderSpace(c));
                    Color outerColor = c;
                    outerColor.a *= 0.35f; // softer outer edge
                    colors.Add(ToRenderSpace(outerColor));
                }

                for (int s = 0; s < SegmentsPerSide; s++)
                {
                    int i0 = first + s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                    // Both windings, so the band shows whichever way the material culls.
                    triangles.Add(i0); triangles.Add(i2); triangles.Add(i1);
                    triangles.Add(i1); triangles.Add(i2); triangles.Add(i3);
                    triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                    triangles.Add(i1); triangles.Add(i3); triangles.Add(i2);
                }
            }

            coatingMesh.Clear();
            coatingMesh.SetVertices(vertices);
            coatingMesh.SetColors(colors);
            coatingMesh.SetTriangles(triangles, 0, false);
            coatingMesh.RecalculateBounds();
        }

        private static Color ToRenderSpace(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            Color linear = c.linear;
            linear.a = c.a;
            return linear;
        }

        #endregion
    }
}
