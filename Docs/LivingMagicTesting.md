# Living Magic: testing

What has been tested for LM-01 to LM-08, where, and with what result, and the checklist for
testing on a phone. Only results that were actually produced are recorded here. Passing
compilation proves compilation, not behaviour; a successful Android build proves that Unity
compiled, assembled and packaged the game, not that it plays well.

## Where tests run

| Where | What | Needs |
| --- | --- | --- |
| Any computer with the .NET 8 SDK (`Tools/ci/compile_check.sh`) | Compiles every Threefold assembly against the real Unity assemblies, for the Editor and with Android player defines; runs the tests that do not need the Unity engine; checks the shaders with glslang | Nothing from Unity's licence |
| `python3 Tools/ci/check_unity_repo.py` | Meta files, GUIDs, LFS, JSON, conflict markers, and (since LM-08) Unity script classes in files of their own name | Python 3 |
| The owner's laptop runner, **Android build** workflow, self-hosted | The EditMode tests in Unity (since LM-08, before the build), then the scenes assembled from code, validated, and the development APK built | The laptop on, with the runner and Unity 6000.6.0f1 |
| GitHub-hosted Unity job (`ci.yml`) | EditMode tests and a build | Licence secrets, which are not set; it is skipped |

## Results

### Static checks and engine-free tests (cloud session, 2026-11-14, branch of LM-08)

| Check | Result |
| --- | --- |
| `check_unity_repo.py` | 0 errors, 0 warnings |
| `compile_check.sh`: compilation | All assemblies compile, Editor and Android player defines. One warning, already on `main` (`ThirdPersonCamera.invertVertical` never assigned) |
| `compile_check.sh`: tests | 878 tests: **760 passed, 0 failed**, 118 need the Unity engine and were not run there |
| `compile_check.sh`: shaders | 54 compilations passed (glslang; not Unity's compiler) |

The 758 cover, among others: the attack gesture and pointer routing (input), discipline
sequencing and resolution, the magical properties and combinations, the reaction scheduler and
chain reactions, persistence (save file, reload, checkpoints, forgetting a level), discovery
rules, the Broken Foundry's routes, layout and softlock exploration, and LM-08's presets, frame
pacing guard, diagnostics warnings and allocation tests.

**LM-08 tests (21):** preset names and frame-rate targets; the starting preset; the one-time
upgrade of older presets; every preset keeps the contour lines and shadows; the frame pacing guard
(kept 60 not lowered, unkept 60 lowered once after two slow windows, loading hitches ignored,
isolated hitches never decide a window, a long loading frame does not use up the warm-up, slow
windows must be consecutive, a new choice gives 60 another chance, 30 not watched, nonsense frame
times ignored, no allocation); the diagnostics warnings; and zero allocation per frame for the
reaction queue, the six example objects, the simulation step and a creature with armour and ward.
An allocation injected into the queue and into the property set was caught by these tests
(576,000 bytes each) before being removed. The allocation results hold for the .NET runner. In
Unity's own test runs up to 15 November 2026 they were inconclusive, because Unity's Mono does not count
allocated bytes; since then the tests read Unity's `GC.Alloc` recorder there (see the run of 15 November
below).

### Unity: Android builds on the laptop runner

| Run | Commit | Result |
| --- | --- | --- |
| 3 | `d0e21ea` (Sprint 11) | APK built, 54.6 MB |
| 4 | `353d92c` (`main` after LM-07) | **Failed.** Unity compiled every script, assembled the Broken Foundry (validation and level checks passed), then stopped at the Combat Arena: "The Arcane / Flame / Aegis discipline has no charged attack". Cause: `ChargedProjectileDefinition` and `ChargedAreaDefinition` were declared in `ChargedAbilities.cs`, and Unity links an asset to a ScriptableObject only through a file of the same name. Fixed in LM-08 |

