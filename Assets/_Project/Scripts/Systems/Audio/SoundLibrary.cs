using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Systems.Audio
{
    /// <summary>
    /// The game's sound effects, one entry per <see cref="SoundId"/>: which clip(s) to play, how loud, and how
    /// much to vary the pitch so repeats don't sound identical. Lives at Assets/_Project/Resources/SoundLibrary
    /// (it must stay in a Resources folder, under that name, so <see cref="Sfx"/> can find it).
    ///
    /// To change a sound: select the asset, find its entry and drag a different clip in. No code needed.
    /// </summary>
    [CreateAssetMenu(menuName = "Project Circle/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
    {
        /// <summary>One sound effect.</summary>
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private SoundId id;

            [Tooltip("One is picked at random each time.")]
            [SerializeField] private AudioClip[] clips = new AudioClip[0];

            [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
            [SerializeField, Range(0.25f, 2f)] private float pitch = 1f;

            [Tooltip("Random pitch change either way, e.g. 0.06 = up to 6% higher or lower.")]
            [SerializeField, Range(0f, 0.5f)] private float pitchJitter = 0.05f;

            [Tooltip("The same sound won't start again sooner than this (stops pairs or bursts stacking up).")]
            [SerializeField, Min(0f)] private float minInterval = 0.04f;

            public SoundId Id => id;
            public float Volume => volume;
            public float Pitch => pitch;
            public float PitchJitter => pitchJitter;
            public float MinInterval => minInterval;

            public AudioClip PickClip()
            {
                if (clips == null || clips.Length == 0) return null;
                return clips[UnityEngine.Random.Range(0, clips.Length)];
            }

            /// <summary>Editor setup only (the library builder).</summary>
            public Entry(SoundId id, AudioClip[] clips, float volume, float pitch, float pitchJitter, float minInterval)
            {
                this.id = id;
                this.clips = clips;
                this.volume = volume;
                this.pitch = pitch;
                this.pitchJitter = pitchJitter;
                this.minInterval = minInterval;
            }
        }

        [Tooltip("Scales every sound effect (not music or ambience).")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<SoundId, Entry> lookup;

        public float MasterVolume => masterVolume;

        /// <summary>The entry for a sound, or null if it isn't set up.</summary>
        public Entry Get(SoundId id)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<SoundId, Entry>();
                foreach (Entry e in entries) if (e != null) lookup[e.Id] = e;
            }
            return lookup.TryGetValue(id, out Entry entry) ? entry : null;
        }

        /// <summary>Editor setup only (the library builder): replaces every entry.</summary>
        public void SetEntries(List<Entry> newEntries)
        {
            entries = newEntries;
            lookup = null;
        }

        private void OnValidate() => lookup = null;
    }
}
