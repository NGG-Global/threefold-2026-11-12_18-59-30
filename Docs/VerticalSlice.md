# First playable vertical slice

Sprint 6 joins the existing systems into one playable loop: start in the courtyard, defeat
three magical training constructs, see a completion screen, restart.

**Status:** the code compiles against Unity 6000.6.0f1 (see `Docs/ReleaseReadiness.md`), but it
has never run in Unity and has not been played. Unity validation in GitHub Actions is switched off.

## What exists, and what does not

| System | In the repository |
| --- | --- |
| Movement, camera | Yes (Sprint 1) |
| Android touch controls | Yes (Sprint 2) |
| Combat, health, mana, HUD | Yes (Sprint 3) |
| Fantasy Playground environment | Yes (Sprint 5) |
| Objective, completion, restart | Yes (this sprint) |
| **Animations** | **No.** There is no animation code, no Animator and no character model in the repository. The Mage is a placeholder capsule. The two animation library archives supplied earlier were never imported. No Sprint 4 work reached GitHub |

## The loop

1. **Start.** The Mage stands at the south gate. The top of the screen shows
   "Defeat the training constructs 0 / 3" and three markers.
2. **Play.** Three constructs must be defeated. They are darker than the practice dummies and
   carry a glowing gem. They stand on the terrace, on the high platform at the end of the jump
   course, and in the west passage, so the objective takes the player up the stair, across the
   jumps and through the confined space. A defeated construct stays down.
3. **Progress.** Each defeat lights one marker.
4. **Complete.** When the third falls, a panel shows "Training complete", the time taken and a
   restart button. The player keeps control.
5. **Restart.** The constructs return, health and mana are refilled, the Mage is placed back at
   the start, and the timer and markers are cleared. Nothing is reloaded, so it is instant.

| Restart with | Where |
| --- | --- |
| **R** key | Desktop, at any time |
| **Start** button | Gamepad, at any time |
| **Restart** button | Tap it on the completion panel (touch), or click it while the mouse is free |

The four practice dummies do not count towards the objective and still return by themselves.

## Build the scene

Run **Threefold > Setup > Fantasy Playground**. It builds the level, the Mage, controls, combat,
the constructs, the objective and its display, then validates the result and reports any problem
in its summary. See `Docs/FantasyPlayground.md`.

**Threefold > Validate > Open Scene** runs the same check at any time. It reports missing
scripts and every reference the loop depends on: input, camera, touch controls, the spell and
its prefab, the HUD, the objective and its targets, and the session.

## Fixes and changes in this sprint

Found by two independent reviews of all existing code and by integrating the systems:

| Area | Change |
| --- | --- |
| Attack input | A press made up to 0.2 s before the cooldown ends is now performed when it ends. Before, a tap that came slightly early was lost unless the button was held |
| Generated UI shapes | Sprites are created with a full-rectangle mesh. The previous call asked Unity to trace an outline from pixels that had already been released |
| Aiming and camera collision | Hit buffers enlarged from 8 to 32. With more than 8 colliders along a ray, the nearest hit could be dropped |
| Scheme selection | The Escape key no longer switches to desktop controls, because Android reports its Back button as Escape |
| Hit flash | No error if the flashing object's visual was destroyed first |
| Training dummy | Can be set not to return, and can be restored on demand, which the objective needs |
| Mana | Can be refilled at once, for restart |
| Setup | The grey-box and the Fantasy Playground commands share one routine for the playable rig, so both produce the same wiring |

Camera responsiveness was reviewed: look input from mouse, stick and touch reaches the camera in
the same frame, with no frame of delay in the code path. The smoothing values are unchanged,
because they can only be judged by playing.

## Architecture notes for Rogue and Monk

- `DefeatTargetsObjective` watches `Health` components only. It does not know which class the
  player is or what the targets are.
- `PrototypeSession` restarts by reviving, refilling and repositioning. It refers to the player
  through `Health`, `Mana` and `ThirdPersonMotor`, none of which is Mage-specific.
- `MagePrototypeSetup.EnsureCharacterRig` is the one place that assembles a playable character.
  A second class would add its own ability assets and reuse the rest.

## Validation

| Check | Result |
| --- | --- |
| Repository checker (`Tools/ci/check_unity_repo.py`) | Passed |
| C# syntax parse of every script | Passed |
| Independent review of the runtime code | No definite compile errors found; six risks, four fixed |
| Independent review of the Editor code and tests | No definite compile errors found; two layout bugs, one idempotence gap and three risks, fixed |
| Compilation against Unity 6000.6.0f1 assemblies | Passed, outside the Unity Editor (`Tools/ci/compile_check.sh`) |
| Edit Mode tests (80 written) | 26 that need no engine passed outside Unity; **54 not run** |
| **GitHub Actions** | **Disabled.** All workflow runs so far ended as "skipped" because the repository variable `CI_ENABLED` is not set, and no Unity licence secret exists |
| **Android build** | **Not run.** A workflow is prepared but has never executed |

A code review is not a compiler. The reviews lower the chance of errors; they do not remove it.

### Tests added

- `ObjectiveTests`: progress counting, completion exactly once, no completion without targets,
  reset, restart reviving targets and refilling the player, and a non-returning dummy.
- `PrototypeSceneAssemblyTests`: builds the whole scene from code and validates it, twice, to
  prove the setup is complete and repeatable. It runs only in batch mode, because it replaces
  the open scene; in an open Editor it is reported as ignored.

## Android build workflow

`.github/workflows/android-build.yml` is started by hand (Actions > Android build > Run
workflow). It assembles the scene from code and builds a development APK, so the generated scene
and assets do not need to be committed first. This is what allows a build without the laptop.

It needs the same Unity licence secrets as the test job (`Docs/DevelopmentWorkflow.md`). No
signing keys are involved: a development build is signed with a debug key and cannot be
published to a store. The workflow has never run; expect the first run to need adjustment.

## Android performance risks

Reviewed from the code. None has been measured on a device.

| Risk | Assessment | Mitigation in place or advised |
| --- | --- | --- |
| Post-processing (bloom, tone mapping, vignette) | The largest single cost on low-end phones | One checkbox on the camera turns it off. Measure with and without |
| Real-time shadows | One directional light, 1024 shadow map, 50 m distance in the Mobile pipeline asset | Lower the distance if frame time is high |
| Render scale | The Mobile pipeline asset already renders at 0.8 scale | Keep |
| Draw calls | About 230 renderers on one shader with the SRP Batcher on | Acceptable for this size; combine static meshes if needed |
| Overdraw | Vegetation is opaque. Additive effects are small and brief. The HUD covers little of the screen | Low |
| Mesh colliders | Architecture uses its visible mesh for collision | Low at this polygon count; replace with boxes if physics time shows up |
| UI rebuilds | Damage numbers are on their own canvas. The mana bar changes its anchors while mana changes, which rebuilds the HUD canvas | Low; move the bars to their own canvas if profiling shows it |
| Memory allocation | Projectiles and effects are pooled, physics queries use fixed buffers, texts are cached | Per-frame allocation is not expected; confirm with the Profiler |
| First-use hitches | Shaders compile on first use, and each generated shape is built on first use | Expect a short hitch on the first cast; prewarm if it is noticeable |
| Thermal throttling | Unknown without a device | Test a ten-minute session |

## Known limits

- No animation, no character model, no sound.
- Nothing attacks the Mage, so the player cannot fail. The objective is a test of traversal and aim.
- Text uses Unity's built-in font. If it is unavailable, the markers still show progress and the
  restart button still works, but labels are blank.
- The restart button cannot be clicked while the mouse is captured; on desktop use the R key.
