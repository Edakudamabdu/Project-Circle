using System.IO;
using ProjectCircle.Enemies;
using ProjectCircle.Systems;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// Creates the level-building prefabs once (skips any that already exist, so an artist's changes to them are
    /// kept). All of them are plain placeholder shapes meant to be re-skinned:
    ///   Enemies/TriangleChaser   the basic triangle without its laser (a prefab variant of TriangleEnemy)
    ///   World/CapeString         a string in an alcove: joins the cape, unlocks an ability
    ///   World/VertexSeal         a Vertex Seal (story flag pickup)
    ///   World/TriangleMould      the Triangle Mould (story flag pickup)
    ///   World/SealGate           opens with 2 Vertex Seals
    ///   World/MouldGate          opens with the Triangle Mould (also the Rank Gate)
    ///   World/WardenGate         opens once the Base Warden is beaten
    ///   World/CrumblingWall      breaks when dashed into
    ///   World/LoreTablet         a tablet read with Interact
    ///   Decor/ShardPile          a heap of snapped-off corners (no collision)
    ///   Decor/ScaleneHouse       a Slums house with a crooked roof (no collision)
    /// </summary>
    public static class LevelPrefabFactory
    {
        private const string Root = "Assets/_Project/Prefabs/";
        private const int GroundLayer = 3;

        public static void EnsureAll()
        {
            Directory.CreateDirectory(Root + "World");
            Directory.CreateDirectory(Root + "Decor");
            Chaser();
            CapeString();
            Seal();
            Mould();
            Gate("SealGate", new string[0], 2, new Color(0.55f, 0.5f, 0.38f), new Color(1f, 0.82f, 0.4f), 2, false);
            Gate("MouldGate", new[] { "Mould.Triangle" }, 0, new Color(0.42f, 0.36f, 0.28f), new Color(0.85f, 0.65f, 0.2f), 1, true);
            Gate("WardenGate", new[] { "Seal.Warden" }, 0, new Color(0.45f, 0.18f, 0.15f), new Color(1f, 0.82f, 0.4f), 1, false);
            Crumbling();
            Tablet();
            ShardPile();
            House();
            Edge();
            BuildingDoor();
            AssetDatabase.SaveAssets();
        }

        /// <summary>An invisible scene transition at a level edge or an opening (walk off the screen into the next region).</summary>
        private static void Edge()
        {
            if (Exists("World/SceneEdge")) return;
            var root = new GameObject("SceneEdge");
            var box = root.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1f, 6f);
            box.offset = new Vector2(0f, 3f);
            root.AddComponent<SceneDoor>();
            Save(root, "World/SceneEdge");
        }

        /// <summary>A doorway into a building or a cave, for entrances in the middle of a level.</summary>
        private static void BuildingDoor()
        {
            if (Exists("World/BuildingDoor")) return;
            var root = new GameObject("BuildingDoor");
            var box = root.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1.4f, 2.4f);
            box.offset = new Vector2(0f, 1.2f);
            var door = root.AddComponent<SceneDoor>();
            var so = new SerializedObject(door);
            so.FindProperty("arrivalOffset").vector2Value = new Vector2(0f, 0.5f); // appear standing in the doorway
            so.ApplyModifiedPropertiesWithoutUndo();
            var stone = new Color(0.42f, 0.36f, 0.3f);
            Sprite("Doorway", root.transform, new Vector3(0f, 1.4f, 0.3f), new Vector3(1.8f, 2.8f, 1f), new Color(0.04f, 0.035f, 0.04f), -2);
            Sprite("Post_L", root.transform, new Vector3(-1.1f, 1.5f, 0.2f), new Vector3(0.4f, 3f, 1f), stone, -1);
            Sprite("Post_R", root.transform, new Vector3(1.1f, 1.5f, 0.2f), new Vector3(0.4f, 3f, 1f), stone, -1);
            Sprite("Lintel", root.transform, new Vector3(0f, 3.15f, 0.2f), new Vector3(2.8f, 0.45f, 1f), stone, -1);
            var label = new GameObject("Label");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, 3.85f, 0f);
            var tmp = label.AddComponent<TextMeshPro>();
            tmp.fontSize = 2.6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.85f, 0.8f, 0.7f, 0.85f);
            tmp.rectTransform.sizeDelta = new Vector2(8f, 1.2f);
            tmp.sortingOrder = 2;
            Save(root, "World/BuildingDoor");
        }

        private static bool Exists(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + path + ".prefab") != null;

        private static void Save(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, Root + path + ".prefab");
            Object.DestroyImmediate(go);
        }

        private static GameObject Sprite(string name, Transform parent, Vector3 pos, Vector3 scale, Color color, int order, Sprite sprite = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : RegionBuilder.Square;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        // The basic triangle, minus its laser: the early-game chaser (blade + pogo is all it takes).
        private static void Chaser()
        {
            if (Exists("Enemies/TriangleChaser")) return;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(RegionBuilder.Prefab("Enemies/TriangleEnemy"));
            var laser = inst.GetComponent<TriangleLaser>();
            if (laser != null) Object.DestroyImmediate(laser);
            inst.name = "TriangleChaser";
            PrefabUtility.SaveAsPrefabAsset(inst, Root + "Enemies/TriangleChaser.prefab");
            Object.DestroyImmediate(inst);
        }

        private static void CapeString()
        {
            if (Exists("World/CapeString")) return;
            var root = new GameObject("CapeString");
            var pickup = root.AddComponent<CapeStringPickup>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            // An alcove of soft light, with one red string hanging down the middle.
            Sprite("Alcove", visual, new Vector3(0f, 0.2f, 0.5f), new Vector3(1.8f, 3.4f, 1f), new Color(0.95f, 0.82f, 0.78f, 0.35f), -3);
            Sprite("Glow", visual, new Vector3(0f, -0.4f, 0f), new Vector3(1.5f, 1.5f, 1f), new Color(1f, 0.45f, 0.4f, 0.35f), -2, RegionBuilder.Circle);
            var s = new GameObject("String");
            s.transform.SetParent(visual, false);
            var line = s.AddComponent<LineRenderer>();
            line.sharedMaterial = Fx.Material;
            line.useWorldSpace = false;
            line.positionCount = 6;
            line.SetPositions(new[] { new Vector3(0f, 1.9f), new Vector3(-0.12f, 1.2f), new Vector3(0.1f, 0.5f), new Vector3(-0.08f, -0.1f), new Vector3(0.06f, -0.6f), new Vector3(0f, -0.9f) });
            line.widthMultiplier = 0.09f;
            line.startColor = line.endColor = new Color(0.78f, 0.15f, 0.14f, 1f);
            line.sortingOrder = 3;
            var so = new SerializedObject(pickup);
            so.FindProperty("visual").objectReferenceValue = visual.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, "World/CapeString");
        }

        private static void Seal()
        {
            if (Exists("World/VertexSeal")) return;
            var root = new GameObject("VertexSeal");
            var pickup = root.AddComponent<FlagPickup>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            Sprite("Glow", visual.transform, Vector3.zero, new Vector3(1.8f, 1.8f, 1f), new Color(1f, 0.82f, 0.4f, 0.35f), 2, RegionBuilder.Circle);
            var corner = new GameObject("Seal");
            corner.transform.SetParent(visual.transform, false);
            RegionBuilder.Shape(corner, 0.45f, new Color(1f, 0.86f, 0.45f), new Color(0.6f, 0.42f, 0.1f), 4);
            var so = new SerializedObject(pickup);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, "World/VertexSeal");
        }

        private static void Mould()
        {
            if (Exists("World/TriangleMould")) return;
            var root = new GameObject("TriangleMould");
            var pickup = root.AddComponent<FlagPickup>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            Sprite("Altar", visual.transform, new Vector3(0f, -1.0f, 0.2f), new Vector3(2.2f, 0.6f, 1f), new Color(0.55f, 0.45f, 0.3f), 1);
            Sprite("Glow", visual.transform, Vector3.zero, new Vector3(2.6f, 2.6f, 1f), new Color(1f, 0.8f, 0.3f, 0.3f), 2, RegionBuilder.Circle);
            var m = new GameObject("Mould");
            m.transform.SetParent(visual.transform, false);
            RegionBuilder.Shape(m, 0.7f, new Color(0.95f, 0.75f, 0.25f), new Color(0.5f, 0.35f, 0.08f), 4);
            var so = new SerializedObject(pickup);
            so.FindProperty("flag").stringValue = "Mould.Triangle";
            so.FindProperty("displayName").stringValue = "The Triangle Mould";
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, "World/TriangleMould");
        }

        /// <summary>A solid barrier (1 wide; the builder sets its height) with a symbol: diamonds (seals) or a triangle (Mould).</summary>
        private static void Gate(string name, string[] flags, int minSeals, Color body, Color symbol, int symbols, bool triangleSymbol)
        {
            if (Exists("World/" + name)) return;
            var root = new GameObject(name);
            root.layer = GroundLayer;
            root.transform.localScale = new Vector3(1.2f, 4f, 1f);
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = RegionBuilder.Square;
            sr.color = body;
            sr.sortingOrder = 1;
            root.AddComponent<BoxCollider2D>();
            var gate = root.AddComponent<ProgressGate>();
            var so = new SerializedObject(gate);
            var f = so.FindProperty("requiredFlags");
            f.arraySize = flags.Length;
            for (int i = 0; i < flags.Length; i++) f.GetArrayElementAtIndex(i).stringValue = flags[i];
            so.FindProperty("minSeals").intValue = minSeals;
            so.ApplyModifiedPropertiesWithoutUndo();
            // Symbols in the middle (child scale is relative to the stretched gate, so counter-scale).
            for (int i = 0; i < symbols; i++)
            {
                float y = symbols == 1 ? 0f : (i == 0 ? 0.08f : -0.08f);
                if (triangleSymbol)
                {
                    var t = new GameObject("Symbol");
                    t.transform.SetParent(root.transform, false);
                    t.transform.localPosition = new Vector3(0f, y, -0.1f);
                    t.transform.localScale = new Vector3(1f / 1.2f, 1f / 4f, 1f);
                    RegionBuilder.Shape(t, 0.4f, symbol, Color.Lerp(symbol, Color.black, 0.4f), 3);
                }
                else
                {
                    var d = Sprite("Symbol", root.transform, new Vector3(0f, y, -0.1f), new Vector3(0.45f / 1.2f, 0.45f / 4f, 1f), symbol, 3);
                    d.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                }
            }
            Save(root, "World/" + name);
        }

        private static void Crumbling()
        {
            if (Exists("World/CrumblingWall")) return;
            var root = new GameObject("CrumblingWall");
            root.layer = GroundLayer;
            root.transform.localScale = new Vector3(1.2f, 4f, 1f);
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = RegionBuilder.Square;
            sr.color = new Color(0.58f, 0.52f, 0.46f);
            sr.sortingOrder = 1;
            root.AddComponent<BoxCollider2D>();
            root.AddComponent<Systems.CrumblingWall>();
            // Cracks: a zigzag line so it reads as breakable.
            var c = new GameObject("Cracks");
            c.transform.SetParent(root.transform, false);
            var line = c.AddComponent<LineRenderer>();
            line.sharedMaterial = Fx.Material;
            line.useWorldSpace = false;
            line.positionCount = 6;
            line.SetPositions(new[] { new Vector3(-0.3f, 0.45f), new Vector3(0.15f, 0.25f), new Vector3(-0.2f, 0.05f), new Vector3(0.25f, -0.15f), new Vector3(-0.1f, -0.3f), new Vector3(0.2f, -0.45f) });
            line.widthMultiplier = 0.06f;
            line.startColor = line.endColor = new Color(0.25f, 0.2f, 0.16f, 1f);
            line.sortingOrder = 2;
            Save(root, "World/CrumblingWall");
        }

        private static void Tablet()
        {
            if (Exists("World/LoreTablet")) return;
            var root = new GameObject("LoreTablet");
            Sprite("Stone", root.transform, new Vector3(0f, 0.65f, 0.2f), new Vector3(0.9f, 1.3f, 1f), new Color(0.5f, 0.42f, 0.32f), -1);
            Sprite("Face", root.transform, new Vector3(0f, 0.72f, 0.1f), new Vector3(0.62f, 0.95f, 1f), new Color(0.66f, 0.57f, 0.44f), 0);
            var prompt = Object.Instantiate(RegionBuilder.PromptTemplate(), root.transform);
            prompt.name = "Prompt";
            prompt.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            prompt.GetComponent<TMP_Text>().text = "Read";
            var conv = root.AddComponent<NpcConversation>();
            var so = new SerializedObject(conv);
            so.FindProperty("prompt").objectReferenceValue = prompt.GetComponent<TMP_Text>();
            so.FindProperty("talkRadius").floatValue = 1.6f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, "World/LoreTablet");
        }

        private static void ShardPile()
        {
            if (Exists("Decor/ShardPile")) return;
            var root = new GameObject("ShardPile");
            var rnd = new System.Random(7);
            for (int i = 0; i < 12; i++)
            {
                var s = new GameObject("Shard");
                s.transform.SetParent(root.transform, false);
                float x = (float)(rnd.NextDouble() * 4.4 - 2.2);
                float y = (float)(rnd.NextDouble() * 0.7) * (1f - Mathf.Abs(x) / 2.4f);
                s.transform.localPosition = new Vector3(x, y + 0.15f, 0.5f);
                s.transform.localRotation = Quaternion.Euler(0f, 0f, (float)(rnd.NextDouble() * 360));
                float r = 0.2f + (float)rnd.NextDouble() * 0.35f;
                var c = Color.Lerp(new Color(0.55f, 0.48f, 0.4f), new Color(0.72f, 0.64f, 0.52f), (float)rnd.NextDouble());
                RegionBuilder.Shape(s, r, c, Color.Lerp(c, Color.black, 0.35f), -6);
            }
            Save(root, "Decor/ShardPile");
        }

        private static void House()
        {
            if (Exists("Decor/ScaleneHouse")) return;
            var root = new GameObject("ScaleneHouse");
            var body = Sprite("Body", root.transform, new Vector3(0f, 1.5f, 1f), new Vector3(5f, 3f, 1f), new Color(0.55f, 0.43f, 0.3f), -8);
            Sprite("Doorway", body.transform, new Vector3(0.2f, -0.25f, -0.1f), new Vector3(0.18f, 0.5f, 1f), new Color(0.25f, 0.18f, 0.12f), -7);
            var roof = new GameObject("Roof");
            roof.transform.SetParent(root.transform, false);
            roof.transform.localPosition = new Vector3(0f, 3.55f, 1f);
            roof.transform.localScale = new Vector3(3f, 0.9f, 1f);
            RegionBuilder.Shape(roof, 1f, new Color(0.62f, 0.38f, 0.24f), new Color(0.35f, 0.2f, 0.12f), -7);
            Save(root, "Decor/ScaleneHouse");
        }
    }
}
