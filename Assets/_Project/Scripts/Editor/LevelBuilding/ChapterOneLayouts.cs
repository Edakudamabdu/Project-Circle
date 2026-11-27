using System.Collections.Generic;
using ProjectCircle.Enemies;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// The greybox layouts of Chapter 1 (Triangle World), following Docs/TriangleWorld_MapSketch and
    /// Docs/Chapter1_LevelLayout. Units: 1 = one Unity unit; floors usually have their top at y = -1.
    /// Jump ≈ 4.6 high and ≈ 6 across; dash adds ≈ 3.5 across; the double jump reaches ≈ 8.5 high.
    ///
    /// Regions join like Hollow Knight rooms: walk off an opening at the edge (SceneEdge), jump up a shaft or drop
    /// down a hole (Hatch), or enter a building/cave in the middle of a level (BuildingDoor).
    /// Mirrors and tablets sit in safe spots, well away from enemies.
    /// Triangles follow the strata: scalene in the Slums, isosceles on the Terraces, equilateral in the Spire
    /// (the more perfect, the more aggressive: see TriangleKinds).
    ///
    /// Menu: Project Circle ▸ Level Building. Building a region OVERWRITES its scene, so only use it for regions
    /// nobody has started editing by hand.
    /// </summary>
    public static class ChapterOneLayouts
    {
        private const PlayerAbility Blade = PlayerAbility.Attack;
        private const PlayerAbility BladeDash = PlayerAbility.Attack | PlayerAbility.Dash;
        private const PlayerAbility BladeDashParry = PlayerAbility.Attack | PlayerAbility.Dash | PlayerAbility.Parry;
        private const PlayerAbility AllAbilities = PlayerAbility.All;

        private static readonly Color Stone = new Color(0.42f, 0.36f, 0.3f);

        /// <summary>Region scene name → builder. Order = suggested Build Settings order.</summary>
        public static readonly Dictionary<string, System.Action> Regions = new Dictionary<string, System.Action>
        {
            { "Graveyard", Graveyard },
            { "ScaleneSlums", Slums },
            { "IsoscelesTerraces", Terraces },
            { "TwinIsosceles", TwinIsosceles },
            { "EquilateralSpire", Spire },
            { "ApexThrone", ApexThrone },
            { "TempleOfThreePoints", Temple },
            { "RoadToSquareRealm", Road },
            { "WhisperingFissure", Fissure },
            { "HallOfMeasure", HallOfMeasure },
            { "PrismCaverns", PrismCaverns },
            { "RefractorLair", RefractorLair },
            { "SquaresRefuge", SquaresRefuge },
            { "ShrineOfFirstBreak", Shrine },
        };

        [MenuItem("Project Circle/Level Building/Build ALL Chapter 1 regions (overwrites!)")]
        private static void BuildAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Build Chapter 1",
                "This regenerates every Chapter 1 region scene from the greybox layouts and OVERWRITES them " +
                "(hand edits in those scenes are lost). Continue?", "Rebuild all", "Cancel")) return;
            BuildAll();
        }

        public static void BuildAll()
        {
            LevelPrefabFactory.EnsureAll();
            foreach (var kv in Regions) kv.Value();
            WireExistingScenes();
            SetBuildOrder();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/StoryTest.unity", OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
        }

        public static void Build(string region)
        {
            LevelPrefabFactory.EnsureAll();
            Regions[region]();
        }

        // ---------------------------------------------------------------- shared pieces

        /// <summary>
        /// Openings at both ends of a room whose floor top is <paramref name="floorTop"/>: the floor runs out past
        /// each opening, a wall stands above it (up to <paramref name="top"/>), and a backstop sits beyond.
        /// Pass NaN for a side without an opening (it gets a plain wall).
        /// </summary>
        private static void Ends(RegionBuilder r, float leftX, float rightX, float floorTop, float top)
        {
            // Each opening leads into a short gap (a tunnel 6 deep) the circle walks into. The camera stops at the
            // wall's outer face, so the circle walks off-screen into the gap before the edge trigger fires.
            if (!float.IsNaN(leftX))
            {
                r.Ground(leftX - 7f, leftX + 1f, floorTop);             // floor of the gap
                r.Solid(leftX - 7f, leftX + 1f, floorTop + 6f, top);   // wall above the opening + tunnel roof
                r.Solid(leftX - 9f, leftX - 7f, -14, top);             // backstop at the end of the gap (never seen)
            }
            if (!float.IsNaN(rightX))
            {
                r.Ground(rightX - 1f, rightX + 7f, floorTop);
                r.Solid(rightX - 1f, rightX + 7f, floorTop + 6f, top);
                r.Solid(rightX + 7f, rightX + 9f, -14, top);
            }
        }

        // ====================================================================== LOWER STRATUM

        private static void Graveyard()
        {
            var r = new RegionBuilder("Graveyard", "Broken Vertex Graveyard",
                new Color(0.09f, 0.08f, 0.075f), new Color(0.31f, 0.27f, 0.23f), new Color(0.42f, 0.37f, 0.31f),
                Blade, new string[0],
                "First fights with the cape (blade + pogo only): chasers, a Spire pit, a Splitter.\n" +
                "Ends with the Runner's String (+ DASH), a gap that needs it, and the first Wedge Charger.\n" +
                "The ruined doorway (sealed by the Triangle Mould) leads to the secret Shrine of the First Break.\n" +
                "Exits: west opening → Hall of Strings, east opening → Scalene Slums. No Mirror here.");

            Ends(r, -11, 125, -1, 18);
            r.Solid(-12, 124, 15, 18);               // cave ceiling
            r.Ground(-14, 40, -1);
            r.Ground(40, 54, -6);                    // Spire pit
            r.Ground(51, 54, -3.6f, -6);             // step out of the pit
            r.Ground(54, 92, -1);
            r.Platform(60, 64, 1.5f);
            r.Platform(68, 72, 3f);
            r.Ground(92, 100, -9);                   // the dash gap (8 wide)
            r.Ground(92, 94, -5, -9);                // climb back out if you fall
            r.Ground(100, 128, -1);

            r.PlayerStart(-6, -1);
            r.Edge("FromHall", -11, -1, "StoryTest", "EnemyDoor", exitIsOnRight: false);
            r.Edge("ToSlums", 125, -1, "ScaleneSlums", "FromGraveyard", exitIsOnRight: true);
            r.Building("ToShrine", 80, -1, "ShrineOfFirstBreak", "FromGraveyard", "", Stone);
            r.Gate("MouldGate", 80, -1, 3.6f, new[] { "Mould.Triangle" }, 0, "Mould seal on the Shrine");

            // Lore, each well clear of the enemies.
            r.Tablet("T1", 4, -1, "Worn Tablet",
                "Here lie the corners they snapped off us.",
                "A triangle with a broken vertex is no triangle, they said. So down we went, with every other shape that didn't count.");
            r.Tablet("T2", 56.5f, -1, "Scratched Tablet",
                "My corner chipped in a fall. By morning my own family would not look at me.",
                "We are still triangles. We were always still triangles.");
            r.Tablet("T3", 77, -1, "Inscription",
                "Only one who holds the shape of the first may pass.");

            // The discarded: broken, lowest-rank (scalene) triangles, each one different.
            r.Kind = TriangleKind.Scalene;
            r.Enemy("TriangleChaser", 22, -1);
            r.Enemy("TriangleChaser", 32, -1);
            r.Enemy("Spire", 43.5f, -6); r.Enemy("Spire", 47f, -6); r.Enemy("Spire", 50.5f, -6);
            r.Enemy("SplitterMedium", 64, -1);
            r.Enemy("TriangleChaser", 68, -1);
            r.CapeString(PlayerAbility.Dash, 87, -1, "The Runner's String", "DASH: Shift / gamepad RT");
            r.Enemy("WedgeCharger", 112, -1);        // only after the dash: dash through it
            r.Kind = TriangleKind.Standard;

            foreach (float x in new[] { 9f, 16f, 30f, 61f, 84f, 117f }) r.ShardPile(x, -1);
            r.BackdropTriangle(20, 6, 4f, 15, new Color(0.2f, 0.18f, 0.16f));
            r.BackdropTriangle(66, 7, 5f, 200, new Color(0.2f, 0.18f, 0.16f));
            r.BackdropTriangle(106, 6, 4f, 80, new Color(0.2f, 0.18f, 0.16f));
            r.Save();
        }

        private static void Slums()
        {
            var r = new RegionBuilder("ScaleneSlums", "Scalene Slums",
                new Color(0.11f, 0.09f, 0.07f), new Color(0.36f, 0.28f, 0.2f), new Color(0.47f, 0.37f, 0.26f),
                BladeDash, new string[0],
                "The lowest-rank village. Its residents are SCALENE triangles (uneven, the least aggressive), corrupted\n" +
                "(Enemies/Corrupted Residents hides once Freed.Slums is set; the freed villagers show instead).\n" +
                "Edge: chasers + a Charger (dash). Square: + PARRY string. Lower streets: Sentry, orb caster, Swarm (parry).\n" +
                "The Mirror is in the quiet stretch before the Warden's Gate. Raised alcove: crumbling wall (dash) → the crack into the Fissure.\n" +
                "Exits: west opening → Graveyard, east through the Warden's Gate → Base Warden.");

            Ends(r, -11, 131, -1, 20);
            r.Ground(-14, 134, -1);
            r.Platform(18, 24, 2f);                  // rooftops
            r.Platform(33, 37, 3.5f);
            r.Platform(80, 85, 3f);
            r.Solid(62, 74, 5f, 6.4f);               // overhang the Sentry hangs from
            // Raised alcove with the crumbling wall and the crack into the Fissure.
            r.Platform(104, 110.6f, 3f, 0.8f);
            r.Solid(104, 110.6f, 7f, 7.6f);
            r.Solid(110f, 110.6f, 3f, 7f);

            foreach (var h in new[] { new[] { 6f, 12f, 3f, 0.3f }, new[] { 17f, 25f, 1.6f, 0.7f }, new[] { 32f, 38f, 3.1f, 0.4f },
                                      new[] { 56f, 61f, 2.1f, 0.6f }, new[] { 75f, 79f, 3.5f, 0.25f }, new[] { 86f, 92f, 2.6f, 0.75f },
                                      new[] { 95f, 102f, 3.2f, 0.35f } })
                r.House(h[0], h[1], h[2], -1, h[3]);

            r.PlayerStart(-6, -1);
            r.Edge("FromGraveyard", -11, -1, "Graveyard", "ToSlums", exitIsOnRight: false);
            r.Edge("ToWarden", 131, -1, "BossArena", "Arena", exitIsOnRight: true);
            r.Building("ToFissure", 108.5f, 3f, "WhisperingFissure", "FromSlums", "", new Color(0.25f, 0.2f, 0.28f));
            r.CrumblingWall("SlumsToFissure", 105.6f, 3f, 4f, 0.9f);
            foreach (float x in new[] { 123.6f, 128.4f }) r.Pillar(x, -1, 7, new Color(0.45f, 0.16f, 0.13f));
            r.Sign("THE WARDEN'S GATE", 126, 7.2f, 4f, new Color(0.9f, 0.55f, 0.45f, 0.9f));

            r.Tablet("T4", 3, -1, "Warden's Notice",
                "BY ORDER OF THE WARDEN:",
                "Scalenes keep below the Line. Uneven sides, uneven rights.",
                "Any scalene found on the Terraces will be returned to the Slums.");
            r.CapeString(PlayerAbility.Parry, 44, -1, "Where They Stood Together", "PARRY: F / gamepad LB, just before a hit");
            r.Mirror("Slums Mirror", 117, -1,
                new[] { "They were thrown down too...", "and still they keep each other below." },
                new[] { "The same whisper the old one heard.", "It's in all of them." });

            r.Kind = TriangleKind.Scalene;
            var residents = r.EnemyGroup("Corrupted Residents", hideWhenFlag: "Freed.Slums");
            r.Enemy("TriangleChaser", 16, -1, residents);
            r.Enemy("TriangleChaser", 21, 2f, residents);     // on a rooftop
            r.Enemy("WedgeCharger", 29, -1, residents);
            r.Sentry(68, 5f, residents);
            r.Enemy("TriangleOrbCaster", 82.5f, 3f, residents);
            r.Enemy("ShardSwarm", 92, -1, residents);
            r.Enemy("TriangleChaser", 98, -1, residents);
            r.Kind = TriangleKind.Standard;

            var freed = r.ShowWhen("Freed Villagers", "Freed.Slums");
            r.Npc("Scalene Villager", 40, -1, false, new Color(0.85f, 0.7f, 0.45f),
                new[] { "The whispering stopped the moment the Warden fell.", "You lifted us. Let us lift you." },
                new[] { "Uneven sides, the notice said. As if any of us chose our angles." }, freed);
            r.Npc("Old Scalene", 60, -1, false, new Color(0.78f, 0.62f, 0.42f),
                new[] { "I remember the Line being drawn. They said it was for our own good." },
                new[] { "Go up, little circle. Show them the bottom can climb." }, freed);
            foreach (var npc in freed.GetComponentsInChildren<RoundedTriangleMesh>(true))
                TriangleKinds.ApplyFrame(npc.transform, TriangleKind.Scalene);
            r.Save();
        }

        // ====================================================================== MIDDLE STRATUM

        private static void Terraces()
        {
            var r = new RegionBuilder("IsoscelesTerraces", "Isosceles Terraces",
                new Color(0.1f, 0.1f, 0.09f), new Color(0.42f, 0.38f, 0.28f), new Color(0.55f, 0.5f, 0.37f),
                AllAbilities, new[] { "Seal.Warden" },
                "Middle-rank tiers of ISOSCELES triangles: each tier 4-6 units higher (rank = height); the upper tiers need the double jump.\n" +
                "The Mirror is at the quiet bottom entrance. An Overseer watches from a perch over the top tier.\n" +
                "Top tier, left: the hall of the Twin Isosceles (building). Right: the Seal Gate (2 Vertex Seals) → Equilateral Spire.\n" +
                "Hidden: a high ledge above the second tier; jump up its shaft to reach the Square's Refuge.");

            Ends(r, -11, float.NaN, -1, 40);
            r.Ground(-14, 30, -1);
            r.Ground(30, 55, 3);                     // tier 1 (one jump up)
            r.Ground(55, 80, 9);                     // tier 2 (double jump)
            r.Ground(80, 127, 15);                   // tier 3 (double jump), on into the east gap
            r.Solid(104, 127, 21, 40);               // over the Seal Gate, the east opening and its gap
            r.Solid(127, 129, -14, 40);              // backstop at the end of the gap (never seen)
            r.Platform(96, 100, 19.5f);              // the Overseer's perch
            r.Platform(58, 64, 16.5f);               // hidden ledge
            r.Solid(57, 58, 18.5f, 24.5f);           // the shaft up from it (you step onto the ledge under these)
            r.Solid(64, 65, 18.5f, 24.5f);

            r.PlayerStart(-6, -1);
            r.Edge("FromWarden", -11, -1, "BossArena", "ToTerraces", exitIsOnRight: false);
            r.Building("ToTwins", 85, 15, "TwinIsosceles", "FromTerraces", "", new Color(0.5f, 0.24f, 0.2f));
            r.Edge("ToSpire", 119, 15, "EquilateralSpire", "FromTerraces", exitIsOnRight: true);
            r.Gate("SealGate", 108, 15, 7f, new string[0], 2, "2 Vertex Seals");
            r.Hatch("ToRefuge", 61, 23.2f, 5f, "SquaresRefuge", "FromTerraces", new Vector2(0f, -2.4f));

            r.Mirror("Terraces Mirror", 2, -1,
                new[] { "Every tier looks down on the one below.", "Nobody here looks up." });
            r.Tablet("T5", 7, -1, "Tier Marker",
                "TIER OF THE ISOSCELES. Two equal sides: close enough to perfect to keep climbing, far enough to be kept down.",
                "Climb, and they call it ambition. Fall, and they call it nature.");

            r.Kind = TriangleKind.Isosceles;
            r.Enemy("WedgeCharger", 22, -1);
            r.Enemy("TriangleEnemy", 27, -1);
            r.Enemy("TriangleChaser", 38, 3);
            r.Enemy("Bulwark", 47, 3);
            r.Enemy("TriangleBrute", 70, 9);
            r.Enemy("WedgeCharger", 66, 9);
            r.Enemy("TriangleOrbCaster", 76, 9);
            r.Enemy("TriangleFlyer", 70, 9);
            r.Enemy("TriangleChaser", 92, 15);
            r.Enemy("Bulwark", 96, 15);
            r.Enemy("TriangleEnemy", 103, 15);
            r.Enemy("Overseer", 98, 19.5f);
            r.Kind = TriangleKind.Standard;

            r.BackdropPanel(30, 55, 3, 12, new Color(0.16f, 0.15f, 0.12f));
            r.BackdropPanel(55, 80, 9, 20, new Color(0.18f, 0.17f, 0.13f));
            r.BackdropPanel(80, 120, 15, 30, new Color(0.2f, 0.19f, 0.15f));
            r.Save();
        }

        /// <summary>
        /// An empty boss room: the circle comes in from the west opening (linked to <paramref name="returnScene"/>/
        /// <paramref name="returnDoor"/>), a seal stands in for the fight, and an optional east opening
        /// (to <paramref name="exitScene"/>) opens once <paramref name="exitFlag"/> is set.
        /// </summary>
        private static void BossRoom(string scene, string title, string notes, string seal, string sealName, string entryId,
                                     string returnScene, string returnDoor, Color bg, Color ground, string[] devFlags,
                                     string exitId = null, string exitScene = null, string exitDoor = null, string exitFlag = null)
        {
            var r = new RegionBuilder(scene, title, bg, ground, Color.Lerp(ground, Color.white, 0.15f), AllAbilities, devFlags, notes);
            bool exit = exitScene != null;
            Ends(r, -11, exit ? 47f : float.NaN, -1, 20);
            r.Ground(-14, exit ? 50 : 44, -1);
            if (!exit) r.Solid(44, 46, -14, 20);
            r.Solid(-12, exit ? 50 : 44, 18, 20);
            r.PlayerStart(-6, -1);
            r.Edge(entryId, -11, -1, returnScene, returnDoor, exitIsOnRight: false);
            r.Sign($"BOSS ROOM\n{title}\n(not built yet)", 19, 9, 5f, new Color(0.9f, 0.6f, 0.5f, 0.85f));
            r.Item("VertexSeal", seal, sealName, 19, -1);
            if (exit)
            {
                r.Edge(exitId, 47, -1, exitScene, exitDoor, exitIsOnRight: true);
                r.Gate("WardenGate", 44.5f, -1, 6f, new[] { exitFlag }, 0, "opens once the boss is beaten");
            }
            r.Save();
        }

        private static void TwinIsosceles()
        {
            BossRoom("TwinIsosceles", "The Twin Isosceles",
            "Empty boss room (BOSS 2), entered from the hall doorway on the Terraces. The seal on the floor stands in for the fight.",
            "Seal.Twins", "Vertex Seal II", "FromTerraces", "IsoscelesTerraces", "ToTwins",
            new Color(0.12f, 0.08f, 0.08f), new Color(0.38f, 0.26f, 0.22f), new[] { "Seal.Warden" });
            TwinArenaBuilder.Build(); // the fight itself (see TwinArenaBuilder)
        }

        // ====================================================================== UPPER STRATUM

        private static void Spire()
        {
            var r = new RegionBuilder("EquilateralSpire", "Equilateral Spire",
                new Color(0.12f, 0.12f, 0.13f), new Color(0.62f, 0.6f, 0.55f), new Color(0.78f, 0.76f, 0.7f),
                AllAbilities, new[] { "Seal.Warden", "Seal.Twins" },
                "Home of the 'perfect': only EQUILATERAL triangles (the most aggressive). A tall tower climbed platform by platform.\n" +
                "The top floor is quiet: the last Mirror before the throne, and the opening to the Apex Throne.\n" +
                "Exits: west opening at the bottom → Terraces, east opening at the top → Apex Throne.");

            Ends(r, -11, float.NaN, -1, 44);
            r.Solid(40, 42, -14, 34.4f);             // right wall, below the top opening
            r.Solid(-12, 52, 42, 44);                // ceiling
            r.Solid(42, 52, 41, 42);                 // roof of the top gap
            r.Solid(50, 52, 34, 42);                 // backstop at the end of the top gap (never seen)
            r.Ground(-14, 40, -1);
            float[][] plats = { new[] { 6f, 16f, 3f }, new[] { 22f, 34f, 7f }, new[] { 2f, 12f, 11f }, new[] { 20f, 30f, 15f },
                                new[] { 30f, 40f, 19f }, new[] { 8f, 18f, 23f }, new[] { 0f, 10f, 27f }, new[] { 14f, 26f, 31f },
                                new[] { 28f, 52f, 35f } };
            foreach (var p in plats) r.Platform(p[0], p[1], p[2]);

            r.PlayerStart(-6, -1);
            r.Edge("FromTerraces", -11, -1, "IsoscelesTerraces", "ToSpire", exitIsOnRight: false);
            r.Edge("ToThrone", 43.5f, 35, "ApexThrone", "FromSpire", exitIsOnRight: true);
            r.Mirror("Spire Mirror", 31, 35,
                new[] { "This is what they all climb towards.", "It's so quiet up here. Like nobody's left." });
            r.Tablet("T6", 9, 3, "Creed of the Perfect",
                "Three equal sides. Three equal angles. Nothing uneven, nothing missing.",
                "To be perfect is to never be questioned.");

            r.Kind = TriangleKind.Equilateral;
            r.Enemy("TriangleChaser", 30, -1);
            r.Sentry(28, 6.4f);
            r.Enemy("TriangleOrbCaster", 4, 11);
            r.Enemy("SplitterLarge", 24, 15);
            r.Enemy("TriangleFlyer", 26, 8);
            r.Enemy("Bulwark", 13, 23);
            r.Enemy("TriangleFlyer", 5, 16);
            r.Kind = TriangleKind.Standard;

            foreach (float x in new[] { 1f, 13f, 25f, 37f }) r.Pillar(x, -1, 42, new Color(0.24f, 0.24f, 0.25f));
            r.Save();
        }

        private static void ApexThrone() => BossRoom("ApexThrone", "The Hierarch",
            "Empty FINAL boss room. The gold seal stands in for the fight: taking it opens the way east to the Temple.",
            "Boss.Hierarch", "The Hierarch falls (placeholder)", "FromSpire", "EquilateralSpire", "ToThrone",
            new Color(0.13f, 0.06f, 0.06f), new Color(0.45f, 0.2f, 0.18f), new[] { "Seal.Warden", "Seal.Twins" },
            "ToTemple", "TempleOfThreePoints", "FromThrone", "Boss.Hierarch");

        private static void Temple()
        {
            var r = new RegionBuilder("TempleOfThreePoints", "Temple of Three Points",
                new Color(0.12f, 0.1f, 0.06f), new Color(0.55f, 0.45f, 0.25f), new Color(0.68f, 0.56f, 0.32f),
                AllAbilities, new[] { "Seal.Warden", "Seal.Twins", "Boss.Hierarch"},
                "The sacred temple behind the throne. The Triangle Mould waits on the altar.\n" +
                "Taking it opens every Mould gate (the Rank Gate, the Shrine) and the east opening to the road onward.");
            Ends(r, -11, 53, -1, 22);
            r.Ground(-14, 56, -1);
            r.Solid(-12, 56, 20, 22);
            r.PlayerStart(-6, -1);
            r.Edge("FromThrone", -11, -1, "ApexThrone", "ToTemple", exitIsOnRight: false);
            r.Edge("ToRoad", 53, -1, "RoadToSquareRealm", "FromTemple", exitIsOnRight: true);
            r.Gate("MouldGate", 50.5f, -1, 6f, new[] { "Mould.Triangle" }, 0, "the Mould");
            r.Item("TriangleMould", "Mould.Triangle", "The Triangle Mould", 24, -1);
            r.Tablet("T7", 10, -1, "Temple Inscription",
                "Here the first shape was cast, and every triangle after it was measured against the mould.");
            r.Tablet("T8", 37, -1, "Temple Inscription",
                "Who holds the mould may wear the shape. Who wears the shape too long forgets their own.");
            foreach (float x in new[] { 4f, 14f, 34f, 44f }) r.Pillar(x, -1, 21, new Color(0.3f, 0.25f, 0.15f));
            r.BackdropTriangle(24, 9, 6f, 0, new Color(0.25f, 0.2f, 0.1f));
            r.Save();
        }

        private static void Road()
        {
            var r = new RegionBuilder("RoadToSquareRealm", "Road to the Square Realm",
                new Color(0.12f, 0.14f, 0.17f), new Color(0.35f, 0.38f, 0.36f), new Color(0.45f, 0.5f, 0.47f),
                AllAbilities, new[] { "Seal.Warden", "Seal.Twins", "Boss.Hierarch", "Mould.Triangle" },
                "The exit from Triangle World. Placeholder end of the chapter.");
            Ends(r, -11, float.NaN, -1, 30);
            r.Ground(-14, 60, -1);
            r.Solid(60, 62, -14, 30);
            r.PlayerStart(-6, -1);
            r.Edge("FromTemple", -11, -1, "TempleOfThreePoints", "ToRoad", exitIsOnRight: false);
            r.Sign("THE ROAD TO THE SQUARE REALM\nend of Triangle World (for now)", 38, 4, 5f);
            foreach (float x in new[] { 46f, 52f, 57f }) r.BackdropPanel(x - 1.2f, x + 1.2f, 2f + (x - 46f) * 0.3f, 4.4f + (x - 46f) * 0.3f, new Color(0.3f, 0.35f, 0.42f));
            r.Save();
        }

        // ====================================================================== SIDE AREAS

        private static void Fissure()
        {
            var r = new RegionBuilder("WhisperingFissure", "Whispering Fissure",
                new Color(0.1f, 0.06f, 0.13f), new Color(0.3f, 0.22f, 0.36f), new Color(0.42f, 0.32f, 0.5f),
                BladeDashParry, new string[0],
                "A crack up through the strata where the Shapeshifter's whisper leaks out. Climb from the bottom to the top.\n" +
                "Exits: west opening → back to the crack in the Slums; east opening at the bottom → Hall of Measure;\n" +
                "east opening at the top, behind the Rank Gate (Mould gate) → Prism Caverns: optional, come back with the Mould.\n" +
                "Late game: the corrupted Old Triangle appears here (not built).");
            Ends(r, -11, float.NaN, -1, 40);
            r.Ground(-14, 40, -1);                   // on into the bottom east gap
            r.Solid(30, 40, 5, 26);                  // right wall between the openings (roof of the bottom gap)
            r.Solid(30, 40, 33, 40);                 // right wall above the top opening (roof of the top gap)
            r.Solid(40, 42, -14, 40);                // backstop at the end of both gaps (never seen)
            r.Solid(-12, 42, 38, 40);                // ceiling
            float[][] plats = { new[] { 4f, 12f, 3f }, new[] { 16f, 24f, 7f }, new[] { 6f, 14f, 11f }, new[] { 18f, 28f, 15f },
                                new[] { 4f, 12f, 19f }, new[] { 14f, 22f, 23f } };
            foreach (var p in plats) r.Platform(p[0], p[1], p[2]);
            r.Platform(2, 40, 27f, 1f);              // top floor (Rank Gate chamber)

            r.PlayerStart(-6, -1);
            r.Edge("FromSlums", -11, -1, "ScaleneSlums", "ToFissure", exitIsOnRight: false);
            r.Edge("ToHall", 33, -1, "HallOfMeasure", "FromFissure", exitIsOnRight: true);
            r.Edge("ToPrism", 33, 27, "PrismCaverns", "FromFissure", exitIsOnRight: true);
            r.Gate("MouldGate", 29, 27, 6f, new[] { "Mould.Triangle" }, 0, "Rank Gate");
            r.Sign("RANK GATE\nonly triangles may pass", 20, 33, 4f, new Color(0.95f, 0.8f, 0.5f, 0.9f));
            r.Tablet("T9", -5, -1, "Carved in the Rock",
                "...corners... it wants the corners... the more you have, the louder it gets...",
                "The scratches stop here.");

            r.Enemy("ShardSwarm", 16, -1);
            r.Sentry(20, 6.4f);
            r.Enemy("TriangleOrbCaster", 10, 11);
            r.Sentry(23, 14.4f);
            r.Enemy("ShardSwarm", 10, 19);
            r.Enemy("TriangleChaser", 18, 23);
            r.BackdropPanel(-12, 30, -1, 38, new Color(0.14f, 0.08f, 0.18f));
            r.Save();
        }

        private static void HallOfMeasure()
        {
            var r = new RegionBuilder("HallOfMeasure", "Hall of Measure",
                new Color(0.1f, 0.1f, 0.11f), new Color(0.4f, 0.39f, 0.36f), new Color(0.52f, 0.5f, 0.46f),
                BladeDashParry, new string[0],
                "Where triangles are measured and ranked: lore about the hierarchy. ISOSCELES Bulwarks guard the ledgers.\n" +
                "Mirror and first tablet at the quiet entrance; the guards stand in the middle; the last tablets past them.\n" +
                "Exit: west opening → Whispering Fissure.");
            Ends(r, -11, float.NaN, -1, 16);
            r.Ground(-14, 80, -1);
            r.Solid(80, 82, -14, 16);
            r.Solid(-12, 80, 14, 16);
            r.Platform(40, 46, 3.5f);
            r.PlayerStart(-6, -1);
            r.Edge("FromFissure", -11, -1, "WhisperingFissure", "ToHall", exitIsOnRight: false);
            r.Mirror("Hall of Measure Mirror", 3, -1,
                new[] { "They measured everything here.", "Nobody ever thought to measure me. There was nothing to count." });
            r.Tablet("T10", 12, -1, "Ledger of Ranks",
                "Three sides: citizen. Four sides: steward. Five and more: the Council.",
                "No sides: not entered in the ledger.");
            r.Tablet("T11", 68, -1, "The Measuring Rod",
                "Angles to be checked yearly. Any triangle found uneven is to be re-ranked downward.");
            r.Tablet("T12", 76, -1, "The Line",
                "Below the Line live those who were measured and found wanting. It is kinder that way, the ledger says.");
            r.Kind = TriangleKind.Isosceles;
            r.Enemy("Bulwark", 30, -1);
            r.Enemy("TriangleChaser", 37, -1);
            r.Enemy("TriangleEnemy", 43, 3.5f);
            r.Enemy("Bulwark", 50, -1);
            r.Kind = TriangleKind.Standard;
            for (int i = 0; i < 6; i++)
            {
                float x = 18 + i * 9;
                r.Pillar(x, -1, 3 + i * 1.5f, new Color(0.26f, 0.25f, 0.23f));
                r.Sign("RANK " + (i + 3), x, 3 + i * 1.5f, 3f);
            }
            r.Save();
        }

        private static void PrismCaverns()
        {
            var r = new RegionBuilder("PrismCaverns", "Prism Caverns",
                new Color(0.07f, 0.08f, 0.13f), new Color(0.28f, 0.27f, 0.42f), new Color(0.4f, 0.38f, 0.58f),
                AllAbilities, new[] { "Seal.Warden", "Seal.Twins", "Boss.Hierarch", "Mould.Triangle" },
                "OPTIONAL: behind the Rank Gate, reached with the Triangle Mould. Crystal caves of EQUILATERAL triangles:\n" +
                "lasers, Sentries, orb casters. Mirror at the quiet entrance. The Refractor (optional boss) is past the east opening.");
            Ends(r, -11, 93, -1, 18);
            r.Ground(-14, 96, -1);
            r.Solid(-12, 96, 16, 18);
            float[][] plats = { new[] { 14f, 20f, 3.5f }, new[] { 26f, 34f, 7f }, new[] { 44f, 52f, 4f }, new[] { 60f, 68f, 7.5f }, new[] { 74f, 80f, 3.5f } };
            foreach (var p in plats) r.Platform(p[0], p[1], p[2]);
            r.PlayerStart(-6, -1);
            r.Edge("FromFissure", -11, -1, "WhisperingFissure", "ToPrism", exitIsOnRight: false);
            r.Edge("ToRefractor", 93, -1, "RefractorLair", "FromPrism", exitIsOnRight: true);
            r.Mirror("Prism Mirror", 1, -1,
                new[] { "The light bends around every corner here.", "It just passes straight through me." });
            r.Kind = TriangleKind.Equilateral;
            r.Enemy("TriangleEnemy", 22, -1);
            r.Sentry(30, 16f);
            r.Enemy("TriangleOrbCaster", 30, 7);
            r.Enemy("TriangleEnemy", 48, 4);
            r.Sentry(56, 16f);
            r.Enemy("TriangleOrbCaster", 64, 7.5f);
            r.Enemy("TriangleFlyer", 70, 3);
            r.Enemy("TriangleEnemy", 82, -1);
            r.Kind = TriangleKind.Standard;
            var crystal = new[] { new Color(0.45f, 0.4f, 0.75f), new Color(0.3f, 0.6f, 0.75f), new Color(0.6f, 0.4f, 0.7f) };
            for (int i = 0; i < 9; i++) r.BackdropTriangle(5 + i * 10, (i % 2 == 0) ? 1.5f : 12.5f, 1.6f + (i % 3) * 0.5f, (i % 2 == 0) ? 0 : 180, crystal[i % 3]);
            r.Save();
        }

        private static void RefractorLair() => BossRoom("RefractorLair", "The Refractor",
            "Empty OPTIONAL boss room (BOSS 3). The seal stands in for the fight.",
            "Seal.Refractor", "Vertex Seal III", "FromPrism", "PrismCaverns", "ToRefractor",
            new Color(0.08f, 0.07f, 0.14f), new Color(0.3f, 0.27f, 0.45f),
            new[] { "Seal.Warden", "Seal.Twins", "Boss.Hierarch", "Mould.Triangle" });

        private static void SquaresRefuge()
        {
            var r = new RegionBuilder("SquaresRefuge", "Square's Refuge",
                new Color(0.08f, 0.1f, 0.13f), new Color(0.3f, 0.35f, 0.42f), new Color(0.42f, 0.48f, 0.56f),
                AllAbilities, new[] { "Seal.Warden" },
                "Hidden from the Shapeshifter. The Square (an ally from a higher society) lives here. Calm room, no enemies.\n" +
                "You come up through the hole in the floor (from the shaft above the Terraces' hidden ledge); drop back down it to leave.");
            r.Solid(-14, -12, -14, 16);
            r.Ground(-12, 3, -1);
            r.Ground(7, 44, -1);                     // the hole is 3..7
            r.Ground(3, 7, -11);                     // bottom of the hole, below the hatch (never seen): the circle lands here while the next scene loads
            r.Solid(44, 46, -14, 16);
            r.Solid(-12, 44, 14, 16);
            r.PlayerStart(10, -1);
            r.Hatch("FromTerraces", 5, -8f, 4f, "IsoscelesTerraces", "ToRefuge", new Vector2(4.5f, 7.5f)); // deep in the hole, below the camera stop
            r.Mirror("Refuge Mirror", 34, -1,
                new[] { "Four sides, and still he hides.", "So it isn't only the bottom they push away." });
            r.Npc("The Square", 22, -1, true, new Color(0.55f, 0.65f, 0.82f),
                new[] { "You came up from the bottom... a shape with no corners at all.",
                        "Down there they'd call that nothing. Up here, they'd call it a mistake.",
                        "I left my tier the night they measured my sister. Four sides, and still not enough for them.",
                        "The thing in the vertices can't see in here. I made sure of it. Rest if you need to." },
                new[] { "The more corners they have, the louder it whispers to them. Be careful near the top." });
            r.Save();
        }

        private static void Shrine()
        {
            var r = new RegionBuilder("ShrineOfFirstBreak", "Shrine of the First Break",
                new Color(0.08f, 0.07f, 0.06f), new Color(0.32f, 0.28f, 0.22f), new Color(0.44f, 0.38f, 0.3f),
                AllAbilities, new[] { "Seal.Warden", "Seal.Twins", "Boss.Hierarch", "Mould.Triangle" },
                "SECRET: entered through the ruined doorway in the Graveyard (Mould gate). Where the first triangle lost a vertex:\n" +
                "the origin of the outcast law. Exit: west opening → back out of the doorway.");
            Ends(r, -11, float.NaN, -1, 14);
            r.Ground(-14, 30, -1);
            r.Solid(30, 32, -14, 14);
            r.Solid(-12, 30, 12, 14);
            r.PlayerStart(-6, -1);
            r.Edge("FromGraveyard", -11, -1, "Graveyard", "ToShrine", exitIsOnRight: false);
            r.Tablet("T13", 10, -1, "The First Break",
                "The first triangle to lose a vertex was a builder. A stone fell; a corner broke.",
                "The Council met that night. By morning there was a law, and a pit, and a word for what she had become.");
            r.Tablet("T14", 21, -1, "The First Break",
                "She was the first thrown down. Every string you carry began with her.");
            r.BackdropTriangle(16, 5, 4.5f, 0, new Color(0.22f, 0.19f, 0.15f));
            r.ShardPile(16, -1);
            r.Save();
        }

        // ====================================================================== existing scenes + build order

        /// <summary>
        /// Hooks the existing scenes into the route. StoryTest: the end of the Hall of Strings leads to the Graveyard
        /// and the cape starts with the blade only. BossArena: walk-off openings on both sides, with the east side a
        /// corridor OUTSIDE the arena behind a gate that opens once the Warden is beaten (nothing inside the fight).
        /// Safe to run more than once.
        /// </summary>
        public static void WireExistingScenes()
        {
            // ---- StoryTest
            var story = EditorSceneManager.OpenScene("Assets/_Project/Scenes/StoryTest.unity", OpenSceneMode.Single);
            foreach (var door in Object.FindObjectsByType<SceneDoor>(FindObjectsInactive.Include))
            {
                var so = new SerializedObject(door);
                if (so.FindProperty("doorId").stringValue != "EnemyDoor") continue;
                so.FindProperty("targetScene").stringValue = "Graveyard";
                so.FindProperty("targetDoorId").stringValue = "FromHall";
                // No portal: the corridor past the Hall of Strings opens on the east, and the circle walks out.
                so.FindProperty("arrivalOffset").vector2Value = new Vector2(-2.5f, 0.5f);
                so.FindProperty("glow").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                door.gameObject.name = "Edge - EnemyDoor (to Graveyard)";
                door.transform.position = new Vector3(99f, -22f, 0f);
                var box = door.GetComponent<BoxCollider2D>();
                box.size = new Vector2(1f, 6f);
                box.offset = new Vector2(RegionBuilder.EdgeTriggerDepth, 3f);
                foreach (Transform child in door.transform) child.gameObject.SetActive(false); // no portal art
                RegionBuilder.SetCameraStop(door, exitIsOnRight: true);
            }
            // The east wall of the corridor opens (6 high) onto a short gap the camera never shows.
            var storyLevel = GameObject.Find("Level").transform;
            foreach (string n in new[] { "Exit Gap Roof", "Exit Backstop" })
            {
                var old = storyLevel.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            Transform eastWall = storyLevel.Find("Corridor_Wall_East"), caveFloor = storyLevel.Find("Cave_Floor");
            SetBox(eastWall, 98f, 100f, -16f, -8f);
            SetBox(caveFloor, 8f, 107f, -30f, -22f);
            AddBlock(storyLevel, "Exit Gap Roof", 100f, 107f, -16f, -4f, eastWall);
            AddBlock(storyLevel, "Exit Backstop", 107f, 109f, -30f, -4f, eastWall);
            var storyComp = storyLevel.GetComponent<CompositeCollider2D>();
            if (storyComp != null)
            {
                foreach (var bc in storyLevel.GetComponentsInChildren<BoxCollider2D>()) { bc.enabled = false; bc.enabled = true; }
                Physics2D.SyncTransforms();
                storyComp.GenerateGeometry();
            }
            var opening = Object.FindAnyObjectByType<Systems.Story.OpeningSequence>();
            if (opening != null)
            {
                var so = new SerializedObject(opening);
                so.FindProperty("capeAbilities").intValue = (int)PlayerAbility.Attack;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(story);
            EditorSceneManager.SaveScene(story);

            // ---- BossArena
            var arena = EditorSceneManager.OpenScene("Assets/_Project/Scenes/BossArena.unity", OpenSceneMode.Single);
            // Remove what an earlier version put inside the arena.
            foreach (string n in new[] { "Door To Isosceles Terraces", "WardenGate (to the Terraces)", "Edge - ToTerraces (to IsoscelesTerraces)",
                                         "WardenGate (corridor)", "Corridor Ceiling", "Corridor Backstop", "West Backstop", "Floor Extension" })
            {
                var old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }
            var level = GameObject.Find("Level").transform;
            Transform floor = level.Find("Floor"), wallL = level.Find("Wall_Left"), wallR = level.Find("Wall_Right");
            // Floor out past both openings; side walls only above them (openings are 0..5 high).
            // Both openings lead into a short gap; the camera stops at the walls' outer faces (x -20 and 36), so the
            // circle walks off-screen into the gap before the edge trigger fires.
            SetBox(floor, -26f, 44f, -6f, 0f);
            SetBox(wallL, -26f, -18f, 5f, 18f);          // wall above the west opening + roof of its gap
            SetBox(wallR, 34f, 36f, 5f, 18f);
            AddBlock(level, "West Backstop", -28f, -26f, -6f, 18f, wallL);
            AddBlock(level, "Corridor Ceiling", 36f, 44f, 5f, 18f, wallR);
            AddBlock(level, "Corridor Backstop", 44f, 46f, -6f, 18f, wallR);

            foreach (var door in Object.FindObjectsByType<SceneDoor>(FindObjectsInactive.Include))
            {
                var so = new SerializedObject(door);
                if (so.FindProperty("doorId").stringValue != "Arena") continue;
                // The old portal door becomes a walk-off opening at the far west of the corridor.
                so.FindProperty("targetScene").stringValue = "ScaleneSlums";
                so.FindProperty("targetDoorId").stringValue = "ToWarden";
                so.FindProperty("arrivalOffset").vector2Value = new Vector2(2.5f, 0.5f);
                so.FindProperty("glow").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                door.gameObject.name = "Edge - Arena (to ScaleneSlums)";
                door.transform.position = new Vector3(-19f, 0f, 0f);
                var box = door.GetComponent<BoxCollider2D>();
                box.size = new Vector2(1f, 5f);
                box.offset = new Vector2(-RegionBuilder.EdgeTriggerDepth, 2.5f);
                foreach (Transform child in door.transform) child.gameObject.SetActive(false); // no portal art
                RegionBuilder.SetCameraStop(door, exitIsOnRight: false);
            }
            var exit = (GameObject)PrefabUtility.InstantiatePrefab(RegionBuilder.Prefab("World/SceneEdge"), arena);
            exit.name = "Edge - ToTerraces (to IsoscelesTerraces)";
            exit.transform.position = new Vector3(35f, 0f, 0f);
            var ebox = exit.GetComponent<BoxCollider2D>();
            ebox.size = new Vector2(1f, 5f);
            ebox.offset = new Vector2(RegionBuilder.EdgeTriggerDepth, 2.5f);
            var eso = new SerializedObject(exit.GetComponent<SceneDoor>());
            eso.FindProperty("doorId").stringValue = "ToTerraces";
            eso.FindProperty("targetScene").stringValue = "IsoscelesTerraces";
            eso.FindProperty("targetDoorId").stringValue = "FromWarden";
            eso.FindProperty("arrivalOffset").vector2Value = new Vector2(-2.5f, 0.5f);
            eso.ApplyModifiedPropertiesWithoutUndo();
            RegionBuilder.SetCameraStop(exit.GetComponent<SceneDoor>(), exitIsOnRight: true);
            // The arena's east wall below 5 is a gate: shut through the fight, sinks once the Warden is beaten.
            var gate = (GameObject)PrefabUtility.InstantiatePrefab(RegionBuilder.Prefab("World/WardenGate"), arena);
            gate.name = "WardenGate (corridor)";
            gate.transform.position = new Vector3(35f, 2.5f, 0f);
            gate.transform.localScale = new Vector3(2f, 5f, 1f);

            // Side ledges 1 lower so they can be reached before the double jump (4 up instead of 5).
            var ledges = GameObject.Find("Ledges");
            if (ledges != null)
            {
                foreach (Transform t in ledges.transform)
                {
                    if ((t.name == "Ledge_Left" || t.name == "Ledge_Right") && t.position.y + t.lossyScale.y * 0.5f > 4.5f)
                        t.position += Vector3.down;
                }
            }
            if (Object.FindAnyObjectByType<RegionInfo>() == null)
            {
                var info = new GameObject("Region").AddComponent<RegionInfo>();
                var so = new SerializedObject(info);
                so.FindProperty("regionName").stringValue = "The Base Warden";
                so.FindProperty("showNameOnEnter").boolValue = false;
                so.FindProperty("devAbilities").intValue = (int)BladeDashParry;
                so.FindProperty("notes").stringValue = "BOSS 1. Beating the Warden grants Seal.Warden and the double jump (it does NOT free the Slums) (BossArena component).\n" +
                    "West opening → Scalene Slums. East: a corridor outside the arena (gate opens after the fight) → Isosceles Terraces.";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // The Warden leaves the double jump behind as a cape string to collect.
            var bossArena = Object.FindAnyObjectByType<BossArena>();
            if (bossArena != null)
            {
                var bso = new SerializedObject(bossArena);
                bso.FindProperty("rewardStringPrefab").objectReferenceValue = RegionBuilder.Prefab("World/CapeString").GetComponent<CapeStringPickup>();
                bso.ApplyModifiedPropertiesWithoutUndo();
            }

            // (The two edge doors stop the camera at the walls' outer faces themselves.)
            var oldLimits = Object.FindAnyObjectByType<CameraBounds2D>();
            if (oldLimits != null) Object.DestroyImmediate(oldLimits);

            var comp = level.GetComponent<CompositeCollider2D>();
            if (comp != null)
            {
                foreach (var bc in level.GetComponentsInChildren<BoxCollider2D>()) { bc.enabled = false; bc.enabled = true; }
                Physics2D.SyncTransforms();
                comp.GenerateGeometry();
            }
            EditorSceneManager.MarkSceneDirty(arena);
            EditorSceneManager.SaveScene(arena);
        }

        private static void SetBox(Transform t, float x0, float x1, float y0, float y1)
        {
            if (t == null) return;
            t.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, t.position.z);
            t.localScale = new Vector3(x1 - x0, y1 - y0, 1f);
        }

        private static void AddBlock(Transform level, string name, float x0, float x1, float y0, float y1, Transform copyFrom)
        {
            var go = Object.Instantiate(copyFrom.gameObject, level);
            go.name = name;
            SetBox(go.transform, x0, x1, y0, y1);
        }

        /// <summary>StoryTest first (the game starts there), then Chapter 1 in map order, the boss arena, the test scene. Main is dropped.</summary>
        public static void SetBuildOrder()
        {
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene("Assets/_Project/Scenes/StoryTest.unity", true) };
            foreach (string name in Regions.Keys) list.Add(new EditorBuildSettingsScene($"{RegionBuilder.SceneFolder}/{name}.unity", true));
            list.Add(new EditorBuildSettingsScene("Assets/_Project/Scenes/BossArena.unity", true));
            list.Add(new EditorBuildSettingsScene("Assets/_Project/Scenes/EnemyTest.unity", true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
