using System;
using System.Collections;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// The circle's health. Other systems damage or heal it through <see cref="TakeDamage"/> and
    /// <see cref="Heal"/>; UI listens to <see cref="HealthChanged"/>.
    ///
    /// After a hit the circle is briefly invulnerable and flickers. At zero health it disappears and,
    /// after a short delay, respawns with full health: at the last Mirror rested at (loading that Mirror's
    /// scene if it is elsewhere, see <see cref="Systems.CheckpointRecord"/>), or where the scene started it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField, Min(1)] private int maxHealth = 5;

        [Header("After A Hit")]
        [Tooltip("Seconds of invulnerability after taking damage.")]
        [SerializeField, Min(0f)] private float invulnerableTime = 1f;

        [Tooltip("How fast the circle flickers while invulnerable (flashes per second).")]
        [SerializeField, Min(1f)] private float flickerRate = 14f;

        [Tooltip("Renderers that flicker while invulnerable and hide on death. Left empty, all child renderers are used.")]
        [SerializeField] private Renderer[] flickerRenderers;

        [Tooltip("If on, the circle can't be hurt while dashing (dash through attacks).")]
        [SerializeField] private bool invulnerableWhileDashing = true;

        [Header("Death")]
        [Tooltip("Seconds between dying and respawning.")]
        [SerializeField, Min(0f)] private float respawnDelay = 1.2f;

        /// <summary>Raised whenever health changes (damage, healing, respawn). Arguments: current, max.</summary>
        public event Action<int, int> HealthChanged;

        /// <summary>Raised when damage is taken. Argument: amount.</summary>
        public event Action<int> Damaged;

        /// <summary>Raised when damage is taken, with where the hit came from (for effects). Arguments: amount, source position.</summary>
        public event Action<int, Vector2> Hit;

        /// <summary>Raised when health reaches zero.</summary>
        public event Action Died;

        /// <summary>Raised after respawning with full health.</summary>
        public event Action Respawned;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        public bool IsInvulnerable =>
            IsDead || Time.time < invulnerableUntil || Time.time < graceUntil ||
            (invulnerableWhileDashing && movement != null && movement.IsDashing);

        /// <summary>
        /// Brief, invisible protection from damage (no flicker). Used when bouncing off an enemy with a down
        /// attack, so touching it in the same instant doesn't also hurt.
        /// </summary>
        public void GrantGrace(float seconds) => graceUntil = Mathf.Max(graceUntil, Time.time + seconds);

        private float graceUntil = float.NegativeInfinity;

        private Rigidbody2D body;
        private PlayerMovement movement;
        private PlayerCape cape;
        private Vector2 respawnPoint;
        private float invulnerableUntil = float.NegativeInfinity;
        private bool renderersVisible = true;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            movement = GetComponent<PlayerMovement>();
            cape = GetComponentInChildren<PlayerCape>();

            if (flickerRenderers == null || flickerRenderers.Length == 0)
            {
                flickerRenderers = GetComponentsInChildren<Renderer>();
            }

            CurrentHealth = maxHealth;
            respawnPoint = body.position;
        }

        private void Start() => HealthChanged?.Invoke(CurrentHealth, maxHealth);

        private void Update()
        {
            if (IsDead) return;

            // Flicker while invulnerable after a hit.
            bool flickering = Time.time < invulnerableUntil;
            bool visible = !flickering || Mathf.Repeat(Time.time * flickerRate, 1f) < 0.5f;
            SetRenderersVisible(visible);
        }

        #region Public API

        /// <summary>
        /// Deals damage with no knockback (e.g. hazards). Returns true if the damage was applied.
        /// </summary>
        public bool TakeDamage(int amount) => ApplyDamage(amount, body.position, knockback: false);

        /// <summary>
        /// Deals damage from a source position: the circle is knocked back away from it.
        /// Returns true if the damage was applied (false while invulnerable, dashing or dead).
        /// </summary>
        public bool TakeDamage(int amount, Vector2 sourcePosition) => ApplyDamage(amount, sourcePosition, knockback: true);

        private bool ApplyDamage(int amount, Vector2 sourcePosition, bool knockback)
        {
            if (amount <= 0 || IsInvulnerable) return false;

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            Damaged?.Invoke(amount);
            Hit?.Invoke(amount, sourcePosition);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (knockback && CurrentHealth > 0 && movement != null)
            {
                movement.ApplyKnockback(body.position - sourcePosition);
            }

            if (CurrentHealth == 0)
            {
                Die();
            }
            else
            {
                invulnerableUntil = Time.time + invulnerableTime;
            }

            return true;
        }

        /// <summary>Restores health, up to the maximum.</summary>
        public void Heal(int amount)
        {
            if (amount <= 0 || IsDead || CurrentHealth == maxHealth) return;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        /// <summary>Where the circle respawns after dying (e.g. the last Mirror it rested at).</summary>
        public void SetRespawnPoint(Vector2 point) => respawnPoint = point;

        #endregion

        #region Death & respawn

        private void Die()
        {
            IsDead = true;
            Died?.Invoke();

            // Freeze and hide the circle until it respawns.
            if (movement != null) movement.enabled = false;
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
            SetRenderersVisible(false);

            StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            // The last Mirror rested at is in another scene: go there. That scene's Mirror places the circle,
            // and its fresh Player starts at full health.
            string currentScene = gameObject.scene.name;
            if (Systems.CheckpointRecord.HasCheckpoint && Systems.CheckpointRecord.SceneName != currentScene)
            {
                Systems.CheckpointRecord.RespawnPending = true;
                Systems.ScreenFader.LoadScene(Systems.CheckpointRecord.SceneName, 0.35f);
                yield break;
            }

            body.position = respawnPoint;
            transform.position = respawnPoint;
            body.simulated = true;

            if (movement != null)
            {
                movement.enabled = true;
                movement.ResetMotion();
            }

            if (cape != null) cape.ResetCape();

            IsDead = false;
            CurrentHealth = maxHealth;
            invulnerableUntil = Time.time + invulnerableTime;
            SetRenderersVisible(true);

            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            Respawned?.Invoke();
        }

        private void SetRenderersVisible(bool visible)
        {
            if (visible == renderersVisible) return;
            renderersVisible = visible;

            foreach (Renderer r in flickerRenderers)
            {
                if (r != null) r.enabled = visible;
            }
        }

        #endregion

        #region Testing helpers (Inspector ⋮ menu, Play Mode only)

        [ContextMenu("Test: Take 1 Damage (hit from the right)")]
        private void TestTakeDamage() => TakeDamage(1, body.position + Vector2.right);

        [ContextMenu("Test: Heal 1")]
        private void TestHeal() => Heal(1);

        #endregion
    }
}
