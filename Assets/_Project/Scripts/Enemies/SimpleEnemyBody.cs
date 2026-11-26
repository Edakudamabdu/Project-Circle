using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// The shared "body" of enemies that don't use <see cref="TriangleEnemy"/>: flashes white when hit, and
    /// breaks apart (<see cref="TriangleShatter"/>) and removes itself when its <see cref="EnemyHealth"/> runs out.
    /// Put it next to the EnemyHealth, and point it at the enemy's shape.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class SimpleEnemyBody : MonoBehaviour
    {
        [SerializeField] private RoundedTriangleMesh shape;
        [SerializeField] private Color glowColor = new Color(1f, 0.8f, 0.6f, 1f);
        [SerializeField, Min(0f)] private float flashTime = 0.08f;

        /// <summary>The enemy's shape (for attacks that need its corners).</summary>
        public RoundedTriangleMesh Shape => shape;

        private EnemyHealth health;
        private Color top, bottom;
        private float flashUntil;
        private bool flashing;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
            if (shape != null)
            {
                top = shape.FillColor;
                bottom = shape.FillBottomColor;
            }
            glowColor = TriangleKinds.Tint(glowColor, TriangleKinds.HueShiftOf(this)); // its own colour, if it has one
        }

        private void OnEnable()
        {
            health.Hit += HandleHit;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            health.Hit -= HandleHit;
            health.Died -= HandleDied;
        }

        private void Update()
        {
            bool shouldFlash = Time.time < flashUntil;
            if (shape == null || shouldFlash == flashing) return;
            flashing = shouldFlash;
            if (flashing) shape.SetFill(Color.white, Color.Lerp(bottom, Color.white, 0.6f));
            else shape.SetFill(top, bottom);
        }

        private void HandleHit(int damage, Vector2 source) => flashUntil = Time.time + flashTime;

        private void HandleDied()
        {
            if (shape != null)
            {
                Material material = shape.GetComponent<MeshRenderer>().sharedMaterial;
                var player = FindAnyObjectByType<Player.PlayerMovement>();
                Vector2 from = player != null ? (Vector2)player.transform.position : (Vector2)transform.position + Vector2.down;
                float radius = shape.Circumradius * Mathf.Max(shape.transform.lossyScale.x, shape.transform.lossyScale.y);
                TriangleShatter.Spawn(shape.transform.position, radius, top, bottom, new Color(0.16f, 0.08f, 0.1f, 1f), glowColor, material, from);
            }
            Destroy(gameObject);
        }
    }
}
