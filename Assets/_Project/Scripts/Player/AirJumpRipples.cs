using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Air-jump visuals: when the circle springs off its cape in mid-air, flattened rings ripple outward
    /// through the air from the platform, like a shockwave pushed out by the cape slapping flat.
    ///
    /// Each ripple is a thin elliptical band, sharp on its outer edge and soft on its inner edge, that grows
    /// and fades. Pooled: nothing is allocated after Awake.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class AirJumpRipples : MonoBehaviour
    {
        private const int Segments = 48;

        [Tooltip("Vertex-colour material (e.g. ShapeUnlit), the same one the dash trail uses.")]
        [SerializeField] private Material effectMaterial;

        [Header("Ripples")]
        [Tooltip("Rings per air jump. Each one starts a little after the last.")]
        [SerializeField, Range(1, 4)] private int ripplesPerJump = 2;

        [Tooltip("Delay between consecutive rings.")]
        [SerializeField, Min(0f)] private float rippleDelay = 0.07f;

        [Tooltip("How long one ring takes to grow and fade.")]
        [SerializeField, Min(0.05f)] private float rippleLifetime = 0.38f;

        [Tooltip("Half-width of a ring when it appears and when it has fully grown.")]
        [SerializeField] private Vector2 rippleRadius = new Vector2(0.35f, 1.7f);

        [Tooltip("Height of a ring as a fraction of its width. Low = flat, seen almost edge-on.")]
        [SerializeField, Range(0.1f, 1f)] private float flatness = 0.3f;

        [Tooltip("Thickness of the ring's band when it appears; it thins as it grows.")]
        [SerializeField, Min(0.01f)] private float bandWidth = 0.16f;

        [Tooltip("Each later ring is this much smaller and fainter.")]
        [SerializeField, Range(0f, 0.8f)] private float laterRingFalloff = 0.3f;

        [Tooltip("How far below the platform's top the rings are centred.")]
        [SerializeField] private float verticalOffset = -0.08f;

        [SerializeField] private Color color = new Color(0.85f, 0.92f, 1f, 0.75f);

        [Tooltip("Order in Layer: behind the circle (10) and its cape, in front of the level.")]
        [SerializeField] private int sortingOrder = 7;

        [SerializeField, Range(2, 12)] private int poolSize = 6;

        private sealed class Ripple
        {
            public GameObject Root;
            public Mesh Mesh;
            public Vector3[] Vertices;
            public Color[] Colors;
            public Vector2 Centre;
            public float Age;        // negative while waiting for its delayed start
            public float Strength;   // 1 for the first ring, less for later ones
            public bool Active;
        }

        private PlayerMovement movement;
        private readonly List<Ripple> ripples = new List<Ripple>();
        private Transform effectsRoot;

        #region Unity lifecycle

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();

            // World space, so the rings stay where the jump happened while the circle rises away.
            effectsRoot = new GameObject("Air Jump Ripples").transform;
            for (int i = 0; i < poolSize; i++) ripples.Add(CreateRipple(i));
        }

        private void OnEnable() => movement.AirJumped += HandleAirJumped;
        private void OnDisable() => movement.AirJumped -= HandleAirJumped;

        private void OnDestroy()
        {
            foreach (Ripple r in ripples)
            {
                if (r.Mesh != null) Destroy(r.Mesh);
            }

            if (effectsRoot != null) Destroy(effectsRoot.gameObject);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            foreach (Ripple r in ripples)
            {
                if (!r.Active) continue;

                r.Age += dt;
                if (r.Age >= rippleLifetime)
                {
                    r.Active = false;
                    r.Root.SetActive(false);
                    continue;
                }

                if (r.Age < 0f) continue; // still waiting to start
                if (!r.Root.activeSelf) r.Root.SetActive(true);
                DrawRipple(r);
            }
        }

        #endregion

        private void HandleAirJumped(Vector2 platformPoint)
        {
            if (effectMaterial == null) return;

            Vector2 centre = platformPoint + Vector2.up * verticalOffset;
            for (int i = 0; i < ripplesPerJump; i++)
            {
                Ripple r = TakeFree();
                r.Centre = centre;
                r.Age = -i * rippleDelay;
                r.Strength = 1f - laterRingFalloff * i;
                r.Active = true;
                r.Root.SetActive(false); // shown once its delay has passed
            }
        }

        private Ripple CreateRipple(int index)
        {
            var r = new Ripple { Root = new GameObject("Ripple " + index) };
            r.Root.transform.SetParent(effectsRoot, false);

            r.Mesh = new Mesh { name = "Air Ripple" };
            r.Mesh.MarkDynamic();
            r.Vertices = new Vector3[Segments * 2];
            r.Colors = new Color[Segments * 2];

            // A closed band: an outer and an inner ring of vertices, joined by two triangles per segment.
            var triangles = new int[Segments * 6];
            for (int s = 0; s < Segments; s++)
            {
                int outer = s * 2, inner = outer + 1;
                int nextOuter = (s + 1) % Segments * 2, nextInner = nextOuter + 1;
                int t = s * 6;
                triangles[t] = outer; triangles[t + 1] = nextOuter; triangles[t + 2] = inner;
                triangles[t + 3] = inner; triangles[t + 4] = nextOuter; triangles[t + 5] = nextInner;
            }

            r.Mesh.vertices = r.Vertices;
            r.Mesh.colors = r.Colors;
            r.Mesh.triangles = triangles;

            r.Root.AddComponent<MeshFilter>().sharedMesh = r.Mesh;
            var meshRenderer = r.Root.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = effectMaterial;
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            r.Root.SetActive(false);
            return r;
        }

        /// <summary>Grows the ring (fast at first, slowing down), thins its band and fades it out.</summary>
        private void DrawRipple(Ripple r)
        {
            float t = Mathf.Clamp01(r.Age / rippleLifetime);
            float grow = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic

            float scale = Mathf.Lerp(1f, 1f - laterRingFalloff * 0.5f, 1f - r.Strength); // later rings a bit smaller
            float radius = Mathf.Lerp(rippleRadius.x, rippleRadius.y, grow) * scale;
            float band = bandWidth * (1f - 0.7f * t);
            float alpha = color.a * r.Strength * (1f - t) * (1f - t);

            Color edge = ToRenderSpace(new Color(color.r, color.g, color.b, alpha));
            Color clear = edge;
            clear.a = 0f;

            r.Root.transform.position = new Vector3(r.Centre.x, r.Centre.y, 0f);
            for (int s = 0; s < Segments; s++)
            {
                float angle = (float)s / Segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * flatness, 0f);

                r.Vertices[s * 2] = dir * radius;
                r.Vertices[s * 2 + 1] = dir * Mathf.Max(0f, radius - band);
                r.Colors[s * 2] = edge;      // sharp outer edge
                r.Colors[s * 2 + 1] = clear; // soft inner edge
            }

            r.Mesh.vertices = r.Vertices;
            r.Mesh.colors = r.Colors;
            r.Mesh.RecalculateBounds();
        }

        /// <summary>Returns an unused ripple, or the oldest one if all are in use.</summary>
        private Ripple TakeFree()
        {
            Ripple oldest = ripples[0];
            foreach (Ripple r in ripples)
            {
                if (!r.Active) return r;
                if (r.Age > oldest.Age) oldest = r;
            }

            return oldest;
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
