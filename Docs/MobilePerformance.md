# Mobile performance (LM-08)

What was inspected for Android, what was changed, what was measured and what was not. The
targets below are goals, not claims: **no frame rate, frame time, temperature or memory figure
has been measured on a device for LM-01 to LM-08.** Numbers in this document are configuration
values read from the project, sizes reported by Unity's own build, or allocation counts from
tests; each says which.

## Targets

| Target | Means |
| --- | --- |
| 60 fps on capable devices | Balanced and Quality presets aim for 60 (`Application.targetFrameRate`) |
| Stable 30 fps fallback | Performance aims for 30; a 60 target that the phone cannot keep is lowered to a steady 30 for the session (`FramePacingGuard`) |
| Responsive movement and camera | Input is read and applied in the same frame; no smoothing added (see Input) |
| Stable during combat and chain reactions | The magic's per-frame work is bounded by budgets and allocates nothing (tests below) |

## What changed in LM-08

| Change | Why | Where |
| --- | --- | --- |
| **Frame-rate targets.** Each graphics preset sets one: Performance 30, Balanced 60, Quality 60 | Nothing set `Application.targetFrameRate` before, so Android ran at Unity's platform default. Unity's documentation gives 30 fps as that default on Android; it was not measured here | `GraphicsPresets`, `VisualStyleController.ApplyFrameRate` |
| **Frame pacing guard.** On a phone, a 60 target is watched in 5-second windows after a 3-second warm-up; after two windows in a row averaging more than 15% over 16.7 ms it is lowered to 30 for the session. A frame longer than two target frames counts as two, so isolated hitches (a shader compiled on first use, a loading frame) neither decide a window nor use up the warm-up | An even 30 is easier to play than a rate that wavers between 40 and 60. It never steps back up by itself; choosing a preset again gives 60 a new chance | `FramePacingGuard` (plain C#, 9 tests) |
| **Player-facing presets.** Performance, Balanced, Quality, chosen from a Graphics button in every styled level, kept between launches | The brief asks for player selection | `GraphicsMenu`, `GraphicsMenuSetup`, `PlayerPrefs` key `Threefold.GraphicsPreset` |
| **Contour lines kept on Performance** (were off on Low) | The Art Bible's illustrated identity depends on them; they cost one extra draw of the few outlined meshes | `VisualQualityPreset.Low()`, upgraded once in existing scenes |
| **Nested canvases** for the HUD bars, the joystick and the ATTACK button | Each moves its graphics on every frame while in use; a change inside a canvas rebuilds the whole canvas. A canvas of their own limits the rebuild to them | `CombatSetup.GiveBarsTheirOwnCanvas`, `TouchControlsSetup.GiveMovingPartsTheirOwnCanvas` |
| **Development diagnostics** extended and added to every level | FPS, frame time, CPU and GPU time where reported, approximate memory, garbage collections, enemies, projectiles, magical states, reaction queue, warnings | `DevDiagnosticsOverlay`, `PerformanceWarnings`, `DiagnosticsSetup` |
| **Android frame pacing settings** shown before they are changed | Optimized Frame Pacing (Swappy) and Frame Timing Stats are off. Project settings are not changed without the owner seeing them first (CLAUDE.md) | **Threefold > Validate > Android Performance Settings** |
| **Charged ability scripts in their own files** | Found by the first Unity build of LM-01 to LM-07: the Arcane Arrow, Flame Wave and Force Pulse assets were saved without their script. A correctness fix, listed here because the build found it | `ChargedProjectileDefinition.cs`, `ChargedAreaDefinition.cs`; now checked by `check_unity_repo.py` |

Nothing in the game's rules changed. No reaction is dropped, delayed or reordered to save time.

## The graphics presets

| | Performance | Balanced | Quality |
| --- | --- | --- | --- |
| Frame-rate target | 30 | 60 | 60 |
| Render scale | 0.7 | 0.85 | 1.0 |
| Shadows | Hard, 22 m, 512 | Soft, 35 m, 1024 | Soft, 50 m, 2048 |
| Anti-aliasing (MSAA) | Off | 2x | 4x |
| Post-processing, HDR | Off | Off | On |
| Contour lines | On | On | On |
| Painted materials, illustrated shader, lighting | Same in all three | | |

The starting preset: one chosen earlier in the run, then the player's saved choice, then the
device's suggestion (`SuggestLevel`: under 3.5 GB of memory or an old graphics level gives
Performance; other phones Balanced; Quality is never chosen automatically on a phone), then
Balanced. These values are proposals carried over from Sprint 11 and have not been measured.

## Audit

