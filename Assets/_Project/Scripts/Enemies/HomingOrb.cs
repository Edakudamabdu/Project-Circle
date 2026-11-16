using System;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A glowing orb of vertex power that hunts the player (fired by <see cref="TriangleVolley"/>).
    ///
    /// Life of an orb:
    /// 1. Forming  — grows on a corner of the triangle that is making it. Harmless; the owner moves it.
    /// 2. Drifting — released: flies straight out from the corner and slows down, so a volley fans apart.
    /// 3. Homing   — speeds up towards the player while turning to follow them. Its turning fades to nothing
    ///               over a couple of seconds, so it can be shaken off: a double jump, a dash through it (the
    ///               dash's invulnerability), or a sharp change of direction makes it overshoot.
    /// 4. Coasting — flies straight on, then fades out. It pops early if it touches the ground or the player.
    ///
    /// Built entirely in code (no prefab), using <see cref="VertexGlow"/> for the look and a trail.
    /// </summary>
    public sealed class HomingOrb : MonoBehaviour
    {
        /// <summary>Tuning for an orb. Set on the component that fires them.</summary>
        [Serializable]
        public struct Settings
        {
            [Tooltip("Colour of the orb's glow and trail.")]
            public Color color;

            [Tooltip("Size of the glow (visual only).")]
            [Min(0.05f)] public float glowRadius;

            [Tooltip("Radius that hurts the player. Smaller than the glow, so near misses feel fair.")]
            [Min(0.01f)] public float hitRadius;

            [Min(0)] public int damage;

            [Tooltip("Speed it is flung out from its corner when released.")]
            [Min(0f)] public float driftSpeed;

            [Tooltip("Seconds it drifts outward before it starts hunting.")]
            [Min(0f)] public float driftTime;

            [Tooltip("Top speed while hunting. The circle runs at about 8.5, so keep this lower.")]
            [Min(0.1f)] public float maxSpeed;

            [Tooltip("How quickly it reaches top speed.")]
            [Min(0.1f)] public float acceleration;

            [Tooltip("How fast it can turn towards the player while hunting, in degrees per second.")]
            [Min(0f)] public float turnRate;

            [Tooltip("Seconds of hunting before it stops turning and just flies straight.")]
            [Min(0f)] public float homingTime;

            [Tooltip("Fraction of Homing Time spent turning at full rate (lining up on the player). After that the turning fades to nothing, which is what lets a late dodge shake it off.")]
            [Range(0f, 1f)] public float fullTurnFraction;

            [Tooltip("Seconds from release until it fades out on its own.")]
            [Min(0.1f)] public float lifetime;

            public LayerMask playerLayers;
            public LayerMask groundLayers;

            public static Settings Default => new Settings
            {
                color = new Color(0.82f, 0.5f, 1f, 1f),
                glowRadius = 0.42f,
                hitRadius = 0.18f,
                damage = 1,
                driftSpeed = 3.2f,
                driftTime = 0.35f,
                maxSpeed = 5.5f,
                acceleration = 8f,
                turnRate = 220f,
                homingTime = 1.6f,
                fullTurnFraction = 0.3f,
                lifetime = 3.6f,
                playerLayers = 1 << 8,
                groundLayers = 1 << 3,
            };
        }

        private enum Phase { Forming, Drifting, Homing, Coasting, Popping }

        private const float FadeTime = 0.3f;  // fade at the end of its life
        private const float PopTime = 0.35f;  // burst ring after hitting something

        private static readonly Color DeflectedColor = new Color(0.85f, 0.95f, 1f, 1f);
        private const int EnemyLayers = 1 << 9;
        private const float DeflectSpeedBoost = 1.35f;
        private const float DeflectedLifetime = 3f; // seconds a parried orb flies before fading

        private Settings settings;
        private Transform target;
        private bool deflected; // parried by the circle: now flies straight and hurts enemies instead
        private VertexGlow glow;
        private TrailRenderer trail;
        private Phase phase = Phase.Forming;
        private float formProgress;
        private float releaseTime;
        private float popTime;
        private Vector2 velocity;

        /// <summary>True once released and still able to hurt.</summary>
        public bool IsDangerous => phase == Phase.Drifting || phase == Phase.Homing || phase == Phase.Coasting;

        /// <summary>True once parried by the circle (it now hurts enemies instead).</summary>
        public bool IsDeflected => deflected;

        /// <summary>
        /// Optional: once parried, the orb homes in on this (e.g. the boss that fired it) instead of flying
        /// straight back. Leave null for the normal straight return.
        /// </summary>
        public Transform ReturnTo { get; set; }

        /// <summary>Creates an orb in its forming state at <paramref name="position"/>.</summary>
        public static HomingOrb Spawn(Vector2 position, Settings settings, Material material, Transform target)
        {
            var go = new GameObject("Homing Orb");
            go.transform.position = position;

            var orb = go.AddComponent<HomingOrb>();
            orb.settings = settings;
            orb.target = target;

            orb.glow = go.AddComponent<VertexGlow>();
            orb.glow.GetComponent<MeshRenderer>().sharedMaterial = material;
            orb.glow.Color = settings.color;
            orb.glow.Radius = settings.glowRadius;
            orb.glow.BurstRadius = settings.glowRadius * 2.2f;
            orb.glow.Intensity = 0f;

            orb.trail = go.AddComponent<TrailRenderer>();
            orb.trail.sharedMaterial = material;
            orb.trail.time = 0.25f;
            orb.trail.minVertexDistance = 0.05f;
            orb.trail.widthCurve = AnimationCurve.Linear(0f, settings.glowRadius * 0.55f, 1f, 0f);
            orb.trail.sortingOrder = 7;
            orb.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orb.trail.receiveShadows = false;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(settings.color, Color.white, 0.4f), 0f), new GradientColorKey(settings.color, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            orb.trail.colorGradient = gradient;
            orb.trail.emitting = false;

            return orb;
        }

        #region Control (used by the triangle firing it)

        /// <summary>While forming: where the orb sits and how grown it is (0..1).</summary>
        public void SetForming(Vector2 position, float progress)
        {
            if (phase != Phase.Forming) return;
            transform.position = position;
            formProgress = Mathf.Clamp01(progress);
        }

        /// <summary>Lets the orb go, flung out in <paramref name="direction"/>. From now on it can hurt.</summary>
        public void Release(Vector2 direction)
        {
            if (phase != Phase.Forming) return;
            phase = Phase.Drifting;
            releaseTime = Time.time;
            velocity = direction.normalized * settings.driftSpeed;
            trail.Clear();
            trail.emitting = true;
            glow.Burst();
        }

        /// <summary>Removes the orb with a small pop (e.g. its triangle was interrupted or died before releasing it).</summary>
        public void Cancel() => Pop();

        #endregion

        private void Update()
        {
            float dt = Time.deltaTime;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 18f);

            switch (phase)
            {
                case Phase.Forming:
                    glow.Intensity = formProgress * formProgress * 1.1f * pulse;
                    return;

                case Phase.Popping:
                    if (Time.time - popTime >= PopTime) Destroy(gameObject);
                    return;
            }

            float age = Time.time - releaseTime;
            if (age >= settings.lifetime)
            {
                Destroy(gameObject);
                return;
            }

            Steer(age, dt);
            transform.position += (Vector3)(velocity * dt);

            // Overcharged (whiter) while it hunts, fading out at the end of its life.
            float fade = Mathf.Clamp01((settings.lifetime - age) / FadeTime);
            glow.Intensity = 1.25f * pulse * fade;

            if (fade > 0.5f) CheckHits();
        }

        /// <summary>Drift outward and slow down, then hunt with fading turn rate, then fly straight.</summary>
        private void Steer(float age, float dt)
        {
            if (deflected)
            {
                // Sent back by a parry. Flies straight, unless it has an owner to return to: then it turns
                // hard towards that owner, so a parry always pays off.
                if (ReturnTo != null)
                {
                    Vector2 toOwner = (Vector2)ReturnTo.position - (Vector2)transform.position;
                    float turn = Vector2.SignedAngle(velocity, toOwner);
                    velocity = Rotate(velocity, Mathf.Clamp(turn, -900f * dt, 900f * dt));
                }
                return;
            }

            if (age < settings.driftTime)
            {
                velocity *= Mathf.Exp(-4f * dt); // slow down so the volley hangs for a beat before hunting

                // An orb flung at the floor bounces off it instead of popping straight away.
                Vector2 next = (Vector2)transform.position + velocity * dt;
                if (Physics2D.OverlapCircle(next, settings.hitRadius, settings.groundLayers) != null)
                {
                    velocity = -velocity * 0.6f;
                }
                return;
            }

            float huntAge = age - settings.driftTime;
            phase = huntAge < settings.homingTime ? Phase.Homing : Phase.Coasting;

            Vector2 direction = velocity.sqrMagnitude > 1e-4f ? velocity.normalized : Vector2.down;
            if (phase == Phase.Homing && target != null)
            {
                // Turn hard towards the player at first (while still slow, so it swings round tightly),
                // then less and less, so a well-timed dodge makes it overshoot.
                float fullTime = settings.homingTime * settings.fullTurnFraction;
                float turnLeft = huntAge < fullTime
                    ? 1f
                    : 1f - (huntAge - fullTime) / Mathf.Max(0.01f, settings.homingTime - fullTime);
                float maxTurn = settings.turnRate * turnLeft * dt;
                Vector2 toTarget = (Vector2)target.position - (Vector2)transform.position;
                float angle = Vector2.SignedAngle(direction, toTarget);
                direction = Rotate(direction, Mathf.Clamp(angle, -maxTurn, maxTurn));
            }

            float speed = Mathf.MoveTowards(velocity.magnitude, settings.maxSpeed, settings.acceleration * dt);
            velocity = direction * speed;
        }

        private void CheckHits()
        {
            Vector2 position = transform.position;

            if (phase != Phase.Drifting && Physics2D.OverlapCircle(position, settings.hitRadius * 0.8f, settings.groundLayers) != null)
            {
                Pop();
                return;
            }

            if (deflected)
            {
                // Sent back by a parry: it now hurts enemies.
                Collider2D enemy = Physics2D.OverlapCircle(position, settings.hitRadius * 1.6f, EnemyLayers);
                if (enemy == null) return;
                var enemyHealth = enemy.GetComponentInParent<Combat.EnemyHealth>();
                if (enemyHealth != null) enemyHealth.TakeHit(Mathf.Max(1, settings.damage), position);
                Pop();
                return;
            }

            Collider2D player = Physics2D.OverlapCircle(position, settings.hitRadius, settings.playerLayers);
            if (player == null) return;

            // Parried: fly back the way it came.
            var parry = player.GetComponentInParent<PlayerParry>();
            if (parry != null && parry.TryDeflect(position))
            {
                Deflect(velocity.sqrMagnitude > 0.01f ? -velocity.normalized : Vector2.up);
                return;
            }

            // A dashing (invulnerable) player passes straight through and the orb flies on.
            var health = player.GetComponentInParent<PlayerHealth>();
            if (health != null && health.TakeDamage(settings.damage, position)) Pop();
        }

        /// <summary>
        /// Sent back by the circle's parry: flies straight in <paramref name="direction"/>, faster, glowing pale,
        /// and from now on hurts enemies instead of the circle. Its lifetime starts over.
        /// </summary>
        public void Deflect(Vector2 direction)
        {
            if (!IsDangerous && phase != Phase.Forming) return;

            deflected = true;
            phase = Phase.Coasting;
            // Restart its life, already past the drifting and hunting stages, with a fresh few seconds to fly back
            // (long-homing orbs would otherwise fade out on the way).
            releaseTime = Time.time - settings.driftTime - settings.homingTime;
            settings.lifetime = settings.driftTime + settings.homingTime + DeflectedLifetime;
            float speed = Mathf.Max(velocity.magnitude, settings.maxSpeed) * DeflectSpeedBoost;
            velocity = direction.normalized * speed;

            glow.Color = DeflectedColor;
            glow.Burst();
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(DeflectedColor, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }

        private void Pop()
        {
            if (phase == Phase.Popping) return;
            phase = Phase.Popping;
            popTime = Time.time;
            glow.Intensity = 0f;
            glow.Burst();
            trail.emitting = false;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
