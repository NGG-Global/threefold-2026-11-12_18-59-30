# The Broken Foundry

Sprint LM-07 (Living Magic, part 7) puts the systems of LM-01 to LM-06 into one compact level that
is played rather than demonstrated: an abandoned magical foundry, entered through its forecourt and
left through a sealed exit. It is a gameplay prototype, not a production level. The intended first
playthrough is five to ten minutes; that figure is a design estimate, not a measurement.

**Status:** the code compiles against the Unity 6000.6.0f1 assemblies, for the Editor and with
Android player defines, and the level's rules pass 43 tests outside Unity (see Tests). **Nothing has
run inside the Unity Editor or on a device.** The scene has not been generated, nobody has seen or
played it, the Guardian has not walked its navigation mesh, no checkpoint has been reached in a real
scene, and no frame time has been measured. The look follows the approved direction of the Art Bible
(Soft Illustrated Realism) through the visual style of Sprint 11, which is itself still a proposal.
Every number is a first estimate.

## The level

North is forward. The player starts in the south and never needs to go back.

| Part | What is there | What it asks | Discipline |
| --- | --- | --- | --- |
| **Forecourt** | A walled yard before the foundry's front. A dormant crystal on an iron-bound plinth, an iron conduit along the ground from it to the frame of the barred gate | Wake the crystal. The gate sinks into the floor for good | Arcane |
| **Ore Hall** | A partition whose only doorway is filled by a damaged barricade (split planks, scorch marks, straw at its foot); beyond it a seven-metre pit of molten slag, crossed by a crumbling plank bridge on hoist chains | Clear the barricade, which stays gone. Then brace the bridge and cross before the brace runs out | Flame or Aegis; then the Aegis guard |
| **Lift Hall** | A 4.4 m ledge across the hall, too high to climb. A stone block on a rail with a rune lock beside it, and a rune conduit up the face of the ledge | Light the lock, push the block along its rail until it stands against the ledge, climb from it | Arcane, then Aegis |
| **Stair** | An enclosed stair down from the ledge | Walk down; the camera works in a confined space | |
| **Casting Floor** | A Ruins Guardian on patrol, a furnace against the west wall with scorched paving in front of its mouth, two loose scaffolds, four columns | Defeat the Guardian. The gate north opens for good | Any; combinations help |
| **Exit Court** | The foundry's 9.5 m north wall with three ways through: a culvert choked with slag, a barred exit gate with a counterweight on a rune-locked rail, and a breach at 5 m with a crumbling gantry leaning up to it | Get through, by any one of them | See below |
| **Exit Yard** | The way out: an opening onto a passage, with the distant spire beyond | Walk out. The level is complete | |

### Progression

| Brief | Where | How |
| --- | --- | --- |
| Opening: a simple arcane-reactive mechanism | Forecourt | The dormant crystal (new definition: Latch) and the gate that follows it |
| Middle: temporary and persistent changes | Ore Hall | The barricade's destruction lasts (saved); its fire, the bridge's brace and the bridge's fall are temporary |
| Combination: a structure that answers more than one discipline | Lift Hall | The rune-locked slide of LM-06: Arcane activates its lock, and only then does Aegis force move it |
| Combat: a guardian that shares the world's properties | Casting Floor | See Combat |
| Final: at least two genuinely different solutions | Exit Court | Three, below |

The order is checked as a graph (see Tests): the Ore Hall needs the crystal, the Lift Hall the
barricade and a brace, the casting floor the lift, the exit court the Guardian's defeat.

## The sealed exit: three solutions

Each is complete on its own, with the other two left alone. Each reprises one lesson of the level
in a new place, so the final room tests what the player has learnt rather than introducing a puzzle.

| Solution | What the player does | Disciplines | What lasts | Learnt in |
| --- | --- | --- | --- | --- |
| **Weaken and break: the culvert** | Heat the slag plug with Flame (strikes or a Flame Wave), then shatter it with a full Force Pulse while it is still hot, and walk through the culvert | Flame, Aegis | The plug is destroyed, for good | The Guardian's stone plates, which are the same stone with the same brittleness |
| **Power and reposition: the gate** | Light the counterweight's rune lock with Arcane (one full arrow, or two uncharged ones in quick succession), then push the counterweight along its rail to its far stop with Force Pulses. The chain over the pulley raises the exit gate | Arcane, Aegis | The counterweight's position, and so the open gate | The rune lift |
| **Brace and climb: the breach** | Hold the Aegis guard at the foot of the gantry (about 2.5 s builds a brace of up to 8 s), walk up to the breach in the wall, and climb down the rubble steps on the far side | Aegis guard | Nothing. The gantry must be braced each time | The slag bridge |

