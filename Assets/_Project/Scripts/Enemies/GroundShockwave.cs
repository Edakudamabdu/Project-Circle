using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A shockwave that races along the floor after a slam: a row of triangle shards jutting out of the
    /// ground, rippling as they travel. Touching it hurts; jumping over it is the way to avoid it.
    ///
    /// It stops at walls and ledges, or after its travel distance, then sinks back into the ground.
    /// Built entirely in code (no prefab). Created by <see cref="TriangleSlam"/>.
    /// </summary>
    public sealed class GroundShockwave : MonoBehaviour
    {
        /// <summary>Tuning for a shockwave. Set on the component that creates them.</summary>
        [System.Serializable]
        public struct Settings
        {
            [Tooltip("Travel speed along the floor.")]
            [Min(0.1f)] public float speed;

            [Tooltip("How far it travels before sinking away.")]
            [Min(0.1f)] public float distance;

            [Tooltip("Height of the leading shard (and of the area that hurts).")]
            [Min(0.1f)] public float height;

            [Min(0)] public int damage;

            public Color topColor;
            public Color bottomColor;
            public Color outlineColor;

            public LayerMask playerLayers;
            public LayerMask groundLayers;

            public static Settings Default => new Settings
            {
                speed = 10f,
                distance = 9f,
                height = 1.7f,
                damage = 1,
                topColor = new Color(0.86f, 0.93f, 1f, 1f),
                bottomColor = new Color(0.55f, 0.68f, 0.92f, 1f),
                outlineColor = new Color(0.14f, 0.18f, 0.32f, 1f),
                playerLayers = 1 << 8,
                groundLayers = 1 << 3,
            };
        }

        private const int ShardCount = 3;
        private const float SinkTime = 0.22f;

        private Settings settings;
        private int direction;
        private float floorY;
        private float travelled;
        private bool sinking;
        private float sinkStartTime;
        private bool hasHit;
        private float nextChipTime;
        private readonly Transform[] shards = new Transform[ShardCount];
        private readonly float[] shardSize = new float[ShardCount];

        /// <summary>
        /// Starts a shockwave at <paramref name="origin"/> (on the floor surface) travelling in
        /// <paramref name="dir"/> (-1 left, +1 right).
        /// </summary>
        public static GroundShockwave Spawn(Vector2 origin, int dir, Settings settings, Material material)
        {
            var go = new GameObject("Ground Shockwave");
            go.transform.position = origin;
            var wave = go.AddComponent<GroundShockwave>();
            wave.settings = settings;
            wave.direction = dir >= 0 ? 1 : -1;
            wave.floorY = origin.y;

            // Leading shard biggest, smaller ones trailing behind it.
            for (int i = 0; i < ShardCount; i++)
            {
                var shard = new GameObject("Shard " + i);
                shard.transform.SetParent(go.transform, false);
                shard.AddComponent<MeshFilter>();
                var meshRenderer = shard.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                var mesh = shard.AddComponent<RoundedTriangleMesh>();

                float size = settings.height * (1f - 0.3f * i);
                wave.shardSize[i] = size;
                // A triangle of height h has circumradius 2h/3.
                mesh.Configure(size * 2f / 3f, size * 0.12f, 0.035f,
                               settings.topColor, settings.bottomColor, settings.outlineColor, 6);
                wave.shards[i] = shard.transform;
            }

            wave.LayoutShards(0f);
            return wave;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (sinking)
            {
                float t = (Time.time - sinkStartTime) / SinkTime;
                if (t >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                LayoutShards(t);
                return;
            }

            float step = settings.speed * dt;
            if (Blocked(step))
            {
                StartSinking();
                return;
            }

            transform.position += Vector3.right * (direction * step);
            travelled += step;

            // Chips kicked up behind the wave.
            if (Time.time >= nextChipTime)
            {
                nextChipTime = Time.time + 0.05f;
                Systems.Fx.Burst(new Vector2(transform.position.x, floorY + 0.05f), new Systems.Fx.BurstSettings
                {
                    count = 2, colorA = settings.bottomColor, colorB = settings.topColor,
                    speedMin = 2f, speedMax = 5f, sizeMin = 0.07f, sizeMax = 0.16f, lifeMin = 0.25f, lifeMax = 0.45f,
                    direction = direction > 0 ? 120f : 60f, spread = 25f, gravity = 2f, drag = 1f, triangles = true, sortingOrder = 11,
                });
            }
            if (travelled >= settings.distance) StartSinking();

            LayoutShards(0f);
            CheckHit();
        }

        /// <summary>Stops at a wall in front, or where the floor ends (a ledge or pit).</summary>
        private bool Blocked(float step)
        {
            Vector2 front = new Vector2(transform.position.x + direction * (step + 0.15f), floorY);
            if (Physics2D.Raycast(new Vector2(transform.position.x, floorY + 0.3f), Vector2.right * direction, step + 0.2f, settings.groundLayers).collider != null)
            {
                return true;
            }

            RaycastHit2D floor = Physics2D.Raycast(front + Vector2.up * 0.4f, Vector2.down, 0.8f, settings.groundLayers);
            return floor.collider == null;
        }

        /// <summary>Shards jut up from the floor with a travelling ripple; <paramref name="sink"/> 0..1 pulls them under.</summary>
        private void LayoutShards(float sink)
        {
            for (int i = 0; i < ShardCount; i++)
            {
                float size = shardSize[i];
                float ripple = 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 22f + i * 1.3f));
                float rise = ripple * (1f - sink);

                // Centroid sits a third of the height above the base: base on the floor, the rest pokes up.
                Transform shard = shards[i];
                shard.localScale = new Vector3(1f, rise, 1f);
                shard.position = new Vector3(
                    transform.position.x - direction * (i * settings.height * 0.55f),
                    floorY + size / 3f * rise,
                    0f);
            }
        }

        private void CheckHit()
        {
            if (hasHit || settings.damage <= 0) return;

            Vector2 centre = new Vector2(transform.position.x - direction * settings.height * 0.4f, floorY + settings.height * 0.45f);
            Vector2 size = new Vector2(settings.height * 1.2f, settings.height * 0.9f);
            Collider2D player = Physics2D.OverlapBox(centre, size, 0f, settings.playerLayers);
            if (player == null) return;

            var health = player.GetComponentInParent<PlayerHealth>();
            if (health != null && health.TakeDamage(settings.damage, centre)) hasHit = true;
        }

        private void StartSinking()
        {
            sinking = true;
            sinkStartTime = Time.time;
        }
    }
}
