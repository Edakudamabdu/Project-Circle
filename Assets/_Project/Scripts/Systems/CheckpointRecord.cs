using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Remembers the last Mirror the circle rested at, across scene changes: which scene it is in and its name.
    /// When the circle dies, it respawns at that Mirror, loading its scene first if needed.
    ///
    /// Kept in memory for the current play session only. (A save system can store these two strings later.)
    /// </summary>
    public static class CheckpointRecord
    {
        /// <summary>Scene of the last Mirror rested at (null = none yet).</summary>
        public static string SceneName { get; private set; }

        /// <summary>Name of the last Mirror rested at.</summary>
        public static string MirrorName { get; private set; }

        /// <summary>
        /// True while a respawn is being carried into the checkpoint's scene: the Mirror there places the
        /// circle in front of it when the scene starts.
        /// </summary>
        public static bool RespawnPending { get; set; }

        public static bool HasCheckpoint => !string.IsNullOrEmpty(SceneName);

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneName = null;
            MirrorName = null;
            RespawnPending = false;
        }

        /// <summary>Records a Mirror as the checkpoint.</summary>
        public static void Set(string sceneName, string mirrorName)
        {
            SceneName = sceneName;
            MirrorName = mirrorName;
        }

        /// <summary>True if this Mirror is the checkpoint.</summary>
        public static bool Is(string sceneName, string mirrorName) =>
            HasCheckpoint && SceneName == sceneName && MirrorName == mirrorName;
    }
}
