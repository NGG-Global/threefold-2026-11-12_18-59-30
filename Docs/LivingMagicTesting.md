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
(576,000 bytes each) before being removed. The allocation results hold for the .NET runner; in
Unity's own test run they are inconclusive, because Unity's Mono does not count allocations.

### Unity: Android builds on the laptop runner

| Run | Commit | Result |
| --- | --- | --- |
| 3 | `d0e21ea` (Sprint 11) | APK built, 54.6 MB |
| 4 | `353d92c` (`main` after LM-07) | **Failed.** Unity compiled every script, assembled the Broken Foundry (validation and level checks passed), then stopped at the Combat Arena: "The Arcane / Flame / Aegis discipline has no charged attack". Cause: `ChargedProjectileDefinition` and `ChargedAreaDefinition` were declared in `ChargedAbilities.cs`, and Unity links an asset to a ScriptableObject only through a file of the same name. Fixed in LM-08 |

| 5 | `b64de60` (LM-08 branch, with the fix) | **Succeeded** in 34 minutes on the runner (Unity's build step 26 minutes). The Broken Foundry, the Combat Arena and the Fantasy Playground were assembled and each passed validation (the foundry's level checks too); the charged abilities were regenerated with their scripts. 0 errors, 972 warnings (most from the Sentis package's shaders). APK artifact 55.4 MB (zip); Unity's report: 44.4 MB of user assets |

| 6 | `27284d7` (LM-08 branch, after the review fixes) | APK **built** (artifact 54.2 MB zip, build step 48 minutes). **First EditMode test run in Unity**: 878 tests, 870 passed, **3 failed**, 5 inconclusive (see below). The run is marked failed because of the tests |

Later runs are recorded below as they complete.

### Unity: EditMode tests (run 6, the first time they ran in Unity)

878 tests in 96 seconds: 870 passed, 3 failed, 5 inconclusive. The 5 inconclusive are the LM-08
allocation tests: Unity's Mono does not count allocations, as expected. The 3 failures were all in
tests that need Unity and had never run before; none was in game code:

| Test | Cause | Fix |
| --- | --- | --- |
| `BrokenFoundryAssemblyTests.AssemblingTheFoundryTwiceGivesTheSameScene` ("A tuned value is kept: expected 7, was 25") | The test tuned the hazard without saving, and the setup opens the scene from disk. The setup itself keeps an existing hazard | The test saves the scene after tuning, as a person would |
| `BrokenFoundryAssemblyTests.FoundryIsCompleteConnectedAndValid` ("Checkpoint 1: the Mage fits there") | The first checkpoint's respawn point is the level's start, where the Mage stands; the test counted the Mage's own collider. The other five checkpoints passed | The test ignores the Mage's colliders and names anything else in the way |
| `MagicalObjectSetupTests.EveryExampleIsGeneratedCompleteAndValid` ("ArcaneCrystal: a reaction to linked targets only, but none listed") | The validator (LM-04) applied a rule for placed objects to the crystal's prefab template, whose links are given where it is placed | The rule skips prefab assets; a placed crystal without links is still reported (`AnIdentifierWithNothingToSaveAndABrokenLinkAreReported` covers it) |

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
