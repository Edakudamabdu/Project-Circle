# Project Circle: level editing guide

How the Chapter 1 (Triangle World) regions are put together, and how to redesign them safely.
Every region is a **greybox**: plain coloured shapes that show the layout, enemy order and story beats.
Replace the looks freely, but keep the gameplay objects (doors, gates, pickups) and their settings.

The layout plan is in `Docs/TriangleWorld_MapSketch.png` (the world) and `Docs/Chapter1_LevelLayout.png`
(the Graveyard and the Slums in detail).

## The route

| # | Region | Scene | What happens |
|---|---|---|---|
| 1–4 | Outskirts → Discard Pit → Old Triangle's Hollow → Hall of Strings | `Scenes/StoryTest` | the opening; the cape forms (blade + pogo) |
| 5 | Broken Vertex Graveyard | `Chapter1/Graveyard` | first fights; the Runner's String (+ dash) |
| 6 | Scalene Slums | `Chapter1/ScaleneSlums` | + parry; Mirror; the Warden's Gate |
| 7 | Base Warden (BOSS 1) | `Scenes/BossArena` | Seal I; frees the Slums; + double jump |
| 8 | Isosceles Terraces | `Chapter1/IsoscelesTerraces` | climb the tiers; Overseer |
| 9 | Twin Isosceles (BOSS 2, empty) | `Chapter1/TwinIsosceles` | placeholder Seal II |
| 10 | Equilateral Spire | `Chapter1/EquilateralSpire` | needs 2 seals; last Mirror |
| 11 | Apex Throne (FINAL BOSS, empty) | `Chapter1/ApexThrone` | placeholder |
| 12 | Temple of Three Points | `Chapter1/TempleOfThreePoints` | the Triangle Mould |
| 13 | Road to the Square Realm | `Chapter1/RoadToSquareRealm` | end of Triangle World |
| side | Whispering Fissure | `Chapter1/WhisperingFissure` | from the Slums (dash wall); Rank Gate at the top |
| side | Hall of Measure | `Chapter1/HallOfMeasure` | lore about ranking |
| optional | Prism Caverns → Refractor (empty boss) | `Chapter1/PrismCaverns`, `Chapter1/RefractorLair` | behind the Rank Gate: come back with the Mould |
| secret | Square's Refuge | `Chapter1/SquaresRefuge` | high ledge in the Terraces (double jump) |
| secret | Shrine of the First Break | `Chapter1/ShrineOfFirstBreak` | Mould door in the Graveyard |

`Scenes/EnemyTest` is a test room for enemies and is not part of the route.

## How a region scene is organised

```
_Core        Player, camera, light (don't edit)
HUD, DialogueCanvas   UI (don't edit)
Region       RegionInfo: the name shown on entering, and the "dev start" (see below)
Level        everything solid: ground, walls, platforms
Doors        SceneDoors to the neighbouring regions
Checkpoints  Mirrors
Pickups      cape strings, Vertex Seals, the Mould
Gates        Seal/Mould/Warden gates and crumbling walls
Enemies      enemy prefabs (in the Slums, "Corrupted Residents" vanishes once the Slums are freed)
Lore         tablets and characters you can read or listen to
Decor        looks only, no collision: shard piles, houses, pillars, signs, backdrops
```

## Editing the level shape

- Everything solid is a child of **Level**: plain white squares, tinted, with a Box Collider 2D.
  **Move and scale them freely** (Rect tool, `T`). The Level object merges them into one collider on its own.
  - New solid piece: duplicate an existing one (`Ctrl+D`) so it keeps its settings (layer **Ground**, Box Collider 2D
    with **Composite Operation = Merge**).
  - If the green collider outline ever looks wrong after moving pieces, select **Level** and toggle a
    child's Box Collider 2D off and on.
- Anything that should *look* solid but not block (background walls, distant shapes) goes under **Decor**, with no collider.
- Floors in the greybox have their top at **y = -1**. Sizes for reference: the circle is 1 unit wide; a jump reaches
  about **4.5 up / 6 across**; a dash adds about **3.5 across**; the double jump reaches about **8.5 up**.
  Keep the gates honest: the dash gap in the Graveyard must stay wider than a plain jump (about 8), and ledges that
  need the double jump must stay higher than 5.

## Replacing the art

