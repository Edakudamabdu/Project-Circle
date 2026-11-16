using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>An expanding ring that fades out, then destroys itself. Created by <see cref="Fx.Ring"/>.</summary>
    public sealed class FxRing : MonoBehaviour
    {
        private const int Segments = 40;

        private Mesh mesh;
        private Color color;
        private float radius, time, thickness, start;
        private readonly Vector3[] vertices = new Vector3[Segments * 2];
        private readonly Color[] colors = new Color[Segments * 2];

        public void Play(Color ringColor, float ringRadius, float duration, float ringThickness)
        {
            color = ringColor;
            radius = ringRadius;
            time = Mathf.Max(0.05f, duration);
            thickness = ringThickness;
            start = Time.time;

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh = new Mesh { name = "Fx Ring" };
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Fx.Material;
            renderer.sortingOrder = 13;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var triangles = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                int a = i * 2, b = ((i + 1) % Segments) * 2;
                int t = i * 6;
                triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1;
                triangles[t + 3] = b; triangles[t + 4] = b + 1; triangles[t + 5] = a + 1;
            }
            Rebuild(0f);
            mesh.triangles = triangles;
        }

        private void Update()
        {
            float p = (Time.time - start) / time;
            if (p >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            Rebuild(p);
        }

        private void Rebuild(float p)
        {
            float eased = 1f - (1f - p) * (1f - p);
            float r = Mathf.Lerp(radius * 0.2f, radius, eased);
            float w = thickness * (1f - p * 0.7f);
            Color c = new Color(color.r, color.g, color.b, color.a * (1f - p));
            for (int i = 0; i < Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                vertices[i * 2] = dir * r;
                vertices[i * 2 + 1] = dir * (r + w);
                colors[i * 2] = colors[i * 2 + 1] = c;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }

    /// <summary>A line (crack, speed line, light column) that fades out, then destroys itself.</summary>
    public sealed class FxLine : MonoBehaviour
    {
        private LineRenderer line;
        private Color color;
        private float width, time, start, endAlpha;
        private bool fadeWidth;

        public static void Spawn(Vector3[] points, Color color, float width, float time, int sortingOrder,
                                 bool fadeWidth = false, float endAlpha = -1f)
        {
            var go = new GameObject("Fx Line");
            var fx = go.AddComponent<FxLine>();
            fx.line = go.AddComponent<LineRenderer>();
            fx.line.sharedMaterial = Fx.Material;
            fx.line.useWorldSpace = true;
            fx.line.positionCount = points.Length;
            fx.line.SetPositions(points);
            fx.line.sortingOrder = sortingOrder;
            fx.line.numCapVertices = 2;
            fx.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx.color = color;
            fx.width = width;
            fx.time = Mathf.Max(0.05f, time);
            fx.fadeWidth = fadeWidth;
            fx.endAlpha = endAlpha;
            fx.start = Time.time;
            fx.Apply(0f);
        }

        private void Update()
        {
            float p = (Time.time - start) / time;
            if (p >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            Apply(p);
        }

        private void Apply(float p)
        {
            float a = color.a * (1f - p);
            line.startColor = new Color(color.r, color.g, color.b, a);
            line.endColor = new Color(color.r, color.g, color.b, endAlpha >= 0f ? endAlpha * a : a);
            line.widthMultiplier = fadeWidth ? width * (1f - p * 0.6f) : width;
        }
    }

    /// <summary>Embers drifting up off a moving object for a while. Created by <see cref="Fx.Embers"/>.</summary>
    public sealed class FxEmbers : MonoBehaviour
    {
        private Transform follow;
        private Color color;
        private float until, next;

        public void Play(Transform target, Color emberColor, float time)
        {
            follow = target;
            color = emberColor;
            until = Time.time + time;
        }

        private void Update()
        {
            if (follow == null || Time.time > until)
            {
                Destroy(gameObject);
                return;
            }
            if (Time.time < next) return;
            next = Time.time + 0.05f;

            Fx.Burst(follow.position, new Fx.BurstSettings
            {
                count = 2, colorA = color, colorB = Color.Lerp(color, Color.white, 0.6f),
                speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.05f, sizeMax = 0.12f,
                lifeMin = 0.3f, lifeMax = 0.6f, direction = -90f, spread = 60f, gravity = 0.15f, drag = 1f,
                area = new Vector2(0.3f, 0.3f), sortingOrder = 8,
            });
        }
    }
}
