using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// The Hall of Strings: hundreds of discarded shapes' resistance, hanging from the roof as glowing strings.
    /// They sway gently and brighten as the circle passes. <see cref="FormCape"/> pulls them all loose and
    /// swirls them into the circle, where they become its cape.
    ///
    /// Built in code from LineRenderers. Place this object at the middle of the hall's ceiling and set the
    /// hall's width. The strings hang down from this object's height.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StringHall : MonoBehaviour
    {
        private sealed class Strand
        {
            public LineRenderer line;
            public Vector2 top;
            public float length;
            public float phase;
            public float brightness;
            public float delay;
            public Vector3[] points;
        }

        [Header("Look")]
        [Tooltip("Vertex-colour material (e.g. ShapeUnlit).")]
        [SerializeField] private Material material;

        [SerializeField] private Color dimColor = new Color(0.62f, 0.6f, 0.78f, 0.45f);
        [SerializeField] private Color litColor = new Color(1f, 0.93f, 0.78f, 1f);
        [SerializeField, Min(0.005f)] private float width = 0.05f;
        [SerializeField] private int sortingOrder = -2;

        [Header("Layout")]
        [SerializeField, Range(4, 120)] private int count = 46;
        [SerializeField, Min(1f)] private float hallWidth = 22f;
        [SerializeField, Min(0.5f)] private float minLength = 3f;
        [SerializeField, Min(0.5f)] private float maxLength = 9f;
        [SerializeField, Range(4, 24)] private int pointsPerString = 12;
        [SerializeField] private int seed = 7;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float swayAmount = 0.25f;
        [SerializeField, Min(0f)] private float swaySpeed = 0.9f;

        [Tooltip("Strings within this distance of the circle light up.")]
        [SerializeField, Min(0f)] private float lightRadius = 2.6f;

        private readonly List<Strand> strands = new List<Strand>();
        private Transform player;
        private bool forming;

        private void Awake()
        {
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float x = (i + 0.5f) / count - 0.5f + ((float)random.NextDouble() - 0.5f) * 0.6f / count;
                var s = new Strand
                {
                    top = (Vector2)transform.position + new Vector2(x * hallWidth, 0f),
                    length = Mathf.Lerp(minLength, maxLength, (float)random.NextDouble()),
                    phase = (float)random.NextDouble() * 10f,
                    points = new Vector3[pointsPerString],
                };
                s.line = MakeLine(i);
                strands.Add(s);
            }
        }

        private void Start()
        {
            var movement = FindAnyObjectByType<Player.PlayerMovement>();
            if (movement != null) player = movement.transform;
        }

        private void Update()
        {
            if (forming) return;

            float time = Time.time;
            foreach (Strand s in strands)
            {
                if (!s.line.enabled) continue;

                // Sway: the top is fixed, the tip swings most.
                for (int i = 0; i < pointsPerString; i++)
                {
                    float t = i / (pointsPerString - 1f);
                    float sway = Mathf.Sin(time * swaySpeed + s.phase + t * 2.2f) * swayAmount * t * t;
                    s.points[i] = new Vector3(s.top.x + sway, s.top.y - s.length * t, 0f);
                }
                s.line.SetPositions(s.points);

                // Light up near the circle (measured to the nearest point on the string), fade back slowly.
                float target = 0f;
                if (player != null)
                {
                    Vector2 p = player.position;
                    float y = Mathf.Clamp(p.y, s.top.y - s.length, s.top.y);
                    float d = Vector2.Distance(p, new Vector2(s.top.x, y));
                    target = Mathf.Clamp01(1f - d / lightRadius);
                }
                s.brightness = Mathf.MoveTowards(s.brightness, target, (target > s.brightness ? 3f : 0.6f) * Time.deltaTime);
                SetColor(s, Color.Lerp(dimColor, litColor, s.brightness), 1f);
            }
        }

        /// <summary>
        /// Every string lights up, comes loose and swirls into <paramref name="target"/>. Ends with the strings
        /// gone (they are the cape now).
        /// </summary>
        public IEnumerator FormCape(Transform target, float duration = 2.4f)
        {
            // 1. Everything glows.
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                foreach (Strand s in strands) SetColor(s, Color.Lerp(dimColor, litColor, Mathf.Max(s.brightness, t / 0.8f)), 1f);
                yield return null;
            }

            forming = true;
            var random = new System.Random(seed + 1);
            var starts = new List<Vector3[]>();
            foreach (Strand s in strands)
            {
                starts.Add((Vector3[])s.points.Clone());
                s.delay = (float)random.NextDouble() * duration * 0.35f;
            }

            // 2. They come loose and spiral in: the tip leads, the rest of the string follows.
            float travel = duration * 0.65f;
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                Vector3 centre = target.position;
                for (int k = 0; k < strands.Count; k++)
                {
                    Strand s = strands[k];
                    for (int i = 0; i < pointsPerString; i++)
                    {
                        float along = i / (pointsPerString - 1f);
                        float p = Mathf.Clamp01((time - s.delay - (1f - along) * 0.2f) / travel);
                        float e = p * p * (3f - 2f * p);
                        Vector3 offset = starts[k][i] - centre;
                        float angle = e * 540f * (k % 2 == 0 ? 1f : -1f);
                        offset = Quaternion.Euler(0f, 0f, angle) * offset * (1f - e);
                        s.points[i] = centre + offset;
                    }
                    s.line.SetPositions(s.points);
                    float arrival = Mathf.Clamp01((time - s.delay) / travel);
                    SetColor(s, Color.Lerp(litColor, Color.white, arrival), 1f - Mathf.Clamp01((arrival - 0.85f) / 0.15f));

                    // The leading tips shed sparks as they swirl in.
                    if (arrival > 0.05f && arrival < 0.95f && Random.value < 0.08f)
                        Fx.Sparks(s.points[pointsPerString - 1], Random.Range(0f, 360f), litColor, 2, 3f, 180f);
                }
                yield return null;
            }

            HideStrings();
        }

        /// <summary>Removes the strings at once (they already became the cape).</summary>
        public void HideStrings()
        {
            forming = true;
            foreach (Strand s in strands) s.line.enabled = false;
        }

        private static void SetColor(Strand s, Color c, float alphaScale)
        {
            Color tip = new Color(c.r, c.g, c.b, c.a * 0.35f * alphaScale);
            Color root = new Color(c.r, c.g, c.b, c.a * alphaScale);
            s.line.startColor = root;
            s.line.endColor = tip;
        }

        private LineRenderer MakeLine(int index)
        {
            var go = new GameObject("String " + index);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = pointsPerString;
            line.widthCurve = AnimationCurve.Linear(0f, width, 1f, width * 0.5f);
            line.numCapVertices = 2;
            line.sortingOrder = sortingOrder;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.6f, 0.5f);
            Vector3 p = transform.position;
            Gizmos.DrawLine(p + Vector3.left * hallWidth * 0.5f, p + Vector3.right * hallWidth * 0.5f);
            Gizmos.DrawLine(p, p + Vector3.down * maxLength);
        }
#endif
    }
}
