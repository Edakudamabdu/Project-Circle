using System;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Combat
{
    /// <summary>
    /// Health for an enemy. Attacks call <see cref="TakeHit"/>; the enemy's own scripts listen to
    /// <see cref="Hit"/> (to flinch, flash, get knocked back) and <see cref="Died"/> (to break apart).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 3;

        [Tooltip("Seconds after a hit during which further hits are ignored (stops one swing hitting twice).")]
        [SerializeField, Min(0f)] private float hitCooldown = 0.08f;

        /// <summary>Raised when a hit lands. Arguments: damage, where the hit came from.</summary>
        public event Action<int, Vector2> Hit;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action Died;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsDead { get; private set; }

        private float lastHitTime = float.NegativeInfinity;
        private float invulnerableUntil = float.NegativeInfinity;

        /// <summary>True while hits are ignored (see <see cref="MakeInvulnerable"/>).</summary>
        public bool IsInvulnerable => Time.time < invulnerableUntil;

        /// <summary>
        /// Extra hit points granted from outside (an Overseer's command). Hits use these up before real health,
        /// and they can be taken away at any time (<see cref="SetWard"/> 0).
        /// </summary>
        public int Ward { get; private set; }

        /// <summary>Raised when <see cref="Ward"/> changes (granted, chipped or removed).</summary>
        public event Action<int> WardChanged;

        private void Awake() => CurrentHealth = maxHealth;

        /// <summary>Sets the extra hit points (0 removes them).</summary>
        public void SetWard(int points)
        {
            points = Mathf.Max(0, points);
            if (points == Ward) return;
            Ward = points;
            WardChanged?.Invoke(Ward);
        }

        /// <summary>Ignores all hits for <paramref name="seconds"/> (e.g. pieces of a Splitter as they fly apart).</summary>
        public void MakeInvulnerable(float seconds) => invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);

        /// <summary>Back to full health and alive (e.g. a boss fight restarting after the player died).</summary>
        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            lastHitTime = float.NegativeInfinity;
        }

        /// <summary>Sets the health directly (1..max) without counting as a hit, e.g. bosses sharing health.</summary>
        public void SetHealth(int value)
        {
            if (IsDead) return;
            CurrentHealth = Mathf.Clamp(value, 1, maxHealth);
        }

        /// <summary>
        /// Deals damage from a source position. Returns true if the hit landed (false if already dead or
        /// hit a moment ago).
        /// </summary>
        public bool TakeHit(int damage, Vector2 sourcePosition)
        {
            if (IsDead || damage <= 0 || Time.time < lastHitTime + hitCooldown || IsInvulnerable) return false;

            lastHitTime = Time.time;

            // The ward soaks damage first.
            if (Ward > 0)
            {
                int soaked = Mathf.Min(Ward, damage);
                damage -= soaked;
                Ward -= soaked;
                WardChanged?.Invoke(Ward);
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            Hit?.Invoke(damage, sourcePosition);

            if (CurrentHealth == 0)
            {
                IsDead = true;
                // Breaking apart: big ones (bosses) deeper and a little softer, their arena adds its own sound.
                bool big = MaxHealth >= 20;
                Sfx.Play(SoundId.EnemyShatter, transform.position, big ? 0.7f : 1f, big ? 0.7f : 1f);
                Died?.Invoke();
            }

            return true;
        }
    }
}
