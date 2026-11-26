using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Gives a non-<see cref="TriangleEnemy"/> triangle (a Ceiling Sentry, a flyer...) the look of its kind:
    /// scalene in the Slums, isosceles on the Terraces, each with its own proportions and colour (its abilities
    /// follow the colour). Looks only; <see cref="TriangleEnemy"/> has this built in (its Kind field), along with
    /// the matching aggression.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // before the body scripts read the shape's colours
    public sealed class TriangleShapeKind : MonoBehaviour, ITriangleLook
    {
        [SerializeField] private TriangleKind kind = TriangleKind.Scalene;

        [Tooltip("Picks its individual proportions and colour. 0 = pick from where it's placed. Type any other number to choose a look.")]
        [SerializeField] private int lookSeed;

        private TriangleKinds.Look look;
        private bool lookReady;

        /// <summary>How far its palette is turned round the colour wheel.</summary>
        public float HueShift { get { EnsureLook(); return look.HueShift; } }

        private void EnsureLook()
        {
            if (lookReady) return;
            lookReady = true;
            look = TriangleKinds.LookFor(kind, lookSeed != 0 ? lookSeed : TriangleKinds.SeedFrom(transform.position));
        }

        private void Awake()
        {
            EnsureLook();
            var shape = GetComponentInChildren<RoundedTriangleMesh>();
            if (shape == null) return;
            TriangleKinds.ApplyFrame(shape.transform, look.Width, look.Lean);
            shape.SetFill(TriangleKinds.Tint(shape.FillColor, look.HueShift), TriangleKinds.Tint(shape.FillBottomColor, look.HueShift));
        }
    }
}
