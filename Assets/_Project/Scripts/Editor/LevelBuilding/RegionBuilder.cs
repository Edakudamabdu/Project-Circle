using System.Collections.Generic;
using System.IO;
using ProjectCircle.Combat;
using ProjectCircle.Enemies;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// Builds placeholder ("greybox") region scenes from short layout descriptions (see <see cref="ChapterOneLayouts"/>).
    /// Every region scene comes out with the same tidy hierarchy, so it is easy to redesign by hand:
    ///
    ///   _Core        Player, camera, light, HUD, dialogue (leave as they are)
    ///   Region       RegionInfo: the region's name, and what you start with when you press Play here
    ///   Level        solid ground/walls/platforms: plain squares, merged into one collider (move/scale freely)
    ///   Doors        SceneDoors to the neighbouring regions (door ids must match the other side)
    ///   Checkpoints  Mirrors
    ///   Pickups      cape strings (abilities), Vertex Seals, the Mould
    ///   Gates        seal/Mould gates, crumbling walls
    ///   Enemies      enemy prefabs
    ///   Lore         tablets (read with Interact)
    ///   Decor        looks only, no collision: piles, houses, pillars, signs
    ///
    /// The builder only makes the first version of a scene. Running it again OVERWRITES that scene, so once
    /// someone has started editing a region by hand, don't rebuild it.
    /// </summary>
    public sealed class RegionBuilder
    {
        public const string SceneFolder = "Assets/_Project/Scenes/Chapter1";
        public const string DialogueFolder = "Assets/_Project/ScriptableObjects/Dialogue/Chapter1";
        private const string TemplateScene = "Assets/_Project/Scenes/EnemyTest.unity";
        private const string SquareSprite = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Square.png";
        private const string CircleSprite = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Circle.png";
        private const int GroundLayer = 3;

        private readonly string sceneName;
        private Scene scene;
        private Transform level, doors, checkpoints, pickups, gates, enemies, lore, decor, region;
        private Color groundColor, platformColor;
        private int blockCount;

        private static Sprite square, circle;
        private static Material shapeMaterial;

        public static Sprite Square => square != null ? square : (square = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSprite));
        public static Sprite Circle => circle != null ? circle : (circle = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSprite));
        public static Material ShapeMaterial => shapeMaterial != null ? shapeMaterial : (shapeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Settings/Materials/ShapeUnlit.mat"));

        public string SceneName => sceneName;

        // ------------------------------------------------------------------ scene setup

        /// <summary>Makes a fresh scene from the template (core objects only) and the empty groups.</summary>
        public RegionBuilder(string sceneName, string regionName, Color background, Color ground, Color platform,
                             PlayerAbility devAbilities, string[] devFlags, string notes)
        {
            this.sceneName = sceneName;
            groundColor = ground;
            platformColor = platform;

            Directory.CreateDirectory(SceneFolder);
            string path = $"{SceneFolder}/{sceneName}.unity";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset(TemplateScene, path);
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            var keep = new HashSet<string> { "Main Camera", "Global Light 2D", "Level", "Player", "HUD", "DialogueCanvas" };
            var core = new GameObject("_Core").transform;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.transform == core) continue;
                if (!keep.Contains(root.name)) { Object.DestroyImmediate(root); continue; }
                if (root.name == "Level") continue;
                // Canvases must stay at the root; the rest goes under _Core.
                if (root.GetComponent<Canvas>() == null) root.transform.SetParent(core, true);
            }

            level = GameObject.Find("Level").transform;
            for (int i = level.childCount - 1; i >= 0; i--) Object.DestroyImmediate(level.GetChild(i).gameObject);

            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null) cam.backgroundColor = background;

            region = new GameObject("Region").transform;
            var info = region.gameObject.AddComponent<RegionInfo>();
            var so = new SerializedObject(info);
            so.FindProperty("regionName").stringValue = regionName;
            so.FindProperty("devAbilities").intValue = (int)devAbilities;
            var flagsProp = so.FindProperty("devFlags");
            flagsProp.arraySize = devFlags != null ? devFlags.Length : 0;
            for (int i = 0; i < flagsProp.arraySize; i++) flagsProp.GetArrayElementAtIndex(i).stringValue = devFlags[i];
            so.FindProperty("notes").stringValue = notes;
            so.ApplyModifiedPropertiesWithoutUndo();

            doors = Group("Doors");
            checkpoints = Group("Checkpoints");
            pickups = Group("Pickups");
            gates = Group("Gates");
            enemies = Group("Enemies");
            lore = Group("Lore");
            decor = Group("Decor");
        }

        private static Transform Group(string name) => new GameObject(name).transform;

        /// <summary>Where the circle stands when the region is played directly.</summary>
        public void PlayerStart(float x, float floorTop)
        {
            var player = Object.FindAnyObjectByType<PlayerMovement>();
            if (player != null) player.transform.position = new Vector3(x, floorTop + 0.5f, 0f);
        }

        /// <summary>Rebuilds the merged collider, saves the scene and adds it to Build Settings.</summary>
        public void Save()
        {
            var comp = level.GetComponent<CompositeCollider2D>();
            if (comp != null) comp.GenerateGeometry();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild(scene.path);
        }

        public static void AddToBuild(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ level geometry

        private GameObject Block(string name, float x0, float x1, float y0, float y1, Color color, Transform parent = null, bool merge = true)
        {
            var go = new GameObject($"{name}_{++blockCount:00}");
            go.layer = GroundLayer;
            go.transform.SetParent(parent != null ? parent : level, false);
            go.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f);
            go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = color;
            var box = go.AddComponent<BoxCollider2D>();
            if (merge) box.compositeOperation = Collider2D.CompositeOperation.Merge;
            return go;
        }

        /// <summary>A floor with its top at <paramref name="top"/>, solid down to <paramref name="bottom"/>.</summary>
        public void Ground(float x0, float x1, float top, float bottom = -14f) => Block("Ground", x0, x1, bottom, top, groundColor);

        /// <summary>A thin platform (you can stand on it; it is solid from below too).</summary>
        public void Platform(float x0, float x1, float top, float thickness = 0.6f) => Block("Platform", x0, x1, top - thickness, top, platformColor);

        /// <summary>Any solid rectangle (walls, ceilings, overhangs).</summary>
        public void Solid(float x0, float x1, float y0, float y1) => Block("Wall", x0, x1, y0, y1, groundColor);

        // ------------------------------------------------------------------ doors, mirrors, lore

        /// <summary>A door to another scene. <paramref name="facesLeft"/>: the circle arrives on its left side.</summary>
        public void Door(string id, float x, float floorTop, string targetScene, string targetDoor, string label, bool facesLeft = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/SceneDoor"), scene);
            go.name = $"Door - {id} (to {label})";
            go.transform.SetParent(doors, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            var so = new SerializedObject(go.GetComponent<SceneDoor>());
            so.FindProperty("doorId").stringValue = id;
            so.FindProperty("targetScene").stringValue = targetScene;
            so.FindProperty("targetDoorId").stringValue = targetDoor;
            so.FindProperty("arrivalOffset").vector2Value = new Vector2(facesLeft ? -1.6f : 1.6f, 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) tmp.text = label;
        }

        /// <summary>
        /// A walk-off transition at the edge of the level (an opening, Hollow Knight style): an invisible trigger
        /// standing on <paramref name="floorTop"/> at x. <paramref name="exitIsOnRight"/>: the opening is on the right
        /// side of the room, so arrivals appear to its left (walking in).
        /// The object sits in the opening (x); its trigger is <see cref="EdgeTriggerDepth"/> further out, inside the
        /// gap beyond the wall. The camera is stopped 1 unit out from x (the wall's outer face), so the circle walks
        /// on into the gap and off-screen before the trigger fires, and the far side is never shown.
        /// The layout must give the gap a floor and a backstop past it (see ChapterOneLayouts.Ends).
        /// </summary>
        public void Edge(string id, float x, float floorTop, string targetScene, string targetDoor, bool exitIsOnRight, float height = 6f)
        {
            float outward = exitIsOnRight ? 1f : -1f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/SceneEdge"), scene);
            go.name = $"Edge - {id} (to {targetScene})";
            go.transform.SetParent(doors, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            var box = go.GetComponent<BoxCollider2D>();
            box.size = new Vector2(1f, height);
            box.offset = new Vector2(outward * EdgeTriggerDepth, height * 0.5f);
            SetDoor(go, id, targetScene, targetDoor, new Vector2(-outward * 2.5f, 0.5f));

            // Stop the camera at the wall's outer face.
            var limits = region.GetComponent<CameraBounds2D>();
            if (limits == null) limits = region.gameObject.AddComponent<CameraBounds2D>();
            if (exitIsOnRight) limits.AddRightLimit(x + 1f);
            else limits.AddLeftLimit(x - 1f);
            EditorUtility.SetDirty(limits);
        }

        /// <summary>How far past the opening an edge's trigger sits (the circle is off-screen by then).</summary>
        public const float EdgeTriggerDepth = 2.5f;

        /// <summary>A horizontal transition (a hole up through the ceiling, or down through the floor).</summary>
        public void Hatch(string id, float x, float y, float width, string targetScene, string targetDoor, Vector2 arrival)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/SceneEdge"), scene);
            go.name = $"Hatch - {id} (to {targetScene})";
            go.transform.SetParent(doors, false);
            go.transform.position = new Vector3(x, y, 0f);
            var box = go.GetComponent<BoxCollider2D>();
            box.size = new Vector2(width, 1f);
            box.offset = new Vector2(0f, 0.5f);
            SetDoor(go, id, targetScene, targetDoor, arrival);

            // Like an edge's camera stop, but vertical and only around the shaft/hole: the view stops 1.2 short of
            // the trigger, so the circle climbs (or drops) off-screen before it fires. Arrivals below the hatch
            // mean it's in a ceiling (you leave upward); arrivals above it mean it's in a floor.
            bool upward = arrival.y < 0f;
            var zone = go.AddComponent<CameraLimitZone>();
            // The area reaches 9.5 into the room and 8 past the hatch, so the camera keeps still while the next
            // scene loads (the circle keeps moving for a moment).
            zone.Configure(new Vector2(0f, upward ? -0.75f : 1.25f), new Vector2(width + 2f, 17.5f), upward, upward ? -1.2f : 2.2f);
        }

        /// <summary>A doorway into a building or cave in the middle of a level. Arrivals appear standing in it.</summary>
        public void Building(string id, float x, float floorTop, string targetScene, string targetDoor, string label, Color frame)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/BuildingDoor"), scene);
            go.name = $"Building - {id} (to {label})";
            go.transform.SetParent(doors, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            SetDoor(go, id, targetScene, targetDoor, new Vector2(0f, 0.5f));
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
                if (sr.name != "Doorway") sr.color = frame;
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) tmp.text = label;
        }

        private static void SetDoor(GameObject go, string id, string targetScene, string targetDoor, Vector2 arrival)
        {
            var so = new SerializedObject(go.GetComponent<SceneDoor>());
            so.FindProperty("doorId").stringValue = id;
            so.FindProperty("targetScene").stringValue = targetScene;
            so.FindProperty("targetDoorId").stringValue = targetDoor;
            so.FindProperty("arrivalOffset").vector2Value = arrival;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A Mirror checkpoint with the circle's thoughts (each array = one visit's lines).</summary>
        public void Mirror(string name, float x, float floorTop, params string[][] thoughts)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/Mirror"), scene);
            go.name = name;
            go.transform.SetParent(checkpoints, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            var so = new SerializedObject(go.GetComponent<MirrorCheckpoint>());
            so.FindProperty("mirrorName").stringValue = name;
            var list = so.FindProperty("thoughts");
            list.arraySize = thoughts.Length;
            for (int i = 0; i < thoughts.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = DialogueAsset($"{sceneName}_{Safe(name)}_{i + 1}", "Reflection", thoughts[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
            var label = go.transform.Find("Name Label");
            if (label != null && label.GetComponent<TMP_Text>() != null) label.GetComponent<TMP_Text>().text = name;
        }

        /// <summary>A tablet (or notice, inscription...) read with Interact.</summary>
        public void Tablet(string id, float x, float floorTop, string speaker, params string[] lines)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/LoreTablet"), scene);
            go.name = $"{id} - {speaker}";
            go.transform.SetParent(lore, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            var conv = go.GetComponent<NpcConversation>();
            var so = new SerializedObject(conv);
            var asset = DialogueAsset($"{sceneName}_{id}", speaker, lines);
            so.FindProperty("firstMeeting").objectReferenceValue = asset;
            var rep = so.FindProperty("repeatDialogues");
            rep.arraySize = 1;
            rep.GetArrayElementAtIndex(0).objectReferenceValue = asset;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A character to listen to: a shape with a conversation (e.g. a freed villager, the Square).</summary>
        public GameObject Npc(string name, float x, float floorTop, bool isSquare, Color color, string[] first, string[] repeat, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : lore, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            var body = new GameObject("Visual");
            body.transform.SetParent(go.transform, false);
            if (isSquare)
            {
                body.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                body.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
                var sr = body.AddComponent<SpriteRenderer>();
                sr.sprite = Square; sr.color = color; sr.sortingOrder = 4;

                // Two eyes (the triangles have one): they blink and follow the circle (EnemyEye with no shape
                // to ride just stays where it's put). The right eye blinks with the left.
                EnemyEye leftEye = null;
                for (int side = -1; side <= 1; side += 2)
                {
                    var eye = new GameObject(side < 0 ? "Eye L" : "Eye R");
                    eye.transform.SetParent(go.transform, false);
                    eye.transform.localPosition = new Vector3(side * 0.32f, 1.05f, 0f);
                    eye.transform.localScale = new Vector3(0.3f, 0.3f, 1f);
                    var white = eye.AddComponent<SpriteRenderer>();
                    white.sprite = Circle; white.color = new Color(0.98f, 0.95f, 0.88f, 1f); white.sortingOrder = 6;
                    var pupil = new GameObject("Pupil");
                    pupil.transform.SetParent(eye.transform, false);
                    pupil.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
                    var dark = pupil.AddComponent<SpriteRenderer>();
                    dark.sprite = Circle; dark.color = new Color(0.12f, 0.1f, 0.14f, 1f); dark.sortingOrder = 7;
                    var eyeComp = eye.AddComponent<EnemyEye>();
                    if (leftEye == null) leftEye = eyeComp;
                    else
                    {
                        var eso = new SerializedObject(eyeComp);
                        eso.FindProperty("blinkWith").objectReferenceValue = leftEye;
                        eso.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            else
            {
                body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                Shape(body, 0.9f, color, Color.Lerp(color, Color.black, 0.25f), 4);
            }
            var prompt = Object.Instantiate(PromptTemplate(), go.transform);
            prompt.name = "Prompt";
            prompt.transform.localPosition = new Vector3(0f, isSquare ? 2.3f : 2.1f, 0f);
            prompt.GetComponent<TMP_Text>().text = "Listen";
            var conv = go.AddComponent<NpcConversation>();
            var so = new SerializedObject(conv);
            so.FindProperty("firstMeeting").objectReferenceValue = DialogueAsset($"{sceneName}_{Safe(name)}_First", name, first);
            var rep = so.FindProperty("repeatDialogues");
            rep.arraySize = 1;
            rep.GetArrayElementAtIndex(0).objectReferenceValue = DialogueAsset($"{sceneName}_{Safe(name)}_Repeat", name, repeat);
            so.FindProperty("prompt").objectReferenceValue = prompt.GetComponent<TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        /// <summary>World text for signs (e.g. "boss not built yet").</summary>
        public void Sign(string text, float x, float y, float size = 4f, Color? color = null)
        {
            var go = new GameObject("Sign - " + text.Split('\n')[0]);
            go.transform.SetParent(decor, false);
            go.transform.position = new Vector3(x, y, 0f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color ?? new Color(0.85f, 0.8f, 0.7f, 0.9f);
            tmp.rectTransform.sizeDelta = new Vector2(16f, 4f);
            tmp.sortingOrder = 2;
        }

        // ------------------------------------------------------------------ pickups and gates

        public void CapeString(PlayerAbility ability, float x, float floorTop, string stringName, string hint)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/CapeString"), scene);
            go.name = $"Cape String - {ability} ({stringName})";
            go.transform.SetParent(pickups, false);
            go.transform.position = new Vector3(x, floorTop + 1.4f, 0f);
            var so = new SerializedObject(go.GetComponent<CapeStringPickup>());
            so.FindProperty("ability").intValue = (int)ability;
            so.FindProperty("stringName").stringValue = stringName;
            so.FindProperty("hint").stringValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A Vertex Seal, the Mould, or any other story item (FlagPickup).</summary>
        public void Item(string prefab, string flag, string displayName, float x, float floorTop)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/" + prefab), scene);
            go.name = $"{displayName} ({flag})";
            go.transform.SetParent(pickups, false);
            go.transform.position = new Vector3(x, floorTop + 1.3f, 0f);
            var so = new SerializedObject(go.GetComponent<FlagPickup>());
            so.FindProperty("flag").stringValue = flag;
            so.FindProperty("displayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A gate (SealGate, MouldGate or WardenGate prefab) standing on the floor at x.</summary>
        public void Gate(string prefab, float x, float floorTop, float height, string[] flags, int minSeals, string label)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/" + prefab), scene);
            go.name = $"{prefab} ({label})";
            go.transform.SetParent(gates, false);
            go.transform.position = new Vector3(x, floorTop + height * 0.5f, 0f);
            go.transform.localScale = new Vector3(go.transform.localScale.x, height, 1f);
            var so = new SerializedObject(go.GetComponent<ProgressGate>());
            var f = so.FindProperty("requiredFlags");
            f.arraySize = flags != null ? flags.Length : 0;
            for (int i = 0; i < f.arraySize; i++) f.GetArrayElementAtIndex(i).stringValue = flags[i];
            so.FindProperty("minSeals").intValue = minSeals;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public void CrumblingWall(string id, float x, float floorTop, float height, float width = 1.2f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("World/CrumblingWall"), scene);
            go.name = "Crumbling Wall - " + id;
            go.transform.SetParent(gates, false);
            go.transform.position = new Vector3(x, floorTop + height * 0.5f, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);
            var so = new SerializedObject(go.GetComponent<Systems.CrumblingWall>());
            so.FindProperty("wallId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ enemies

        /// <summary>Triangle kind given to enemies placed from now on (scalene in the Slums, isosceles on the Terraces...).</summary>
        public TriangleKind Kind { get; set; } = TriangleKind.Standard;

        /// <summary>Places an enemy prefab with its feet on <paramref name="floorTop"/> (offset per type).</summary>
        public GameObject Enemy(string prefab, float x, float floorTop, Transform parent = null)
        {
            var go = PlaceEnemy(prefab, x, floorTop, parent);
            ApplyKind(go);
            return go;
        }

        /// <summary>Gives a placed enemy the current <see cref="Kind"/>: rank + shape on triangles, shape only on others.</summary>
        private void ApplyKind(GameObject go)
        {
            if (Kind == TriangleKind.Standard) return;
            var tri = go.GetComponent<TriangleEnemy>();
            if (tri != null)
            {
                var so = new SerializedObject(tri);
                so.FindProperty("kind").intValue = (int)Kind;
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
            // Wedge Chargers, Overseers, Spires and Swarms have their own fixed silhouettes.
            if (go.GetComponent<WedgeCharger>() != null || go.GetComponent<Overseer>() != null ||
                go.GetComponent<Spire>() != null || go.GetComponent<ShardSwarm>() != null) return;
            if (go.GetComponentInChildren<RoundedTriangleMesh>() == null) return;
            var shapeKind = go.AddComponent<TriangleShapeKind>();
            var sk = new SerializedObject(shapeKind);
            sk.FindProperty("kind").intValue = (int)Kind;
            sk.ApplyModifiedPropertiesWithoutUndo();
        }

        private GameObject PlaceEnemy(string prefab, float x, float floorTop, Transform parent)
        {
            float lift;
            switch (prefab)
            {
                case "Spire": lift = 0.855f; break;
                case "Overseer": lift = 0.75f; break;
                case "SplitterLarge": lift = 0.75f; break;
                case "TriangleFlyer": lift = 3f; break;
                case "ShardSwarm": lift = 5f; break;
                default: lift = 0.6f; break;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Enemies/" + prefab), scene);
            go.transform.SetParent(parent != null ? parent : enemies, false);
            go.transform.position = new Vector3(x, floorTop + lift, 0f);
            return go;
        }

        /// <summary>A Ceiling Sentry hanging under a ceiling whose underside is at <paramref name="ceilingBottom"/>.</summary>
        public GameObject Sentry(float x, float ceilingBottom, Transform parent = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Enemies/CeilingSentry"), scene);
            go.transform.SetParent(parent != null ? parent : enemies, false);
            go.transform.position = new Vector3(x, ceilingBottom - 0.375f, 0f);
            ApplyKind(go);
            return go;
        }

        /// <summary>A sub-group of enemies (e.g. the Slums' residents, which vanish once the Slums are freed).</summary>
        public Transform EnemyGroup(string name, string hideWhenFlag = null)
        {
            var g = new GameObject(name).transform;
            g.SetParent(enemies, false);
            if (!string.IsNullOrEmpty(hideWhenFlag)) SetFlagVisibility(g.gameObject, hideWhenFlag, false);
            return g;
        }

        /// <summary>A group under Decor/Lore that only shows once <paramref name="flag"/> is set.</summary>
        public Transform ShowWhen(string name, string flag)
        {
            var g = new GameObject(name).transform;
            g.SetParent(lore, false);
            SetFlagVisibility(g.gameObject, flag, true);
            return g;
        }

        private static void SetFlagVisibility(GameObject go, string flag, bool showWhenSet)
        {
            var a = go.AddComponent<ActiveWhenFlag>();
            var so = new SerializedObject(a);
            so.FindProperty("flag").stringValue = flag;
            so.FindProperty("showWhenSet").boolValue = showWhenSet;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ decor (no collision)

        public void ShardPile(float x, float floorTop, int count = 12, float spread = 2.2f, int seed = 1)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Decor/ShardPile"), scene);
            go.transform.SetParent(decor, false);
            go.transform.position = new Vector3(x, floorTop, 0f);
            go.transform.localScale = new Vector3(spread / 2.2f, spread / 2.2f, 1f);
        }

        public void House(float x0, float x1, float top, float floorTop, float tilt)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Decor/ScaleneHouse"), scene);
            go.transform.SetParent(decor, false);
            go.transform.position = new Vector3((x0 + x1) * 0.5f, floorTop, 0f);
            float w = x1 - x0, h = top - floorTop;
            go.transform.Find("Body").localScale = new Vector3(w, h, 1f);
            go.transform.Find("Body").localPosition = new Vector3(0f, h * 0.5f, 0f);
            var roof = go.transform.Find("Roof");
            roof.localPosition = new Vector3((tilt - 0.5f) * w * 0.3f, h + 0.55f, 0f);
            roof.localScale = new Vector3(w * 0.62f, 0.9f, 1f);
            roof.localRotation = Quaternion.Euler(0f, 0f, (0.5f - tilt) * 14f);
        }

        public void Pillar(float x, float floorTop, float height, Color color)
        {
            var go = new GameObject("Pillar");
            go.transform.SetParent(decor, false);
            go.transform.position = new Vector3(x, floorTop + height * 0.5f, 0.5f);
            go.transform.localScale = new Vector3(0.8f, height, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square; sr.color = color; sr.sortingOrder = -8;
        }

        /// <summary>A big background shape (crystal, tier facade, silhouette): a triangle, no collision.</summary>
        public void BackdropTriangle(float x, float y, float size, float angle, Color color)
        {
            var go = new GameObject("Backdrop Triangle");
            go.transform.SetParent(decor, false);
            go.transform.position = new Vector3(x, y, 1f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            Shape(go, size, color, Color.Lerp(color, Color.black, 0.3f), -9);
        }

        /// <summary>A background panel (darker band, wall, tier facade). No collision.</summary>
        public void BackdropPanel(float x0, float x1, float y0, float y1, Color color)
        {
            var go = new GameObject("Backdrop Panel");
            go.transform.SetParent(decor, false);
            go.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 2f);
            go.transform.localScale = new Vector3(x1 - x0, y1 - y0, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square; sr.color = color; sr.sortingOrder = -12;
        }

        // ------------------------------------------------------------------ helpers

        public static GameObject Prefab(string path)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/{path}.prefab");
            if (p == null) throw new System.Exception("Missing prefab: " + path);
            return p;
        }

        /// <summary>Adds a RoundedTriangleMesh to <paramref name="go"/>.</summary>
        public static RoundedTriangleMesh Shape(GameObject go, float radius, Color top, Color outline, int sortingOrder)
        {
            if (go.GetComponent<MeshFilter>() == null) go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>() != null ? go.GetComponent<MeshRenderer>() : go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ShapeMaterial;
            var shape = go.AddComponent<RoundedTriangleMesh>();
            shape.Configure(radius, radius * 0.18f, 0.06f, top, Color.Lerp(top, Color.black, 0.18f), outline, sortingOrder);
            return shape;
        }

        private static GameObject promptTemplate;

        /// <summary>The "Look" prompt from the Mirror prefab, reused for tablets and NPCs.</summary>
        public static GameObject PromptTemplate()
        {
            if (promptTemplate == null) promptTemplate = Prefab("World/Mirror").transform.Find("Prompt").gameObject;
            return promptTemplate;
        }

        public static Dialogue DialogueAsset(string assetName, string speaker, string[] lines)
        {
            Directory.CreateDirectory(DialogueFolder);
            string path = $"{DialogueFolder}/{assetName}.asset";
            var d = AssetDatabase.LoadAssetAtPath<Dialogue>(path);
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<Dialogue>();
                AssetDatabase.CreateAsset(d, path);
            }
            var so = new SerializedObject(d);
            so.FindProperty("speaker").stringValue = speaker;
            var l = so.FindProperty("lines");
            l.arraySize = lines.Length;
            for (int i = 0; i < lines.Length; i++) l.GetArrayElementAtIndex(i).stringValue = lines[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(d);
            return d;
        }

        private static string Safe(string s) => s.Replace(" ", "").Replace("'", "");
    }
}