They differ in action, not only in colour: one destroys a structure, one moves a mechanism, one is
crossed on a temporary state without changing anything for good. No solution accepts merely "any
discipline". These three are the only ones the level has; no other route is claimed. Not every one
is required: the culvert needs Flame, the other two do not.

**Numbers that make each one fair.** The heated plug stays brittle for about five seconds after a
full Flame Wave, enough to select Aegis and fully charge a Force Pulse (0.9 s) with time to spare.
The counterweight's lock stays lit for ten seconds, and more charge restarts the ten seconds; if it
goes dark half-way, the block stays where it was and the lock can be lit again. The gantry gives way
in under half a second under unbraced weight, so it is not a matter of running fast.

## Discovery

The level uses LM-05's clues, resonance and answers; it adds no markers, outlines, icons or text.
What each object says about itself:

| Object | Clue (always) | Resonates with |
| --- | --- | --- |
| Dormant crystal | A slow pale pulse; the conduit to the gate | Arcane |
| Damaged barricade | Wood, scorch marks, straw at its foot | Flame, Aegis |
| Slag bridge, crumbling gantry | Sway, hoist chains, a strain tint and dust under weight | Aegis |
| Rune lift, counterweight | A rail with stops, a rune lock on a post, a rune conduit | Arcane; Aegis once lit |
| Furnace | A dark mouth with an ember bed, a scorched half-circle of paving as far as its heat reaches | Flame |
| Loose scaffold | Cracks, chips at its foot, a sway | Aegis |
| Slag plug | Vents and scorched rims on both faces | Flame, Aegis |

The objects that matter are large (the gate, the ledge, the wall, the gantry are several metres) and
stand on the player's line of travel; the gates are bars, so the next room can be seen through them.
Whether every clue reads at the gameplay camera's distance on a phone screen **has not been judged**:
it needs the Editor and a device. The strength of the clues is set in each object's Discovery Clues
component and on the Discovery object.

## Combat

One Ruins Guardian (LM-02 to LM-06, unchanged) patrols the middle of the casting floor. The director
of the Combat Arena paces it, so its attacks are telegraphed as before (slam, and below 60% health a
shockwave).

| Interaction | Rule (existing) | In the level |
| --- | --- | --- |
| **Flame: heat** | The Guardian heats at 0.6 of the heat it receives | Heated, its stone plates turn brittle |
| **Aegis: force** | Force against its stagger force (3.5) and on its plates | A full Force Pulse staggers it; heated, even an uncharged pulse breaks its plates |
| **Arcane** | Damage only; the Guardian has no ward | The plain way to fight it |
| **Environment: the furnace** | The furnace (new definition) sends 22 heat a second within 3.5 m of its mouth while it is hot. Creatures take reactions | Stoke the furnace with Flame and lure the Guardian into its heat: about four seconds there heats it, about eight sets it alight. The Mage heats too, and catches fire after about eight seconds |
| **Environment: the scaffolds** | A structure coming down sends force 3.5 within 3.5 m; the Guardian's shockwave carries force 3.5 for objects | Topple a scaffold onto the Guardian with a full Force Pulse to stagger it. Its own shockwave can bring a scaffold down on itself |

On its patrol the Guardian stays out of the furnace's heat (a test checks the distances), so using
the furnace is the player's idea, not something that happens anyway. The scaffolds have no saved
state: they stand again after a restart.

**Readability on a phone.** One enemy, in an open hall 20 m square with four columns, on the
existing telegraphs. The Guardian's navigation mesh covers the casting floor only, so it never
follows the player up the stair or through a gate. Whether the fight reads well on a phone screen
has not been judged.

## Traversal, checkpoints and recovery

**Traversal.** The level uses the existing traversal: catching and climbing the lift hall's ledge from
the lift, climbing down and back up the rubble steps beyond the breach, the edge guard at the slag
pit and at the ledge, the stair. The geometry is checked by arithmetic against the motor's and the
traversal controller's defaults: the Mage reaches ledges about 3.77 m above where it stands and a
sprinting jump covers about 4.9 m (`TraversalReach`). Every wall is high enough that nothing the player
can stand on (floor, ledge, prop, scaffold, lift, gantry) leads onto a wall top or a column top.

**Checkpoints.** Six, in order: forecourt, Ore Hall (beyond the barricade, before the bridge), Lift
Hall, the doorway to the stair, exit court, way out.

