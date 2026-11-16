using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Death effect for a triangle: it breaks into its three vertices (plus a few smaller chips) that fly
    /// apart, spin, fall and shrink away, with a flash of light where it died.
    /// Create it with <see cref="Spawn"/>; it cleans itself up.
    /// </summary>
    public sealed class TriangleShatter : MonoBehaviour
    {
        private const float Gravity = 26f;

        private struct Piece
        {
            public Transform Transform;
            public Vector2 Velocity;
            public float Spin;
            public float StartScale;
        }

        private Piece[] pieces;
        private VertexGlow flash;
        private float age;
        private float lifetime;

        /// <summary>
        /// Shatters a triangle at <paramref name="position"/> with its colours.
        /// <paramref name="awayFrom"/> is where the killing blow came from; pieces fly away from it.
        /// </summary>
        public static void Spawn(Vector2 position, float circumradius, Color top, Color bottom, Color outline,
                                 Color glowColor, Material material, Vector2 awayFrom, float lifetime = 0.9f)
        {
            var root = new GameObject("Triangle Shatter").AddComponent<TriangleShatter>();
            root.transform.position = position;
            root.Build(circumradius, top, bottom, outline, glowColor, material, (position - awayFrom).normalized, lifetime);

            // Every triangle death: a pop of light, a little dust and grit.
            Systems.Fx.Ring(position, glowColor, circumradius * 2f, 0.3f, 0.12f);
            Systems.Fx.Shards(position, top, Mathf.RoundToInt(6 + circumradius * 6f), 6f, 0.12f + circumradius * 0.05f);
            Systems.Fx.Puff(position + Vector2.down * circumradius * 0.4f, 0.8f + circumradius * 0.4f);
        }

        private void Build(float circumradius, Color top, Color bottom, Color outline, Color glowColor,
                           Material material, Vector2 push, float life)
        {
            lifetime = life;
            if (push.sqrMagnitude < 0.01f) push = Vector2.up;

            const int vertexPieces = 3;
            const int chips = 5;
            pieces = new Piece[vertexPieces + chips];

            for (int i = 0; i < pieces.Length; i++)
            {
                bool isVertex = i < vertexPieces;

                // The three big pieces start where the corners were; chips start near the middle.
                Vector2 start = isVertex
                    ? Direction(90f + i * 120f) * (circumradius * 0.55f)
                    : Random.insideUnitCircle * (circumradius * 0.3f);

                var go = new GameObject(isVertex ? "Vertex" : "Chip");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = start;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, isVertex ? i * 120f : Random.Range(0f, 360f));
                go.AddComponent<MeshFilter>();
                var meshRenderer = go.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                var shape = go.AddComponent<RoundedTriangleMesh>();
                float size = isVertex ? circumradius * 0.42f : circumradius * Random.Range(0.12f, 0.2f);
                shape.Configure(size, size * 0.15f, isVertex ? 0.035f : 0f, top, bottom, outline, 6);

                // Fly outwards from the centre, biased away from the hit and upwards.
                Vector2 outward = start.sqrMagnitude > 1e-4f ? start.normalized : Random.insideUnitCircle.normalized;
                Vector2 velocity = outward * Random.Range(3f, 6f) + push * Random.Range(2f, 4f) + Vector2.up * Random.Range(3f, 6f);

                pieces[i] = new Piece
                {
                    Transform = go.transform,
                    Velocity = velocity,
                    Spin = Random.Range(-720f, 720f),
                    StartScale = 1f
                };
            }

            // Flash of light where it died.
            var flashObject = new GameObject("Flash");
            flashObject.transform.SetParent(transform, false);
            flashObject.AddComponent<MeshFilter>();
            flashObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            flash = flashObject.AddComponent<VertexGlow>();
            flash.Color = glowColor;
            flash.Intensity = 1.6f;
            flash.Burst();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float t = Mathf.Clamp01(age / lifetime);

            for (int i = 0; i < pieces.Length; i++)
            {
                Piece p = pieces[i];
                p.Velocity += Vector2.down * (Gravity * dt);
                p.Transform.localPosition += (Vector3)(p.Velocity * dt);
                p.Transform.Rotate(0f, 0f, p.Spin * dt);

                // Hold full size for the first half, then shrink away.
                float shrink = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
                p.Transform.localScale = Vector3.one * Mathf.Max(0f, shrink);
                pieces[i] = p;
            }

            if (flash != null) flash.Intensity = 1.6f * Mathf.Max(0f, 1f - age / 0.3f);

            if (age >= lifetime) Destroy(gameObject);
        }

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
    }
}
