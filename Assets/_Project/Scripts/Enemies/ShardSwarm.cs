using System.Collections.Generic;
using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Swarm Shards: a loose flock of tiny triangles. They circle above the circle; every so often one pulls
    /// back, glows, and dives straight at it, then rejoins the flock. Each shard breaks in one hit, and they make
    /// good pogo chains in the air.
    ///
    /// Put this on an empty object where the swarm should wait; it spawns <see cref="count"/> shards from
    /// <see cref="shardPrefab"/> (each needs a <see cref="SwarmShard"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShardSwarm : MonoBehaviour
    {
        [SerializeField] private GameObject shardPrefab;
        [SerializeField, Range(1, 12)] private int count = 5;

        [Header("Flock")]
        [Tooltip("Wakes when the circle comes this close to where it waits.")]
        [SerializeField, Min(1f)] private float wakeRange = 12f;

        [Tooltip("Gives up the chase and drifts home when the circle gets this far from where it waits.")]
        [SerializeField, Min(1f)] private float leashRange = 18f;

        [Tooltip("How far above the circle the flock circles.")]
        [SerializeField] private float hoverHeight = 3.8f;

        [SerializeField, Min(0.2f)] private float orbitRadius = 1.6f;
        [SerializeField] private float orbitSpeed = 120f; // degrees per second

        [Header("Dives")]
        [SerializeField, Min(0.2f)] private float diveInterval = 1.4f;
        [SerializeField, Min(0.05f)] private float aimTime = 0.4f;
        [SerializeField, Min(1f)] private float diveSpeed = 14f;
        [SerializeField, Min(0.1f)] private float diveTime = 0.6f;

        private readonly List<SwarmShard> shards = new List<SwarmShard>();
        private Transform player;
        private Vector2 centre;
        private bool awake;
        private float nextDive;

        public float AimTime => aimTime;
        public float DiveSpeed => diveSpeed;
        public float DiveTime => diveTime;
        public Transform Player => player;

        private void Start()
        {
            var movement = FindAnyObjectByType<Player.PlayerMovement>();
            if (movement != null) player = movement.transform;
            centre = transform.position;
            for (int i = 0; i < count; i++)
            {
                GameObject go = Instantiate(shardPrefab, centre, Quaternion.identity);
                var shard = go.GetComponent<SwarmShard>();
                shard.Bind(this, i * 360f / count);
                shards.Add(shard);
            }
            nextDive = Time.time + diveInterval;
        }

        private void Update()
        {
            shards.RemoveAll(s => s == null);
            if (shards.Count == 0) { Destroy(gameObject); return; }

            // Wake when the circle comes near home; give up and drift home once it gets far away.
            if (player != null)
            {
                float fromHome = Vector2.Distance(player.position, transform.position);
                if (!awake && fromHome <= wakeRange) awake = true;
                else if (awake && fromHome > leashRange) awake = false;
            }

            // The flock's centre drifts to a spot above the circle (or waits at home).
            Vector2 goal = awake && player != null ? (Vector2)player.position + Vector2.up * hoverHeight : (Vector2)transform.position;
            centre = Vector2.Lerp(centre, goal, 1f - Mathf.Exp(-2.5f * Time.deltaTime));

            // Send one shard diving now and then.
            if (awake && Time.time >= nextDive)
            {
                nextDive = Time.time + diveInterval;
                SwarmShard pick = null;
                foreach (SwarmShard s in shards) if (s.IsOrbiting) { pick = s; break; }
                if (pick != null) pick.BeginDive();
            }
        }

        /// <summary>Where a shard with orbit angle <paramref name="angle"/> should be right now.</summary>
        public Vector2 OrbitPoint(float angle)
        {
            float a = (angle + Time.time * orbitSpeed) * Mathf.Deg2Rad;
            return centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f) * orbitRadius;
        }
    }

}
