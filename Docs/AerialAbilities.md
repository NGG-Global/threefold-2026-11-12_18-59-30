# Aerial abilities (Sprint 4)

Each discipline replaces the generic second jump with an aerial ability of its own:

| Discipline | Ability | What it does |
| --- | --- | --- |
| Arcane | **Ascension** | A brief gathering, then a strong upward impulse: about 3 m above where it starts, twice and more the height of a jump |
| Flame | **Fire Glide** | While Jump is held, fire directed downward slows the fall to a capped glide and lets the joystick steer, from a limited fuel budget |
| Aegis | **Force Dash** | A short, fast horizontal dash where the joystick points (or where the Mage faces), stopped by walls |

**Status:** written; tested outside Unity with 36 tests (28 for the rules, covering every scenario of the brief
at 15 to 120 frames per second; 7 for the test course, checked against the motor, the traversal limits and the
tuning; 1 for multi-touch input); everything compiles against the Unity 6000.6.0f1 assemblies for the Editor
and with Android player defines. **Nothing has been played in Unity or on a device.** How the abilities feel
(the gather time, the glide's weight, the dash's speed), what the placeholder animations look like, and the
effects on screen can only be judged by playing. Expect tuning.

## How to use them

| Control | Touch | Keyboard / gamepad |
| --- | --- | --- |
| First jump | JUMP (unchanged: tap for a short jump, hold for a full one) | Space / South |
| Aerial ability | JUMP again, in the air | The same |
| Fire Glide | Hold JUMP; release to stop; press again to resume with the fuel left | The same |
| Dash direction | The joystick, pushed past 30% | The left stick or WASD |

No new buttons. The fixed joystick, the free camera drag and the separate JUMP and ATTACK buttons are
unchanged; a test holds the joystick, drags the camera and holds JUMP at the same time through the real
touch routing.

## Rules

