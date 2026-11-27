using ProjectCircle.Combat;
using ProjectCircle.Enemies.Boss;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using ProjectCircle.Systems.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// Turns the empty TwinIsosceles room into the boss arena: an open, symmetric hall (no ledges),
    /// gates at both ends that close for the fight, an entry trigger, the two twins (bodies copied from the
    /// Base Warden, without its moves) and the <see cref="BossArena"/> + <see cref="TwinIsosceles"/> that run it.
    /// Menu: <b>Project Circle ▸ Level Building ▸ Build Twin Isosceles Arena</b>. It replaces only the "Twin Arena"
    /// object, so other edits to the scene are kept.
    /// </summary>
    public static class TwinArenaBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Chapter1/TwinIsosceles.unity";
        private const string WardenScene = "Assets/_Project/Scenes/BossArena.unity";
        private const string MusicPath = "Assets/_Project/Audio/Music/twin_boss_fight_music.mp3";

        private const float Floor = -1f;        // floor top of the room
        private const float CentreX = 18f;
        private const float GateLeftX = 2f, GateRightX = 34f;
        private const float GateWidth = 0.8f, GateHeight = 5.5f;

        [MenuItem("Project Circle/Level Building/Build Twin Isosceles Arena", priority = 20)]
        private static void BuildMenu()
        {
            if (!EditorUtility.DisplayDialog("Twin Isosceles arena",
                "Rebuilds the 'Twin Arena' object in the TwinIsosceles scene (gates, the twins, the fight). " +
                "Changes you made inside that object are lost; the rest of the scene is kept.", "Rebuild", "Cancel")) return;
            Build();
        }

        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Out with the placeholder (sign + seal) and any earlier build.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Twin Arena") { Object.DestroyImmediate(root); continue; }
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    if (t.name.StartsWith("Sign - BOSS ROOM") || t.name.Contains("(Seal.Twins)")) Object.DestroyImmediate(t.gameObject);
                }
            }

            var arena = new GameObject("Twin Arena");
            SceneManager.MoveGameObjectToScene(arena, scene);
            Color stone = new Color(0.46f, 0.33f, 0.28f);

            // Gates at both ends, shut during the fight (they sink into the floor otherwise).
            var gates = Child(arena.transform, "Gates");
            Color gateColor = new Color(0.62f, 0.3f, 0.26f);
            Transform gateLeft = Block(gates, "Gate_Left", GateLeftX - GateWidth * 0.5f, GateLeftX + GateWidth * 0.5f, Floor, Floor + GateHeight, gateColor).transform;
            Transform gateRight = Block(gates, "Gate_Right", GateRightX - GateWidth * 0.5f, GateRightX + GateWidth * 0.5f, Floor, Floor + GateHeight, gateColor).transform;

            // A mirrored floor pattern: the symmetry is visible before the fight starts.
            var decor = Child(arena.transform, "Decor");
            for (int i = 0; i < 7; i++)
            {
                float d = i * 2.2f;
                Color c = new Color(0.55f, 0.42f, 0.34f, 0.5f - i * 0.05f);
                Sprite(decor, "Floor Mark L" + i, CentreX - d, Floor + 0.04f, 0.18f, 0.08f, c);
                if (i > 0) Sprite(decor, "Floor Mark R" + i, CentreX + d, Floor + 0.04f, 0.18f, 0.08f, c);
            }
            Sprite(decor, "Mirror Line", CentreX, Floor + 9f, 0.06f, 18f, new Color(0.7f, 0.55f, 0.45f, 0.12f));

            var centre = new GameObject("Arena Centre").transform;
            centre.SetParent(arena.transform, false);
            centre.position = new Vector3(CentreX, Floor + 7.6f, 0f);

            // The fight starts once the circle reaches the middle of the hall: the twins drop in on either side.
            var trigger = new GameObject("Trigger - Fight Start");
            trigger.transform.SetParent(arena.transform, false);
            trigger.transform.position = new Vector3(CentreX, Floor + 6f, 0f);
            var box = trigger.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1f, 12f);
            trigger.AddComponent<StoryTrigger>();

            // The twins: bodies copied from the Base Warden, moves removed (the TwinIsosceles runs them). They stand
            // on their landing spots here; in play they wait out of sight above and drop in when the fight starts.
            var twinsRoot = new GameObject("Twin Isosceles");
            twinsRoot.transform.SetParent(arena.transform, false);
            BossBrain iso = CopyWarden(scene, twinsRoot.transform, "Iso (Twin)", "Iso", CentreX - 10f,
                new Color(0.6f, 0.72f, 0.8f), new Color(0.33f, 0.43f, 0.52f), centre);
            BossBrain skelos = CopyWarden(scene, twinsRoot.transform, "Skelos (Twin)", "Skelos", CentreX + 10f,
                new Color(0.82f, 0.7f, 0.5f), new Color(0.53f, 0.41f, 0.28f), centre);

            var twins = twinsRoot.AddComponent<TwinIsosceles>();
            var tso = new SerializedObject(twins);
            tso.FindProperty("left").objectReferenceValue = iso;
            tso.FindProperty("right").objectReferenceValue = skelos;
            tso.FindProperty("arenaCentre").objectReferenceValue = centre;
            tso.ApplyModifiedPropertiesWithoutUndo();

            // The arena: gates, camera framing, the reward (Vertex Seal II opens the way to the Spire).
            var arenaComp = arena.AddComponent<BossArena>();
            var aso = new SerializedObject(arenaComp);
            aso.FindProperty("boss").objectReferenceValue = twins;
            aso.FindProperty("entryTrigger").objectReferenceValue = trigger.GetComponent<StoryTrigger>();
            var gatesProp = aso.FindProperty("gates");
            gatesProp.arraySize = 2;
            gatesProp.GetArrayElementAtIndex(0).objectReferenceValue = gateLeft;
            gatesProp.GetArrayElementAtIndex(1).objectReferenceValue = gateRight;
            aso.FindProperty("cameraCentre").objectReferenceValue = centre;
            aso.FindProperty("cameraSize").floatValue = 8.5f;
            aso.FindProperty("defeatedText").stringValue = "The Twin Isosceles defeated";
            var flags = aso.FindProperty("rewardFlags");
            flags.arraySize = 1;
            flags.GetArrayElementAtIndex(0).stringValue = "Seal.Twins";
            aso.FindProperty("rewardAbilities").intValue = (int)PlayerAbility.None;
            aso.FindProperty("arenaHalfWidth").floatValue = 17f;
            aso.FindProperty("fightMusic").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            aso.ApplyModifiedPropertiesWithoutUndo();

            var info = Object.FindAnyObjectByType<RegionInfo>();
            if (info != null)
            {
                var iso2 = new SerializedObject(info);
                iso2.FindProperty("notes").stringValue =
                    "BOSS 2 · The Twin Isosceles (Iso and Skelos). Press Play here to start just outside the arena; walk right to begin.\n" +
                    "Phase 1 (both): Pincer Charge, Mirror Slam, Measuring Line, Passing Orb (parry it back), the Compass; they Rebalance once.\n" +
                    "Phase 2 (one falls): the survivor warps scalene, turns crimson, fights alone and faster.\n" +
                    "Reward: Seal.Twins (Vertex Seal II). The east opening (gate) leads back to the Terraces.";
                iso2.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static BossBrain CopyWarden(Scene target, Transform parent, string objectName, string displayName, float x,
                                            Color top, Color bottom, Transform centre)
        {
            Scene source = EditorSceneManager.OpenScene(WardenScene, OpenSceneMode.Additive);
            BossBrain warden = null;
            foreach (GameObject root in source.GetRootGameObjects())
            {
                warden = root.GetComponentInChildren<BossBrain>(true);
                if (warden != null) break;
            }
            GameObject copy = Object.Instantiate(warden.gameObject);
            copy.name = objectName;
            SceneManager.MoveGameObjectToScene(copy, target);
            EditorSceneManager.CloseScene(source, true);

            foreach (BossMove move in copy.GetComponents<BossMove>()) Object.DestroyImmediate(move);
            copy.transform.SetParent(parent, true);
            var brain = copy.GetComponent<BossBrain>();
            float inradius = brain.Shape.Circumradius * 0.5f;
            copy.transform.position = new Vector3(x, Floor + inradius + 0.02f, 0f);

            var so = new SerializedObject(brain);
            so.FindProperty("bossName").stringValue = displayName;
            so.FindProperty("arenaCentre").objectReferenceValue = centre;
            so.FindProperty("arenaHalfWidth").floatValue = (GateRightX - GateLeftX - GateWidth) * 0.5f;
            so.FindProperty("arenaHeight").floatValue = 18f;
            so.FindProperty("phaseThresholds").arraySize = 0;
            so.FindProperty("passThrough").arraySize = 0; // an open hall: nothing to pass through
            so.ApplyModifiedPropertiesWithoutUndo();

            var hso = new SerializedObject(copy.GetComponent<EnemyHealth>());
            hso.FindProperty("maxHealth").intValue = 28;
            hso.ApplyModifiedPropertiesWithoutUndo();

            var mso = new SerializedObject(brain.Shape);
            mso.FindProperty("fillColor").colorValue = top;
            mso.FindProperty("fillBottomColor").colorValue = bottom;
            mso.ApplyModifiedPropertiesWithoutUndo();
            return brain;
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Collider2D Block(Transform parent, string name, float x0, float x1, float y0, float y1, Color color)
        {
            GameObject go = Sprite(parent, name, (x0 + x1) * 0.5f, (y0 + y1) * 0.5f, x1 - x0, y1 - y0, color);
            go.layer = LayerMask.NameToLayer("Ground");
            return go.AddComponent<BoxCollider2D>();
        }

        private static GameObject Sprite(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(w, h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RegionBuilder.Square;
            sr.color = color;
            sr.sortingOrder = 1;
            return go;
        }
    }
}