- Swap or add sprites **as children** of an object rather than replacing the object, so its scripts and settings stay.
- Pickups (`CapeString`, `VertexSeal`, `TriangleMould`): put new art under their **Visual** child (it's hidden once collected).
  They trigger by distance (the coloured circle gizmo), so no collider is needed.
- Gates (`SealGate`, `MouldGate`, `WardenGate`, `CrumblingWall`): the root object is the solid barrier. Keep its
  Box Collider 2D and layer **Ground**; reskin the sprite or add art children (they sink/vanish with it).
- Enemies are prefabs in `Prefabs/Enemies`. Editing the prefab changes every copy; edit an instance to change just one.
  Keep each enemy *after* the ability that answers it (see the map sketch): no Ceiling Sentries or orb casters before
  the parry string, no Wedge Chargers before the dash string, and so on.
- Reusable pieces live in `Prefabs/World` (gameplay) and `Prefabs/Decor` (looks).
- **Triangle kinds (lore).** Every triangle has a **Kind** on its `TriangleEnemy` component (or a `TriangleShapeKind`
  component on the special enemies). The kind reshapes it and sets its aggression — the more "perfect", the meaner:

  | Kind | Where | Shape | Speed / attack rate | Sight |
  |---|---|---|---|---|
  | Scalene | Scalene Slums, Graveyard | uneven, leaning | ×0.8 | ×0.75 |
  | Isosceles | Terraces, Hall of Measure, Fissure | tall, two equal sides | ×1.0 | ×1.0 |
  | Equilateral | Spire, Prism Caverns, Throne | perfect | ×1.3 | ×1.25 |

  The shape is applied at runtime (the "Shape Frame" objects appear in Play Mode), so the prefab stays the same.
- **Every imperfect triangle looks different.** Scalene and isosceles triangles each get their own proportions
  (how wide, how crooked) and their own colour, and their attacks (lasers, orbs, shockwaves, wards, glows) take the
  same colour. It's picked from where the triangle is placed, so it stays the same between runs; to choose a look,
  type a number in **Look Seed** (on `TriangleEnemy`, or `TriangleShapeKind` for sentries). Equilateral triangles
  all share one look on purpose: the "perfect" ones are uniform. The pieces of a Splitter keep its look.
- **Reading is safe.** While a tablet, Mirror or cutscene has the controls locked, the circle can't be hurt. Still keep
  Mirrors and tablets in quiet spots, well away from enemies (the greybox keeps Mirrors 19+ units and tablets 7+ units
  clear), so nothing is waiting for the player when the text closes.

## Transitions (no portal doors)

Regions connect like Hollow Knight: you walk off the edge of one room into the next. All three kinds below use the
same **SceneDoor** script, so they're set up the same way:
- **SceneEdge** (`Prefabs/World`): an opening at the left/right end of a room. Past the opening is a short **gap**
  (a 6-deep tunnel with a floor, a roof and a backstop at the end). The SceneEdge object sits in the opening; its
  trigger box is offset 2.5 units out into the gap. Its **Camera Stop** field (Left / Right) stops the camera at the
  wall's outer face (the yellow line in the Scene view), so the circle walks off-screen into the gap before the
  trigger fires and the far side is never shown. **The stop belongs to the edge: drag the edge object and the camera
  stop, trigger and arrival point all move with it.** Just keep the gap's floor under the trigger and the wall around
  the opening.
- **Hatch**: the same thing lying flat, for shafts you drop down or climb up through (e.g. Terraces ↔ Squares' Refuge).
  It works like an edge, but vertically: the hatch's **Camera Limit Zone** component stops the view 1.2 units short of
  the trigger, only while the circle is inside its area (cyan box), so the circle climbs or drops off-screen
  before the next room loads. The rest of the room keeps its normal camera. The limit and area are relative to the
  hatch, so they move with it. A hole you drop through needs a floor somewhere below the hatch, out of view, so the
  circle doesn't keep falling while the next scene loads.
- **BuildingDoor** (`Prefabs/World`): a doorway into a building or side room (Shrine, Fissure, Twins). The circle
  walks in by pressing into it; the frame and label are just art and can be reskinned.

Each one has:
- **Door Id**: its own name in this scene (e.g. `ToSlums`),
- **Target Scene / Target Door Id**: where it leads (e.g. `ScaleneSlums` / `FromGraveyard`),
- **Arrival Offset**: where the circle appears relative to it when arriving. For an edge, point it *into* the room
  (about ±2.5 x) so the circle doesn't land straight back in the trigger.

Transitions come in pairs: if you rename an id, rename the matching **Target Door Id** in the other scene too.
A gate (SealGate, MouldGate, WardenGate) placed in front of an edge or building is what locks it — the edge itself is
always open.

**Editing levels safely.** Move platforms, walls and doors freely; the transitions are built to cope:
- In the Scene view every door shows a label (`ToSlums → ScaleneSlums / FromGraveyard`), a green circle where the
  circle arrives, and (for room edges) the yellow camera-stop line.
- The **Target Scene** is a dropdown of the game's scenes in the door's Inspector, so it can't be misspelled.
- If an arrival point ends up inside a wall or platform after an edit, the circle is lifted out of it on arrival.
- **After editing, run `Project Circle ▸ Level Building ▸ Check Scene Links`** (or the *Check All Scene Links* button on
  any door). It opens every scene and lists, in the Console, any door whose target scene or door is missing, any
  duplicate Door Id, any arrival point stuck in a wall, and any room edge without a camera stop. "All fine" means
  every room still connects. (The three warnings about the old *EnemyTest* scene are expected.)
- Health and Mend Threads carry from one scene to the next; only dying or resting at a Mirror refills them.

**Base Warden arena:** you come in from the Slums on the west edge; the fight starts once you walk past the entry
trigger. The way on is a corridor to the east, sealed by a WardenGate that opens on `Seal.Warden`. Keep the corridor
outside the arena walls (x > 34), or the Warden can wander into it. When the Warden falls, it leaves the double jump
behind as a cape string in the middle of the arena (the **BossArena** component's *Reward* fields); the camera keeps
the arena framed until the circle walks out.

**Twin Isosceles arena** (`TwinIsosceles` scene, object **Twin Arena**): an open, mirrored hall with gates at both ends.
The twins (Iso and Skelos, under *Twin Arena/Twin Isosceles*) wait out of sight above the arena and drop in on either
side when the circle walks in; the gates rise once both have landed. Their moves and timings are on the **TwinIsosceles**
component (charge speed, crosshair chase and lock time, plunge height and speed, compass size and orb rate, entrance
height...). Keep the hall symmetric around *Arena Centre*: the Mirror Slam, the landing spots and the Compass use it. `Project Circle ▸ Level Building ▸ Build Twin Isosceles Arena` rebuilds only the *Twin Arena* object.
New scenes must be added to **File ▸ Build Profiles ▸ Scene List**, or doors can't load them.

## Progress, gates and testing

The run's progress (abilities, Vertex Seals, the Mould, freed villages, broken walls) is kept in `GameProgress` and
carries from scene to scene. Story flags used: `Seal.Warden`, `Seal.Twins`, `Seal.Refractor`, `Boss.Hierarch`,
`Mould.Triangle`, `Freed.Slums`, `Wall.<name>`.

- **SealGate** opens with 2 Vertex Seals; **MouldGate** (also the Rank Gate) with `Mould.Triangle`;
  **WardenGate** with the flags in its *Required Flags*.
- **ActiveWhenFlag** shows or hides a group by a flag (e.g. the Slums' residents vs. the freed villagers).
- **Testing one region:** just press Play in it. The **Region** object's *Dev Abilities* and *Dev Flags* decide what
  the circle has, as if it had walked there. When you arrive through a door instead, the real progress is used.

## Rebuilding the greybox

`Project Circle ▸ Level Building ▸ Build ALL Chapter 1 regions` regenerates every region from the layouts in
`Scripts/Editor/LevelBuilding/ChapterOneLayouts.cs`. **It overwrites those scenes**: never run it once anyone has
started redesigning a region by hand.

## Sound

- **Sound effects** are set in one asset: `Assets/_Project/Resources/SoundLibrary`. Select it and each entry
  (Swing, EnemyHit, Gate, ...) shows its clip(s), volume, pitch and pitch jitter. To change a sound, drag a
  different clip in (several clips = one picked at random). Don't move or rename the asset: the game loads it by name.
- **Clips** live in `Assets/_Project/Audio/SFX/<Player|Enemies|Bosses|World|UI>` and `Audio/Ambience`.
- **Region ambience**: the `Region` object's RegionInfo has an *Ambience* clip and volume. Neighbouring regions
  with the same loop keep it playing through doors; a different loop cross-fades.
- **Boss music**: the BossArena component's *Fight Music* field. The ambience dips while it plays.
- **Project Circle → Audio → Set Up Sounds** fixes import settings, adds library entries for any new sound ids,
  and gives regions without ambience their default loop. It never overwrites choices you've made.