Each area: what the project does now (read from the code and settings), and what remains open.
"Checked by test" means an automated test covers it; everything else was read, not measured. The
allocation tests count through `AllocationMeter`. In the .NET 8 runner of `compile_check.sh` it reads
`GC.GetAllocatedBytesForCurrentThread`. Unity's Mono runtime returns 0 from that counter whatever is
allocated (the tests were inconclusive in every Unity run up to 15 November 2026 for that reason), so in
Unity the meter reads the profiler's `GC.Alloc` recorder instead, the counter behind Unity's own
`Is.Not.AllocatingGCMemory` constraint. That path was written without Unity at hand and has not run yet:
until a Unity run shows these tests passed, treat their Unity result as unknown. Where neither counter
works a test stays inconclusive; it is never reported as passed.
Neither runtime is IL2CPP: the tests show that the C# allocates nothing, which IL2CPP does not
change, but they are not a measurement on the device.

| Area | Found | Open |
| --- | --- | --- |
| **URP renderer** | `Mobile_RPAsset`: forward renderer, no renderer features, native render pass on, SRP Batcher on, dynamic batching off, render scale 0.8 (the preset overrides it), HDR allowed, MSAA off. The style controller sets render scale, MSAA, HDR, shadows and post-processing at runtime and puts them back afterwards | Volume framework update mode is Every Frame; Via Scripting would save a little CPU but needs every volume change to be pushed by code. Not changed |
| **Lighting and shadows** | One directional sun with shadows, one cascade, no additional-light shadows, up to 4 additional lights per object. No baked lighting. The Android build on the runner turns on soft shadows in `Mobile_RPAsset` (the style's proposed change, made only in automation) | Soft shadows cost more than hard ones on a phone; Performance uses hard. No light probes or lightmaps |
| **Materials and shaders** | One illustrated lit shader with five passes (ForwardLit, Outline, ShadowCaster, DepthOnly, DepthNormals); 54 keyword variants compile in the shader check. The outline is a second draw of selected meshes only | Shader variant count in the real build, and GPU cost per pass, are not measured |
| **Transparency and overdraw** | Transparent, unlit materials for effects: additive impact bursts and glows, and alpha-blended enemy attack warnings (telegraph areas). No full-screen transparent layers except post-processing (Quality only) | Overdraw where several bursts and a telegraph overlap close to the camera is a likely GPU peak; not measured |
| **Particle systems** | Pooled, with caps per kind (for example 12 impact effects, a ceiling per spell for projectiles). `maxParticles` set from the burst size | No global particle budget across all kinds |
| **Animation** | The Mage's Animator uses Cull Update Transforms. Enemies are animated by code (`EnemyPresentation`), without Animators | — |
| **Physics** | Fixed step 0.02 s (50 Hz). Every query uses the non-allocating forms. Projectiles sweep a sphere instead of using Rigidbodies; moving platforms and pushes are controlled glides. The builds report generated meshes on Mesh Colliders without pre-baked collision (47 in run 3, 113 in run 5 with the foundry and arena); Unity still bakes them at build time today and warns that a future version will not | Box colliders for the box-shaped blocks would be cheaper and remove the warning; not changed, because it alters generated scenes and needs a check in Unity |
| **AI** | Decisions in plain C# (`EnemyBrain`). Perception every 0.2 s near the player and 0.6 s further away, with a random offset so enemies do not all think in one frame. One `EnemyDirector` | — |
| **Projectiles** | Pooled; one sphere sweep per frame each; no Rigidbody. The diagnostics count those in flight | — |
| **Magical property updates** | No magical object has an update of its own. One `WorldSimulation` advances at most 32 objects with temporary state per frame; idle objects cost nothing. **Checked by test: the six example objects allocate 0 bytes per frame while taking effects and timing.** | If more than 32 objects stay restless indefinitely, the ones waiting are never advanced (by design, their state stands still; nothing is lost). Not seen in any current level, which have fewer magical objects than that |
| **Chain reaction scheduler** | Depth 3, 8 reactions per frame, queue of 64 per priority; critical reactions are never refused (the queue grows for them and the overflow is counted). **Checked by test: offering, refusing and carrying out reactions allocates 0 bytes once warm.** Determinism is covered by the LM-04 tests (`TheSameChainHappensTheSameWayEveryTime`) | The budgets are provisional, not measured |
| **Mobile UI** | Text is set on events, not every frame. Moving parts now have their own canvases (see above). The diagnostics overlay uses IMGUI and allocates while shown; it exists only in development builds | — |
| **Touch input** | See Input below | — |
| **Garbage collection** | Incremental GC on. No LINQ in runtime code. **Checked by test: the frame pacing guard, the reaction queue, the property set, the simulation step and the creature effect model allocate nothing per frame.** The overlay shows collections per 10 s and warns when more than one | Allocation in MonoBehaviours (Unity callbacks, physics callbacks) cannot be counted outside Unity; use the Profiler on a device |
| **Asset memory** | From the LM-08 build (run 5), Unity's build report: user assets 44.4 MB uncompressed (textures 2.9, meshes 4.4, animations 0.3, shaders 6.9, levels 1.8, other 27.5); APK artifact 55.4 MB. The largest single file is a Sentis compute shader (19.6 MB); the Sentis package (`com.unity.ai.inference`), which no Threefold code uses, adds 482 files, about 28.6 MB uncompressed. 113 generated meshes are on Mesh Colliders without pre-baked collision | Removing that package would make the APK smaller and builds faster. It is a package change, so it needs the owner's agreement (CLAUDE.md); not done |

### Input

The touch-to-action pipeline, in the order Unity runs it each frame:

| Order | Step |
| --- | --- |
| -200 | `ControlSchemeSelector` notices which device was used last |
| -100 | Input sources read Enhanced Touch and the devices: joystick, camera drag, buttons, the attack gesture |
| -50 | `TraversalController`, `PlayerDisciplines` (discipline switching) |
| 0 | Motor and ability input: movement, attack, charge, aim, release |
| 100 | `CharacterAnimationDriver` |
| LateUpdate | `ThirdPersonCamera` applies the look delta read this frame |

| Part | Value | Effect on latency |
| --- | --- | --- |
| Frame rate | 30 before LM-08 by default, now 60 on Balanced and Quality | The largest lever: at 60 every step happens 16.7 ms after the touch instead of up to 33.3 ms |
| Joystick | Dead zone 0.12, response exponent 1.4 | Shapes the response; adds no delay |
| Camera drag | Delta in screen heights, applied in the same frame | No delay |
| Camera rotation smoothing | `rotationSmoothTime` 0.04 s | The one deliberate lag: about 40 ms to catch up while dragging. Not changed without a device test. Setting it to 0 on the camera gives direct look |
| Camera follow | 0.06 s horizontal, 0.18 s vertical | Smoothing of the camera's position, not of input |
| Tap versus hold | Hold threshold 0.22 s; a drag over 14 dp starts the charge at once | A tap fires on release, as designed; a charge starts at the threshold |
| Charged attack | Full charge 0.9 to 1.1 s by discipline | Game design, not latency |
| Discipline switching | Same frame as the selection | No delay |

No smoothing was added in LM-08.

## Android settings shown, not changed

**Threefold > Validate > Android Performance Settings** lists these and asks before changing them:

| Setting | Now | Proposed | Why |
| --- | --- | --- | --- |
| Optimized Frame Pacing (Android) | Off | On | Times each frame to the display, for an even 30 or 60 |
| Frame Timing Stats | Off | On | CPU and GPU time per frame in the diagnostics (Unity may report them in development builds without it; not verified) |

Neither needs a package. Their effect on this game has not been measured. After changing them on
the laptop, commit `ProjectSettings/ProjectSettings.asset`.

## Development diagnostics

Development builds only (it switches itself off otherwise). F3, back-quote, gamepad Select or a
short three-finger tap. Its **Scenes** button lists every scene of the build and loads the one chosen;
**Next scene**, F4, or a short four-finger tap while the diagnostics are shown load the next one. The
gestures are taps of at most 0.7 s, so playing with several fingers on the screen changes nothing
(`DevSceneSwitcher`, tested outside Unity; not yet tried on a device). It shows:

- which build this is: the version, the commit, the number of the build run, the branch and the build
  time (`BuildStamp`). The build writes them into the game; a build started by hand has no run number and
  says so, a build without the information says "not stamped", and the Editor says "Editor session".
  Written and tested outside and inside Unity (`BuildStampTests`); **not yet seen in a build**;
- frames per second, average, slowest 5% and worst frame time, the preset and the target, and
  whether the target was lowered;
- main-thread CPU and GPU time of the last frame where Unity reports them;
- Unity's allocated and reserved memory, the managed heap, the device's memory, and garbage
  collections (all approximate, from Unity's profiling interface);
- enemies alive, projectiles in flight, objects with temporary states;
- warnings (`PerformanceWarnings`, thresholds are rules of thumb): frames slower than the target,
  stutter, a lowered target, frequent garbage collections, critical reactions beyond the queue's
  capacity, refused non-critical reactions, the simulation at its limit, many projectiles;
- the discipline, the magical object in view, temporary states, the reaction queue and the
  level's progress, as in LM-07.

## Budgets (provisional)

| Budget | Value | Status |
| --- | --- | --- |
| Frame time | 16.7 ms (60) or 33.3 ms (30) | Target |
| Reaction depth, reactions per frame, active objects | 3, 8, 32 | Provisional (LM-04) |
| Reaction queue | 64 per priority; critical never refused | Provisional |
| Projectiles in flight before a warning | 24 | Rule of thumb |
| Allocation per frame in the magic's rules | 0 bytes | Checked by test |

## How to measure on a device

1. Install the development APK (see `Docs/CloudBuild.md`).
2. Three-finger tap to open the diagnostics. Note the frame time and the warnings in each level:
   standing still, running, in a fight, during a Flame Wave on the barricade, during the foundry's
   chain reactions.
3. Play for 15 minutes on Balanced and note whether the target was lowered, and how warm the phone
   is. Repeat on Performance.
4. For detail, connect the Unity Profiler to the development build over USB.

Record what was measured in `Docs/LivingMagicTesting.md`, with the device model.