The rules are one explicit state machine, `AerialAbilityModel` (plain C#): Idle, Ascending, Gliding, Dashing.

- **The first jump is the motor's own and is unchanged** (height, coyote time, jump buffer, variable height).
  An ability becomes usable only after the Mage left the ground by a jump; falling off an edge does not count.
- **A new press in the air** starts the ability. The press of the first jump, held, starts nothing, so holding
  JUMP for a full jump never triggers the glide. A press the ability uses is taken from the motor, so it
  neither jumps nor is remembered for a jump on landing.
- **One activation per airborne sequence**, whatever the discipline: switching in the air, however fast, never
  gives another. An ability under way finishes as the one it began as (a dash stays a dash if Flame is selected
  mid-dash).
- **A confirmed landing gives it back**: ground under the feet for 2 frames and 0.04 s in a row. A one-frame
  touch (brushing a ledge top, a flicker on a slope) and contact with a wall do not. A jump from the ground
  starts a new sequence. A jump off a ledge (traversal) makes the ability usable but does not give back a spent
  one.
- **Glide fuel** is spent only while propelling. Releasing JUMP keeps what is left; a new press in the same
  sequence resumes the glide while Flame is selected and at least 0.1 s of fuel remains. Resuming is not a new
  activation. Landing refills it.
- **No unlimited flight:** the ascension is a single impulse, over once the Mage stops rising; the glide never
  adds height (while rising it slows at least as fast as under gravity) and runs out of fuel; a dash is
  0.18 s long and holds a small, fixed lift.
- **Low frame rate:** the dash moves only for the time left on its last frame, so it covers 4.2 m at any frame
  rate; glide fuel is counted in seconds; the ascension's impulse is corrected for the frame time, so it adds
  3.0 m at 15 or 120 frames per second. (The first jump's height drifts with the frame rate, by about 0.2 m
  between 120 and 15 fps; that is the motor's existing behaviour and is unchanged.)

### With combat

| Situation | Behaviour |
| --- | --- |
| Charging a spell | No aerial ability starts. A charge started during a glide pauses it (fuel kept) |
| Blocking | No ability starts; blocking pauses a glide |
| Staggered (a hit of 10 or more in the last 0.35 s) | No ability starts; the hit pauses a glide |
| Defeated, hanging, climbing, vaulting | No ability; one under way ends |
| Attacking | An ability started during a swing that has not landed **abandons the swing** (no hit, no damage, its effects cancelled). While an ability uses the hands (the ascension's first 0.25 s, a glide while propelling, a dash) no attack starts; afterwards aerial attacks work as before |
| Projectiles and effects | Projectiles fly in the world once launched; effects attached to the hands follow the bones; nothing is spawned relative to the moving Mage after launch |

This follows the existing rule that climbing, hanging and vaulting make the hands busy
(`DisciplineCombatModel`), so no new combat rule was needed.

## Default values

All values are serialized on the `AerialAbilities` component of the Mage (Inspector) and start from
`AerialAbilityDefaults`. They are proposals until played.

| Group | Value | Default | Effect |
| --- | --- | --- | --- |
| Rules | `landingConfirmFrames` / `landingConfirmSeconds` | 2 / 0.04 s | How long the ground must be under the feet before the ability comes back |
| | `ledgeJumpRestoresAbility` | off | Whether jumping off a ledge gives back a spent ability |
| | `staggerDamage` / `staggerSeconds` | 10 / 0.35 s | A hit this strong blocks abilities for this long |
| Ascension | `height` | 3.0 m | Height of the impulse above where it starts (a jump is 1.5 m) |
| | `gatherSeconds` | 0.06 s | The gathering pause before the impulse; 0 for none |
| | `horizontalSpeedKept` | 0.85 | Horizontal speed kept at the impulse: mostly vertical, steering unchanged |
| | `handsBusySeconds` | 0.25 s | Time the hands are busy at the start |
| Glide | `fuelSeconds` | 1.6 s | Propulsion per airborne sequence |
| | `maxFallSpeed` | 2.4 m/s | Fastest fall while propelling |
| | `fallGravityScale` | 0.35 | Gravity share while falling slower than the cap |
| | `brakeAcceleration` | 45 m/s² | How quickly a faster fall is brought to the cap (about 0.3 s from 15 m/s) |
| | `riseGravityScale` | 1.0 | Gravity share while still rising: 1 or more, so propulsion never adds height |
| | `speed` / `steerAcceleration` | 7.5 m/s / 16 m/s² | Glide speed and steering (sprint is 7 m/s) |
| | `minimumToResume` | 0.1 s | Fuel needed to resume after a release |
| Dash | `distance` / `seconds` | 4.2 m / 0.18 s | About 23 m/s |
| | `inputThreshold` | 0.3 | Joystick deflection that chooses the direction; below it, the facing |
| | `verticalSpeed` | 0.4 m/s | Lift held during the dash |
| | `exitSpeed` | 6 m/s | Speed kept after it, set outright (never added), so speed cannot build up |
| | `stopAngle` | 50° | A wall met more head-on stops the dash; a more glancing one turns it along the wall |
| Camera | `cameraFeedback` | 1 | 0 to 1; 0 turns the camera response off |
| | `ascensionTrauma` / `dashTrauma` / `wallTrauma` | 0.06 / 0.1 / 0.12 | Light shake at the start of an ascension, a dash, and a dash stopped by a wall |
| Abilities | `abilityBySlot` | Ascension, Glide, Dash | Which discipline has which ability: data, not code |

What the defaults give (60 fps, from `AerialSimulator`, the motor's own gravity): a full jump rises 1.6 m and
carries 5.0 m at a sprint; Ascension at the top of a jump lifts the feet to 4.6 m; from a 4.2 m tower to a 1 m
platform a jump carries 6.5 m, a jump and a dash 10.5 m, an ascension 10.9 m, the glide 16.2 m; on level ground
a jump and a dash carry 9.1 m.

### How to adjust

- **More or less height:** `height` (Ascension). The impulse is worked out from the motor's gravity, so it stays
  right if gravity changes.
- **Floatier or heavier glide:** `maxFallSpeed` first, then `fallGravityScale`. Longer glides: `fuelSeconds`.
  Faster response when the glide starts during a fast fall: `brakeAcceleration`.
- **Longer or snappier dash:** `distance` and `seconds` (speed = distance / seconds). Less momentum after it:
  `exitSpeed`.
- **Less camera motion:** `cameraFeedback` (0 to 1) on the component. The camera already follows height over
  0.18 s and never lags more than 1.2 m, so the ascension and the dash do not snap the view.
- After changing a value that affects reach, run the tests: `AerialCourseLayoutTests` fails if the course no
  longer separates the abilities from a jump.

## How it works

```
input ──► AerialAbilities (runs before the motor) ──► AerialAbilityModel (plain C#)
              │  combat state, ground, walls                │ motion for this frame, events
              ▼                                             ▼
         ThirdPersonMotor.ApplyAerialMotion          AbilityEvent ──► AerialAbilityEffects (VFX)
                                                                  └─► camera feedback
         AerialMove (0–3) ──► CharacterAnimationDriver ──► Animator
         HandsBusy ──► DisciplineCombat (abandons a swing, starts no attack)
```

- `ThirdPersonMotor` gained a per-frame `ApplyAerialMotion` (a vertical speed, a stored or one-frame horizontal
  velocity, different air steering), `ConsumeJumpPress`, jump counters, and the wall the last move ran into.
  Its gravity step moved unchanged into `JumpPhysics.AirborneStep`, which the simulator shares. Collisions are
  the character controller's own: a dash is a swept move, so it cannot pass through a wall at any frame rate.
- `AerialSimulator` runs a jump with the real model and the motor's gravity step, so flights are tested outside
  Unity. It stands in for the character controller with flat ground and an optional wall.

## Animation

The Animator follows an integer, `AerialMove` (0 none, 1 ascension, 2 glide, 3 dash), set by the animation
driver every frame from the gameplay state. Each pose lasts exactly as long as its ability and returns to the
fall or the landing when the value goes back to 0, so no state can stay stuck and no trigger can be left
pending. The Mage's controller is rebuilt by **Threefold > Setup > Mage Prototype** (its version changed).

| Ability | State | Clip now | Wanted |
| --- | --- | --- | --- |
| Ascension | `AerialAscend` (base layer) | `NinjaJump_Start` at 1.3× (the shared leap) | A gathering crouch, both arms sweeping upward, the body extending into a vertical leap |
| Fire Glide | `AerialGlide` (own upper-body layer, over the fall) | `Spell_Simple_Idle_Loop` (hands held out) | Arms and hands directed downward, torso and legs answering the forward motion |
| Force Dash | `AerialDash` (base layer) | `Push_Loop` from its thrust (frame 37), at 2× | Arms drawn in, a compressed pose, a strong forward thrust, the body leaning into it |

### What needs new animation

All three poses above are **placeholders** from the existing library, not final. Each wants a clip made from
library motion with the martial-styles tool (`Tools/blender/martial_styles.py`), as the brief describes them.

## Effects

`AerialAbilityEffects` (on `CombatVisuals`) plays each discipline's aerial effects from the ability events,
pooled. The slots are in the discipline asset (Presentation, *Aerial ability*); the setup fills them.

| | Start | While it lasts | End |
| --- | --- | --- | --- |
| Arcane | `ArcaneAscendBurst`: a ring flat beneath the feet, a blue flash, a narrow cone rising | `ArcaneAscendRibbon`: a narrow ribbon and motes trailing the ascent | Fades |
| Flame | `FlameGlideIgnite` | `FlameGlideJet` on each hand: flames streaming straight down in world space (whatever the hands do), trailing behind as the Mage moves, with embers; only while propelling | Stops at once on release (0.06 s) |
| Aegis | `AegisDashBurst`: a compressed flash, a shockwave cone along the dash, a ring | `AegisDashTrail`: a short trail from the chest | A wall: `AegisDashImpact`, a ripple on the wall. Otherwise fades, nothing lingers |

They follow the Sprint 3 budget and quality levels (`Docs/MagicVfx.md`).

## Test course

**Threefold > Setup > Discipline Range** builds the aerial course beyond the arrow lane, from
`AerialCourseLayout` (change the numbers there, never the scene):

| Station | Size | A jump | The ability |
| --- | --- | --- | --- |
| Ascension pillar | 4.2 m high, standing at least 6 m from anything to jump from | Jump and ledge catch reach at most 4.06 m | Arcane Ascension at the top of a jump lifts the feet to 4.56 m or more |
| Glide tower and landing | Three 1.05 m steps up to a 4.2 m tower, a 12 m gap, a 1 m platform 6 m deep | 6.5 to 7 m | Only the glide crosses (about 16 m); a dash or an ascension reaches about 11 m |
| Dash gap | Two 1 m platforms, 7 m apart | About 5 m | A jump and a dash: about 9 m |
| Dash wall | A 2.6 m wall 3.5 m beyond a floor marker | - | A dash from the marker stops at the wall |

`AerialCourseLayoutTests` checks each station at 15, 30, 60 and 120 frames per second, that the steps can be
climbed, that nothing near the pillar can be jumped from, and that no block overlaps another or leaves the
floor.

## Tests

| Scenario of the brief | Test |
| --- | --- |
| Normal first jump | `TheFirstJumpIsTheMotorsOwnAndArmsTheAbility`, `HoldingTheFirstJumpStartsNothing`, `FallingOffAnEdgeGivesNoAbility` |
| Arcane vertical clearance | `AscensionClimbsWellAboveAJumpAtEveryFrameRate`, `AscensionGivesOneImpulseWhileJumpIsHeld`, `TheAscensionPillarIsBeyondAJumpAndALedgeCatchButNotBeyondAscension` |
| Flame sustained descent and directional glide | `TheGlideCapsTheDescentQuicklyAndNeverClimbs`, `TheGlideCarriesMuchFartherThanAJumpAndSteers`, `OnlyTheGlideCrossesFromTheTowerToTheLanding` |
| Flame release and re-press | `ReleasingKeepsTheFuelAndAPressResumesIt`, `ResumingNeedsFlameSelected` |
| Flame fuel exhaustion | `TheFuelRunsOutAndNormalGravityReturns`, `TheFuelBudgetIsTheSameAtAnyFrameRate` |
| Aegis dash distance | `ADashCoversItsDistanceAtEveryFrameRate`, `ADashGoesWhereTheJoystickPointsOrWhereTheMageFaces`, `TheDashCrossesTheDashGapAndAJumpDoesNot` |
| Aegis dash into a wall | `AWallStopsADashHeadOnAndTurnsAGlancingOne`, `TheDashWallStopsADashFromItsMarker` |
| Landing resets | `AConfirmedLandingGivesTheAbilityBackForTheNextJump`, `AOneFrameTouchOfGroundOrAWallDoesNotGiveTheAbilityBack`, `AJumpOffALedgeDoesNotGiveASpentAbilityBack` |
| Rapid discipline switching | `SwitchingDisciplinesInTheAirNeverGivesAnotherUse`, `AnAbilityInProgressFinishesAsTheOneItBeganAs` |
| Jump-button spam | `OneActivationPerAirborneSequenceHoweverOftenJumpIsPressed`, `ADashNeverStacksSpeed` |
| Attack interruption | `AnAerialAbilityAbandonsASwingThatHasNotLanded` (with the real combat rules), `TheHandsAreBusyWhileAnAbilityUsesThem`, `NothingStartsWhileChargingBlockingStaggeredOrClimbing`, `AChargeOrAHitPausesTheGlideAndKeepsItsFuel` |
| Low frame rate | `EveryAbilityBehavesTheSameAtLowFrameRates`, and every flight test at 15 to 120 fps |
| Mobile multi-touch | `MultiTouchRoutingTests.AGlideHeldOnJumpSurvivesMovingAndTurningTheCameraAtTheSameTime` |
| No allocation | `AerialAbilityTests.SteadyFramesAllocateNothing` |

Not tested outside Unity: the motor with the character controller (collisions, slopes, steps), the
Animator, the effects, and the component wiring. Play Mode tests for movement do not exist yet in this
project.

## Laptop actions

1. Pull `main`.
2. Run **Threefold > Setup > Mage Prototype** (rebuilds the Mage's Animator with the aerial states), then
   **Threefold > Setup > Disciplines** in each scene with the Mage (adds `AerialAbilities` and the aerial
   effects), then **Threefold > Setup > Discipline Range** (builds the aerial course). Save the scenes.
3. Play the Discipline Range: walk to the far end, past the arrow lane. Try each station with its discipline,
   and each ability on flat ground; then the checks below.
4. Commit the regenerated files.

## Review in Unity and on a device

- [ ] Normal jumps feel as before (tap and hold heights, coyote jumps, jumping just before landing).
- [ ] Arcane: a second press lifts the Mage onto the pillar; holding JUMP gives no second lift.
- [ ] Flame: holding JUMP from the tower glides onto the landing; releasing drops at normal speed; pressing again
      resumes; the fuel runs out; holding JUMP for the first jump does not start the glide.
- [ ] Aegis: the dash crosses the gap; it goes where the joystick points, or straight ahead without input; it
      stops at the dash wall with a flash; it never passes through.
- [ ] Switching discipline in the air, and pressing JUMP repeatedly, never gives a second ability.
- [ ] A swing started just before a dash is abandoned; charging blocks the abilities; blocking blocks them.
- [ ] No pose stays stuck after landing, a ledge catch or a hit.
- [ ] On a phone: joystick, camera drag and JUMP held together during a glide.
- [ ] At the Performance preset (30 fps), the abilities reach the same places.

## Code

| Part | Files |
| --- | --- |
| Rules (plain C#) | `AerialAbilityModel.cs`, `AerialAbilityTuning.cs`, `JumpPhysics.cs`, `AerialSimulator.cs` (Characters); `AerialCourseLayout.cs` (Levels) |
| Runtime | `AerialAbilities.cs` (Magic), `ThirdPersonMotor.cs`, `CharacterAnimationDriver.cs`, `IAerialMoveSource.cs`, `DisciplineCombat.cs`, `AerialAbilityEffects.cs` (CombatFeedback) |
| Editor | `DisciplineSetup.cs` (adds the component), `MageCharacterSetup.cs` (Animator), `MagicVfxPrefabs.cs` and `MagicVfxSetup.cs` (effects), `AerialCourseSetup.cs` and `DisciplineRangeSetup.cs` (course) |
| Tests | `AerialAbilityTests.cs`, `AerialCourseLayoutTests.cs`, `MultiTouchRoutingTests.cs` |
