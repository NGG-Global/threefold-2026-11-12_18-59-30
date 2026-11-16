# Threefold: guidance for Claude Code sessions

Threefold is an Android-first, third-person, high-fantasy action-adventure game with three
playable classes (Mage, Rogue, Monk). The owner develops from an Android tablet through Claude
Code and is new to Unity and Git. Read `Docs/DevelopmentWorkflow.md` for the full process.

Visual work follows `Docs/ArtBible.md` (approved direction: Soft Illustrated Realism). It
separates approved decisions from proposals; do not present a proposal as decided.

## Project facts

- Unity 6000.6.0f1, Universal Render Pipeline, new Input System only (`activeInputHandler: 1`).
- Primary platform Android (IL2CPP, minimum API 26). Package ID `com.nggglobal.threefold`
  (set on the owner's laptop; `main` still holds the template value until that work is committed).
- Game code lives in `Assets/Threefold/`. Namespaces start with `Threefold.`.
- Large binaries (models, textures, audio) are stored through Git LFS; see `.gitattributes`.

## Constraints of the cloud session

- There is no Unity Editor here. Compilation can be checked with `Tools/ci/compile_check.sh`.
  Never state that anything runs or works in Unity unless a CI run or the owner has confirmed it.
  Say what was checked and what was not.
- Do not edit binary assets by hand. New binary assets (models, images) must be produced by a
  committed script, with their source and licence recorded (see `Docs/Characters/Mage.md`).
  Blender 4 can be installed from Ubuntu's packages (`blender`, `python3-numpy`) and run headless.
- The owner's laptop may hold uncommitted files that are not in GitHub, including
  `Assets/Scenes/MagePrototype.unity`. Never add or replace a file at a path that may exist only
  on the laptop. Ask for it to be committed from the laptop first.

## Rules for Unity content

- Do not hand-write or hand-edit `.unity`, `.prefab`, `.mat`, `.anim` or `.controller` files.
  Create and change them through an Editor script with a menu command under `Threefold/`, so the
  owner runs one command instead of working in the Inspector.
- Editor commands must be safe to run repeatedly: add what is missing, fill only empty
  references, keep existing objects and tuned values, register Undo, mark the scene dirty.
- Every new file and folder under `Assets/` needs a `.meta` file with a new unique GUID, committed
  in the same change. Reuse the existing `.meta` for a folder that another open branch also creates.
- Avoid namespaces that shadow Unity types: use `Threefold.InputHandling`, `Threefold.Cameras`,
  `Threefold.Characters`, `Threefold.Enemies`, `Threefold.VisualStyle`, `Threefold.EditorTools`,
  not `Threefold.Input`, `.Camera`, `.AI` or `.Editor`.
- Runtime code goes in a runtime assembly, Editor-only code in an Editor assembly (`.asmdef`).
- `Assets/Threefold/Generated/` holds assets produced by the setup commands on the laptop (prefabs,
  materials, spell assets). Never author or edit those files here; change the generating code instead.
- Gameplay code reads input through `CharacterInputSource`, never from a device directly.
- Enemy decisions live in plain C# (`EnemyBrain`, `EnemyStates`, `EnemyRules`) that never touches a
  Unity object, so they can be tested outside Unity. Keep it that way; see `Docs/EnemyAI.md`.
- The Mage's combat is split the same way (`Docs/Disciplines.md`): sequencing and resolution rules
  (`DisciplineCombatModel`, `MeleeComboMachine`, `TargetSelection`, `HeatMeter`, `BlockRules`) are
  plain C#, and `DisciplineCombat` only carries out their commands. The starting numbers of the
  disciplines are in `DisciplineDefaults`; generated discipline assets are the truth afterwards.
  Input-side decisions likewise stay in plain C# (`AttackGestureRecognizer`, `PointerRouter`).
- Combat animations (`Docs/CombatAnimationArchitecture.md`) follow the combat rules and never lead
  them: no animation deals damage, delays a hit or extends an action. What plays and when is decided
  in plain C# (`CombatAnimationModel`, `CombatTimeWarp`, `CombatCueTracker`, `CombatSlotResolver`);
  `CombatAnimationDirector` only carries it out. Gameplay never names a clip: slots in the
  `CombatAnimationSet` do, and states are named by `CombatSlotId`. Timing comes from normalised
  moments in the slots, fitted to the rules' clock; do not add Animation Events to imported clips.
  Contact and release come only from the rules, once per action; every action ends with exactly one
  `Recovered` or `Cancelled` cue. The starting slots are in `CombatAnimationDefaults`; the generated
  set is the truth afterwards. `Mage.fbx` changes only through `Tools/blender/build_mage.py`, checked
  with `compare_mage.py`.
- The martial styles (`Docs/MartialStyles.md`) are clips derived from library motion by
  `Tools/blender/martial_styles.py`: adapt authored motion (cut, join, retime, layered rotations,
  hand poses), never generate a whole strike from nothing, and keep every strike starting and ending
  on its discipline's stance (`analyse_martial_styles.py` must report 0.0 m joins). Slot moments come
  from that tool's measurements of the exported model. Movement restriction of a strike is data
  (`MeleeStepProfile` commitment), never code per discipline. A placeholder that stands in for a
  missing motion (the Flame kick) is listed under "What needs new animation"; do not call it final.
- The Mage's looks and bearing (`Docs/Characters/Mage.md`, `Docs/CharacterIdentity.md`): `Mage.fbx` is the only
  source of clips and is not changed for a look. The Wayfarer look is a separate mesh on the same skeleton
  (`MageWayfarer.fbx`, built only by `Tools/blender/build_mage_wayfarer.py`, checked with
  `check_mage_wayfarer.py`); `MageCharacterSetup` uses it only if its Humanoid mapping equals `Mage.fbx`'s
  (`MageLookRules`) and otherwise keeps the mesh of `Mage.fbx`. Both are intermediate characters on a library
  mannequin: never call either production art, and keep "implemented" apart from "proposed" in the documents.
  Personality is a layer on top of the clips, never an edited clip: what it adds is decided in plain C#
  (`PersonalityPoseModel`, `SecondaryMotionModel`) and `CharacterPersonality` only carries it out, after the
  Animator. Differences between disciplines are data (`PersonalityProfile`; starting values in
  `PersonalityDefaults`). It reads gameplay and never writes it, never touches legs, hips or the root, and its
  arm share is zero during any action. Bones named `sec_...` swing on its springs; `face_blink` and `face_brows`
  are the only facial animation the model supports. Generated parts are weighted to at most two bones (the
  Mobile quality level's limit). No cloth simulation.
- Magical combat effects (`Docs/MagicVfx.md`) follow the rules and the animation cues and never lead them:
  no effect deals damage or calls a rule. What starts, grows and stops when is decided in plain C#
  (`BodyEffectSequencer`, `MagicVfxGeometry`, `MagicVfxQuality`, `MagicVfxBudget`, `WorldReactionVfx`);
  `DisciplineBodyEffects` and `WorldMagicFeedback` only carry it out. Differences between disciplines are data
  (`BodyEffectTiming` in each discipline's presentation; starting values in `MagicVfxDefaults`), never code
  per discipline. Every effect is pooled, has no light, collision, audio or sub-emitter, and stays within
  `MagicVfxBudget`. Third-party pack prefabs are never edited: `MagicVfxPackAdapter` makes project-owned
  copies. Pack facts come from `Tools/vfx/analyse_vfx_packs.py`, not from prefab names. The look is a proposal
  until the owner has reviewed it in Unity and on a device.
- Aerial abilities (`Docs/AerialAbilities.md`): the rules are one plain-C# state machine
  (`AerialAbilityModel`, with `JumpPhysics` and `AerialSimulator`); `AerialAbilities` only feeds it and hands its
  motion to `ThirdPersonMotor.ApplyAerialMotion`. The motor's first jump is not changed by them. One activation per
  airborne sequence, whatever the discipline; which discipline has which ability is data (`abilityBySlot`). Keep
  `AerialCourseLayoutTests` passing when tuning changes. Animator poses follow the `AerialMove` integer, never a
  trigger; the three poses are placeholders listed under "What needs new animation".
- How the world responds to magic (`Docs/WorldMagic.md`) follows from properties and effects, not
  from a script per object or a table of spell-on-object cases. The rules (`MagicalPropertySet`,
  the properties in `WorldProperties`, `CreatureEffectModel`, `MagicalObjectRules`) are plain C#;
  `MagicalObject` only carries them out. A new kind of object is a `MagicalObjectDefinition`
  asset, not new code. Damage goes to `Health`, effects go to `IMagicEffectReceiver`: keep them
  apart. The starting values of the examples are in `WorldMagicDefaults`.
- What the world remembers (`Docs/WorldState.md`) is in one store, `WorldState.Current`, which
  `PuzzleSessionState` also uses: do not add a second save mechanism. A state is saved only if
  the definition names it under Persisted and the object has a Persistent Id; heat and charge are
  temporary unless named as an exception. Loading is silent: it announces nothing and sends no
  reaction. No magical object has an update or a coroutine of its own; time passes through the
  one `WorldSimulation`. One object acts on another only through a reaction rule in its
  definition, offered to the `ReactionScheduler`, never by calling the other object directly.
  The budgets (depth 3, 8 per frame, 32 active objects) are provisional, not measured.
- Discovery (`Docs/Discovery.md`) only reads the world and shows it: nothing under
  `Runtime/WorldMagic` may depend on `Runtime/Discovery`. What it decides is in plain C#
  (`DiscoveryRules.cs`). An object resonates only if an effect the selected discipline really
  carries could change it as it is now (`CouldRespondTo`): never add a table of discipline-object
  pairs, and never show compatibility that the rules do not grant. No outlines, icons, overlays
  or text on interactable objects, and no numbers shown to the player. Every answer keeps a
  movement of its own, so none depends on colour. Only the `DiscoveryDirector` has an update, and
  it attends to a bounded number of nearby objects. Its look is a proposal until the owner has
  reviewed it in Unity and on a device.
- Combinations (`Docs/Combinations.md`) follow from properties and effects through the shared
  relationships in `CombinationRules`: never add a rule keyed to a spell order, a discipline pair
  or a kind of object. Creatures take effects through `CreatureEffectModel` with the same property
  classes as objects; each kind of enemy gets some capabilities (heat, push, stagger force, armour,
  ward), never all. Only designated projectiles can be deflected (`DeflectionRules`), once.
- The visual style (`Docs/VisualStyle.md`) is a proposal until the owner has reviewed it in Unity
  and on a device. Its shader is `Assets/Threefold/Shaders/IllustratedLit.shader`. The shader's
  arithmetic is repeated in C# (`IllustratedLightingReference`) for tests and previews: change
  both together. Style rules (palette, textures, presets) stay in plain C# with no Unity engine
  calls, so they can be tested and previewed here (`Tools/visualstyle/render_previews.sh`).
- The Broken Foundry (`Docs/BrokenFoundry.md`) is built only from `FoundryLayout`: change the numbers
  there, never the generated scene, and keep `BrokenFoundryLayoutTests` passing (they check every climb,
  jump and wall against `TraversalReach`). Its progression is mirrored in `FoundryRoutes`; a new lasting
  change must keep the softlock test green. Checkpoints and remembered enemy defeats are puzzle records
  in `WorldState.Current`, and everything the level saves starts with `FoundryLayout.KeyPrefix`, which
  replaying the level forgets. Structures that stand again by themselves (`recoverSeconds`) never save
  their collapse.
- Graphics presets and mobile performance (`Docs/MobilePerformance.md`): the style's three quality
  levels are the player's presets Performance, Balanced and Quality, with frame-rate targets 30, 60 and
  60 (`GraphicsPresets`, plain C#). Every preset keeps the illustrated look, contour lines included.
  A 60 target the phone cannot keep is lowered to 30 by `FramePacingGuard`; never add smoothing to
  input. The magic's per-frame rules must allocate nothing: `MobilePerformanceTests` measures it through
  `AllocationMeter`: bytes in the .NET runner of `compile_check.sh`, the profiler's `GC.Alloc` count in Unity, whose
  Mono does not count bytes. A test is inconclusive only where neither counter works, never passed. The
  development overlay is in every level through the character rig and off in release builds.
- The main menu (`Docs/MainMenu.md`) is a live scene, never a flattened picture, built only by
  `MainMenuSetup` from `MainMenuStage` (character, camera, land) and `MainMenuLayout` (interface): change
  the numbers there and keep `MainMenuTests` passing. Its rules are plain C# (`MainMenuModel`);
  `MainMenuScreen` only carries them out. It offers one predefined protagonist: no character creation,
  class selection or switching. The name Eryn, his described appearance and the menu's look are proposals
  until the owner approves them; the character shown is the intermediate Mage, never call it Eryn's final art.
- The Reference Courtyard (`Docs/VisualReferencePrototype.md`) is a review scene for the visual direction,
  built only by `ReferenceCourtyardSetup` from `ReferenceCourtyardLayout`: change the numbers there and keep
  `ReferenceCourtyardLayoutTests` passing (they check the first view's composition as arithmetic). Its meshes
  are made by code (`ReferenceCourtyardMeshes`); generated meshes are kept, so a changed shape needs a new
  name (raise `Version`). Nothing in it is transparent, it has one light, and it stays under the layout's
  triangle limit. It is isolated: no gameplay scene leads to it and it adds no rule. Never say it matches
  the owner's reference picture; say what the renders show. `ReviewRenders` pictures are Editor renders,
  not the game on a device.
- Generated style materials and assembled scenes are kept between runs, with their values. A change of a
  material preset or of a provided lighting preset reaches them only through a revision: raise
  `StyleMaterialPresets.Revision` or `LightingPreset.Revision` with the change (`Docs/MaterialCohesion.md`).
  Generated textures are never regenerated by a revision; a changed texture is a new file. Record every
  changed lighting value in the documents, and take before-and-after renders with `ReviewRenders` from the
  layout's viewpoints; `shader_check.py --measure` compares the shader's size.
- Camera and controls feel (`Docs/CameraAndControlsFeel.md`): values that decide feel and are meant to be
  tried on a device live in `FeelSettings` (plain C#; kept by `FeelSettingsStore`; changed from the
  diagnostics' Feel panel). Every default must leave the game as it was before the setting existed. What the
  camera does by itself is decided in plain C# (`CameraRecenter`) and `ThirdPersonCamera` only carries it out.
  Never change `Time.timeScale` for feel: the combat rules run on game time. Hit-stop and haptics are
  evaluated in that document and not built; do not add them without the owner's decision.
- The development build's scenes are `PrototypeBuild.DevelopmentScenes`: the gameplay scenes first and
  unchanged, review scenes after them, reached only through the diagnostics' scene list
  (`DevSceneSwitcher`, plain C#). A release build must not take the review scenes.
- A class deriving from `MonoBehaviour` or `ScriptableObject` (directly or not) must be alone in a
  file of its own name, or Unity saves its assets without a script. `check_unity_repo.py` checks it.
- Project rendering settings (`Assets/Settings`, `ProjectSettings`) are not changed by tooling
  without showing the owner what was found and what would change.
- No paid assets and no new packages without the owner's agreement.

## Before opening a pull request

1. Run `python3 Tools/ci/check_unity_repo.py`. It must report no errors.
2. Run `Tools/ci/compile_check.sh`. It compiles all Threefold assemblies against the real Unity
   assemblies without running Unity (needs the .NET 8 SDK; the first run downloads about 4 GB).
   It must pass. Passing proves compilation only, never runtime behaviour. Its last step checks
   the shaders with `Tools/ci/shader_check.py` (needs the `glslang-tools` package; skipped with a
   notice if it is missing). That check uses a different compiler from Unity's: it finds errors
   in the shader source, and does not prove that Unity accepts the shader.
3. Use a separate branch per change and open a pull request against `main`. Do not merge it.
4. Fill in the pull request template, including the "Laptop actions required" section.
