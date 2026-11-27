using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Systems.Audio
{
    /// <summary>
    /// Plays sound effects and the region's ambience loop. Call from anywhere:
    /// <c>Sfx.Play(SoundId.Swing, transform.position)</c>.
    ///
    /// - Sounds come from the <see cref="SoundLibrary"/> asset (Resources/SoundLibrary).
    /// - A sound given a world position is panned left/right by where it is on screen, and fades out when it
    ///   is far off screen (so an enemy across the level is quiet). Without a position it plays centred.
    /// - Ambience: one loop at a time, cross-faded when a new region sets a different one; it carries on
    ///   uninterrupted through doors between regions that share it.
    ///
    /// A hidden object (DontDestroyOnLoad) holds the pooled AudioSources; it is made the first time it's needed.
    /// </summary>
    public static class Sfx
    {
        private static SoundLibrary library;
        private static bool libraryMissing;
        private static SfxHost host;

        private static SoundLibrary Library
        {
            get
            {
                if (library == null && !libraryMissing)
                {
                    library = Resources.Load<SoundLibrary>("SoundLibrary");
                    if (library == null)
                    {
                        libraryMissing = true;
                        Debug.LogWarning("Sfx: no SoundLibrary in a Resources folder; sound effects are off.");
                    }
                }
                return library;
            }
        }

        private static SfxHost Host
        {
            get
            {
                if (host == null)
                {
                    var go = new GameObject("Sfx (runtime)");
                    Object.DontDestroyOnLoad(go);
                    host = go.AddComponent<SfxHost>();
                }
                return host;
            }
        }

        // Entering Play Mode without a domain reload keeps statics: start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            library = null;
            libraryMissing = false;
            host = null;
        }

        /// <summary>Plays a sound centred (UI, things on the circle itself).</summary>
        public static void Play(SoundId id, float volumeScale = 1f, float pitchScale = 1f)
            => PlayInternal(id, null, volumeScale, pitchScale);

        /// <summary>Plays a sound at a world position (panned and faded by where it is on screen).</summary>
        public static void Play(SoundId id, Vector2 position, float volumeScale = 1f, float pitchScale = 1f)
            => PlayInternal(id, position, volumeScale, pitchScale);

        private static void PlayInternal(SoundId id, Vector2? position, float volumeScale, float pitchScale)
        {
            if (!Application.isPlaying || Library == null) return;
            SoundLibrary.Entry entry = Library.Get(id);
            if (entry == null) return;
            AudioClip clip = entry.PickClip();
            if (clip == null) return;
            Host.PlayOneShot(id, entry, clip, position, volumeScale * Library.MasterVolume, pitchScale);
        }

        /// <summary>
        /// Starts a sound that keeps going until <see cref="Stop"/> is called (or it ends, if not looping), e.g. a
        /// crackle held under a cutscene shot. Returns its source (null if the sound isn't set up).
        /// </summary>
        public static AudioSource PlayHeld(SoundId id, float volumeScale = 1f, bool loop = true)
        {
            if (!Application.isPlaying || Library == null) return null;
            SoundLibrary.Entry entry = Library.Get(id);
            AudioClip clip = entry != null ? entry.PickClip() : null;
            if (clip == null) return null;
            return Host.PlayHeld(clip, entry.Volume * volumeScale * Library.MasterVolume, entry.Pitch, loop);
        }

        /// <summary>Plays a piece of music once, centred (e.g. the title theme). Returns its source.</summary>
        public static AudioSource PlayMusic(AudioClip clip, float volume)
        {
            if (!Application.isPlaying || clip == null) return null;
            return Host.PlayHeld(clip, volume, 1f, loop: false);
        }

        /// <summary>Fades out and stops a sound started with <see cref="PlayHeld"/> or <see cref="PlayMusic"/>.</summary>
        public static void Stop(AudioSource source, float fade = 0.5f)
        {
            if (source == null || host == null) return;
            host.StopHeld(source, fade);
        }

        /// <summary>
        /// Sets the ambience loop (null = silence), cross-fading over <paramref name="fade"/> seconds.
        /// Setting the loop that is already playing just adjusts its volume.
        /// </summary>
        public static void SetAmbience(AudioClip clip, float volume, float fade = 2f)
        {
            if (!Application.isPlaying) return;
            Host.SetAmbience(clip, volume, fade);
        }

        /// <summary>Turns the ambience down (e.g. under boss music) or back up.</summary>
        public static void DuckAmbience(bool duck, float fade = 1f)
        {
            if (!Application.isPlaying || host == null) return;
            host.Duck(duck ? 0.25f : 1f, fade);
        }
    }

    /// <summary>The runtime half of <see cref="Sfx"/>: the AudioSource pool and the ambience players.</summary>
    [AddComponentMenu("")] // made by Sfx, not added by hand
    public sealed class SfxHost : MonoBehaviour
    {
        private const int PoolSize = 24;

        // Screen-space falloff: full volume within this many units of the camera centre, silent beyond the second.
        private const float PanWidth = 14f;
        private const float FullVolumeDistance = 16f;
        private const float SilentDistance = 32f;

        private readonly List<AudioSource> pool = new List<AudioSource>();
        private readonly HashSet<AudioSource> held = new HashSet<AudioSource>(); // reserved until stopped
        private readonly Dictionary<SoundId, float> lastPlayed = new Dictionary<SoundId, float>();
        private int next;

        private AudioSource ambienceA, ambienceB;
        private float duck = 1f, duckTarget = 1f, duckSpeed = 1f;
        private Coroutine ambienceFade;

        private void Awake()
        {
            for (int i = 0; i < PoolSize; i++) pool.Add(MakeSource());
            ambienceA = MakeSource();
            ambienceB = MakeSource();
            ambienceA.loop = ambienceB.loop = true;
        }

        private AudioSource MakeSource()
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 2D: panning is done by hand below
            return s;
        }

        public void PlayOneShot(SoundId id, SoundLibrary.Entry entry, AudioClip clip, Vector2? position, float volumeScale, float pitchScale)
        {
            // Unscaled time: hit-stop freezes Time.time, and a sound shouldn't be blocked by that.
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(id, out float last) && now - last < entry.MinInterval) return;
            lastPlayed[id] = now;

            float volume = entry.Volume * volumeScale;
            float pan = 0f;
            if (position.HasValue)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector2 offset = position.Value - (Vector2)cam.transform.position;
                    pan = Mathf.Clamp(offset.x / PanWidth, -1f, 1f) * 0.6f;
                    float distance = offset.magnitude;
                    volume *= 1f - Mathf.InverseLerp(FullVolumeDistance, SilentDistance, distance);
                }
            }
            if (volume <= 0.001f) return;

            // Take a free source; if all are busy, reuse the oldest (round robin).
            AudioSource source = null;
            for (int i = 0; i < pool.Count; i++)
            {
                AudioSource s = pool[(next + i) % pool.Count];
                if (!s.isPlaying && !held.Contains(s)) { source = s; next = (next + i + 1) % pool.Count; break; }
            }
            if (source == null)
            {
                // All busy: reuse the next one that isn't held.
                for (int i = 0; i < pool.Count && source == null; i++)
                {
                    AudioSource s = pool[(next + i) % pool.Count];
                    if (!held.Contains(s)) source = s;
                }
                next = (next + 1) % pool.Count;
                if (source == null) return;
            }

            source.loop = false;
            source.clip = clip;
            source.volume = volume;
            source.panStereo = pan;
            source.pitch = entry.Pitch * pitchScale * (1f + Random.Range(-entry.PitchJitter, entry.PitchJitter));
            source.Play();
        }

        public AudioSource PlayHeld(AudioClip clip, float volume, float pitch, bool loop)
        {
            // Held sounds get their own source so one-shots never steal it.
            AudioSource source = MakeSource();
            held.Add(source);
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.loop = loop;
            source.Play();
            if (!loop) StartCoroutine(ReleaseWhenDone(source));
            return source;
        }

        public void StopHeld(AudioSource source, float fade)
        {
            if (!held.Contains(source)) return;
            StartCoroutine(FadeOutHeld(source, fade));
        }

        private IEnumerator FadeOutHeld(AudioSource source, float fade)
        {
            float from = source.volume;
            for (float t = 0f; t < fade && source != null; t += Time.unscaledDeltaTime)
            {
                source.volume = Mathf.Lerp(from, 0f, t / fade);
                yield return null;
            }
            Release(source);
        }

        private IEnumerator ReleaseWhenDone(AudioSource source)
        {
            yield return new WaitForSecondsRealtime(source.clip.length / Mathf.Max(0.1f, source.pitch) + 0.1f);
            Release(source);
        }

        private void Release(AudioSource source)
        {
            if (source == null || !held.Remove(source)) return;
            source.Stop();
            Destroy(source);
        }

        public void SetAmbience(AudioClip clip, float volume, float fade)
        {
            if (ambienceA.clip == clip && (clip == null || ambienceA.isPlaying))
            {
                // Same loop: keep it going, just ease to the new volume.
                if (ambienceFade != null) StopCoroutine(ambienceFade);
                ambienceFade = StartCoroutine(FadeVolume(ambienceA, volume, fade, stopAtEnd: false));
                return;
            }

            // Cross-fade: the old loop fades out on B, the new one fades in on A.
            (ambienceA, ambienceB) = (ambienceB, ambienceA);
            if (ambienceFade != null) StopCoroutine(ambienceFade);
            StartCoroutine(FadeVolume(ambienceB, 0f, fade, stopAtEnd: true));
            ambienceA.clip = clip;
            if (clip == null) return;
            SetBase(ambienceA, 0f);
            ambienceA.Play();
            ambienceFade = StartCoroutine(FadeVolume(ambienceA, volume, fade, stopAtEnd: false));
        }

        public void Duck(float target, float fade)
        {
            duckTarget = target;
            duckSpeed = fade > 0f ? Mathf.Abs(duck - target) / fade : 1000f;
        }

        // Each ambience source's own (fade) volume; the ducking multiplier is applied on top in LateUpdate.
        private readonly Dictionary<AudioSource, float> baseVolume = new Dictionary<AudioSource, float>();

        private void Update()
        {
            duck = Mathf.MoveTowards(duck, duckTarget, duckSpeed * Time.unscaledDeltaTime);
        }

        private IEnumerator FadeVolume(AudioSource source, float target, float time, bool stopAtEnd)
        {
            baseVolume.TryGetValue(source, out float from);
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                SetBase(source, Mathf.Lerp(from, target, t / time));
                yield return null;
            }
            SetBase(source, target);
            if (stopAtEnd) { source.Stop(); source.clip = null; }
        }

        private void SetBase(AudioSource source, float v) => baseVolume[source] = v;

        private void LateUpdate()
        {
            // A streamed loop asked to play before its data was ready can fail to start: start it once it's ready.
            if (ambienceA.clip != null && !ambienceA.isPlaying && ambienceA.clip.loadState == AudioDataLoadState.Loaded)
                ambienceA.Play();

            baseVolume.TryGetValue(ambienceA, out float a);
            baseVolume.TryGetValue(ambienceB, out float b);
            ambienceA.volume = a * duck;
            ambienceB.volume = b * duck;
        }
    }
}
