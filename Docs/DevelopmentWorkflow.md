# Threefold development workflow

Threefold is developed tablet-first. Features are requested, reviewed and merged from an Android
tablet through Claude Code and GitHub. The Windows laptop is used only for work that needs the
Unity Editor, Windows tooling or a physical Android device.

## Roles

| Where | Used for |
| --- | --- |
| Android tablet (Claude Code, GitHub) | Requesting features, reviewing and merging pull requests, reading validation results |
| GitHub | The single source of truth for code, assets and project settings |
| GitHub Actions (CI) | Automated validation of every pull request, once enabled |
| Windows laptop | Running the Unity Editor, play-testing, device testing, and the one-time setup steps below |

## The feature loop

1. **Request.** Describe the feature to Claude Code from the tablet.
2. **Implementation.** Claude Code writes the code and the Editor scripts on a new branch and opens a pull request against `main`. It does not merge.
3. **Validation.** CI runs on the pull request and reports its result on the pull request page.
4. **Review.** Read the pull request summary, its "Verification" section and its "Laptop actions required" section.
5. **Merge.** Merge from the tablet when validation is green and you accept the change.
6. **Laptop, only if listed.** Pull `main`, run the menu command named in the pull request, play-test.

## Current state

| Capability | State |
| --- | --- |
| Feature branches and pull requests from the tablet | Working |
| Git LFS for models, textures and audio | Working |
| Scene setup by Editor command (`Threefold > Setup > Mage Prototype`) | Written in pull request #2; not yet run in Unity |
| Compile check without Unity (`Tools/ci/compile_check.sh`) | Working when run by Claude Code: all assemblies compile against Unity 6000.6.0f1 |
| Repository checks without Unity (`Tools/ci/check_unity_repo.py`) | Working when run by Claude Code; runs in CI after `CI_ENABLED` is set |
| Unity compile check and Edit Mode tests in CI | GitHub-hosted job (`ci.yml`): written, **never run**, needs a Unity licence secret. Since LM-08 the Edit Mode tests run in Unity on the laptop runner before the APK build (Android build run 8: 878 tests, 873 passed, 0 failed, 5 inconclusive); results in `Docs/LivingMagicTesting.md` |
| Mobile controls LM-01: fixed joystick, camera drag, ATTACK (tap, charge, aim, cancel), BLOCK, JUMP, discipline selector, desktop equivalents | Written; input rules tested outside Unity (102 tests); layout checked on ten screen sizes; not yet compiled by Unity or run on a device. See `Docs/TouchControls.md` |
| Mage combat (spell, health, mana, dummy, HUD, feedback) | Written; compiled by Unity on the laptop runner; not yet played |
| Fantasy Playground environment (generated level, lighting, modules) | Written; compiled by Unity on the laptop runner; not yet seen |
| Objective, completion and restart loop | Written; compiled by Unity on the laptop runner; not yet played |
| Animated Mage character (intermediate, CC0 mannequin base) | Written; model built and checked in Blender; the first (10-clip) model was imported by Unity as a valid Humanoid on the laptop runner (Android build run 13); the 41-clip model with combat and martial-style clips has not been imported yet. See `Docs/Characters/Mage.md` |
| Combat animations, sprint 1: stances, three-step combos (two for Aegis), charge start, hold, release and cancel, block, stagger, aerial attacks, defeat, timing cues for effects | Written; rules tested outside Unity (39 tests, zero allocation per frame); compiled against the Unity assemblies; not yet imported, seen or played in Unity, or run on a device. See `Docs/CombatAnimationArchitecture.md` |
| Martial styles, sprint 2: Arcane flowing palms, Flame punch, hook and sweep, Aegis palm, forearm and slam; a stance per discipline (whole body when standing); three strikes for Aegis; commitment of heavy strikes, recovery-tap buffering, a double-touch guard; whole-body strikes handed to the upper body when moving; comparison figures in the Discipline Range | Written; clips built and measured in Blender; rules and animation model tested outside Unity (`MartialStylesTests`); not yet imported or compiled by Unity, seen, played, or run on a device. The kick and true palm strikes need new animation. See `Docs/MartialStyles.md` |
| Magical combat VFX, sprint 3: body effects timed by the animation cues (limb trails, gathering force, contact, embers, charges that concentrate, release at the hand, Arcane Arrow ribbon, Flame Wave front, Force Pulse ring, Aegis barrier and deflection flash), magic arriving at objects, Android-ready copies of pack effects, two quality levels and an intensity value, budget validation | Written; rules tested outside Unity (36 tests, zero allocation per frame); compiled against the Unity assemblies; effects not yet generated, seen or measured. See `Docs/MagicVfx.md` |
| Traversal: ledges, climbing, vaulting, edge guarding, training course | Written; rules tested outside Unity; not yet run in Unity. See `Docs/Traversal.md` |
| Enemy AI: Ruins Guardian, Arcane Wisp, Combat Arena, player defeat | Written; decision logic tested outside Unity (53 tests); not yet run in Unity. See `Docs/EnemyAI.md` |
| Disciplines LM-02: Arcane, Flame and Aegis melee combos and charged attacks, heat, stagger, push, block, aim preview, Discipline Range | Written; combat rules tested outside Unity (88 tests); compiled by Unity on the laptop runner (Android build run 4); not yet played, or run on a device. See `Docs/Disciplines.md` |
| Magical world properties LM-03: heat, arcane charge, force and stabilise effects; thermal, structural, kinetic, arcane-reactive and stability properties; six example objects; authoring and validation commands | Written; rules tested outside Unity (118 tests); compiled by Unity on the laptop runner (Android build run 4); not yet played, or run on a device; the example prefabs have not been generated. See `Docs/WorldMagic.md` |
| World state LM-04: three persistence tiers, saved world state with identifiers, restart and new game, temporary-effect policies, one world simulation, bounded chain reactions | Written; rules tested outside Unity (83 tests); compiled by Unity on the laptop runner (Android build run 4); not yet played, or run on a device; no save file has been written by a real build. See `Docs/WorldState.md` |
| Discovery LM-05: environmental clues, resonance with the selected discipline, six kinds of answer to an attempt, attention escalation, accessibility options, sound and haptic hooks | Written; rules tested outside Unity (33 tests); compiled by Unity on the laptop runner (Android build run 4); not yet seen, heard or run on a device; the look is a proposal, not approved. See `Docs/Discovery.md` |
| Combinations LM-06: heat + force = fracture (objects and enemy armour), arcane + mechanism = activation (rune-locked slide, Wisp ward), force + projectile = deflection (Aegis guard, Cinder Wisp), stabilise + instability = bracing (crumbling walkway), stagger by force, hazards for the player, combination court in the Discipline Range | Written; rules tested outside Unity (51 tests); compiled by Unity on the laptop runner (Android build run 4); not yet played, or run on a device; the court and the Cinder Wisp have not been generated; balance is arithmetic, not play-tested. See `Docs/Combinations.md` |
| Broken Foundry LM-07: a level of five parts generated by `Threefold > Setup > Broken Foundry`; dormant crystal, damaged barricade, self-recovering slag bridge, rune lift, Ruins Guardian with furnace and scaffolds, three ways through the sealed exit; six checkpoints, slag hazard, replay after completion, development diagnostics | Written; rules, routes, softlocks and geometry tested outside Unity (46 tests); compiled by Unity on the laptop runner (Android build run 4); not yet generated, seen, played, or run on a device; Android performance not measured. See `Docs/BrokenFoundry.md` |
| Mobile optimisation LM-08: graphics presets Performance, Balanced and Quality with 30/60/60 frame-rate targets, a guard that lowers an unkept 60 to 30, a Graphics menu, nested canvases for moving UI, diagnostics with memory, CPU/GPU time, enemies, projectiles and warnings in every level, allocation tests, Edit Mode tests on the build runner | Written; rules tested outside Unity (19 tests); not yet seen or measured on a device. See `Docs/MobilePerformance.md` |
| Play Mode tests for movement and camera | Not started |
| Interaction and puzzles: prompts, doors, plates, switches, spell activators, timers, Sealed Gateway | Written; rules tested outside Unity; not yet run in Unity. See `Docs/Puzzles.md` |
| Visual style: Illustrated Lit shader, painted materials, lighting presets, contour lines, quality levels, Style Lab | Written; shader checked against the URP shader library with a second compiler, rules tested outside Unity, previews rendered outside Unity; compiled by Unity on the laptop runner; **not yet seen in the engine or measured on a device**. See `Docs/VisualStyle.md` |
| Android build (APK) from GitHub | `android-build.yml`, started by hand. The self-hosted route on the laptop builds APKs; the GitHub-hosted route has never run (no licence secrets). Runs and results: `Docs/LivingMagicTesting.md`; how to: `Docs/CloudBuild.md` |
| Running Editor commands in CI | The Android build assembles the Broken Foundry, the Combat Arena and the Fantasy Playground from code in batch mode on the laptop runner. Committing the generated files back is not implemented |
| Play-testing, feel tuning, device testing | Laptop only |

