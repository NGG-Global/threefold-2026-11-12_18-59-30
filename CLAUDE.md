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
- The visual style (`Docs/VisualStyle.md`) is a proposal until the owner has reviewed it in Unity
  and on a device. Its shader is `Assets/Threefold/Shaders/IllustratedLit.shader`. The shader's
  arithmetic is repeated in C# (`IllustratedLightingReference`) for tests and previews: change
  both together. Style rules (palette, textures, presets) stay in plain C# with no Unity engine
  calls, so they can be tested and previewed here (`Tools/visualstyle/render_previews.sh`).
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
