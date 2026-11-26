using System.Collections;
using System.Collections.Generic;
using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Overseer: a tall triangle with a glowing crown that never attacks and never joins the fight. It stands
    /// apart (put it somewhere out of the way: a high perch, behind a gap) and commands: every triangle within
    /// <see cref="commandRange"/> is linked to it by a thin line of light, and while linked it
    /// - walks, decides and attacks faster (<see cref="TriangleEnemy.Haste"/>), and
    /// - carries a ward of <see cref="wardPoints"/> extra hit points that must be broken first.
    /// Break the Overseer and the wards vanish; its triangles falter, slowed for a moment, before carrying on
    /// at normal speed.
    ///
    /// What you see: every commanded triangle wears a pulsing gold aura, and every <see cref="orderInterval"/> a
    /// bright pulse ("an order") runs down each link and the triangle flares when it arrives. Break the
    /// Overseer and the auras go out: its triangles turn grey and slow down for a moment.
    ///
    /// Rank as a mechanic: the ones at the top make everyone below them worse. Push through to reach it.
    /// Setup: root with a Rigidbody2D, collider and EnemyHealth; the shape is a child "Visual".
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
    public sealed class Overseer : MonoBehaviour
    {
        [SerializeField] private RoundedTriangleMesh shape;

        [Header("Command")]
        [SerializeField, Min(1f)] private float commandRange = 20f;

        [Tooltip("How much faster commanded triangles walk, decide and attack.")]
        [SerializeField, Min(1f)] private float haste = 1.7f;

        [Tooltip("Extra hit points each commanded triangle carries (used up first; gone when the Overseer breaks).")]
        [SerializeField, Min(0)] private int wardPoints = 2;

        [Tooltip("When the Overseer breaks: commanded triangles slow to this for Falter Time.")]
        [SerializeField, Range(0.05f, 1f)] private float falterHaste = 0.35f;

        [SerializeField, Min(0f)] private float falterTime = 1.4f;
        [SerializeField] private Color linkColor = new Color(1f, 0.82f, 0.4f, 0.55f);

        [Header("Showing it")]
        [Tooltip("Seconds between the order pulses that run down each link.")]
        [SerializeField, Min(0.3f)] private float orderInterval = 2f;

        [SerializeField, Min(0.05f)] private float orderTravelTime = 0.35f;
        [SerializeField] private Color auraColor = new Color(1f, 0.8f, 0.35f, 0.5f);
        [SerializeField] private Color falterColor = new Color(0.55f, 0.6f, 0.7f, 0.6f);

        private Rigidbody2D body;
        private readonly List<TriangleEnemy> commanded = new List<TriangleEnemy>();
        private readonly List<LineRenderer> links = new List<LineRenderer>();
        private float nextScan;
        private float nextOrder;
        private float orderStart = -99f;
        private readonly Dictionary<TriangleEnemy, VertexGlow> auras = new Dictionary<TriangleEnemy, VertexGlow>();
        private readonly List<VertexGlow> orderPulses = new List<VertexGlow>();
        private readonly HashSet<TriangleEnemy> orderArrived = new HashSet<TriangleEnemy>();

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.freezeRotation = true;
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
            GetComponent<EnemyHealth>().Died += HandleDied;
        }

        private void OnDisable()
        {
            // Leaving play or being removed: give everything back.
            foreach (TriangleEnemy t in commanded) Release(t);
        }

        private void Update()
        {
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.5f;
                Rescan();
            }

            // The crown pulses; the links shimmer from the Overseer to each commanded triangle.
            if (shape != null) shape.SetCornerGlow(0, 0.6f + 0.4f * Mathf.Sin(Time.time * 4f), linkColor);
            for (int i = 0; i < links.Count; i++)
            {
                bool live = i < commanded.Count && commanded[i] != null;
                links[i].enabled = live;
                if (!live) continue;
                Vector2 from = shape != null ? (Vector2)shape.transform.TransformPoint(shape.GetCornerPoint(0)) : (Vector2)transform.position;
                links[i].SetPosition(0, from);
                links[i].SetPosition(1, commanded[i].transform.position);
                float a = linkColor.a * (0.6f + 0.4f * Mathf.Sin(Time.time * 8f + i));
                links[i].startColor = links[i].endColor = new Color(linkColor.r, linkColor.g, linkColor.b, a);
            }

            UpdateAuras();
            UpdateOrders();
        }

        /// <summary>
        /// Each commanded triangle wears a soft, pulsing gold aura: bright while its ward holds, faint once the
        /// ward has been broken.
        /// </summary>
        private void UpdateAuras()
        {
            float pulse = 0.55f + 0.15f * Mathf.Sin(Time.time * 5f);
            foreach (KeyValuePair<TriangleEnemy, VertexGlow> pair in auras)
            {
                if (pair.Key == null || pair.Value == null) continue;
                var health = pair.Key.GetComponent<EnemyHealth>();
                bool warded = health != null && health.Ward > 0;
                pair.Value.Intensity = warded ? pulse : pulse * 0.35f;
            }
        }

        /// <summary>Now and then an order runs down every link; the triangle flares when it arrives.</summary>
        private void UpdateOrders()
        {
            if (Time.time >= nextOrder && commanded.Count > 0)
            {
                nextOrder = Time.time + orderInterval;
                orderStart = Time.time;
                orderArrived.Clear();
                if (shape != null) Systems.Fx.Ring(shape.transform.TransformPoint(shape.GetCornerPoint(0)), linkColor, 1f, 0.25f, 0.06f);
            }

            float t = (Time.time - orderStart) / orderTravelTime;
            while (orderPulses.Count < commanded.Count) orderPulses.Add(MakeGlow(transform, 0.28f, Color.white, 9));
            Vector2 from = shape != null ? (Vector2)shape.transform.TransformPoint(shape.GetCornerPoint(0)) : (Vector2)transform.position;
            for (int i = 0; i < orderPulses.Count; i++)
            {
                TriangleEnemy target = i < commanded.Count ? commanded[i] : null;
                bool travelling = target != null && t >= 0f && t < 1f;
                orderPulses[i].Intensity = travelling ? 1.2f : 0f;
                if (!travelling)
                {
                    // Arrived: the triangle flares.
                    if (target != null && t >= 1f && orderArrived.Add(target) && auras.TryGetValue(target, out VertexGlow aura) && aura != null) aura.Burst();
                    continue;
                }
                orderPulses[i].transform.position = Vector2.Lerp(from, target.transform.position, t * t);
            }
        }

        private static VertexGlow MakeGlow(Transform parent, float radius, Color color, int order)
        {
            var go = new GameObject("Overseer Glow");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = Systems.Fx.Material;
            var glow = go.AddComponent<VertexGlow>();
            go.GetComponent<MeshRenderer>().sortingOrder = order;
            glow.Color = color;
            glow.Radius = radius;
            glow.BurstRadius = radius * 1.8f;
            glow.Intensity = 0f;
            return glow;
        }

        private void FixedUpdate()
        {
            // It holds its post: never walks (gravity still keeps it standing on its perch).
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        }

        /// <summary>Gives a triangle its normal speed back and takes its ward away.</summary>
        private static void Release(TriangleEnemy t)
        {
            if (t == null) return;
            t.Haste = 1f;
            var health = t.GetComponent<EnemyHealth>();
            if (health != null) health.SetWard(0);
        }

        /// <summary>Takes command of every triangle in range (and lets go of any that left it).</summary>
        private void Rescan()
        {
            var inRange = new List<TriangleEnemy>();
            foreach (TriangleEnemy t in FindObjectsByType<TriangleEnemy>(FindObjectsInactive.Exclude))
            {
                if (Vector2.Distance(t.transform.position, transform.position) <= commandRange) inRange.Add(t);
            }

            foreach (TriangleEnemy t in commanded) if (t != null && !inRange.Contains(t)) Release(t);
            commanded.Clear();
            commanded.AddRange(inRange);
            foreach (TriangleEnemy t in commanded) t.Haste = haste;

            // Auras: add to newly commanded triangles, remove from the ones that left (or died).
            var gone = new List<TriangleEnemy>();
            foreach (KeyValuePair<TriangleEnemy, VertexGlow> pair in auras) if (pair.Key == null || !inRange.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (TriangleEnemy t in gone)
            {
                if (auras[t] != null) Destroy(auras[t].gameObject);
                auras.Remove(t);
            }
            foreach (TriangleEnemy t in commanded)
            {
                if (auras.ContainsKey(t)) continue;
                var c = t.GetComponent<Collider2D>();
                float size = c != null ? Mathf.Max(c.bounds.extents.x, c.bounds.extents.y) : 0.8f;
                VertexGlow aura = MakeGlow(t.transform, size * 1.5f, auraColor, 4);
                aura.Burst();
                auras.Add(t, aura);

                // Newly commanded: it gets its ward (once, when it joins; not topped up while it stays).
                var health = t.GetComponent<EnemyHealth>();
                if (health != null) health.SetWard(wardPoints);
            }

            while (links.Count < commanded.Count) links.Add(MakeLink());
        }

        private LineRenderer MakeLink()
        {
            var go = new GameObject("Command Link");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Systems.Fx.Material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.05f;
            line.sortingOrder = 3;
            return line;
        }

        /// <summary>Its triangles lose their orders: they falter, slowed, then carry on at normal speed.</summary>
        private void HandleDied()
        {
            var survivors = new List<TriangleEnemy>();
            foreach (TriangleEnemy t in commanded) if (t != null) survivors.Add(t);
            foreach (TriangleEnemy t in survivors) { var h = t.GetComponent<EnemyHealth>(); if (h != null) h.SetWard(0); }
            commanded.Clear(); // they're the falter's now (OnDisable must not snap them back to full speed)

            // Its command ends now, even while its body is still shattering: no more rescans (which would take
            // the triangles back), links and order pulses go dark.
            foreach (LineRenderer link in links) if (link != null) link.enabled = false;
            foreach (VertexGlow pulse in orderPulses) if (pulse != null) pulse.Intensity = 0f;
            enabled = false;
            foreach (VertexGlow aura in auras.Values) if (aura != null) Destroy(aura.gameObject);
            auras.Clear();
            if (survivors.Count > 0)
            {
                var runner = new GameObject("Overseer Falter").AddComponent<FalterRunner>();
                runner.Run(survivors, falterHaste, falterTime, falterColor);
            }
            foreach (TriangleEnemy t in survivors) Systems.Fx.Ring(t.transform.position, linkColor, 1.5f, 0.35f, 0.08f);
        }

        /// <summary>Outlives the Overseer to put its triangles back to normal speed after faltering.</summary>
        private sealed class FalterRunner : MonoBehaviour
        {
            public void Run(List<TriangleEnemy> triangles, float slow, float time, Color greyColor) => StartCoroutine(Falter(triangles, slow, time, greyColor));

            /// <summary>Slowed and greyed out (a dull grey haze over each one) for a moment, then back to normal.</summary>
            private IEnumerator Falter(List<TriangleEnemy> triangles, float slow, float time, Color greyColor)
            {
                var hazes = new List<VertexGlow>();
                foreach (TriangleEnemy t in triangles)
                {
                    if (t == null) continue;
                    t.Haste = slow;
                    var c = t.GetComponent<Collider2D>();
                    float size = c != null ? Mathf.Max(c.bounds.extents.x, c.bounds.extents.y) : 0.8f;
                    VertexGlow haze = MakeGlow(t.transform, size * 1.4f, greyColor, 6);
                    haze.Intensity = 0.9f;
                    hazes.Add(haze);
                }
                for (float e = 0f; e < time; e += Time.deltaTime)
                {
                    foreach (VertexGlow h in hazes) if (h != null) h.Intensity = 0.9f * (1f - e / time) + 0.1f;
                    yield return null;
                }
                foreach (TriangleEnemy t in triangles) if (t != null) t.Haste = 1f;
                foreach (VertexGlow h in hazes) if (h != null) Destroy(h.gameObject);
                Destroy(gameObject);
            }
        }
    }
}
