using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A soft glow drawn as a procedural mesh: a bright core fading out to nothing, plus an optional
    /// expanding ring ("burst") for the moment power is released. Used to show a shape drawing on the
    /// power of one of its vertices.
    ///
    /// Set <see cref="Intensity"/> every frame (0 = invisible, 1 = full, above 1 = overcharged and whiter)
    /// and call <see cref="Burst"/> when the power is released. Place this object on the vertex.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class VertexGlow : MonoBehaviour
    {
        [SerializeField] private Color color = new Color(1f, 0.86f, 0.45f, 1f);

        [Tooltip("Radius of the glow at full intensity.")]
        [SerializeField, Min(0.01f)] private float radius = 0.55f;

        [SerializeField, Range(8, 48)] private int segments = 28;

        [SerializeField] private int sortingOrder = 8;

        [Header("Burst")]
        [Tooltip("How far the release ring expands.")]
        [SerializeField, Min(0.01f)] private float burstRadius = 1.2f;

        [SerializeField, Min(0.01f)] private float burstTime = 0.32f;

        [SerializeField, Min(0.005f)] private float burstThickness = 0.09f;

        private Mesh mesh;
        private float intensity;
        private float burstStartTime = float.NegativeInfinity;
        private bool meshIsEmpty = true;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        /// <summary>0 = off, 1 = full glow, above 1 = overcharged (bigger and whiter).</summary>
        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Max(0f, value);
        }

        public Color Color
        {
            get => color;
            set => color = value;
        }

        /// <summary>Radius of the glow at full intensity.</summary>
        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0.01f, value);
        }

        /// <summary>How far the release ring expands.</summary>
        public float BurstRadius
        {
            get => burstRadius;
            set => burstRadius = Mathf.Max(0.01f, value);
        }

        /// <summary>Fires an expanding ring from the glow, for the moment the power is released.</summary>
        public void Burst() => burstStartTime = Time.time;

        private void Awake()
        {
            mesh = new Mesh { name = "Vertex Glow", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private void LateUpdate()
        {
            float burst = (Time.time - burstStartTime) / burstTime;
            bool burstActive = burst >= 0f && burst < 1f;

            if (intensity <= 0.001f && !burstActive)
            {
                if (!meshIsEmpty)
                {
                    mesh.Clear();
                    meshIsEmpty = true;
                }
                return;
            }

            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            if (intensity > 0.001f) AddHalo();
            if (burstActive) AddBurstRing(burst);

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshIsEmpty = false;
        }

        /// <summary>Soft disc: white-hot core → glow colour → transparent edge.</summary>
        private void AddHalo()
        {
            float level = Mathf.Min(intensity, 1f);
            float over = Mathf.Clamp01(intensity - 1f);
            float outer = radius * (0.65f + 0.35f * level + 0.3f * over);

            Color core = Color.Lerp(color, Color.white, 0.55f + 0.45f * over);
            core.a = level;
            Color mid = color;
            mid.a = 0.55f * level;
            Color edge = color;
            edge.a = 0f;

            AddRings(new[] { 0f, outer * 0.28f, outer }, new[] { core, mid, edge });
        }

        /// <summary>A thin ring that grows and fades.</summary>
        private void AddBurstRing(float t)
        {
            float eased = 1f - (1f - t) * (1f - t);
            float r = Mathf.Lerp(radius * 0.3f, burstRadius, eased);
            float w = burstThickness * (1f - 0.5f * t);

            Color ring = Color.Lerp(Color.white, color, 0.4f);
            ring.a = 0.9f * (1f - t);
            Color clear = ring;
            clear.a = 0f;

            AddRings(new[] { Mathf.Max(0f, r - w), r, r + w }, new[] { clear, ring, clear });
        }

        /// <summary>Adds concentric rings joined into bands. A first radius of 0 makes a filled centre.</summary>
        private void AddRings(float[] radii, Color[] ringColors)
        {
            int first = vertices.Count;
            int count = segments;

            for (int ring = 0; ring < radii.Length; ring++)
            {
                Color c = ToRenderSpace(ringColors[ring]);
                for (int s = 0; s < count; s++)
                {
                    float angle = s * Mathf.PI * 2f / count;
                    vertices.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radii[ring]);
                    colors.Add(c);
                }
            }

            for (int ring = 0; ring < radii.Length - 1; ring++)
            {
                int inner = first + ring * count;
                int outerRing = inner + count;
                for (int s = 0; s < count; s++)
                {
                    int next = (s + 1) % count;
                    triangles.Add(inner + s); triangles.Add(outerRing + s); triangles.Add(outerRing + next);
                    triangles.Add(inner + s); triangles.Add(outerRing + next); triangles.Add(inner + next);
                }
            }
        }

        /// <summary>Mesh vertex colours are not converted to the project's colour space automatically.</summary>
        private static Color ToRenderSpace(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            Color linear = c.linear;
            linear.a = c.a;
            return linear;
        }
    }
}
