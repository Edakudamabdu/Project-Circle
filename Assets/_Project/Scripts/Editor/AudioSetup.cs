using System.Collections.Generic;
using System.IO;
using ProjectCircle.Systems;
using ProjectCircle.Systems.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// One-click audio setup (menu: Project Circle → Audio → Set Up Sounds):
    /// 1. Import settings for every clip under Audio/ (short effects decompressed in memory, long loops streamed).
    /// 2. The <see cref="SoundLibrary"/> asset (Resources/SoundLibrary): adds an entry for any <see cref="SoundId"/>
    ///    that doesn't have one yet. Entries already there are left alone, so tweaks made in the Inspector stay.
    /// 3. Region ambience: gives each region scene's <see cref="RegionInfo"/> its default loop, if it has none.
    /// Safe to run again at any time.
    /// </summary>
    public static class AudioSetup
    {
        private const string AudioRoot = "Assets/_Project/Audio";
        private const string LibraryPath = "Assets/_Project/Resources/SoundLibrary.asset";
        private const string Sfx = AudioRoot + "/SFX/";
        private const string Amb = AudioRoot + "/Ambience/";

        /// <summary>Default sound for each id: clip file(s) under Audio/SFX, volume, pitch, pitch jitter, min interval.</summary>
        private static readonly (SoundId id, string[] clips, float volume, float pitch, float jitter, float interval)[] Defaults =
        {
            (SoundId.Swing,        new[] { "Player/Swing" },        0.70f, 1.00f, 0.08f, 0.05f),
            (SoundId.Jump,         new[] { "Player/Jump" },         0.45f, 1.00f, 0.06f, 0.05f),
            (SoundId.AirJump,      new[] { "Player/AirJump" },      0.60f, 1.00f, 0.05f, 0.05f),
            (SoundId.Dash,         new[] { "Player/Dash" },         0.45f, 1.00f, 0.05f, 0.05f),
            (SoundId.Land,         new[] { "Player/Land" },         0.25f, 0.80f, 0.08f, 0.10f),
            (SoundId.ParrySnap,    new[] { "Player/ParrySnap" },    0.70f, 1.00f, 0.04f, 0.05f),
            (SoundId.ParryRing,    new[] { "Player/ParryRing" },    0.55f, 1.00f, 0.03f, 0.10f),
            (SoundId.PlayerHurt,   new[] { "Player/Hurt" },         0.55f, 1.00f, 0.06f, 0.10f),
            (SoundId.PlayerDeath,  new[] { "Player/Death" },        0.80f, 1.00f, 0.00f, 0.50f),
            (SoundId.Mend,         new[] { "Player/Mend" },         0.60f, 1.00f, 0.00f, 0.30f),
            (SoundId.EnemyHit,     new[] { "Enemies/Hit_01", "Enemies/Hit_02", "Enemies/Hit_03" }, 0.45f, 1.00f, 0.10f, 0.03f),
            (SoundId.EnemyShatter, new[] { "Enemies/Shatter" },     0.60f, 1.00f, 0.10f, 0.05f),
            (SoundId.LaserCharge,  new[] { "Enemies/LaserCharge" }, 0.50f, 1.00f, 0.03f, 0.10f),
            (SoundId.LaserFire,    new[] { "Enemies/LaserFire" },   0.45f, 1.00f, 0.05f, 0.10f),
            (SoundId.OrbCast,      new[] { "Enemies/OrbCast" },     0.35f, 1.00f, 0.12f, 0.20f),
            (SoundId.Shot,         new[] { "Enemies/Shot" },        0.40f, 1.00f, 0.08f, 0.08f),
            (SoundId.Slam,         new[] { "Enemies/Slam" },        0.60f, 1.00f, 0.06f, 0.08f),
            (SoundId.Tell,         new[] { "Enemies/Tell" },        0.50f, 1.00f, 0.05f, 0.10f),
            (SoundId.BossLand,     new[] { "Bosses/BossLand" },     0.80f, 1.00f, 0.05f, 0.10f),
            (SoundId.BossDefeat,   new[] { "Bosses/BossDefeat" },   0.90f, 1.00f, 0.00f, 0.50f),
            (SoundId.VertexLink,   new[] { "Bosses/VertexLink" },   0.55f, 1.00f, 0.05f, 0.20f),
            (SoundId.Rebalance,    new[] { "Bosses/Rebalance" },    0.60f, 1.00f, 0.00f, 0.50f),
            (SoundId.PhaseWarp,    new[] { "Bosses/PhaseWarp" },    0.80f, 1.00f, 0.00f, 0.50f),
            (SoundId.ChargeWhoosh, new[] { "Bosses/ChargeWhoosh" }, 0.70f, 0.85f, 0.05f, 0.10f),
            (SoundId.Gate,         new[] { "World/Gate" },          0.70f, 1.00f, 0.05f, 0.10f),
            (SoundId.StringPickup, new[] { "World/StringPickup" },  0.70f, 1.00f, 0.00f, 0.50f),
            (SoundId.MirrorRest,   new[] { "World/MirrorRest" },    0.60f, 1.00f, 0.00f, 0.50f),
            (SoundId.WallCrumble,  new[] { "World/WallCrumble" },   0.70f, 1.00f, 0.05f, 0.20f),
            (SoundId.DialogueOpen, new[] { "UI/DialogueOpen" },     0.40f, 1.00f, 0.00f, 0.10f),
            (SoundId.DialogueTick, new[] { "UI/DialogueTick" },     0.18f, 1.00f, 0.15f, 0.045f),
            (SoundId.MenuMove,     new[] { "UI/MenuMove" },         0.35f, 1.00f, 0.05f, 0.05f),
            (SoundId.MenuConfirm,  new[] { "UI/MenuConfirm" },      0.45f, 1.00f, 0.03f, 0.10f),
            (SoundId.Shove,        new[] { "Story/Shove" },         0.75f, 1.00f, 0.00f, 0.20f),
            (SoundId.PovHit,       new[] { "Story/PovHit" },        0.90f, 1.00f, 0.00f, 0.50f),
            (SoundId.VertexCrackle, new[] { "Story/VertexCrackle" }, 0.45f, 1.00f, 0.00f, 0.50f),
            (SoundId.VertexZap,    new[] { "Story/VertexZap" },     0.60f, 1.00f, 0.00f, 0.30f),
            (SoundId.Rumble,       new[] { "Story/Rumble" },        0.80f, 1.00f, 0.00f, 0.50f),
            (SoundId.LightPour,    new[] { "Story/LightPour" },     0.65f, 1.00f, 0.00f, 0.50f),
            (SoundId.StringsGather, new[] { "Story/StringsGather" }, 0.65f, 1.00f, 0.00f, 0.50f),
            (SoundId.CapeWrap,     new[] { "Story/CapeWrap" },      0.70f, 1.00f, 0.00f, 0.50f),
        };

        /// <summary>Default ambience per region scene: loop file under Audio/Ambience and its volume.</summary>
        private static readonly Dictionary<string, (string clip, float volume)> RegionAmbience = new Dictionary<string, (string, float)>
        {
            { "StoryTest",          ("Cavern", 0.35f) },
            { "Graveyard",          ("Cavern", 0.35f) },
            { "WhisperingFissure",  ("Cavern", 0.35f) },
            { "BossArena",          ("Cavern", 0.35f) },
            { "TwinIsosceles",      ("Cavern", 0.35f) },
            { "PrismCaverns",       ("IceCavern", 0.40f) },
            { "ShrineOfFirstBreak", ("IceCavern", 0.40f) },
            { "SquaresRefuge",      ("IceCavern", 0.40f) },
            { "HallOfMeasure",      ("PowerHum", 0.25f) },
            { "TempleOfThreePoints", ("PowerHum", 0.25f) },
            { "RefractorLair",      ("PowerHum", 0.25f) },
            { "ScaleneSlums",       ("TunnelWind", 0.30f) },
            { "IsoscelesTerraces",  ("TunnelWind", 0.30f) },
            { "EquilateralSpire",   ("TunnelWind", 0.30f) },
            { "ApexThrone",         ("TunnelWind", 0.30f) },
            { "RoadToSquareRealm",  ("TunnelWind", 0.30f) },
        };

        [MenuItem("Project Circle/Audio/Set Up Sounds (import settings, library, region ambience)")]
        public static void SetUpAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("AudioSetup: leave Play Mode first.");
                return;
            }
            ApplyImportSettings();
            BuildLibrary();
            ApplyAmbienceToAllRegions();
        }

        /// <summary>Short effects: decompressed in memory (no stutter on play). Loops and music: streamed from disk.</summary>
        public static void ApplyImportSettings()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                bool longClip = path.Contains("/Ambience/") || path.Contains("/Music/");

                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                s.loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = longClip ? 0.6f : 0.75f;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                if (s.loadType == importer.defaultSampleSettings.loadType && Mathf.Approximately(s.quality, importer.defaultSampleSettings.quality)
                    && s.compressionFormat == importer.defaultSampleSettings.compressionFormat) continue;

                importer.defaultSampleSettings = s;
                importer.loadInBackground = longClip;
                importer.SaveAndReimport();
            }
        }

        /// <summary>Creates the library if needed and adds entries for ids it doesn't have yet.</summary>
        public static SoundLibrary BuildLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (library == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                library = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            // Keep every existing entry as it is; only fill in the gaps.
            var so = new SerializedObject(library);
            SerializedProperty entries = so.FindProperty("entries");
            var have = new HashSet<int>();
            for (int i = 0; i < entries.arraySize; i++) have.Add(entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").intValue);

            int added = 0;
            foreach (var d in Defaults)
            {
                if (have.Contains((int)d.id)) continue;
                var clips = new List<AudioClip>();
                foreach (string name in d.clips)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Sfx + name + ".wav");
                    if (clip != null) clips.Add(clip);
                    else Debug.LogWarning($"AudioSetup: missing clip {Sfx}{name}.wav for {d.id}");
                }

                entries.arraySize++;
                SerializedProperty e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                e.FindPropertyRelative("id").intValue = (int)d.id;
                SerializedProperty clipsProp = e.FindPropertyRelative("clips");
                clipsProp.arraySize = clips.Count;
                for (int c = 0; c < clips.Count; c++) clipsProp.GetArrayElementAtIndex(c).objectReferenceValue = clips[c];
                e.FindPropertyRelative("volume").floatValue = d.volume;
                e.FindPropertyRelative("pitch").floatValue = d.pitch;
                e.FindPropertyRelative("pitchJitter").floatValue = d.jitter;
                e.FindPropertyRelative("minInterval").floatValue = d.interval;
                added++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"AudioSetup: SoundLibrary has {entries.arraySize} sounds ({added} added).");
            return library;
        }

        /// <summary>Gives a region its default ambience loop if it has none (used by the region builders too).</summary>
        public static bool ApplyAmbience(RegionInfo info)
        {
            if (info == null || !RegionAmbience.TryGetValue(info.gameObject.scene.name, out var amb)) return false;
            var so = new SerializedObject(info);
            SerializedProperty clipProp = so.FindProperty("ambience");
            if (clipProp.objectReferenceValue != null) return false; // someone chose one already: keep it
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Amb + amb.clip + ".wav");
            if (clip == null) return false;
            clipProp.objectReferenceValue = clip;
            so.FindProperty("ambienceVolume").floatValue = amb.volume;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// Opens each region scene (additively, so whatever is open stays untouched), sets its ambience and saves it.
        /// Scenes that are already open are updated but not saved, so no one's unsaved work is written out.
        /// </summary>
        public static void ApplyAmbienceToAllRegions()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!RegionAmbience.ContainsKey(Path.GetFileNameWithoutExtension(path))) continue;

                Scene scene = SceneManager.GetSceneByPath(path);
                bool wasOpen = scene.IsValid() && scene.isLoaded;
                if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                string sceneName = scene.name;
                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (RegionInfo info in root.GetComponentsInChildren<RegionInfo>(true))
                        changed |= ApplyAmbience(info);

                if (wasOpen)
                {
                    if (changed) EditorSceneManager.MarkSceneDirty(scene);
                }
                else
                {
                    if (changed) EditorSceneManager.SaveScene(scene);
                    EditorSceneManager.CloseScene(scene, true);
                }
                if (changed) Debug.Log($"AudioSetup: ambience set in {sceneName}" + (wasOpen ? " (open: save it to keep)" : ""));
            }
        }
    }
}
