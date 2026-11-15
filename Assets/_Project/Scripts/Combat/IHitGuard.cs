using UnityEngine;

namespace ProjectCircle.Combat
{
    /// <summary>
    /// Something that can block the player's attacks before they reach an <see cref="EnemyHealth"/>, such as a
    /// shield with weak points. Put a component implementing it on the enemy (or a parent of its collider).
    /// </summary>
    public interface IHitGuard
    {
        /// <summary>
        /// Asked when an attack touches the enemy. Return true to deflect it (no damage; the attacker is
        /// parried back), false to let it through.
        /// </summary>
        /// <param name="hitPoint">Where the attack touched the enemy.</param>
        /// <param name="attackerPosition">Where the attacker is.</param>
        bool TryDeflect(Vector2 hitPoint, Vector2 attackerPosition);
    }
}