Unity compiled every script of LM-01 to LM-07 on the laptop runner (Android build run 4, 14 November
2026); that run then stopped at scene validation, which LM-08 fixed. Compiling is not playing: what
has been seen working is recorded in `Docs/LivingMagicTesting.md`.

## Building content through code

Scenes, prefabs, materials, animation controllers and component setup are produced by Editor
scripts, not by hand in the Inspector. Each one follows the same rules:

- It is a menu command under `Threefold/`, in an Editor assembly.
- It can be run repeatedly. It adds what is missing, fills only empty references, and keeps existing objects and tuned values.
- It supports Undo and marks the scene as changed.
- It never creates or replaces a scene file without asking.

This keeps the laptop work to one menu click per feature today. The planned next step removes
that click as well: CI runs the same commands in Unity's batch mode and commits the result.
That requires the commands to run without dialogs, which the Mage setup command does not do yet.

## Automated validation (CI)

The workflow is `.github/workflows/ci.yml`. **It is switched off until you enable it**, and it
has two levels.

### Level 1: repository checks

Checks that need neither Unity nor a licence: every asset has its `.meta` file, GUIDs are
unique, LFS file types are stored through LFS, no large binary is outside LFS, JSON files are
valid, and no merge conflict markers remain. It takes well under a minute.