| Event | What happens |
| --- | --- |
| Reaching a checkpoint | It is remembered in the world state (a puzzle record), and the world state's checkpoint is taken |
| Restart (R, Start, the defeat panel's button) | The player returns to the furthest checkpoint reached. Lasting changes stay; everything temporary is reset |
| Falling into the slag | 25 damage, and back to the last checkpoint with nothing else reset. Four falls from full health defeat the Mage, and the restart follows |
| Closing and reopening the game (a build) | The furthest checkpoint is remembered; the player starts there, and the level is as it was left |
| Reaching the way out | The level is complete. Restart now replays the foundry from the start: everything the foundry saved is forgotten, other levels' saved state is kept |
| Retry from checkpoint (diagnostics) | The world state goes back to the last checkpoint and the player restarts there. A plug broken after it stands again: one way to try the other exits |

**Recovery.** The slag bridge and the gantry use a new property value, **Recover Seconds** on
stability: five seconds after they came down, counted while nobody stands on them, they stand again.
A failed crossing is therefore tried again at once, never a dead end and never a reason to restart.
It is off (0) by default and on no earlier object, so the LM-03 structure and the LM-06 walkway
behave as before. A collapse that recovers is temporary: it is never saved, and clearing temporary
state stands the object up again. The validation reports a definition that both recovers and saves its collapse.

**No softlocks.** Every lasting change in the level only opens a way. The tests explore every lasting
state the level can reach, in every order, and every place the player can stand in each, and check
that the way out can still be reached from all of them, and from every checkpoint. The Guardian's
defeat is remembered: a restart does not bring it back (`PersistentDefeat`).

## Development diagnostics

Hidden in normal play, and switched off entirely in a build that is not a development build.
**F3** or the back-quote key, the gamepad's **Select** (View), or a **three-finger tap** shows it.

It shows: the frame rate and frame times (average, slowest 5%, worst, over the last 240 frames), the
quality level; the selected discipline; the magical object in the middle of the view with its
properties, state, values and saved id; every temporary state running in the scene; the reaction
queue (waiting, delivered and per second, peak, refusals) and the simulated objects; and the
foundry's progression (what has changed for good, the checkpoint, which ways through the exit wall
are open). Buttons: Hide, Restart, Retry from checkpoint, Next scene.

The frame times are Unity's reported frame durations; they are not a profiler. The overlay is drawn
with Unity's immediate-mode GUI, which allocates while it is shown. **Whether its buttons take touches
on a device**, with the Input System as the only input handler, has not been checked; the
three-finger toggle uses the Input System and should work, but is also untried.

## Visual direction and performance

- **Silhouette.** Three tall chimneys outside the walls, a 9.5 m north wall with a breach, a stair
  well, a furnace stack: each part has its own shape against the sky. The way out frames the distant
  spire of the Fantasy Playground's background.
- **Materials.** The architecture uses the environment's grey-box materials, which the visual style
  replaces with its painted ones when it is applied; the foundry's props use the painted wood, metal
  and stone directly. Slag and embers are unlit colours. No new shader.
- **Light.** The sun and sky of the playground, then the visual style's preset. Three point lights
  without shadows: the slag pit (always), the furnace's mouth (while hot), the crystal (once awake).
- **Restraint.** Effects are the existing pooled bursts. Nothing new uses physics bodies, particles
  that collide, or transparency.

Estimated from the generating code, not measured: about 85 blocks of architecture and about 400 to
500 renderers in all, including about 46 trees, the mountains and the Guardian. That is roughly twice
the Fantasy Playground. Decoration and architecture are marked static so Unity can batch them, but
the magical objects are built from many small parts (as the LM-03 and LM-06 examples are) and cannot
be. **This must be measured on a phone**; if frame time is high, the first candidates are the trees
outside the walls, the props, and merging the 21 stair steps into one mesh.

## Set up

1. Pull `main` on the laptop and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Broken Foundry**. It creates `Assets/Scenes/BrokenFoundry.unity`,
   generates the foundry's assets, builds the level, bakes the Guardian's navigation mesh, adds the
   Mage, the HUD and the director, applies the visual style, adds the scene to Build Settings (after
   the others, so a build still starts in the playground) and validates. Read the report: it should
   end with "Validation: no problems found." and "Level checks: no problems found."
3. Press Play. Keys 1, 2 and 3 choose Arcane, Flame and Aegis. F3 shows the diagnostics.
4. Run **Threefold > Validate > Open Scene** at any time.
5. Upload the scene and the new or changed assets (see `Docs/DevelopmentWorkflow.md`):
   `Assets/Scenes/BrokenFoundry.unity`, `Assets/Threefold/Generated/Foundry`,
   `Assets/Threefold/Generated/Navigation/BrokenFoundryNavMesh.asset`, any new files in
   `Generated/WorldMagic`, `Generated/Enemies`, `Generated/Environment` and `Generated/VisualStyle`,
   and `ProjectSettings/EditorBuildSettings.asset`.

