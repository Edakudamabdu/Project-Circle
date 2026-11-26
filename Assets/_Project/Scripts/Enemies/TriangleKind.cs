using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// What kind of triangle a shape is, which in Triangle World is also its rank:
    /// scalene (all sides uneven, the lowest), isosceles (two equal sides), equilateral (the "perfect").
    /// The more perfect, the more aggressive (see <see cref="TriangleKinds.Haste"/> and <see cref="TriangleKinds.Sight"/>).
    /// Standard = the plain shape with no rank adjustments (test rooms, boss minions).
    /// </summary>
    public enum TriangleKind { Standard, Scalene, Isosceles, Equilateral }

    /// <summary>
    /// A triangle with an individual look. Its abilities ask it (<see cref="TriangleKinds.HueShiftOf"/>) how far
    /// to turn their own colours, so a teal triangle fires teal lasers.
    /// </summary>
    public interface ITriangleLook
    {
        /// <summary>How far its palette is turned round the colour wheel (0 = the default palette).</summary>
        float HueShift { get; }
    }

    /// <summary>
    /// Reshapes a triangle visual into its kind, and gives each kind's aggression.
    ///
    /// The shapes are all drawn as equilateral meshes; the kind is a linear stretch-and-lean applied by a pair of
    /// transforms inserted above the visual ("Shape Frame" / "Shape Lean"). The map keeps horizontal lines
    /// horizontal and the floor line where it is, and an equilateral triangle looks the same after a third of a
    /// turn, so a tumbling triangle still lands flat on a side every step, just with its kind's proportions.
    /// Corner positions read through the visual's transform (lasers, glows, orbs) follow automatically.
    /// </summary>
    public static class TriangleKinds
    {
        /// <summary>x' = Width·x + Lean·y, y' = y (in the triangle's own space).</summary>
        public static void Shape(TriangleKind kind, out float width, out float lean)
        {
            switch (kind)
            {
                case TriangleKind.Scalene: width = 1.15f; lean = 0.38f; break;   // uneven sides, crooked apex
                case TriangleKind.Isosceles: width = 0.72f; lean = 0f; break;    // narrow: two long equal sides
                default: width = 1f; lean = 0f; break;                           // equilateral / standard
            }
        }

        /// <summary>Speed of decisions, movement and attacks: the more perfect, the faster.</summary>
        public static float Haste(TriangleKind kind)
        {
            switch (kind)
            {
                case TriangleKind.Scalene: return 0.8f;
                case TriangleKind.Equilateral: return 1.3f;
                default: return 1f;
            }
        }

        /// <summary>How far it notices the circle (detection and losing it): the more perfect, the further.</summary>
        public static float Sight(TriangleKind kind)
        {
            switch (kind)
            {
                case TriangleKind.Scalene: return 0.75f;
                case TriangleKind.Equilateral: return 1.25f;
                default: return 1f;
            }
        }

        /// <summary>Maps a point in the triangle's own space to its kind's shape.</summary>
        public static Vector2 Map(Vector2 p, float width, float lean) => new Vector2(width * p.x + lean * p.y, p.y);

        // ------------------------------------------------------------------ individual looks

        /// <summary>Hue of the default triangle palette (the warm red-orange every colour field starts from).</summary>
        private const float BaseHue = 0.02f;

        /// <summary>
        /// Hues the imperfect triangles are drawn from: clearly different from each other, so two neighbours rarely
        /// match. (The perfect, equilateral ones all keep the one default palette: they look alike on purpose.)
        /// </summary>
        private static readonly float[] Hues = { 0.02f, 0.12f, 0.3f, 0.45f, 0.58f, 0.7f, 0.82f };

        /// <summary>One triangle's individual proportions and colour.</summary>
        public struct Look
        {
            /// <summary>Stretch along x (see <see cref="Map"/>).</summary>
            public float Width;
            /// <summary>Shear of the apex (see <see cref="Map"/>).</summary>
            public float Lean;
            /// <summary>How far its whole palette (body and abilities) is turned round the colour wheel (0..1).</summary>
            public float HueShift;
        }

        /// <summary>
        /// The look of a triangle of <paramref name="kind"/> with this <paramref name="seed"/>. Scalene and isosceles
        /// triangles each get their own proportions and colour; equilateral and standard ones get the kind's shape
        /// and the default palette.
        /// </summary>
        public static Look LookFor(TriangleKind kind, int seed)
        {
            var look = new Look();
            Shape(kind, out look.Width, out look.Lean);
            if (kind != TriangleKind.Scalene && kind != TriangleKind.Isosceles) return look;

            var rng = new System.Random(seed);
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            if (kind == TriangleKind.Scalene)
            {
                // All three sides uneven: any width, and an apex pushed clearly off-centre to either side.
                look.Width = R(0.85f, 1.45f);
                look.Lean = R(0.2f, 0.6f) * (rng.Next(2) == 0 ? -1f : 1f);
            }
            else
            {
                // Two equal sides: symmetric, from needle-thin to just short of equilateral.
                look.Width = R(0.48f, 0.85f);
                look.Lean = 0f;
            }
            look.HueShift = Hues[rng.Next(Hues.Length)] - BaseHue;
            return look;
        }

        /// <summary>A stable seed from where a triangle was placed, so each one keeps its look between runs.</summary>
        public static int SeedFrom(Vector3 position)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(position.x * 10f), y = Mathf.RoundToInt(position.y * 10f);
                int h = x * 73856093 ^ y * 19349663;
                return h == 0 ? 1 : h;
            }
        }

        /// <summary>Turns a colour round the colour wheel by <paramref name="hueShift"/>, keeping its brightness and alpha.</summary>
        public static Color Tint(Color color, float hueShift)
        {
            if (Mathf.Approximately(hueShift, 0f)) return color;
            Color.RGBToHSV(color, out float h, out float s, out float v);
            Color c = Color.HSVToRGB(Mathf.Repeat(h + hueShift, 1f), s, v, true);
            c.a = color.a;
            return c;
        }

        /// <summary>The hue shift of the triangle that owns <paramref name="part"/> (0 if it has no individual look).</summary>
        public static float HueShiftOf(Component part)
        {
            var owner = part.GetComponentInParent<ITriangleLook>();
            return owner != null ? owner.HueShift : 0f;
        }

        // Handed from a splitting triangle to the pieces it spawns, so they keep its kind and colour.
        private static bool inheritPending;
        private static TriangleKind inheritKind;
        private static int inheritSeed;

        /// <summary>The next triangle that wakes up takes this kind and look (call right before Instantiate).</summary>
        public static void PassOn(TriangleKind kind, int seed)
        {
            inheritPending = true;
            inheritKind = kind;
            inheritSeed = seed;
        }

        /// <summary>Takes a pending <see cref="PassOn"/>, if any.</summary>
        public static bool TakeInheritance(out TriangleKind kind, out int seed)
        {
            kind = inheritKind;
            seed = inheritSeed;
            bool had = inheritPending;
            inheritPending = false;
            return had;
        }

        /// <summary>
        /// Puts <paramref name="visual"/> under a frame that stretches/leans it into <paramref name="kind"/>.
        /// Safe to call once; returns false (and does nothing) for an equilateral/standard shape.
        /// </summary>
        public static bool ApplyFrame(Transform visual, TriangleKind kind)
        {
            Shape(kind, out float a, out float b);
            return ApplyFrame(visual, a, b);
        }

        /// <summary>
        /// Puts <paramref name="visual"/> under a frame that maps it by x' = width·x + lean·y (see <see cref="Map"/>).
        /// Safe to call once; returns false (and does nothing) when there's nothing to change.
        /// </summary>
        public static bool ApplyFrame(Transform visual, float a, float b)
        {
            if (Mathf.Approximately(a, 1f) && Mathf.Approximately(b, 0f)) return false;
            if (visual.parent != null && visual.parent.name == "Shape Lean") return false; // already framed

            // M = [[a, b], [0, 1]] = R(theta) · diag(sx, sy) · R(phi)  (2×2 SVD, closed form).
            float m00 = a, m01 = b, m10 = 0f, m11 = 1f;
            float e = (m00 + m11) * 0.5f, f = (m00 - m11) * 0.5f, g = (m10 + m01) * 0.5f, h = (m10 - m01) * 0.5f;
            float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
            float sx = q + r, sy = q - r;
            float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
            float theta = (a2 + a1) * 0.5f * Mathf.Rad2Deg, phi = (a2 - a1) * 0.5f * Mathf.Rad2Deg;

            Transform parent = visual.parent;
            var frame = new GameObject("Shape Frame").transform;
            frame.SetParent(parent, false);
            frame.localRotation = Quaternion.Euler(0f, 0f, theta);
            frame.localScale = new Vector3(sx, sy, 1f);
            var leanT = new GameObject("Shape Lean").transform;
            leanT.SetParent(frame, false);
            leanT.localRotation = Quaternion.Euler(0f, 0f, phi);
            visual.SetParent(leanT, false);
            return true;
        }
    }
}
