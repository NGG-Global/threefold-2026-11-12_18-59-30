# Threefold v0.1: release readiness

Audit date: 12 November 2026. Audited code: branch `release/v0.1-readiness`.

## Summary

- **The code compiles.** Every Threefold assembly was compiled with the C# compiler against the
  real Unity 6000.6.0f1 assemblies, with no errors. This is the first time any of this code has
  been through a compiler.
- **Nothing has run in Unity.** The Unity Editor has never opened this code, no scene has been
  assembled, the game has never been played, and no APK has been built.
- **`main` does not contain the game.** The audit request assumed that all sprints were merged.
  They were not: `main` holds only Sprint 1 and the workflow infrastructure. Sprints 2, 3 and 5
  were open pull requests (#4, #5, #6) and Sprint 6 had not been pushed. This branch contains
  all of them plus the audit fixes, so merging its pull request brings everything into `main`.
- **There is no animation system.** No animation code, Animator, character model or animation
  asset exists in the repository, and no Sprint 4 work reached GitHub. The Mage is a capsule.

## 1. Confirmed working

"Confirmed" here means executed, with the result observed.

| What | How it was confirmed |
| --- | --- |
| All C# compiles for the Editor | `Tools/ci/compile_check.sh`: `Threefold.Runtime` (47 files), `Threefold.Editor` (11) and `Threefold.Tests.Editor` (10) compiled against Unity 6000.6.0f1's own assemblies. 0 errors |
| Runtime code compiles for a player build | `Threefold.Runtime` compiled again with Android player defines and no Editor assemblies. 0 errors, so no Editor-only API leaks into the game |
| Unity UI and Input System API usage | Both packages (uGUI 2.6.0, Input System 1.20.0) were compiled from Unity's own source and the Threefold code compiled against the result |
| No obsolete API use | Compiled at the highest warning level. The only warnings are two "field never assigned" notices on fields that Unity fills from the Inspector |
| 26 logic tests pass | Joystick maths (9), resource pool rules (8), safe-area and density scaling (9), executed under .NET against Unity's maths types |
| Repository hygiene | `Tools/ci/check_unity_repo.py`: every asset has its `.meta`, no duplicate GUIDs, LFS types stored through LFS, valid JSON. 0 errors |
| Render pipeline references | The assembly names `Unity.RenderPipelines.Core.Runtime` and `Unity.RenderPipelines.Universal.Runtime`, and the four members used from them, were checked against the URP 17.6.0 source shipped with Unity 6000.6.0f1 |
| Built-in font name | `LegacyRuntime.ttf` is the name Unity's own UI package uses |

Limits of the compile check: four render-pipeline members are represented by stand-ins
(verified against source as above), and the tests ran outside Unity, not in the Unity Test Runner.

## 2. Statically inspected, not verified

These compile, and were read by two independent reviews, but have never executed.

| System | What is unverified |
| --- | --- |
| Movement and camera | Feel, ground detection, camera collision, shoulder framing, shake |
| Touch controls | Finger routing, joystick feel, safe area, scaling, scheme switching, on any device |
| Combat | Projectile flight and collision, aiming and aim assist, pooling, health and mana in play |
| HUD and feedback | Bars, crosshair, damage numbers, hit flash, effects |
| Fantasy Playground | Everything about how it looks; jump distances; the generated materials |
| Objective loop | Progress, completion panel, restart, in play |
| Setup command | `Threefold > Setup > Fantasy Playground` has never run. It generates about 60 assets and a scene |
| 54 Unity-dependent tests | Health, mana, caster, objective, input merging, mesh builder, scene assembly. They compile; they need the Unity engine to run |
| Android build | Never attempted |

## 3. Confirmed defects and fixes

Found by the compile check, two independent code reviews and the repository audit.

| Defect | Fix |
| --- | --- |
| `main` was missing Sprints 2 to 6 | This branch carries all of them |
| The screen could rotate to portrait; the touch layout is landscape only | The build sets landscape-only rotation |
| The Android package ID in the repository is the template's (`com.UnityTechnologies...urpblank`), company `DefaultCompany` | The build sets `com.nggglobal.threefold`, product "Threefold", company "NGG Global" |
| Only the template's `SampleScene` is in Build Settings | The build uses the generated Fantasy Playground scene explicitly, and the setup command adds it to Build Settings |
| Construct gems were placed on the back of the constructs, hidden from the player | Moved to the front |
| A default construct in the grey-box scene stood inside a jump box | Moved clear |
| Regenerating one combat prefab left the spell asset pointing at nothing | The setup reconnects the spell; the instructions now say to delete the whole folder |
| Generated UI sprites asked Unity to trace an outline from released pixels | Created with a full-rectangle mesh |
| With more than 8 colliders on a ray, the nearest hit could be dropped (aim, camera) | Buffers enlarged to 32 |
| Android's Back button (reported as Escape) hid the touch controls | Escape is ignored for scheme switching |
| A tapped attack during cooldown was lost | Presses are buffered for 0.2 s |
| Rebuilding the environment could not be undone | Registered with Undo |
| Particle settings were changed without stopping the system first | Stopped first |

Open observations, not changed:

- `Assets/Editor/HubForceResolve.cs` is a Unity Hub helper that deletes itself the first time
  the Editor opens. It is harmless, but it makes the working tree differ from the repository.
  Remove it from the repository when convenient.
- The package list includes Unity's template extras (`com.unity.ai.assistant` pre-release,
  `com.unity.ai.inference`, `com.unity.collab-proxy`, `com.unity.visualscripting`,
  `com.unity.timeline`). The game uses none of them. They lengthen imports and builds, and a
  pre-release package is a risk for an automated build. Remove them once a build works.
