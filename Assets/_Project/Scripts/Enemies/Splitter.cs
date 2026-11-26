using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Splitter: when this triangle breaks, it splits into smaller triangles (<see cref="childPrefab"/>) that
    /// burst apart in a wide fan, landing spread out across the floor. Chain it (large → medium → small) for a
    /// triangle that splits twice: breaking a shape apart doesn't make the problem go away.
    ///
    /// The pieces can't be hurt while they fly apart (<see cref="spawnInvulnerability"/>), and they are born
    /// spread out, so one swing can't clear them all.
    /// Put it on a <see cref="TriangleEnemy"/> next to its EnemyHealth.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class Splitter : MonoBehaviour
    {
        [Tooltip("What it splits into (leave empty on the smallest size).")]
        [SerializeField] private GameObject childPrefab;

        [SerializeField, Min(1)] private int count = 3;

        [Tooltip("Sideways speed of the outermost pieces (the middle ones get less).")]
        [SerializeField, Min(0f)] private float spreadSpeed = 7f;

        [Tooltip("Upward speed of each piece.")]
        [SerializeField, Min(0f)] private float popSpeed = 7f;

        [Tooltip("Width of the row the pieces start in, in world units.")]
        [SerializeField, Min(0f)] private float spawnWidth = 1.6f;

        [Tooltip("Seconds the pieces can't be hurt while they fly apart.")]
        [SerializeField, Min(0f)] private float spawnInvulnerability = 0.45f;

        [Tooltip("Each piece keeps this much more distance from the circle than the one before, so they stay apart.")]
        [SerializeField, Min(0f)] private float distanceStagger = 1.5f;

        [SerializeField] private LayerMask groundLayers = 1 << 3;

        private EnemyHealth health;

        private void Awake() => health = GetComponent<EnemyHealth>();
        private void OnEnable() => health.Died += HandleDied;
        private void OnDisable() => health.Died -= HandleDied;

        private void HandleDied()
        {
            if (childPrefab == null) return;
            Vector2 at = transform.position;

            // Fan out: -1 (far left) .. +1 (far right). With an odd count the middle piece pops straight up.
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : i / (count - 1f) * 2f - 1f;
                Vector2 offset = new Vector2(t * spawnWidth * 0.5f, 0.2f);
                Vector2 spawn = at + offset;
                // Don't start a piece inside a wall.
                if (Physics2D.OverlapCircle(spawn, 0.3f, groundLayers) != null) spawn = at + Vector2.up * 0.2f;

                // The pieces keep the parent's kind, proportions and colour.
                var parentShape = GetComponent<TriangleEnemy>();
                if (parentShape != null) TriangleKinds.PassOn(parentShape.Kind, parentShape.LookSeed);
                GameObject child = Instantiate(childPrefab, spawn, Quaternion.identity);
                var walker = child.GetComponent<TriangleEnemy>();
                if (walker != null)
                {
                    walker.AlwaysHunt = true; // born angry
                    // Each piece stands at its own distance from the circle, so they don't regroup into one clump.
                    walker.PreferredDistance += i * distanceStagger;
                }
                var childHealth = child.GetComponent<EnemyHealth>();
                if (childHealth != null) childHealth.MakeInvulnerable(spawnInvulnerability);
                var body = child.GetComponent<Rigidbody2D>();
                if (body != null) body.linearVelocity = new Vector2(t * spreadSpeed, popSpeed * (1f - 0.25f * Mathf.Abs(t)));
            }
            Systems.Fx.Ring(at, new Color(0.6f, 1f, 0.9f, 0.8f), 2.5f, 0.3f, 0.14f);
        }
    }
}
