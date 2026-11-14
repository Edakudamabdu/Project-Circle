using System;
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

        private void Awake() => CurrentHealth = maxHealth;

        /// <summary>
        /// Deals damage from a source position. Returns true if the hit landed (false if already dead or
        /// hit a moment ago).
        /// </summary>
        public bool TakeHit(int damage, Vector2 sourcePosition)
        {
            if (IsDead || damage <= 0 || Time.time < lastHitTime + hitCooldown) return false;

            lastHitTime = Time.time;
            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            Hit?.Invoke(damage, sourcePosition);

            if (CurrentHealth == 0)
            {
                IsDead = true;
                Died?.Invoke();
            }

            return true;
        }
    }
}
