using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>One shard of a <see cref="ShardSwarm"/>: orbits the flock, dives at the circle when told to.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class SwarmShard : MonoBehaviour
    {
        private enum Phase { Orbit, Aim, Dive, Return }

        [SerializeField] private RoundedTriangleMesh shape;
        [SerializeField] private Color diveColor = new Color(1f, 0.55f, 0.4f, 1f);

        [Tooltip("What a dive stops on (the Ground layer).")]
        [SerializeField] private LayerMask groundLayers = 1 << 3;

        private ShardSwarm swarm;
        private float angle;
        private Phase phase = Phase.Orbit;
        private float phaseStart;
        private Vector2 diveDirection;

        public bool IsOrbiting => phase == Phase.Orbit;

        public void Bind(ShardSwarm owner, float orbitAngle)
        {
            swarm = owner;
            angle = orbitAngle;
        }

        private void Awake()
        {
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
        }

        public void BeginDive()
        {
            phase = Phase.Aim;
            phaseStart = Time.time;
        }

        private void Update()
        {
            if (swarm == null) { Destroy(gameObject); return; }
            float t = Time.time - phaseStart;
            Vector2 pos = transform.position;

            switch (phase)
            {
                case Phase.Orbit:
                    transform.position = Vector2.Lerp(pos, swarm.OrbitPoint(angle), 1f - Mathf.Exp(-6f * Time.deltaTime));
                    PointAlong(swarm.OrbitPoint(angle + 10f) - pos);
                    break;

                case Phase.Aim:
                {
                    // Pull back from the circle, glowing, pointed at it.
                    Vector2 toPlayer = swarm.Player != null ? (Vector2)swarm.Player.position - pos : Vector2.down;
                    transform.position = pos - toPlayer.normalized * 1.5f * Time.deltaTime;
                    PointAlong(toPlayer);
                    if (shape != null) shape.SetCornerGlow(0, t / swarm.AimTime, diveColor);
                    if (t >= swarm.AimTime)
                    {
                        diveDirection = toPlayer.normalized;
                        phase = Phase.Dive;
                        phaseStart = Time.time;
                    }
                    break;
                }

                case Phase.Dive:
                {
                    // Stop on the ground instead of passing through it.
                    float step = swarm.DiveSpeed * Time.deltaTime;
                    RaycastHit2D ground = Physics2D.Raycast(pos, diveDirection, step + 0.25f, groundLayers);
                    if (ground.collider != null)
                    {
                        transform.position = ground.point - diveDirection * 0.25f;
                        Systems.Fx.Dust(ground.point, 0.6f, 0.4f);
                        t = swarm.DiveTime; // end the dive now
                    }
                    else transform.position = pos + diveDirection * step;

                    if (t >= swarm.DiveTime)
                    {
                        if (shape != null) shape.SetCornerGlow(-1, 0f, diveColor);
                        phase = Phase.Return;
                        phaseStart = Time.time;
                    }
                    break;
                }

                case Phase.Return:
                {
                    Vector2 home = swarm.OrbitPoint(angle);
                    transform.position = Vector2.MoveTowards(pos, home, 9f * Time.deltaTime);
                    PointAlong(home - pos);
                    if (Vector2.Distance(transform.position, home) < 0.3f) phase = Phase.Orbit;
                    break;
                }
            }
        }

        /// <summary>Turns the shard so its point (corner 0) faces <paramref name="direction"/>.</summary>
        private void PointAlong(Vector2 direction)
        {
            if (shape == null || direction.sqrMagnitude < 1e-4f) return;
            float a = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            shape.transform.rotation = Quaternion.Slerp(shape.transform.rotation, Quaternion.Euler(0f, 0f, a), 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
    }
}
