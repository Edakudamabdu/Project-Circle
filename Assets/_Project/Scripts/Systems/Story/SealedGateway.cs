using System.Collections;
using ProjectCircle.Enemies;
using UnityEngine;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// A sealed wall that the Old Triangle opens with the last of his strength: motes of light flow from him
    /// into the wall, it glows, then crumbles away and leaves a lit doorway.
    ///
    /// Setup: <see cref="wall"/> is the visible slab, <see cref="blocker"/> its solid collider (Ground layer),
    /// <see cref="doorwayLight"/> an optional sprite behind it that fades in once it opens.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SealedGateway : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer wall;
        [SerializeField] private Collider2D blocker;
        [SerializeField] private SpriteRenderer doorwayLight;

        [Tooltip("Vertex-colour material for the motes of light (e.g. ShapeUnlit).")]
        [SerializeField] private Material glowMaterial;

        [SerializeField] private Color glowColor = new Color(1f, 0.86f, 0.55f, 1f);

        [Tooltip("Motes of light that travel into the wall.")]
        [SerializeField, Range(1, 8)] private int motes = 3;

        /// <summary>True once the way is open.</summary>
        public bool IsOpen { get; private set; }

        private Color wallColor;
        private Color lightColor;

        private void Awake()
        {
            if (wall != null) wallColor = wall.color;
            if (doorwayLight != null)
            {
                lightColor = doorwayLight.color;
                doorwayLight.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0f);
            }
        }

        /// <summary>
        /// Sends motes of light from <paramref name="sources"/> (e.g. the Old Triangle's corners) into the wall,
        /// then breaks it open.
        /// </summary>
        public IEnumerator Open(Vector2[] sources, float travelTime = 1.4f)
        {
            if (IsOpen) yield break;

            int count = Mathf.Max(motes, sources.Length);
            var glows = new VertexGlow[count];
            var starts = new Vector2[count];
            var ends = new Vector2[count];
            var arrived = new bool[count];
            Bounds b = wall != null ? wall.bounds : new Bounds(transform.position, Vector3.one);

            for (int i = 0; i < count; i++)
            {
                starts[i] = sources[i % sources.Length];
                ends[i] = new Vector2(b.center.x, Mathf.Lerp(b.min.y + 1f, b.max.y - 2f, (i + 0.5f) / count));
                glows[i] = MakeMote(starts[i]);
            }

            // Fly along gentle arcs, one after another.
            float total = travelTime + 0.25f * (count - 1);
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                for (int i = 0; i < count; i++)
                {
                    float p = Mathf.Clamp01((t - 0.25f * i) / travelTime);
                    float e = Mathf.SmoothStep(0f, 1f, p);
                    Vector2 pos = Vector2.Lerp(starts[i], ends[i], e) + Vector2.up * Mathf.Sin(e * Mathf.PI) * 1.2f;
                    glows[i].transform.position = new Vector3(pos.x, pos.y, -0.1f);
                    glows[i].Intensity = p >= 1f ? 0f : 1.2f;
                    if (p >= 1f && !arrived[i])
                    {
                        arrived[i] = true;
                        glows[i].Burst(); // a ring as it sinks into the wall

                        // Cracks of light spread out from where it went in.
                        for (int c = 0; c < 3; c++)
                        {
                            Vector2 to = ends[i] + new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-3f, 3f));
                            Fx.WallCrack(ends[i], to, glowColor, total - t + 0.7f);
                        }
                    }
                }

                if (wall != null) wall.color = Color.Lerp(wallColor, glowColor, Mathf.Clamp01(t / total) * 0.7f);
                yield return null;
            }

            // The wall flares, then crumbles away: rubble pours down out of it.
            for (int k = 0; k < 6; k++)
            {
                Vector2 at = new Vector2(b.center.x, Mathf.Lerp(b.min.y + 0.5f, b.max.y - 0.5f, k / 5f));
                Fx.Burst(at, new Fx.BurstSettings
                {
                    count = 8, colorA = wallColor, colorB = glowColor,
                    speedMin = 1f, speedMax = 4f, sizeMin = 0.15f, sizeMax = 0.4f, lifeMin = 0.5f, lifeMax = 0.9f,
                    direction = -90f, spread = 90f, gravity = 2f, drag = 0.5f, area = new Vector2(b.size.x, 1.5f), sortingOrder = 6,
                });
            }
            Fx.Dust(new Vector2(b.center.x, b.min.y), b.size.x * 3f, 1.2f);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                float p = t / 0.6f;
                if (wall != null) wall.color = new Color(glowColor.r, glowColor.g, glowColor.b, 1f - p);
                if (doorwayLight != null) doorwayLight.color = new Color(lightColor.r, lightColor.g, lightColor.b, lightColor.a * p);
                yield return null;
            }

            foreach (VertexGlow g in glows) Destroy(g.gameObject);
            SetOpen();
        }

        /// <summary>Opens at once (when the opening has already been played).</summary>
        public void SetOpen()
        {
            IsOpen = true;
            if (wall != null) wall.enabled = false;
            if (blocker != null) blocker.enabled = false;
            if (doorwayLight != null) doorwayLight.color = lightColor;
        }

        private VertexGlow MakeMote(Vector2 at)
        {
            var go = new GameObject("Light Mote");
            go.transform.SetParent(transform, true);
            go.transform.position = at;
            var glow = go.AddComponent<VertexGlow>();
            go.GetComponent<MeshRenderer>().sharedMaterial = glowMaterial;
            glow.Color = glowColor;
            glow.Radius = 0.35f;
            glow.BurstRadius = 1.4f;
            glow.Intensity = 0f;
            return glow;
        }
    }
}