Running it again rebuilds the walls, floors, decoration, lighting and navigation mesh. The magical
objects, the Guardian, the checkpoints, the Mage, the HUD and anything added by hand are kept; only
what is missing is added and only empty connections are filled. To return to the default layout of
the gameplay objects, delete `BrokenFoundry Gameplay` and run it again. Generated assets are kept once
they exist; delete `Assets/Threefold/Generated/Foundry` to generate them with the default values.

**To reach the level on a device:** the Android build now assembles and includes the foundry, but no
portal leads to it yet. In a development build, open the diagnostics (three-finger tap) and press
**Next scene**.

## Tuning

| To change | Where |
| --- | --- |
| The crystal, the furnace, the bridge and gantry | `Generated/Foundry/*Definition.asset` |
| How long a bridge or gantry stays down | Its definition: Stability, Recover Seconds |
| The furnace's heat and reach | `FurnaceDefinition.asset`: Reactions |
| The barricade, the slag plug, the scaffolds | The LM-03 definitions they share, in `Generated/WorldMagic` (shared with the Discipline Range) |
| The lift and the counterweight | `RuneLockedSlideDefinition.asset` (shared with the range's court) |
| The Guardian | `Generated/Enemies/RuinsGuardian.asset`, as in the arena |
| Slag damage, a hazard's box | `BrokenFoundry Gameplay` > `Slag Pit`: Hazard Volume |
| A checkpoint's box and respawn point | `Checkpoints` > each checkpoint and its `Respawn` child |
| The level's geometry | `Runtime/Levels/FoundryLayout.cs`, then run the setup again. The tests check the new numbers |

## How it is built

| File (under `Assets/Threefold/Scripts`) | Role |
| --- | --- |
| `Runtime/Levels/FoundryLayout.cs` | Every solid, opening, placement, checkpoint and prop, as numbers. Plain C# |
| `Runtime/Levels/FoundryRoutes.cs` | The level as places, passages and actions; exploration of every lasting state. Plain C# |
| `Runtime/Levels/TraversalReach.cs` | What the Mage can climb and jump, from the components' defaults. Plain C# |
| `Runtime/Levels/FoundryDefaults.cs` | The crystal, the furnace and the gantry. Plain C# |
| `Runtime/Levels/FoundryProgressTracker.cs` | Reads the level's progress from the objects for the diagnostics |
| `Runtime/Objectives/Checkpoint.cs`, `CheckpointRoute.cs`, `CheckpointProgress.cs`, `HazardVolume.cs` | Checkpoints, the slag, the one update that checks the player against them; the rules in plain C# |
| `Runtime/Enemies/PersistentDefeat.cs` | An enemy's defeat as lasting progress |
| `Runtime/DevTools/*` | The diagnostics overlay; frame timing and state text in plain C# |
| `Editor/Levels/BrokenFoundrySetup.cs` | The menu command |
| `Editor/Levels/BrokenFoundryAssets.cs` | The foundry's definitions and prefabs |
| `Editor/LevelFlowValidator.cs` | Checks of checkpoints, hazards and remembered defeats, in every scene's validation |

### Changes to earlier systems

| Area | Change |
| --- | --- |
| `StabilitySettings`, `StabilityProperty` | **Recover Seconds** (default 0, off). A recovering collapse is temporary: not saved, ended by clearing temporary state, keeps the object simulated until it stands |
| `MagicalPropertySet.Capture` | Does not save a collapse that will recover |
| `MagicalObjectRules` | Reports a definition that recovers and also saves its collapse |
| `PrototypeSession` | A respawn point set by checkpoints; `Respawn()` without a reset; `RetryFromCheckpoint()`; a replay scope: after the objective is complete, a restart forgets that level's saved state and starts it from the beginning |
| `DefeatTargetsObjective` | Goals: puzzle elements that count while active, besides targets to defeat |
| `ObjectiveHud` | A count of one is not shown ("Find the way out of the foundry", not "… 0 / 1") |
| `EnemyController` | `StaysDefeated`: a restart leaves such an enemy defeated |
| `WorldState`, `WorldStateStore` | `ForgetScope(prefix)` / `RemoveWithPrefix`: forget one level, keep the rest |
| `ObjectiveSetup` | Goals in the objective options |
| `PrototypeValidator` | Goals; the level-flow checks |
| `PrototypeBuild`, `android-build.yml` | The Android build also assembles and includes the foundry |
| `MagicalObjectSetup` | Its part helpers are internal, for reuse |

`WorldState.SaveCheckpoint` and `RevertToCheckpoint` existed since LM-04 with nothing calling them;
the foundry's checkpoints now do.

## Tests

Run by `Tools/ci/compile_check.sh` outside Unity, and by Unity's Test Runner in CI once the Unity job
is enabled.

| Test class | Covers |
| --- | --- |
| `BrokenFoundryLayoutTests` (10) | Jump and reach figures; every opening fits the Mage; the stair and the gantry's slope; the ledge needs the lift at its stop; the pit cannot be jumped and is crossed on the bridge, its edge is guarded, the slag covers it; the breach needs the gantry and its way down is walkable both ways; **no wall top or column top can be reached from anything the player can stand on**; checkpoints and respawn points stand on floors and are clear; the objects fit where they stand; the furnace's heat misses the Guardian's patrol and a scaffold can fall on it |
| `BrokenFoundryTests` (33) | The order of the progression; three complete exit solutions, each alone, genuinely different; **no softlock in any reachable state, in any order, from any checkpoint**; which disciplines the level needs; the crystal; the barricade by Flame and by force; the bridge unbraced, braced, and against two sprinting jumps; recovery, its persistence rules, and that it is off for earlier objects; the lift and its lock going dark half-way; the furnace's heat and the Guardian's plates; the shockwave and the scaffolds; the plug and its heating window; the counterweight and the gate; what lasts and what resets across a reload, for every object; the save file; forgetting one level; checkpoint rules; the hazard rule; frame timing; state text; the new definitions' validity |
| `BrokenFoundryAssemblyTests` (5) | Needs Unity. Two in batch mode only: the level builds, validates and passes its own checks, the objective has its goal, the Guardian is on its mesh and cannot leave the casting floor, every respawn point has ground under it and room above; a second run adds nothing. Three in any Edit Mode session: `TraversalReach` equals the components' defaults, an objective completes on a goal, a checkpoint's box |

Two deliberate faults were put into the layout to see that the reachability test fails: walls lowered
to 3.5 m, and a scaffold moved next to a column. Both were caught; the layout was restored.

## Verification

| Check | Result |
| --- | --- |
| Compiles for the Editor and with Android player defines | Passed (`Tools/ci/compile_check.sh`) |
| Repository checks | Passed (`Tools/ci/check_unity_repo.py`) |
| Progression paths, all three exit solutions, no softlocks, checkpoint recovery, save and reload, temporary state expiry: **as rules** | 43 tests passed outside Unity |
| Combat interactions: **as rules** | Tested with the Guardian's real values (heat, plates, stagger) |
| **The setup command, the scene, its validation** | Not run. Needs the Unity Editor |
| **Anything in Play mode**: movement through the level, climbing, the camera in the stair and culvert, the Guardian's behaviour, checkpoints firing, the slag, the diagnostics | Not verified. Needs the Unity Editor |
| **Touch controls** in the level, the three-finger toggle, the overlay's buttons | Not verified. Needs a device |
| **How it looks and reads** at camera distance, the clues, the lighting | Not judged. Needs the Editor and a device |
| **Android performance** | Not measured. No Android build has ever been made from this repository |

## Known limits

- Not compiled by Unity, run, seen or played. Expect tuning, and expect the first run of the setup
  command to surface problems the arithmetic cannot.
- The geometry checks are arithmetic on boxes. They do not know about the character controller's
  skin width, the camera, or the exact shape of a mesh collider. The margins are kept wide for that reason.
- Every magical object is built from primitive shapes, like the earlier examples. They show behaviour
  and silhouette, not final art.
- Sound is the existing generated placeholders (the gates rumble); there is no music or ambience.
- The Guardian's navigation mesh is baked with the scaffolds standing. Once one has fallen, the
  Guardian can walk through the fallen pieces.
- A player standing on a scaffold or the furnace's chimney side cannot be reached by the Guardian;
  a scaffold top is reachable by climbing. The furnace's heat and the Guardian's shockwave make such a
  place costly, but it is not prevented.
- The bridge and gantry count weight with a trigger, which counts a jumping player as standing.
- The foundry has no portal from the other levels yet; on a device it is reached through the
  diagnostics.
- The level needs Arcane (the crystal) and Aegis (the bridge); Flame is always optional.
- Restart after completion replays the whole level; there is no level select or menu.
