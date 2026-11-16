using System.Collections;
using System.Collections.Generic;
using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Call the Guard (phase 1 only). The boss plants itself, its top corner flaring, and calls a minion
    /// (<see cref="perCall"/>, default 1): ground triangles drop from the ceiling onto open floor (never onto a
    /// ledge), flyers swoop in from above. With several per call, never two projectile shooters together.
    /// It only calls again once every minion from the last call is gone.
    ///
    /// While it calls, it stands still: the opening is to rush it instead of the minions.
    /// Minions break apart when phase 2 starts or the boss is beaten, and vanish if the fight resets.
    /// The guards are bound to him: when the circle breaks one, its light flies back and wounds him.
    /// </summary>
    public sealed class BossSummonMove : BossMove
    {
        [Header("Minions")]
        [SerializeField] private GameObject laserTriangle;
        [SerializeField] private GameObject orbCaster;
        [SerializeField] private GameObject flyer;
        [SerializeField, Min(1)] private int perCall = 1;

        [Tooltip("Height above the floor they appear at.")]
        [SerializeField, Min(1f)] private float dropHeight = 10f;

        [Tooltip("Never spawn a minion closer than this to the circle.")]
        [SerializeField, Min(0f)] private float minDistanceFromPlayer = 4f;

        [Tooltip("Damage the Warden takes when the circle breaks one of his guards.")]
        [SerializeField, Min(0)] private int minionKillDamage = 4;

        [Tooltip("Seconds the broken guard's light takes to fly back into him.")]
        [SerializeField, Min(0.05f)] private float returnTime = 0.55f;

        [Header("Call")]
        [SerializeField, Min(0.05f)] private float chargeTime = 0.7f;
        [SerializeField] private Color callColor = new Color(1f, 0.85f, 0.55f, 1f);

        private readonly List<GameObject> alive = new List<GameObject>();
        private bool subscribed;
        private bool clearing;

        public override bool CanStart()
        {
            // Only once the last guard is gone.
            alive.RemoveAll(m => m == null);
            return alive.Count == 0 && Boss.IsGrounded;
        }

        public override IEnumerator Run()
        {
            Subscribe();

            // Plant and call: the top corner flares while it trembles.
            for (float t = 0f; t < chargeTime; t += Time.deltaTime)
            {
                float p = t / chargeTime;
                Boss.Shape.SetCornerGlow(0, p, callColor);
                Boss.SetPose(Random.insideUnitCircle * 0.05f * p, 0f);
                yield return null;
            }
            Boss.ResetPose();
            Boss.Shape.SetCornerGlow(-1, 0f, callColor);

            // Hit into the next phase while calling: the call fails (no minions in phase 2).
            if (Boss.Phase > MaxPhase) yield break;

            if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.2f, 0.3f);

            // Who comes: random picks, never a second projectile shooter in the same call.
            bool shooterCalled = false;
            for (int i = 0; i < perCall; i++)
            {
                GameObject prefab = Pick(ref shooterCalled);
                if (prefab == null) continue;
                bool flying = prefab == flyer;
                float y = Boss.ArenaFloorY + (flying ? dropHeight - 2f : dropHeight);
                Vector2 at = new Vector2(PickX(y, needsFloor: !flying), y);
                GameObject minion = Instantiate(prefab, at, Quaternion.identity);

                // Summoned to fight: no patrolling, straight at the circle.
                var walker = minion.GetComponent<TriangleEnemy>();
                if (walker != null) walker.AlwaysHunt = true;
                var flier = minion.GetComponent<FlyingTriangle>();
                if (flier != null) flier.AlwaysHunt = true;
                alive.Add(minion);

                // The guards are bound to the Warden: break one and its light flies back to wound him.
                var minionHealth = minion.GetComponent<EnemyHealth>();
                if (minionHealth != null)
                {
                    GameObject m = minion;
                    minionHealth.Died += () => { if (!clearing && Boss != null) Boss.StartCoroutine(ReturnLight(m.transform.position)); };
                }

                // A column of light marks where it arrives; ground guards land in a puff of dust.
                Systems.Fx.LightColumn(new Vector2(at.x, Boss.ArenaFloorY), Boss.ArenaCeilingY - Boss.ArenaFloorY, callColor * new Color(1f, 1f, 1f, 0.35f), 0.9f, 1.6f);
                if (!flying) Boss.StartCoroutine(PuffWhenLanded(minion));
            }
        }

        /// <summary>A guard was broken: its light streaks back into the Warden and hurts him.</summary>
        private IEnumerator ReturnLight(Vector2 from)
        {
            var go = new GameObject("Returning Light");
            go.transform.position = from;
            var glow = go.AddComponent<VertexGlow>();
            go.GetComponent<MeshRenderer>().sharedMaterial = Systems.Fx.Material;
            glow.Color = callColor;
            glow.Radius = 0.45f;
            glow.BurstRadius = 2f;
            glow.Intensity = 1.3f;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Systems.Fx.Material;
            trail.time = 0.25f;
            trail.widthCurve = AnimationCurve.Linear(0f, 0.3f, 1f, 0f);
            trail.startColor = callColor;
            trail.endColor = new Color(callColor.r, callColor.g, callColor.b, 0f);
            trail.sortingOrder = 9;

            // Arc up and over, then into him.
            for (float t = 0f; t < returnTime; t += Time.deltaTime)
            {
                if (Boss == null || !Boss.gameObject.activeInHierarchy) { Destroy(go); yield break; }
                float p = t / returnTime;
                Vector2 to = Boss.Shape.transform.position;
                Vector2 pos = Vector2.Lerp(from, to, p * p) + Vector2.up * Mathf.Sin(p * Mathf.PI) * 2.5f;
                go.transform.position = pos;
                yield return null;
            }

            Vector2 hit = Boss.Shape.transform.position;
            Destroy(go);
            if (Boss.Health.IsDead) yield break;
            Boss.Health.TakeHit(minionKillDamage, hit + Vector2.up);
            Systems.Fx.Ring(hit, callColor, 3f, 0.35f, 0.15f);
            Systems.Fx.Sparks(hit, 90f, callColor, 14, 8f, 180f);
            if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.15f, 0.2f);
        }

        /// <summary>A dust puff the moment a dropped guard hits the floor.</summary>
        private IEnumerator PuffWhenLanded(GameObject minion)
        {
            var walker = minion != null ? minion.GetComponent<TriangleEnemy>() : null;
            float start = Time.time;
            while (minion != null && Time.time - start < 3f)
            {
                if (Time.time - start > 0.15f && walker != null && walker.IsGrounded)
                {
                    Systems.Fx.Puff(new Vector2(minion.transform.position.x, Boss.ArenaFloorY), 1.3f);
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>A random minion; the projectile shooters (orb caster, flyer) at most once per call.</summary>
        private GameObject Pick(ref bool shooterCalled)
        {
            var options = new List<GameObject>();
            if (laserTriangle != null) options.Add(laserTriangle);
            if (!shooterCalled)
            {
                if (orbCaster != null) options.Add(orbCaster);
                if (flyer != null) options.Add(flyer);
            }
            if (options.Count == 0) return null;
            GameObject pick = options[Random.Range(0, options.Count)];
            if (pick == orbCaster || pick == flyer) shooterCalled = true;
            return pick;
        }

        /// <summary>
        /// A spot in the arena away from the circle and the boss. With <paramref name="needsFloor"/>, only where
        /// a drop from <paramref name="dropY"/> lands on the arena floor (not on a ledge it would get stuck on).
        /// </summary>
        private float PickX(float dropY, bool needsFloor)
        {
            float min = Boss.ArenaMinX + 2f, max = Boss.ArenaMaxX - 2f;
            float fallback = Boss.ArenaCentreX;
            for (int tries = 0; tries < 40; tries++)
            {
                float x = Random.Range(min, max);
                if (needsFloor && !LandsOnFloor(x, dropY)) continue;
                fallback = x;
                bool clearOfPlayer = Boss.Target == null || Mathf.Abs(x - Boss.Target.position.x) >= minDistanceFromPlayer;
                bool clearOfBoss = Mathf.Abs(x - Boss.Body.position.x) >= Boss.SideLength;
                bool clearOfOthers = true; // spread the guards out
                foreach (GameObject m in alive) if (m != null && Mathf.Abs(m.transform.position.x - x) < 4f) clearOfOthers = false;
                if (clearOfPlayer && clearOfBoss && clearOfOthers) return x;
            }
            return fallback;
        }

        /// <summary>True if something dropped at <paramref name="x"/> lands on the arena floor (with room either side).</summary>
        private bool LandsOnFloor(float x, float dropY)
        {
            for (int i = -1; i <= 1; i++) // check its width too, so it doesn't clip a ledge's edge
            {
                RaycastHit2D hit = Physics2D.Raycast(new Vector2(x + i * 0.9f, dropY), Vector2.down, dropY - Boss.ArenaFloorY + 1f, Boss.GroundLayers);
                if (hit.collider == null || hit.point.y > Boss.ArenaFloorY + 0.3f) return false;
            }
            return true;
        }

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            Boss.PhaseChanged += _ => BreakAll();
            Boss.Defeated += BreakAll;
            Boss.FightReset += RemoveAll;
        }

        /// <summary>Every minion shatters (phase 2, or the boss is beaten).</summary>
        private void BreakAll()
        {
            clearing = true; // these weren't broken by the circle: no light returns to wound him
            foreach (GameObject m in alive)
            {
                if (m == null) continue;
                var health = m.GetComponent<EnemyHealth>();
                if (health != null && !health.IsDead) health.TakeHit(999, m.transform.position + Vector3.up);
                else Destroy(m);
            }
            alive.Clear();
            clearing = false;
        }

        /// <summary>Minions vanish without a trace (the fight is starting over).</summary>
        private void RemoveAll()
        {
            foreach (GameObject m in alive) if (m != null) Destroy(m);
            alive.Clear();
        }

        public override void Cancel()
        {
            if (Boss == null) return;
            Boss.ResetPose();
            if (Boss.Shape != null) Boss.Shape.SetCornerGlow(-1, 0f, callColor);
        }
    }
}
