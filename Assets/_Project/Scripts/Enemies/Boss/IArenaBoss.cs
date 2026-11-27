using System;
using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// What a <see cref="Systems.BossArena"/> needs from the boss it runs: a single boss (<see cref="BossBrain"/>)
    /// or a group fought together (<see cref="TwinIsosceles"/>).
    /// </summary>
    public interface IArenaBoss
    {
        /// <summary>Name shown when the fight starts.</summary>
        string BossName { get; }

        /// <summary>Raised once when the whole fight is won.</summary>
        event Action Defeated;

        /// <summary>Plays before the arena locks, e.g. bosses dropping in (empty for a boss already standing there).</summary>
        IEnumerator Entrance();

        /// <summary>Starts the fight (called once the arena's gates have closed).</summary>
        void BeginFight();

        /// <summary>Stops everything and puts the boss(es) back as at the start (the player died).</summary>
        void ResetFight(Vector2 home);

        /// <summary>Shows the health bar(s) for this fight.</summary>
        void ShowHealthBar();

        /// <summary>Hides the health bar(s).</summary>
        void HideHealthBar();
    }
}
