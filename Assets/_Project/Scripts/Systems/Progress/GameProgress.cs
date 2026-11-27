using System;
using System.Collections.Generic;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// What the circle has achieved on this run, carried from scene to scene: whether it wears the cape, which
    /// cape strings (abilities) it has, and named story flags (Vertex Seals, the Mould, freed villages, broken
    /// walls...). Not saved to disk yet: it lasts until the game (or Play Mode) is closed.
    ///
    /// The first scene that loads starts the run (<see cref="Begin"/>): normally the opening, but when a region
    /// is played straight from the editor its <see cref="RegionInfo"/> decides what the circle starts with.
    ///
    /// Flag names used so far (strings, so new ones need no code):
    ///   Seal.Warden, Seal.Twins, Seal.Refractor   — Vertex Seals (count with <see cref="SealCount"/>)
    ///   Boss.Hierarch                              — the final boss of Triangle World
    ///   Mould.Triangle                             — the Triangle Mould (opens M gates and the Rank Gate)
    ///   Freed.Slums                                — the Scalene Slums are freed
    ///   Wall.(name)                                — a crumbling wall that has been broken
    /// </summary>
    public static class GameProgress
    {
        /// <summary>Raised whenever anything changes (abilities, cape or flags).</summary>
        public static event Action Changed;

        /// <summary>True once a scene has started the run.</summary>
        public static bool IsStarted { get; private set; }

        public static bool HasCape { get; private set; }
        public static PlayerAbility Abilities { get; private set; }

        private static readonly HashSet<string> flags = new HashSet<string>();

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsStarted = false;
            HasCape = false;
            Abilities = PlayerAbility.None;
            flags.Clear();
            Changed = null;
            CarriedHealth = -1;
            CarriedThreads = -1f;
        }

        /// <summary>
        /// Health the circle had when it left the last scene (-1 = none: arrive at full health, e.g. a fresh run).
        /// Written by <see cref="Player.PlayerHealth"/> as the scene unloads, read back by the next scene's circle.
        /// </summary>
        public static int CarriedHealth { get; set; } = -1;

        /// <summary>Mend Threads carried over from the last scene (-1 = none: the scene's starting amount).</summary>
        public static float CarriedThreads { get; set; } = -1f;

        /// <summary>Starts the run with this cape state and these abilities (only the first call counts).</summary>
        public static void Begin(bool hasCape, PlayerAbility abilities, IEnumerable<string> startFlags = null)
        {
            if (IsStarted) return;
            IsStarted = true;
            HasCape = hasCape;
            Abilities = abilities;
            if (startFlags != null) foreach (string f in startFlags) if (!string.IsNullOrEmpty(f)) flags.Add(f);
            Changed?.Invoke();
        }

        /// <summary>Records the cape and abilities (called by <see cref="PlayerAbilities"/>).</summary>
        public static void SetCape(bool hasCape, PlayerAbility abilities)
        {
            IsStarted = true;
            HasCape = hasCape;
            Abilities = abilities;
            Changed?.Invoke();
        }

        public static bool Has(string flag) => !string.IsNullOrEmpty(flag) && flags.Contains(flag);

        /// <summary>True if every flag in the list is set (an empty or null list is true).</summary>
        public static bool HasAll(string[] list)
        {
            if (list == null) return true;
            foreach (string f in list) if (!string.IsNullOrEmpty(f) && !flags.Contains(f)) return false;
            return true;
        }

        public static void Set(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !flags.Add(flag)) return;
            Changed?.Invoke();
        }

        /// <summary>How many Vertex Seals the circle holds (flags starting with "Seal.").</summary>
        public static int SealCount
        {
            get
            {
                int n = 0;
                foreach (string f in flags) if (f.StartsWith("Seal.", StringComparison.Ordinal)) n++;
                return n;
            }
        }
    }
}
