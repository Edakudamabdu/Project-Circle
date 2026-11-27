using System.Collections.Generic;
using System.IO;
using System.Text;
using ProjectCircle.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// Checks every scene transition (SceneEdge, Hatch, BuildingDoor: anything with a <see cref="SceneDoor"/>) in
    /// the build's scenes, so level edits can't silently break the way between rooms.
    /// Menu: <b>Project Circle ▸ Level Building ▸ Check Scene Links</b>. Results go to the Console, one line per problem.
    ///
    /// It checks: the target scene exists and is in the build list; the target door exists there; the target door
    /// leads back (a warning only: one-way links are allowed); door ids are unique in each scene; the arrival point
    /// isn't inside a wall; and edge openings at the ends of a room have their camera stop set.
    /// </summary>
    public static class SceneLinkChecker
    {
        private struct DoorInfo
        {
            public string Scene, Id, TargetScene, TargetId, Path;
            public Vector2 Arrival;
            public bool ArrivalBlocked;
            public bool LooksLikeEdge;
            public SceneDoor.CameraStopSide Stop;
        }

        [MenuItem("Project Circle/Level Building/Check Scene Links", priority = 0)]
        public static void CheckMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int problems = Check(out string summary);
            EditorUtility.DisplayDialog("Scene links", summary + (problems > 0 ? "\n\nDetails are in the Console." : ""), "OK");
        }

        /// <summary>Runs the check and logs each problem. Returns the number of errors and warnings.</summary>
        public static int Check(out string summary)
        {
            var buildScenes = new Dictionary<string, string>(); // name -> path
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) buildScenes[Path.GetFileNameWithoutExtension(s.path)] = s.path;

            // Collect every door in every build scene (opening each one additively, then closing it again).
            var doors = new List<DoorInfo>();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var kv in buildScenes)
                {
                    Scene scene = SceneManager.GetSceneByPath(kv.Value);
                    bool wasOpen = scene.isLoaded;
                    if (!wasOpen) scene = EditorSceneManager.OpenScene(kv.Value, OpenSceneMode.Additive);
                    Physics2D.SyncTransforms();
                    int ground = LayerMask.GetMask("Ground");

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (SceneDoor door in root.GetComponentsInChildren<SceneDoor>(true))
                        {
                            var so = new SerializedObject(door);
                            Vector2 arrival = (Vector2)door.transform.position + so.FindProperty("arrivalOffset").vector2Value;
                            var box = door.GetComponent<BoxCollider2D>();
                            bool blocked = false;
                            foreach (Collider2D hit in Physics2D.OverlapCircleAll(arrival, 0.4f, ground))
                                if (hit.gameObject.scene == scene && !hit.isTrigger && !IsGate(hit)) blocked = true;
                            doors.Add(new DoorInfo
                            {
                                Scene = kv.Key,
                                Id = door.DoorId,
                                TargetScene = door.TargetScene,
                                TargetId = door.TargetDoorId,
                                Path = PathOf(door.transform),
                                Arrival = arrival,
                                ArrivalBlocked = blocked,
                                LooksLikeEdge = box != null && Mathf.Abs(box.offset.x) > 1f,
                                Stop = door.CameraStop,
                            });
                        }
                    }
                    if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
                }
            }
            finally
            {
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            // Check them.
            int errors = 0, warnings = 0, ok = 0;
            var byScene = new Dictionary<string, List<DoorInfo>>();
            foreach (DoorInfo d in doors)
            {
                if (!byScene.TryGetValue(d.Scene, out var list)) byScene[d.Scene] = list = new List<DoorInfo>();
                list.Add(d);
            }

            foreach (DoorInfo d in doors)
            {
                string where = $"[{d.Scene}] '{d.Id}' ({d.Path})";
                bool fine = true;

                if (byScene[d.Scene].FindAll(o => o.Id == d.Id).Count > 1)
                { Debug.LogError($"Scene link: {where}: another door in this scene has the same Door Id."); errors++; fine = false; }

                if (string.IsNullOrEmpty(d.TargetScene))
                { Debug.LogWarning($"Scene link: {where}: Target Scene is empty, so this door leads nowhere."); warnings++; fine = false; }
                else if (!buildScenes.ContainsKey(d.TargetScene))
                { Debug.LogError($"Scene link: {where}: target scene '{d.TargetScene}' isn't in File > Build Profiles > Scene List (or is misspelled)."); errors++; fine = false; }
                else
                {
                    List<DoorInfo> there = byScene.TryGetValue(d.TargetScene, out var l) ? l : new List<DoorInfo>();
                    DoorInfo target = there.Find(o => o.Id == d.TargetId);
                    if (string.IsNullOrEmpty(target.Id))
                    { Debug.LogError($"Scene link: {where}: scene '{d.TargetScene}' has no door with Door Id '{d.TargetId}'. Ids there: {Ids(there)}."); errors++; fine = false; }
                    else if (target.TargetScene != d.Scene || target.TargetId != d.Id)
                    { Debug.LogWarning($"Scene link: {where} leads to [{d.TargetScene}] '{d.TargetId}', but that door leads to [{target.TargetScene}] '{target.TargetId}', not back here (fine if it's meant to be one-way)."); warnings++; fine = false; }
                }

                if (d.ArrivalBlocked)
                { Debug.LogWarning($"Scene link: {where}: the arrival point {d.Arrival} is inside a wall or platform. Move the door or change its Arrival Offset."); warnings++; fine = false; }

                if (d.LooksLikeEdge && d.Stop == SceneDoor.CameraStopSide.None)
                { Debug.LogWarning($"Scene link: {where}: looks like a room-edge opening but Camera Stop is None, so the camera will show the next room."); warnings++; fine = false; }

                if (fine) ok++;
            }

            var sb = new StringBuilder();
            sb.Append($"{doors.Count} doors in {buildScenes.Count} scenes: {ok} fine");
            if (errors > 0) sb.Append($", {errors} broken");
            if (warnings > 0) sb.Append($", {warnings} warnings");
            sb.Append('.');
            summary = sb.ToString();
            if (errors == 0 && warnings == 0) Debug.Log("Scene links: " + summary);
            else Debug.LogWarning("Scene links: " + summary);
            return errors + warnings;
        }

        /// <summary>Gates and breakable walls block only until they open, so arriving behind one is fine.</summary>
        private static bool IsGate(Collider2D hit) =>
            hit.GetComponentInParent<ProgressGate>() != null || hit.GetComponentInParent<CrumblingWall>() != null;

        private static string Ids(List<DoorInfo> list)
        {
            if (list.Count == 0) return "(none)";
            var names = new List<string>();
            foreach (DoorInfo d in list) names.Add(d.Id);
            return string.Join(", ", names);
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
