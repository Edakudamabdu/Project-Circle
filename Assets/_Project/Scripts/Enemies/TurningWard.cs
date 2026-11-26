using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// The Bulwark's turning ward: bright plates of vertex power cover two of its three sides; the third side is
    /// bare. Every <see cref="turnInterval"/> the ward turns one side round. Just before it turns, the vertex it
    /// is about to sweep past glows (<see cref="warnTime"/>), so the next opening can be read in advance.
    ///
    /// Hits on the bare side land; hits on a warded side glance off (a down swing still bounces). Which side a
    /// blow lands on is judged from where the circle swings from, so it is always the side facing the circle.
    /// The plates are drawn on the shape's own sides, so they roll with it as it tumbles: when the bare side is
    /// face-down on the floor, it is fully guarded until the next turn.
    ///
    /// Works with <see cref="TriangleEnemy"/> (or anything with a <see cref="RoundedTriangleMesh"/>).
    /// Sides: side i runs from corner i to corner i+1.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class TurningWard : MonoBehaviour, IHitGuard
    {
        [SerializeField] private RoundedTriangleMesh shape;

        [Header("Turning")]
        [Tooltip("Seconds between turns of the ward.")]
        [SerializeField, Min(0.3f)] private float turnInterval = 2f;

        [Tooltip("The vertex the ward is about to sweep past glows for this long before it turns.")]
        [SerializeField, Min(0.05f)] private float warnTime = 0.5f;

        [Tooltip("+1 or -1: which way the opening travels round the triangle.")]
        [SerializeField] private int turnDirection = 1;

        [Tooltip("Which side starts bare (0, 1 or 2).")]
        [SerializeField, Range(0, 2)] private int startOpenSide = 1;

        [Header("Look")]
        [SerializeField] private Color wardColor = new Color(0.7f, 0.9f, 1f, 0.95f);
        [SerializeField] private Color warnColor = new Color(1f, 0.95f, 0.75f, 1f);
        [SerializeField, Min(0.02f)] private float plateWidth = 0.2f;

        [Tooltip("How far outside the shape's sides the plates sit.")]
        [SerializeField, Min(0f)] private float plateOffset = 0.12f;

        private readonly LineRenderer[] plates = new LineRenderer[3];
        private readonly float[] flashUntil = new float[3];
        private int openSide;
        private float nextTurn;
        private float turnedAt = float.NegativeInfinity;
        private int previousOpen = -1;

        /// <summary>The side (0..2) that is bare right now.</summary>
        public int OpenSide => openSide;

        private void Awake()
        {
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
            openSide = Mathf.Clamp(startOpenSide, 0, 2);
            float hue = TriangleKinds.HueShiftOf(this); // ward in its owner's colour
            wardColor = TriangleKinds.Tint(wardColor, hue);
            warnColor = TriangleKinds.Tint(warnColor, hue);
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Ward Plate " + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = Systems.Fx.Material;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 3;
                line.sortingOrder = 9;
                plates[i] = line;
            }
            GetComponent<EnemyHealth>().Died += () => { foreach (LineRenderer p in plates) if (p != null) p.enabled = false; enabled = false; };
        }

        private void OnEnable() => nextTurn = Time.time + turnInterval;

        private void OnDisable()
        {
            if (shape != null) shape.SetCornerGlow(-1, 0f, warnColor);
        }

        private void Update()
        {
            // Warn: the vertex between the bare side and the side it is moving to glows, brighter as the turn nears.
            float untilTurn = nextTurn - Time.time;
            int pivot = PivotCorner();
            if (untilTurn <= warnTime)
            {
                float p = 1f - Mathf.Clamp01(untilTurn / warnTime);
                bool on = Mathf.Repeat(Time.time * Mathf.Lerp(5f, 14f, p), 1f) < 0.6f;
                if (shape != null) shape.SetCornerGlow(pivot, on ? Mathf.Lerp(0.5f, 1f, p) : 0.3f, warnColor);
            }

            if (Time.time >= nextTurn)
            {
                previousOpen = openSide;
                openSide = Mod3(openSide + (turnDirection >= 0 ? 1 : -1));
                nextTurn = Time.time + turnInterval;
                turnedAt = Time.time;
                if (shape != null)
                {
                    shape.SetCornerGlow(-1, 0f, warnColor);
                    Systems.Fx.Ring(CornerWorld(pivot), wardColor, 0.9f, 0.2f, 0.06f);
                }
            }
        }

        private void LateUpdate()
        {
            if (shape == null) return;
            Vector2 centre = Centroid();
            float sinceTurn = Time.time - turnedAt;
            for (int i = 0; i < 3; i++)
            {
                bool covered = i != openSide;
                // A freshly covered side slides in over a moment; the side that just opened fades out.
                float grow = covered && i == previousOpen ? Mathf.Clamp01(sinceTurn / 0.18f) : 1f;
                bool fading = !covered && sinceTurn < 0.15f;
                plates[i].enabled = covered || fading;
                if (!plates[i].enabled) continue;

                Vector2 a = CornerWorld(i);
                Vector2 b = CornerWorld(Mod3(i + 1));
                Vector2 mid = (a + b) * 0.5f;
                Vector2 outward = (mid - centre).normalized * plateOffset;
                // Inset from the corners a little so the plates read as separate sides.
                Vector2 from = Vector2.Lerp(a, b, 0.12f) + outward;
                Vector2 to = Vector2.Lerp(a, b, Mathf.Lerp(0.12f, 0.88f, grow)) + outward;
                plates[i].SetPosition(0, from);
                plates[i].SetPosition(1, to);

                float flash = Mathf.Clamp01((flashUntil[i] - Time.time) / 0.2f);
                float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f + i);
                float alpha = fading ? 1f - sinceTurn / 0.15f : 1f;
                Color c = Color.Lerp(wardColor * new Color(1f, 1f, 1f, shimmer * alpha), Color.white, flash);
                plates[i].startColor = plates[i].endColor = c;
                plates[i].widthMultiplier = plateWidth * (1f + 0.6f * flash);
            }
        }

        /// <summary>
        /// Blocked unless the blow comes at the bare side. The side is the one whose outward face points most
        /// towards the attacker.
        /// </summary>
        public bool TryDeflect(Vector2 hitPoint, Vector2 attackerPosition)
        {
            if (shape == null) return false;
            int side = FacingSide(attackerPosition);
            if (side == openSide) return false;

            flashUntil[side] = Time.time + 0.2f;
            Vector2 a = CornerWorld(side), b = CornerWorld(Mod3(side + 1));
            Vector2 normal = ((a + b) * 0.5f - Centroid()).normalized;
            Systems.Fx.Sparks(hitPoint, Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg, wardColor, 12, 9f, 45f);
            return true;
        }

        /// <summary>The side whose outward face points most towards <paramref name="point"/>.</summary>
        private int FacingSide(Vector2 point)
        {
            Vector2 centre = Centroid();
            Vector2 toPoint = point - centre;
            int best = 0;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < 3; i++)
            {
                Vector2 mid = (CornerWorld(i) + CornerWorld(Mod3(i + 1))) * 0.5f;
                float dot = Vector2.Dot((mid - centre).normalized, toPoint);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            return best;
        }

        /// <summary>The vertex the ward sweeps past on its next turn (shared by the bare side and the next one).</summary>
        private int PivotCorner()
        {
            // Opening moves from side o to side o+1: they share corner o+1. Moving to o-1: they share corner o.
            return turnDirection >= 0 ? Mod3(openSide + 1) : openSide;
        }

        private Vector2 CornerWorld(int corner) => shape.transform.TransformPoint(shape.GetCornerPoint(corner));

        private Vector2 Centroid() => (CornerWorld(0) + CornerWorld(1) + CornerWorld(2)) / 3f;

        private static int Mod3(int i) => ((i % 3) + 3) % 3;
    }
}
