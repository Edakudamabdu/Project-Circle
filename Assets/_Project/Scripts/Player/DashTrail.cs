using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Dash visuals: fading afterimages of the circle (and its cape) left along the dash path, plus thin
    /// speed lines streaking out behind it.
    ///
    /// Everything is pooled (created once in Awake and reused), so dashing allocates nothing at runtime
    /// apart from copying the cape's current shape into an afterimage.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class DashTrail : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The circle's sprite, copied into each afterimage.")]
        [SerializeField] private SpriteRenderer bodyRenderer;

        [Tooltip("The cape's mesh, copied into each afterimage. Optional.")]
        [SerializeField] private MeshFilter capeMesh;

        [Tooltip("Vertex-colour material used for cape afterimages and speed lines (e.g. ShapeUnlit).")]
        [SerializeField] private Material effectMaterial;

        [Header("Afterimages")]
        [Tooltip("Seconds between afterimages while dashing.")]
        [SerializeField, Min(0.005f)] private float afterimageInterval = 0.045f;

        [Tooltip("How long each afterimage takes to fade out.")]
        [SerializeField, Min(0.02f)] private float afterimageLifetime = 0.26f;

        [SerializeField] private Color afterimageColor = new Color(0.72f, 0.86f, 1f, 0.55f);

        [Tooltip("How much an afterimage shrinks as it fades (0.2 = to 80% size).")]
        [SerializeField, Range(0f, 0.9f)] private float afterimageShrink = 0.2f;

        [Tooltip("Afterimages are drawn at this Order in Layer (behind the circle, which is at 10).")]
        [SerializeField] private int afterimageSortingOrder = 7;

        [SerializeField, Range(2, 32)] private int afterimagePoolSize = 12;

        [Header("Speed Lines")]
        [Tooltip("Lines spawned the moment the dash starts.")]
        [SerializeField, Range(0, 12)] private int linesAtStart = 5;

        [Tooltip("Seconds between extra lines while dashing.")]
        [SerializeField, Min(0.005f)] private float lineInterval = 0.035f;

        [SerializeField, Min(0.02f)] private float lineLifetime = 0.18f;
        [SerializeField, Min(0.05f)] private float minLineLength = 0.7f;
        [SerializeField, Min(0.05f)] private float maxLineLength = 1.8f;
        [SerializeField, Min(0.005f)] private float lineWidth = 0.05f;

        [Tooltip("How far above/below the circle's centre lines can appear.")]
        [SerializeField, Min(0f)] private float lineSpread = 0.45f;

        [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.85f);

        [Tooltip("Order in Layer for speed lines: in front of the afterimages, behind the circle (10).")]
        [SerializeField] private int lineSortingOrder = 8;
        [SerializeField, Range(2, 32)] private int linePoolSize = 16;

        private sealed class Afterimage
        {
            public GameObject Root;
            public SpriteRenderer Body;
            public GameObject CapeObject;
            public Mesh CapeMesh;
            public Color[] CapeColors = new Color[0];
            public Vector3 BodyScale;
            public float Age;
            public bool Active;
        }

        private sealed class SpeedLine
        {
            public LineRenderer Renderer;
            public Vector3 Head;
            public Vector3 Tail;
            public float Age;
            public bool Active;
        }

        private PlayerMovement movement;
        private readonly List<Afterimage> afterimages = new List<Afterimage>();
        private readonly List<SpeedLine> lines = new List<SpeedLine>();
        private Transform effectsRoot;
        private float nextAfterimageTime;
        private float nextLineTime;
        private int dashDirection = 1;
        private bool wasDashing;

        #region Unity lifecycle

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();

            // Effects live in world space (not under the Player), so they stay where they were spawned.
            effectsRoot = new GameObject("Dash Trail Effects").transform;

            for (int i = 0; i < afterimagePoolSize; i++) afterimages.Add(CreateAfterimage(i));
            for (int i = 0; i < linePoolSize; i++) lines.Add(CreateLine(i));
        }

        private void OnDestroy()
        {
            foreach (Afterimage a in afterimages)
            {
                if (a.CapeMesh != null) Destroy(a.CapeMesh);
            }

            if (effectsRoot != null) Destroy(effectsRoot.gameObject);
        }

        private void LateUpdate()
        {
            float now = Time.time;
            bool dashing = movement.IsDashing;

            if (dashing && !wasDashing)
            {
                // Dash just started: an immediate afterimage and a burst of lines.
                dashDirection = movement.Velocity.x >= 0f ? 1 : -1;
                SpawnAfterimage();
                for (int i = 0; i < linesAtStart; i++) SpawnLine();
                nextAfterimageTime = now + afterimageInterval;
                nextLineTime = now + lineInterval;
            }
            else if (dashing)
            {
                if (now >= nextAfterimageTime)
                {
                    SpawnAfterimage();
                    nextAfterimageTime = now + afterimageInterval;
                }

                if (now >= nextLineTime)
                {
                    SpawnLine();
                    nextLineTime = now + lineInterval;
                }
            }

            wasDashing = dashing;

            float dt = Time.deltaTime;
            UpdateAfterimages(dt);
            UpdateLines(dt);
        }

        #endregion

        #region Afterimages

        private Afterimage CreateAfterimage(int index)
        {
            var image = new Afterimage { Root = new GameObject("Afterimage " + index) };
            image.Root.transform.SetParent(effectsRoot, false);

            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(image.Root.transform, false);
            image.Body = bodyObject.AddComponent<SpriteRenderer>();
            image.Body.sortingOrder = afterimageSortingOrder;
            if (bodyRenderer != null)
            {
                image.Body.sharedMaterial = bodyRenderer.sharedMaterial;
                image.Body.sortingLayerID = bodyRenderer.sortingLayerID;
            }

            if (capeMesh != null && effectMaterial != null)
            {
                image.CapeObject = new GameObject("Cape");
                image.CapeObject.transform.SetParent(image.Root.transform, false);
                image.CapeMesh = new Mesh { name = "Cape Afterimage" };
                image.CapeMesh.MarkDynamic();
                image.CapeObject.AddComponent<MeshFilter>().sharedMesh = image.CapeMesh;
                var meshRenderer = image.CapeObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = effectMaterial;
                meshRenderer.sortingOrder = afterimageSortingOrder - 1;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            image.Root.SetActive(false);
            return image;
        }

        private void SpawnAfterimage()
        {
            if (bodyRenderer == null) return;
            Afterimage image = TakeOldest(afterimages, a => a.Active, a => a.Age);

            // Body: a copy of the circle's sprite exactly where it is now.
            Transform source = bodyRenderer.transform;
            image.Body.sprite = bodyRenderer.sprite;
            image.Body.transform.SetPositionAndRotation(source.position, source.rotation);
            image.BodyScale = source.lossyScale;
            image.Body.transform.localScale = image.BodyScale;

            // Cape: a copy of its current shape.
            if (image.CapeObject != null)
            {
                Mesh sourceMesh = capeMesh.sharedMesh;
                if (sourceMesh != null && sourceMesh.vertexCount > 0)
                {
                    image.CapeObject.SetActive(true);
                    Transform capeTransform = capeMesh.transform;
                    image.CapeObject.transform.SetPositionAndRotation(capeTransform.position, capeTransform.rotation);
                    image.CapeObject.transform.localScale = capeTransform.lossyScale;

                    image.CapeMesh.Clear();
                    image.CapeMesh.vertices = sourceMesh.vertices;
                    image.CapeMesh.triangles = sourceMesh.triangles;
                    if (image.CapeColors.Length != sourceMesh.vertexCount) image.CapeColors = new Color[sourceMesh.vertexCount];
                }
                else
                {
                    image.CapeObject.SetActive(false);
                }
            }

            image.Age = 0f;
            image.Active = true;
            image.Root.SetActive(true);
            ApplyAfterimageFade(image);
        }

        private void UpdateAfterimages(float dt)
        {
            foreach (Afterimage image in afterimages)
            {
                if (!image.Active) continue;

                image.Age += dt;
                if (image.Age >= afterimageLifetime)
                {
                    image.Active = false;
                    image.Root.SetActive(false);
                    continue;
                }

                ApplyAfterimageFade(image);
            }
        }

        private void ApplyAfterimageFade(Afterimage image)
        {
            float t = Mathf.Clamp01(image.Age / afterimageLifetime);
            float fade = (1f - t) * (1f - t);

            Color colour = afterimageColor;
            colour.a *= fade;
            image.Body.color = colour;
            image.Body.transform.localScale = image.BodyScale * (1f - afterimageShrink * t);

            if (image.CapeObject != null && image.CapeObject.activeSelf)
            {
                Color capeColour = ToRenderSpace(new Color(colour.r, colour.g, colour.b, colour.a * 0.8f));
                for (int i = 0; i < image.CapeColors.Length; i++) image.CapeColors[i] = capeColour;
                image.CapeMesh.colors = image.CapeColors;
            }
        }

        #endregion

        #region Speed lines

        private SpeedLine CreateLine(int index)
        {
            var go = new GameObject("Speed Line " + index);
            go.transform.SetParent(effectsRoot, false);
            var lineRenderer = go.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.numCapVertices = 2;
            lineRenderer.sharedMaterial = effectMaterial;
            lineRenderer.sortingOrder = lineSortingOrder;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.enabled = false;
            return new SpeedLine { Renderer = lineRenderer };
        }

        private void SpawnLine()
        {
            if (effectMaterial == null) return;
            SpeedLine line = TakeOldest(lines, l => l.Active, l => l.Age);

            // Start a little behind the circle at a random height, streaking further back.
            Vector3 centre = transform.position;
            float height = Random.Range(-lineSpread, lineSpread);
            float behind = Random.Range(0.1f, 0.6f);
            float length = Random.Range(minLineLength, maxLineLength);

            line.Head = centre + new Vector3(-dashDirection * behind, height, 0f);
            line.Tail = line.Head + new Vector3(-dashDirection * length, 0f, 0f);
            line.Age = 0f;
            line.Active = true;
            line.Renderer.enabled = true;
            ApplyLineFade(line);
        }

        private void UpdateLines(float dt)
        {
            foreach (SpeedLine line in lines)
            {
                if (!line.Active) continue;

                line.Age += dt;
                if (line.Age >= lineLifetime)
                {
                    line.Active = false;
                    line.Renderer.enabled = false;
                    continue;
                }

                ApplyLineFade(line);
            }
        }

        private void ApplyLineFade(SpeedLine line)
        {
            float t = Mathf.Clamp01(line.Age / lineLifetime);

            // The tail catches up with the head as it fades, so lines shrink away instead of just vanishing.
            Vector3 tail = Vector3.Lerp(line.Tail, line.Head, t);
            line.Renderer.SetPosition(0, line.Head);
            line.Renderer.SetPosition(1, tail);

            Color head = lineColor;
            head.a *= 1f - t;
            Color end = head;
            end.a = 0f;
            line.Renderer.startColor = head;
            line.Renderer.endColor = end;
            line.Renderer.startWidth = lineWidth * (1f - 0.5f * t);
            line.Renderer.endWidth = lineWidth * 0.3f;
        }

        #endregion

        #region Helpers

        /// <summary>Returns a free pooled item, or the oldest active one if the pool is full.</summary>
        private static T TakeOldest<T>(List<T> pool, System.Func<T, bool> isActive, System.Func<T, float> age)
        {
            T oldest = pool[0];
            float oldestAge = float.MinValue;
            foreach (T item in pool)
            {
                if (!isActive(item)) return item;
                if (age(item) > oldestAge)
                {
                    oldestAge = age(item);
                    oldest = item;
                }
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

        #endregion
    }
}
