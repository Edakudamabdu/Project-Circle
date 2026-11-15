using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// The one place that freezes time for impact ("hit-stop"). Any number of systems can ask for a freeze at
    /// once; they overlap safely (the longest wins) and time always returns to normal speed afterwards.
    ///
    /// Before this existed, two systems each saved and restored <c>Time.timeScale</c> themselves. When their
    /// freezes overlapped, one restored the other's slowed value and the game stayed in slow motion.
    ///
    /// Creates itself on first use and survives scene loads, so a freeze can never outlive its scene.
    /// </summary>
    public sealed class HitStop : MonoBehaviour
    {
        private static HitStop runner;
        private static float freezeUntil;   // unscaled time
        private static float freezeScale = 1f;
        private static bool frozen;

        /// <summary>True while a hit-stop is slowing time.</summary>
        public static bool IsFrozen => frozen;

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            runner = null;
            freezeUntil = 0f;
            freezeScale = 1f;
            frozen = false;
        }

        /// <summary>
        /// Slows time to <paramref name="timeScale"/> for <paramref name="seconds"/> of real time. Overlapping
        /// requests extend the freeze to the latest end and use the slowest scale.
        /// </summary>
        public static void Freeze(float seconds, float timeScale = 0.05f)
        {
            if (seconds <= 0f) return;
            EnsureRunner();

            freezeUntil = Mathf.Max(freezeUntil, Time.unscaledTime + seconds);
            freezeScale = frozen ? Mathf.Min(freezeScale, timeScale) : timeScale;
            frozen = true;
            Time.timeScale = freezeScale;
        }

        /// <summary>Ends any freeze immediately (e.g. when changing scenes).</summary>
        public static void Release()
        {
            if (!frozen) return;
            frozen = false;
            Time.timeScale = 1f;
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("Hit Stop");
            DontDestroyOnLoad(go);
            runner = go.AddComponent<HitStop>();
        }

        // Update still runs every frame while time is slowed, so the freeze always ends on schedule.
        private void Update()
        {
            if (frozen && Time.unscaledTime >= freezeUntil) Release();
        }

        private void OnDestroy()
        {
            if (runner == this)
            {
                Release();
                runner = null;
            }
        }
    }
}
