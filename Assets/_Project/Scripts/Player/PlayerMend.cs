using System;
using ProjectCircle.Enemies;
using ProjectCircle.Systems;
using ProjectCircle.UI;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Mend: the circle's heal.
    ///
    /// The cape is woven from the resistance of every shape that was thrown away, and its strings don't only
    /// fight: they hold the circle together. Landing hits and parrying gathers Threads (shown under the health
    /// bar). Holding Heal while standing on the ground gathers a warm glow around the circle, light drawn in
    /// from the strings and sparks rising off it (unlike the parry's instant, cold cape-wrap); after
    /// <see cref="mendTime"/> one Thread is spent and one health point is mended.
    /// Getting hit, letting go, or leaving the ground cancels it (nothing is spent).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerHealth), typeof(PlayerMovement))]
    public sealed class PlayerMend : MonoBehaviour
    {
        [Header("Threads")]
        [Tooltip("Most Threads that can be stored (each one is one heal).")]
        [SerializeField, Min(1)] private int maxThreads = 3;

        [Tooltip("Threads gained per cape hit (0.125 = eight hits per Thread).")]
        [SerializeField, Min(0f)] private float threadsPerHit = 0.125f;

        [Tooltip("Threads gained per successful parry (standing together counts for more).")]
        [SerializeField, Min(0f)] private float threadsPerParry = 0.34f;

        [Tooltip("Threads the circle starts each scene with.")]
        [SerializeField, Min(0f)] private float startingThreads = 1f;

        [Header("Mending")]
        [Tooltip("Seconds the button must be held to mend one health point.")]
        [SerializeField, Min(0.1f)] private float mendTime = 0.6f;

        [SerializeField] private Color mendColor = new Color(1f, 0.9f, 0.65f, 1f);

        /// <summary>Raised when the Threads change. Arguments: threads (can be fractional), max.</summary>
        public event Action<float, int> ThreadsChanged;

        /// <summary>Raised each time a health point is mended.</summary>
        public event Action Mended;

        public float Threads { get; private set; }
        public int MaxThreads => maxThreads;

        /// <summary>True while the circle is mending (standing still, cape wrapped).</summary>
        public bool IsMending { get; private set; }

        /// <summary>0..1 progress of the current mend.</summary>
        public float Progress => IsMending ? Mathf.Clamp01((Time.time - mendStart) / mendTime) : 0f;

        private PlayerInputReader input;
        private PlayerHealth health;
        private PlayerMovement movement;
        private PlayerAttack attack;
        private PlayerParry parry;
        private float mendStart;
        private float nextGlowTime;
        private float retryAfter;
        private VertexGlow aura;

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
            attack = GetComponent<PlayerAttack>();
            parry = GetComponent<PlayerParry>();
            Threads = Mathf.Min(startingThreads, maxThreads);
            CreateAura();
        }

        /// <summary>A warm glow around the circle that grows while it mends (the parry is a cold, instant wrap).</summary>
        private void CreateAura()
        {
            var go = new GameObject("Mend Aura");
            go.transform.SetParent(transform, false);
            aura = go.AddComponent<VertexGlow>();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Fx.Material;
            renderer.sortingOrder = 4; // behind the circle
            aura.Color = mendColor;
            aura.Radius = 1.1f;
            aura.BurstRadius = 2.4f;
            aura.Intensity = 0f;
        }

        private void LateUpdate()
        {
            if (aura == null) return;
            float target = IsMending ? 0.35f + Progress * 1.1f + 0.15f * Mathf.Sin(Time.time * 20f) : 0f;
            aura.Intensity = Mathf.MoveTowards(aura.Intensity, target, Time.deltaTime * (IsMending ? 8f : 4f));
        }

        private void Start()
        {
            ThreadMeter.Instance.Bind(this);
            ThreadsChanged?.Invoke(Threads, maxThreads);
        }

        private void OnEnable()
        {
            if (attack != null) attack.HitLanded += HandleHitLanded;
            if (parry != null) parry.Deflected += HandleDeflected;
            health.Hit += HandleHurt;
        }

        private void OnDisable()
        {
            if (attack != null) attack.HitLanded -= HandleHitLanded;
            if (parry != null) parry.Deflected -= HandleDeflected;
            health.Hit -= HandleHurt;
            if (IsMending) Stop();
        }

        private void Update()
        {
            bool held = input.HealHeld(this);

            if (!IsMending)
            {
                if (held && Time.time >= retryAfter && CanMend()) Begin();
                return;
            }

            if (!held || health.IsDead || !movement.IsGrounded)
            {
                Stop(); // let go, died or knocked off the ground: nothing spent
                return;
            }

            // Light keeps gathering in, and warm sparks rise off the circle.
            if (Time.time >= nextGlowTime)
            {
                nextGlowTime = Time.time + 0.1f;
                Fx.Converge(transform.position, mendColor, 0.3f, 1.6f, 4);
                Fx.Burst((Vector2)transform.position + Vector2.down * 0.3f, new Fx.BurstSettings
                {
                    count = 2, colorA = mendColor, colorB = Color.white,
                    speedMin = 1.5f, speedMax = 3f, sizeMin = 0.05f, sizeMax = 0.11f, lifeMin = 0.4f, lifeMax = 0.7f,
                    direction = 90f, spread = 25f, gravity = -0.2f, drag = 1f, area = new Vector2(0.8f, 0.2f), sortingOrder = 13,
                });
            }

            if (Progress >= 1f) Complete();
        }

        private bool CanMend() =>
            Threads >= 1f && health.CurrentHealth < health.MaxHealth && !health.IsDead &&
            movement.IsGrounded && !movement.IsDashing && !movement.IsKnockedBack;

        private void Begin()
        {
            IsMending = true;
            mendStart = Time.time;
            nextGlowTime = 0f;
            input.AddControlLock(this); // stand still while mending (the cape stays loose: wrapping is the parry)
            Fx.Converge(transform.position, mendColor, mendTime * 0.8f, 2.4f, 16);
        }

        private void Stop()
        {
            IsMending = false;
            input.RemoveControlLock(this);
        }

        private void Complete()
        {
            Stop();
            Threads = Mathf.Max(0f, Threads - 1f);
            health.Heal(1);
            ThreadsChanged?.Invoke(Threads, maxThreads);
            Mended?.Invoke();

            Fx.Ring(transform.position, mendColor, 1.8f, 0.4f, 0.14f);
            Fx.Sparks(transform.position, 90f, mendColor, 12, 5f, 60f);
            if (aura != null) aura.Burst();

            // Keep holding to mend again (a short beat in between).
            retryAfter = Time.time + 0.25f;
        }

        private void HandleHurt(int damage, Vector2 source)
        {
            if (IsMending) Stop(); // hit while mending: it's broken off
        }

        private void HandleHitLanded(Collider2D target) => Add(threadsPerHit);

        private void HandleDeflected(Vector2 point) => Add(threadsPerParry);

        /// <summary>Gains Threads (capped at the maximum).</summary>
        public void Add(float amount)
        {
            if (amount <= 0f) return;
            float before = Threads;
            Threads = Mathf.Min(maxThreads, Threads + amount);
            if (!Mathf.Approximately(before, Threads)) ThreadsChanged?.Invoke(Threads, maxThreads);
        }
    }
}