- No Build Profile assets exist. The build does not need them; it passes its settings directly.

## 4. Remaining blockers for the first APK

1. **The pull request must be merged**, or the build run from this branch.
2. **Unity must run once.** An APK can only be produced by the Unity Editor with a licence.
   That is not possible from Claude Code: this environment has no licence and must not use one
   that was not provided. The two routes are in section 6.
3. **First-run defects are expected.** Compilation is proven, behaviour is not. The setup
   command and the build have never executed, so the first run will probably stop on something
   that only Unity can reveal. The build log is what is needed to fix it.

Not blockers: signing keys (a development build uses a debug key), animations, art, sound.

## 5. CI status and actual results

| Item | Status |
| --- | --- |
| GitHub Actions `CI` workflow | **Disabled.** All 6 runs to date ended as "skipped": the repository variable `CI_ENABLED` is not set |
| Unity compile and test job | **Has never run.** It needs a Unity licence secret, and none is evident |
| `Android build` workflow | **Has never run.** Started by hand only; needs the same secret |
| Paid or larger runners | None configured. Both workflows use the standard `ubuntu-latest` runner |

Nothing was enabled during this audit. Enabling CI uses the organisation's Actions allowance on
a private repository and needs your approval; see `Docs/DevelopmentWorkflow.md` for licence,
secrets and cost.

Results actually obtained, all outside GitHub Actions:

```
OK    Threefold.Runtime (Editor)                    47 files   0 errors
OK    Threefold.Runtime (Android player defines)    47 files   0 errors
OK    Threefold.Editor                              11 files   0 errors
OK    Threefold.Tests.Editor                        10 files   0 errors
Tests: 80 total, 26 passed, 0 failed, 54 need the Unity engine
Repository checker: 0 errors, 0 warnings
```

### The compile check

`Tools/ci/compile_check.sh` reproduces these results. It needs no Unity licence and does not
run Unity. It downloads the Unity Editor archive for the project's version from Unity's public
download server, extracts only the reference assemblies and bundled package sources, and
compiles against them with the .NET SDK. Claude Code can run it in a cloud session, so compile
errors are now caught before a pull request, without the laptop.

Please confirm that using Unity's assemblies in this way, as compile references only, is
acceptable under your organisation's Unity licence. Unity was not run or activated.

In this audit the script ran end to end with the downloads already in place. Its download step
uses the same commands that were run by hand, but the script has not been run from an empty cache.

## 6. Steps to the first APK

Both routes run the same code: `Threefold.EditorTools.PrototypeBuild.BuildAndroid`. It assembles
the Fantasy Playground scene from code, validates it, applies the Android settings and builds a
development APK. The generated scene and assets do not need to be committed first.

### Route A: on the laptop (no cost, one action)

1. Merge the pull request, then update the laptop's copy of `main`.
2. Close the Unity Editor.
3. Double-click `Tools\build\build-android.cmd`.

Result: `build\Android\Threefold.apk`, or `build\android-build.log` to send to Claude Code.
The script expects Unity at `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`
(set `UNITY_EDITOR` if it is elsewhere) and the Android Build Support module installed. It
creates `Assets\Scenes\FantasyPlayground.unity` and `Assets\Threefold\Generated`; it does not
touch any other scene. The script itself has not been run.

### Route B: GitHub Actions (no laptop after a one-time setup; uses Actions allowance)

1. One time, on the laptop: copy the Unity licence file's contents into the repository secret
   `UNITY_LICENSE`, and add `UNITY_EMAIL` and `UNITY_PASSWORD` (`Docs/DevelopmentWorkflow.md`).
2. On GitHub: **Actions > Android build > Run workflow**.
3. Download the APK from the run's artifacts, on the tablet.

Route B is the one that fits tablet-first development, because every later build is one tap.
It can run on a GitHub runner or on a runner installed on the laptop. `Docs/CloudBuild.md` covers
both, with the licensing position, the secrets and the cost.

## Android test checklist

Install the APK, then check in this order. Stop at the first failure and report it.

**Start**
- [ ] The app installs and opens without closing again.
- [ ] The screen is landscape and stays landscape when the device is turned.
- [ ] The courtyard, the Mage (a capsule) and the sky are visible. Nothing is bright pink.

**Controls**
- [ ] A joystick appears under the left thumb and moves the Mage.
- [ ] Dragging on the right side turns the camera.
- [ ] Jump, sprint and attack buttons respond.
- [ ] Moving, turning the camera and attacking work at the same time.
- [ ] No control is hidden by a notch or a rounded corner.

**Combat**
- [ ] The attack fires a bolt towards the crosshair.
- [ ] Bolts hit dummies: flash, number, health bar.
- [ ] Mana falls when attacking and returns when not; with too little, the bar flashes red.
- [ ] Standing near the crystal restores mana quickly.

**Objective**
- [ ] The top of the screen shows the objective and three markers.
- [ ] Defeating a construct (darker, with a glowing gem) lights a marker.
- [ ] After the third, "Training complete" appears with a time and a Restart button.
- [ ] Restart returns the Mage to the gate and brings the constructs back.

**Level**
- [ ] The stair to the terrace can be climbed.
- [ ] The jump course can be completed to the high platform.
- [ ] The camera does not pass through walls in the west passage.
- [ ] The Mage cannot leave the level or fall through the ground.

**Performance**
- [ ] Movement and camera feel smooth.
- [ ] Ten minutes of play without the device becoming hot or the app slowing down.