| 5 | `b64de60` (LM-08 branch, with the fix) | **Succeeded** in 34 minutes on the runner (Unity's build step 26 minutes). The Broken Foundry, the Combat Arena and the Fantasy Playground were assembled and each passed validation (the foundry's level checks too); the charged abilities were regenerated with their scripts. 0 errors, 972 warnings (most from the Sentis package's shaders). APK artifact 55.4 MB (zip); Unity's report: 44.4 MB of user assets |

| 6 | `27284d7` (LM-08 branch, after the review fixes) | APK **built** (artifact 54.2 MB zip, build step 48 minutes). **First EditMode test run in Unity**: 878 tests, 870 passed, **3 failed**, 5 inconclusive (see below). The run is marked failed because of the tests |

| 7 | `076d823` (the fixes for run 6), tests only | **EditMode: 878 tests, 873 passed, 0 failed, 5 inconclusive** (the allocation tests). The run was marked failed only because Unity returns exit code 2 when a test is inconclusive and the script treated that as a failure; the script now decides from the count of failed tests and still fails on a crash or an incomplete run |

| 8 | `be1b39d` (LM-08 branch head), tests only | **Green.** EditMode: 878 tests, **873 passed, 0 failed**, 5 inconclusive (the five allocation tests, listed by name). 13 minutes |

| 9 | `ed638fa` (`main`, merge of PR #22), tests and APK | **Interrupted**, not a code failure: 12 minutes into the EditMode tests the log reads "The runner has received a shutdown signal" (the laptop or its runner service stopped) |
| 10 | `ed638fa` (`main`), tests and APK | **Stopped at once**: "The project is open in the Unity Editor". Run 9's Unity, cut off mid-run, had left `Temp\UnityLockfile` in the runner's copy of the project |
| 12 | `738ce5c` (`main`), tests and APK | Stopped at once, same cause |
| 13 | `11552e1` (`main` plus the scripts' handling of a lock left by an interrupted run), tests and APK | **Succeeded.** The scripts set the leftover lock aside (renamed, not deleted). EditMode: 878 tests, 873 passed, **0 failed**, 5 inconclusive. APK built in 6 minutes (the runner's import cache was warm). Artifact `threefold-android-development-13` (54.2 MB zip). The game code is the same as `main`'s |

| 15, 16 | `08e3720` (`main`, merge of PR #31) | Stopped by hand or replaced by a newer run before finishing; no result |
| 17 | `7776a69` (`main`, merge of PR #33), tests and APK | **Succeeded.** EditMode: **1,149 tests, 1,149 passed**, 0 failed, 0 inconclusive (the allocation tests now measure in Unity). APK built, 0 errors, 1,010 warnings (almost all from the Sentis package's shaders); artifact 60 MB (zip). Unity compiled `Threefold/Illustrated Lit` for GLES3 and Vulkan. **The Wayfarer look was not in this APK**: see below |
| 18 | `5fb4867` (the Wayfarer tests, no fix), tests only | 1,168 tests, 1,164 passed, **4 failed**: the four new `MageWayfarerLookTests`, as intended. Their messages gave the first cause |
| 19 | `a20ad34` (texture fix), tests only | 1,168 tests, 1,165 passed, **3 failed**. The texture test passed; the other three gave the second cause |
| 20 | `ca670a9` (both fixes), tests only | **Green.** 1,169 tests, **1,169 passed**, 0 failed, 0 inconclusive |
| 21 | `4846dd0` (Reference Courtyard and visual-review build), tests and review renders | 1,206 tests, 1,205 passed, **1 failed**: a new test allowed the Mage no height above the ground, where the rig sets him 0.1 m up (a fault of the test). **First pictures drawn by Unity**: six review renders of the Reference Courtyard (Direct3D 11, Intel graphics, Editor quality level `PC`). The first picture showed one texture on every surface; the others were right |
| 22 | `dba0ada` (ridges, vines, slabs, render warm-up, test corrected), tests, renders and APK | **Green.** EditMode: **1,206 tests, 1,206 passed**, 0 failed, 0 inconclusive. Six renders, all right. APK built with 0 errors (artifact `threefold-android-development-22`, 62 MB zip): **six scenes** (Fantasy Playground first, Combat Arena, Broken Foundry, Style Lab, Main Menu, Reference Courtyard); the build log reads "Build stamp: commit dba0ada, run 22"; no warning that the Wayfarer look is unused. Reference Courtyard environment: 12,962 triangles |
| 23 | `f65ffae` (measurement tooling only), review renders | Six renders and `metrics.txt` of the Reference Courtyard with the materials and lighting as they were: the "before" of `Docs/MaterialCohesion.md` |
| 24 | `b085042` (material cohesion and lighting), tests, renders and APK | **Green.** EditMode: **1,217 tests, 1,217 passed**, 0 failed, 0 inconclusive. The log reads "13 material(s) of the style were brought to revision 2" (once) and "Brought the lighting presets ... to revision 2" for each scene. Six renders and metrics: the "after". APK built with 0 errors, six scenes (artifact `threefold-android-development-24`, 62 MB zip); build stamp "commit b085042, run 24". Unity compiled the changed `Threefold/Illustrated Lit` for GLES3 and Vulkan without errors: 1.70 MB and 0.90 MB before compression, against 1.69 MB and 0.89 MB in run 17 |
| 25 | `49375d2` (render tool: poses and three views of the character), renders | The "before" of the Wayfarer's second pass: the first model from the new viewpoints. The log said the Mage was posed; the pictures showed the rest pose |
| 26 | `eedc239` (Wayfarer, second pass), tests, renders and APK | **Green.** EditMode: **1,219 tests, 1,219 passed**. The rebuilt model was given the Humanoid mapping of `Mage.fbx` again and accepted; no fallback to the mesh of `Mage.fbx` in tests or build. Unity counts 17,494 triangles and 2 materials on it. APK built with 0 errors (artifact `threefold-android-development-26`); build stamp "commit eedc239, run 26" |
| 27 | `1379561` (render tool: the pose is checked), renders | The bones were posed and the pictures still showed the rest pose: the meshes had not been skinned again |
| 28 | `e86c8c4` (render tool: skin again for every render), renders | **The Mage is drawn posed** with Idle, Jog and Flame_Hook on the rebuilt Wayfarer model: the first pictures from Unity of the model following the game's clips |
| 29 | `f27069c` (camera and controls feel, first pass), tests and APK | **Green.** EditMode: **1,244 tests, 1,244 passed**, including the 25 cases of `CameraFeelTests`. APK built (artifact `threefold-android-development-29`, 62 MB zip). The Feel panel and the recentering have not been tried on a device |
| 30 | `ce071ef` (combat polish, the Flame hook), tests and renders | **Green.** EditMode: **1,259 tests, 1,259 passed**. The existing animation set was brought to version 3 (one discipline changed); `FlameImpactBurst_Hovl` was adapted from the pack's explosion and passed the budget test. **First frame strips of a strike**: the hook drawn by Unity through the real animation model and Animator Controller, 26 frames before and 26 after (`Docs/CombatPolish.md`) |

Later runs are recorded below as they complete.

### Unity: why the Wayfarer look was missing from the build (runs 17 to 20)

Run 17's log said "The Wayfarer look is not used: its painted texture is missing", and every test passed,
because falling back to the mesh of `Mage.fbx` is the designed behaviour. `MageWayfarerLookTests` were
written to fail in that case and to say what Unity made of the files; they ran before any fix (run 18).

| Cause, as Unity reported it | Fix | Verified |
| --- | --- | --- |
| `MageWayfarer_Paint.png` was imported as a **Cubemap** (the `.meta` Unity wrote: `generateCubemap: 6`, `textureShape: 2`). The file is committed with a `.meta` that holds only its identifier; Unity 6000.6 reads that as importer settings of version 1 and upgrades them, which gives a cubemap's shape. The character postprocessor set every other property, not the shape. A cubemap is not a `Texture2D`, so the setup found no painting. The same import logged "Error assigning CUBE texture to 2D texture property" and an infinite import loop | `CharacterModelPostprocessor` sets the shape to 2D (version 5, so every computer imports the files again). The report now says what a file was imported as | Run 19: the texture is a `Texture2D`; no cubemap error, no import loop |
| Unity's automatic Humanoid mapping of `MageWayfarer.fbx` took the extra bone `face_blink` for the right eye, which `Mage.fbx` does not map. `MageLookRules` refuses a differing mapping | `MageCharacterSetup.AlignWayfarerMapping` gives the model the mapping of `Mage.fbx` bone for bone, on its own skeleton, and imports it again. The rule is unchanged | Run 20: valid Humanoid, mappings equal, aligned once and not again; the prefab was rebuilt with the Wayfarer look and the Mage of the assembled playground wears that mesh with that painting |

Generated assets left on the runner by earlier runs did not cause this. One was affected by it: the material
`Mage_Paint` had been generated while the texture was a cubemap and held no painting; the setup's existing
repair gave it the painting in run 19 ("Gave the material 'Mage_Paint' its painted texture, which it lacked").
Nothing was deleted or regenerated by hand.

**Not verified by these runs:** how the Wayfarer look appears and moves. The tests prove which mesh, avatar
and texture are assigned, not that the result looks right. In run 20 the setup also turned the Wayfarer model
by 180 degrees to face forward when it rebuilt the prefab; whether it faces and animates correctly must be
seen in the game. No APK has been built from these commits yet.

### Unity: EditMode tests on `main`, 15 November 2026 (reported by the owner)

1,119 tests: 1,102 passed, **7 failed**, 10 inconclusive. The results file was not available to the
cloud session; the causes below were found from the failure messages and the code, and five of the seven
failures were reproduced outside Unity once the plain runner behaved like NUnit (see the last row).
**The fixes have not run in Unity yet.**

| Test | Kind | Cause | Fix |
| --- | --- | --- | --- |
| `BrokenFoundryAssemblyTests.AssemblingTheFoundryTwiceGivesTheSameScene` (a Light cannot be removed while `UniversalAdditionalLightData` depends on it) | Defect in the setup tooling | The first pack adaptation in `MagicVfxSetup` (`Generated/VFX/Adapted`) removed lights with a plain `DestroyImmediate`. The two Free Slash slashes carry a Light with URP's additional light data, which requires its Light: Unity refuses, logs an error and leaves the light in the effect. The error fails whichever test first generates the effects | It now removes lights the way `MagicVfxPackAdapter` already did: the components that require the Light first |
| `MagicVfxSetupTests.EveryMagicEffectIsWithinTheAndroidBudget` (`AegisSlash_Hovl`: 2 Built-in materials, 6 systems, up to 288 particles) | Defect in the setup tooling | The Aegis swing was the one presentation slot the cue-timed wiring never replaced, so it still held the first adaptation of Hovl's `Charge slash blue`. That path hides systems over its limit by deactivating their objects, which leaves their emission, their Built-in materials and their particle limits in place, and it caps each system at 48 particles, not the effect | The same pack effect is adapted by `MagicVfxPackAdapter` as a burst (at most 3 systems, 48 particles in all, URP copies of the materials) and replaces the old one in the slot. The budget is unchanged |
| `MagicVfxSequencerTests`, four tests (expected 0 or 1, was 1, 2 or 3) | Test defect | The class keeps its command list in a field. NUnit uses one object of a class for all its tests, so each test started with the commands of the one before | The list is cleared before every test |
| `MartialStylesTests.RandomPlayNeverLeavesAnAttackPlayingOrTheMageSlowed(23)` (expected more than 20 attacks, was 15) | Wrong assumption in the test | Every stuck-state assertion passed. The last line assumed 40 seconds of random play always start more than 20 strikes; random play holds the guard or has the hands busy for much of that time, and over 60 seeds the count runs from 9 to 43 (seed 23: 15, deterministically, on .NET as in Unity). The test had never run: the plain runner could not call a test with `[Values]`, and Unity had not run this suite | The play goes on, up to 120 seconds, until more than 20 strikes have started. The threshold and every assertion are unchanged |
| 10 inconclusive allocation tests | Limit of the measurement | Unity's Mono returns 0 from `GC.GetAllocatedBytesForCurrentThread`. An eleventh test, `DiscoveryTests`' allocation test, had no check for that and passed in Unity without measuring anything | `AllocationMeter`: bytes on .NET, the profiler's `GC.Alloc` recorder in Unity; inconclusive only where neither works. All eleven use it |
| The plain runner of `compile_check.sh` | Tooling | It made a new object for every test and could not call tests with `[Values]`, so it could not show the five test-side failures above | One object per class and one test per value, as NUnit does; inconclusive and ignored tests are counted apart |

### Unity: EditMode tests (run 6, the first time they ran in Unity)

878 tests in 96 seconds: 870 passed, 3 failed, 5 inconclusive. The 5 inconclusive are the LM-08
allocation tests: Unity's Mono does not count allocations, as expected. The 3 failures were all in
tests that need Unity and had never run before; none was in game code:

| Test | Cause | Fix |
| --- | --- | --- |
| `BrokenFoundryAssemblyTests.AssemblingTheFoundryTwiceGivesTheSameScene` ("A tuned value is kept: expected 7, was 25") | The test tuned the hazard without saving, and the setup opens the scene from disk. The setup itself keeps an existing hazard | The test saves the scene after tuning, as a person would |
| `BrokenFoundryAssemblyTests.FoundryIsCompleteConnectedAndValid` ("Checkpoint 1: the Mage fits there") | The first checkpoint's respawn point is the level's start, where the Mage stands; the test counted the Mage's own collider. The other five checkpoints passed | The test ignores the Mage's colliders and names anything else in the way |
| `MagicalObjectSetupTests.EveryExampleIsGeneratedCompleteAndValid` ("ArcaneCrystal: a reaction to linked targets only, but none listed") | The validator (LM-04) applied a rule for placed objects to the crystal's prefab template, whose links are given where it is placed | The rule skips prefab assets; a placed crystal without links is still reported (`AnIdentifierWithNothingToSaveAndABrokenLinkAreReported` covers it) |

### Unity: the visual-review build and the Reference Courtyard (runs 21 and 22)

Checked by test in Unity (`ReferenceCourtyardAssemblyTests`): every scene of the development build is
assembled, valid, saved and loads; each but the main menu has the diagnostics with the scene list; the
courtyard holds what its layout says, has no transparent material, one light, no particle system, and
12,962 triangles; the Mage in it wears the Wayfarer mesh, the painted atlas and the style's material; the
game's camera has the values the composition tests assume.

Seen in renders (Editor renders, `Docs/VisualReferencePrototype.md`): the composition, the painted stone,
the haze, the Wayfarer look and its contour line, the Mage facing forward.

**Not verified:** the APK has not been installed. The scene list's buttons and the four-finger tap have not
been tried with touch. The build line has been read in the build log, not on a screen. Nothing has been
measured on a device. In the renders the Mage stands in the model's rest pose.

### Not tested

- Play Mode: there are no Play Mode tests. Nothing frame-by-frame in a scene is covered by an
  automated test.
- On a device: nothing from LM-01 to LM-08 has been played, timed or measured on a phone.
- The graphics menu, the frame pacing guard on a real phone, the nested canvases, and the
  diagnostics' memory and CPU/GPU readings have not been seen working.

## Device checklist

Use a development build (diagnostics available). Note the phone model, Android version, preset and
battery level at the start. For each line: pass, fail, or a note.

**Movement and camera**
- [ ] The joystick responds at once, with no drift at rest; sprint starts past the ring.
- [ ] Dragging anywhere free turns the camera without a felt delay; it stops when the finger stops.
- [ ] A drag that starts on a button never turns the camera.
- [ ] The camera does not clip into walls in the foundry's stair and culvert.

**Attack responsiveness**
- [ ] A quick tap strikes every time; repeated taps give the full combo (three strikes for Arcane
      and Flame, two for Aegis).
- [ ] A tap is never taken for a charge, and a short hold never fires a tap.
- [ ] BLOCK held cancels a charge without firing it.

**Charged aiming**
- [ ] Holding about a quarter of a second starts the charge; the ring fills.
- [ ] Dragging aims precisely enough to hit a crystal at medium distance with the Arcane Arrow.
- [ ] Releasing over the red zone cancels.

**Discipline switching**
- [ ] Drag from the indicator to a slot switches on release; tap then tap also works.
- [ ] Switching while moving works and never moves the camera.

**Environmental interactions**
- [ ] Flame heats the barricade until it burns; Aegis force shatters heated stone; Arcane charge
      wakes the crystal; the Aegis guard braces the gantry.
- [ ] Each response can be read without colour alone (movement or shape as well).

**Chain reactions**
- [ ] A fire spreads along burnable objects and stops; the frame rate holds while it does
      (diagnostics: frame time, warnings).
- [ ] No critical-reaction warning appears in the diagnostics during the foundry.

**Puzzle progression**
- [ ] Each of the foundry's three exits can be completed; checkpoints restore the right place.
- [ ] Closing and reopening the app keeps lasting changes and resets temporary ones.

**Graphics presets**
- [ ] The Graphics button opens the menu; each preset applies at once; the choice survives a
      restart of the app.
- [ ] All three presets keep the painted look and the contour lines.
- [ ] Touches on the menu do not move the Mage or the camera.

**FPS and thermal behaviour**
- [ ] Balanced: note the average and slowest-5% frame time in each foundry area and in a fight.
- [ ] After 15 minutes on Balanced: was the target lowered to 30 (diagnostics)? How warm is the
      phone? Any stutter?
- [ ] Repeat on Performance: steady 30?

Record results in a new section below, with the build's run number and commit.