To enable it, on GitHub open **Settings > Secrets and variables > Actions > Variables**, choose
**New repository variable**, and create `CI_ENABLED` with the value `true`. This can be done
from the tablet.

### Level 2: Unity compile check and tests

Uses [GameCI](https://game.ci) to start Unity 6000.6.0f1 in a Linux container, compile all
scripts and run the Edit Mode tests. A matching GameCI container image exists for this Unity
version. The job is skipped, not failed, while no licence secret is present.

**Unity licence.** CI needs a licence to start Unity.

- *Unity Personal* is free, but only for individuals and organisations with less than
  USD 200,000 of revenue and funding in the last 12 months. The threshold applies to the
  organisation that owns the project, not to the game. **Confirm that NGG qualifies before
  using a Personal licence.**
- *Unity Pro* is required above that threshold. Unity lists it at approximately USD 2,310 per
  seat per year; verify the current price on unity.com.

**Secrets.** Created under **Settings > Secrets and variables > Actions > Secrets**:

| Secret | Personal licence | Pro licence |
| --- | --- | --- |
| `UNITY_LICENSE` | Full contents of `C:\ProgramData\Unity\Unity_lic.ulf` | Not used |
| `UNITY_SERIAL` | Not used | The serial key from the Unity subscription page |
| `UNITY_EMAIL` | Unity account email | Unity account email |
| `UNITY_PASSWORD` | Unity account password | Unity account password |

The licence file exists only on the laptop, so creating `UNITY_LICENSE` is a one-time laptop
action. `C:\ProgramData` is a hidden folder. If the file is missing, open Unity Hub, go to
**Preferences > Licenses > Add**, and activate the licence again. GitHub stores secrets
encrypted, and they cannot be read back after saving. Do not put them in any file in the
repository.

**Runner.** The jobs run on GitHub-hosted Linux runners (`ubuntu-latest`). No machine of yours
needs to be on.

**Cost.** This repository is private, so Actions usage is billed against the organisation's
plan. According to GitHub's billing documentation at the time of writing:

- GitHub Free for organisations includes USD 12 of Actions usage per month, and GitHub Team USD 18.
- A standard Linux runner costs USD 0.006 per minute, so USD 12 is about 2,000 minutes.
- Whether usage beyond the included amount is charged or blocked depends on the organisation's payment method and budget settings. Check them before enabling the Unity job.

The duration of the Unity job is not known yet. A first run downloads a container image of
several gigabytes and imports the whole project; later runs reuse a cache. Read the duration of
the first run on the Actions page and budget from that. Each run also downloads the LFS files,
which counts against the organisation's Git LFS allowance.

Verify the plan, the prices and the LFS allowance on GitHub's billing page before relying on
these figures; they change.

### Not verified

The workflow file is valid YAML and the checker script has been run against this repository and
against deliberately broken input. The workflow itself has not run on GitHub, and the Unity job
has never executed. Expect the first run to need adjustment.

## Keeping GitHub the source of truth

Work that exists only on the laptop is invisible to Claude Code and to CI, and it is the only
copy. The Unity project on the laptop currently contains such work, including
`Assets/Scenes/MagePrototype.unity`.

Git does not overwrite uncommitted files. If a pull or branch switch would replace one, Git
stops and reports it. The risk is the opposite one: local work that is never uploaded.

### Uploading laptop work

Save in Unity first. Then, in a terminal opened in `C:\dev\Threefold\threefold`:

```
git switch -c local/laptop-snapshot
git add -A
git commit -m "Snapshot local Unity work"
git push -u origin local/laptop-snapshot
```

This places the local work on its own branch on GitHub. It changes nothing on `main`. Afterwards,
ask Claude Code from the tablet to review that branch and open a pull request. Use a new branch
name each time, for example `local/laptop-2026-11-20`.

### Testing a pull request on the laptop

```
git fetch
git switch <branch name from the pull request>
```

Then open the project in Unity. If Git refuses because of local changes, upload them first with
the steps above.

## What still needs the laptop

- Creating the `UNITY_LICENSE` secret (once).
- Uploading work that was created in the Unity Editor.
- Running an Editor menu command after a merge, until CI can do it.
- Play-testing and tuning how the game feels.
- Testing on a physical Android device, and anything that needs Android signing keys.

## Sources

- GameCI activation: https://game.ci/docs/github/activation
- GameCI test runner: https://game.ci/docs/github/test-runner
- GameCI builder: https://game.ci/docs/github/builder
- GameCI container images: https://hub.docker.com/r/unityci/editor/tags
- GitHub Actions billing: https://docs.github.com/en/billing/concepts/product-billing/github-actions
- Unity plans and pricing changes: https://unity.com/products/pricing-updates
- Unity Personal: https://unity.com/products/unity-personal
