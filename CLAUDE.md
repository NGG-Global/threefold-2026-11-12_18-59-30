# Threefold: guidance for Claude Code sessions

Threefold is an Android-first, third-person, high-fantasy action-adventure game with three
playable classes (Mage, Rogue, Monk). The owner develops from an Android tablet through Claude
Code and is new to Unity and Git. Read `Docs/DevelopmentWorkflow.md` for the full process.

## Project facts

- Unity 6000.6.0f1, Universal Render Pipeline, new Input System only (`activeInputHandler: 1`).
- Primary platform Android (IL2CPP, minimum API 26). Package ID `com.nggglobal.threefold`
  (set on the owner's laptop; `main` still holds the template value until that work is committed).
- Game code lives in `Assets/Threefold/`. Namespaces start with `Threefold.`.
- Large binaries (models, textures, audio) are stored through Git LFS; see `.gitattributes`.

## Constraints of the cloud session

- There is no Unity Editor here. Never state that code compiles or runs in Unity unless a CI run
  or the owner has confirmed it. Say what was checked and what was not.
- LFS file contents are not downloaded here. Do not read, edit or regenerate binary assets.
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
  `Threefold.Characters`, `Threefold.EditorTools`, not `Threefold.Input`, `.Camera` or `.Editor`.
- Runtime code goes in a runtime assembly, Editor-only code in an Editor assembly (`.asmdef`).
- `Assets/Threefold/Generated/` holds assets produced by the setup commands on the laptop (prefabs,
  materials, spell assets). Never author or edit those files here; change the generating code instead.
- Gameplay code reads input through `CharacterInputSource`, never from a device directly.
- No paid assets and no new packages without the owner's agreement.

## Before opening a pull request

1. Run `python3 Tools/ci/check_unity_repo.py`. It must report no errors.
2. Syntax-check new C# files if a parser is available.
3. Use a separate branch per change and open a pull request against `main`. Do not merge it.
4. Fill in the pull request template, including the "Laptop actions required" section.
