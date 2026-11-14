using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Draws an equilateral triangle with rounded corners and an outline, as a procedural mesh.
    /// The triangle is centred on this transform's origin (its centroid), pointing up at rest.
    ///
    /// Runs in Edit Mode too, so the shape shows in the Scene view and in prefabs. The mesh is rebuilt
    /// from the settings and never saved as an asset.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RoundedTriangleMesh : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Distance from the centre to each (sharp) corner. Side length = this × 1.732.")]
        [SerializeField, Min(0.05f)] private float circumradius = 0.8f;

        [Tooltip("Radius of the rounded corners.")]
        [SerializeField, Min(0f)] private float cornerRadius = 0.13f;

        [Tooltip("Smoothness of each rounded corner.")]
        [SerializeField, Range(1, 16)] private int segmentsPerCorner = 6;

        [Tooltip("Thickness of the outline drawn around the shape. 0 = no outline.")]
        [SerializeField, Min(0f)] private float outlineWidth = 0.06f;

        [Tooltip("A corner that has snapped off, leaving a jagged edge (0 = top, 1 = bottom-left, 2 = bottom-right, -1 = none). Used for the Old Triangle.")]
        [SerializeField, Range(-1, 2)] private int brokenCorner = -1;

        [Tooltip("How much of the broken corner is missing, as a fraction of the centre-to-corner distance.")]
        [SerializeField, Range(0.1f, 0.7f)] private float breakDepth = 0.35f;

        [Header("Colours")]
        [SerializeField] private Color fillColor = new Color(0.8f, 0.5f, 0.46f, 1f);

        [Tooltip("Colour of the fill at the bottom. A slight gradient gives the flat shape some volume.")]
        [SerializeField] private Color fillBottomColor = new Color(0.62f, 0.36f, 0.36f, 1f);

        [SerializeField] private Color outlineColor = new Color(0.16f, 0.08f, 0.1f, 1f);

        [Header("Rendering")]
        [SerializeField] private int sortingOrder = 5;

        private Mesh mesh;
        private bool needsRebuild;

        // Corner glow (vertex power): which corner, how strongly, and in what colour.
        private int glowCorner = -1;
        private float glowAmount;
        private Color glowColor = Color.white;
        private readonly List<Vector2> outline = new List<Vector2>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        /// <summary>Distance from the centre to each sharp corner.</summary>
        public float Circumradius => circumradius;

        /// <summary>Radius of the rounded corners.</summary>
        public float CornerRadius => Mathf.Min(cornerRadius, circumradius * 0.45f);

        /// <summary>Distance from the centre to the middle of each side (the height of the centre above the ground at rest).</summary>
        public float Inradius => circumradius * 0.5f;

        /// <summary>Corner smoothness, for building a matching collider.</summary>
        public int SegmentsPerCorner => segmentsPerCorner;

        public Color FillColor => fillColor;

        #region Unity lifecycle

        private void OnEnable() => Rebuild();

        private void OnValidate()
        {
            // Mesh changes are not allowed during OnValidate itself, so defer them a moment.
            if (isActiveAndEnabled) needsRebuild = true;
        }

        private void Update()
        {
            if (needsRebuild)
            {
                needsRebuild = false;
                Rebuild();
            }
        }

        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        #endregion

        #region Public API

        /// <summary>Sets up the shape from code (used for debris pieces created at runtime).</summary>
        public void Configure(float newCircumradius, float newCornerRadius, float newOutlineWidth,
                              Color top, Color bottom, Color outline, int newSortingOrder)
        {
            circumradius = newCircumradius;
            cornerRadius = newCornerRadius;
            outlineWidth = newOutlineWidth;
            fillColor = top;
            fillBottomColor = bottom;
            outlineColor = outline;
            sortingOrder = newSortingOrder;
            segmentsPerCorner = 3;
            Rebuild();
        }

        /// <summary>Changes the fill colours at runtime (e.g. to show the enemy is alerted).</summary>
        public void SetFill(Color top, Color bottom)
        {
            fillColor = top;
            fillBottomColor = bottom;
            Rebuild();
        }

        /// <summary>
        /// Lights up one corner of the shape (0 = top, 1 = bottom-left, 2 = bottom-right, at rest).
        /// The fill and outline near that corner blend towards <paramref name="color"/>.
        /// Pass a corner of -1 or an amount of 0 to turn it off.
        /// </summary>
        public void SetCornerGlow(int corner, float amount, Color color)
        {
            amount = corner < 0 ? 0f : Mathf.Clamp01(amount);
            bool changed = corner != glowCorner || Mathf.Abs(amount - glowAmount) > 0.01f || color != glowColor;
            if (!changed) return;

            glowCorner = amount > 0f ? corner : -1;
            glowAmount = amount;
            glowColor = color;
            Rebuild();
        }

        /// <summary>
        /// Local position of a corner (0 = top, 1 = bottom-left, 2 = bottom-right, at rest).
        /// <paramref name="inset"/> moves the point in towards the centre, e.g. onto the rounded tip.
        /// </summary>
        public Vector2 GetCornerPoint(int corner, float inset = 0f) =>
            Direction(90f + corner * 120f) * (circumradius - inset);

        /// <summary>
        /// Fills <paramref name="points"/> with the outline of a rounded equilateral triangle centred on the origin,
        /// pointing up, counter-clockwise. <paramref name="grow"/> pushes the outline outward (for outlines).
        /// </summary>
        /// <param name="brokenCorner">A corner drawn snapped off with a jagged edge instead of rounded (-1 = none).</param>
        /// <param name="breakDepth">How much of the broken corner is missing, as a fraction of the circumradius.</param>
        public static void BuildOutline(List<Vector2> points, float circumradius, float cornerRadius, int segmentsPerCorner,
                                        float grow = 0f, int brokenCorner = -1, float breakDepth = 0.35f)
        {
            points.Clear();
            float r = Mathf.Min(cornerRadius, circumradius * 0.45f);

            // Each rounded corner is an arc around a centre pulled in from the sharp corner.
            // For a 60° corner that centre sits 2r from the sharp corner along the line to the middle.
            float arcCentreDistance = circumradius - 2f * r;
            float arcRadius = r + grow;
            int steps = Mathf.Max(1, segmentsPerCorner);

            for (int corner = 0; corner < 3; corner++)
            {
                float cornerAngle = 90f + corner * 120f; // top, bottom-left, bottom-right
                if (corner == brokenCorner)
                {
                    AddBrokenCorner(points, circumradius, cornerAngle, breakDepth, grow);
                    continue;
                }

                Vector2 centre = Direction(cornerAngle) * arcCentreDistance;

                // The arc turns through 120° (the outside angle of the corner).
                for (int s = 0; s <= steps; s++)
                {
                    float angle = cornerAngle - 60f + 120f * s / steps;
                    points.Add(centre + Direction(angle) * arcRadius);
                }
            }
        }

        /// <summary>
        /// Replaces a corner's rounded tip with a jagged break: the tip is cut off along both edges and the
        /// cut zig-zags slightly, like a snapped edge. Points run counter-clockwise like the rest of the outline.
        /// </summary>
        private static void AddBrokenCorner(List<Vector2> points, float circumradius, float cornerAngle, float breakDepth, float grow)
        {
            Vector2 tip = Direction(cornerAngle) * circumradius;
            Vector2 previousCorner = Direction(cornerAngle - 120f) * circumradius;
            Vector2 nextCorner = Direction(cornerAngle + 120f) * circumradius;
            float depth = breakDepth * circumradius;

            Vector2 start = tip + (previousCorner - tip).normalized * depth; // where the break starts on the incoming edge
            Vector2 end = tip + (nextCorner - tip).normalized * depth;       // and ends on the outgoing edge
            Vector2 inward = -tip.normalized;

            // A shallow zig-zag across the break (offsets are towards the centre, as fractions of the depth).
            float[] along = { 0f, 0.28f, 0.5f, 0.74f, 1f };
            float[] bite = { 0f, 0.22f, 0.04f, 0.18f, 0f };
            for (int i = 0; i < along.Length; i++)
            {
                Vector2 p = Vector2.Lerp(start, end, along[i]) + inward * (bite[i] * depth);
                if (grow > 0f) p += p.normalized * grow; // outline: push out by its width
                points.Add(p);
            }
        }

        #endregion

        #region Mesh building

        private void Rebuild()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Rounded Triangle", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }

            // Always re-assign: a duplicated object starts out pointing at the original's mesh.
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            // Outline first (drawn underneath), then the fill on top. Both are fans around the centre.
            if (outlineWidth > 0f)
            {
                BuildOutline(outline, circumradius, cornerRadius, segmentsPerCorner, outlineWidth, brokenCorner, breakDepth);
                AddFan(outline, p => ToRenderSpace(Color.Lerp(outlineColor, glowColor, GlowWeight(p) * 0.9f)));
            }

            BuildOutline(outline, circumradius, cornerRadius, segmentsPerCorner, 0f, brokenCorner, breakDepth);
            float top = circumradius;
            float bottom = -Inradius;
            AddFan(outline, p =>
            {
                Color fill = Color.Lerp(fillBottomColor, fillColor, Mathf.InverseLerp(bottom, top, p.y));
                return ToRenderSpace(Color.Lerp(fill, glowColor, GlowWeight(p)));
            });

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Adds a triangle fan from the origin to the given closed outline.</summary>
        private void AddFan(List<Vector2> points, System.Func<Vector2, Color> colorAt)
        {
            int centre = vertices.Count;
            vertices.Add(Vector3.zero);
            colors.Add(colorAt(Vector2.zero));

            for (int i = 0; i < points.Count; i++)
            {
                vertices.Add(points[i]);
                colors.Add(colorAt(points[i]));
            }

            for (int i = 0; i < points.Count; i++)
            {
                int a = centre + 1 + i;
                int b = centre + 1 + (i + 1) % points.Count;
                triangles.Add(centre);
                triangles.Add(b);
                triangles.Add(a);
            }
        }

        /// <summary>How strongly the corner glow affects a point: full at the corner, fading out over most of a side.</summary>
        private float GlowWeight(Vector2 point)
        {
            if (glowCorner < 0 || glowAmount <= 0f) return 0f;
            float distance = Vector2.Distance(point, GetCornerPoint(glowCorner));
            float falloff = 1f - Mathf.Clamp01(distance / (circumradius * 0.9f));
            return glowAmount * falloff * falloff;
        }

        /// <summary>
        /// Unity converts SpriteRenderer colours to the project's colour space, but not raw mesh vertex
        /// colours. Converting here makes the Inspector colours match what a sprite would show.
        /// </summary>
        private static Color ToRenderSpace(Color color) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        #endregion
    }
}
