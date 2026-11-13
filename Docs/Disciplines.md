# The three disciplines

Sprint LM-02 (Living Magic, part 2) gives the Mage three magical disciplines: **Arcane**,
**Flame** and **Aegis**. Each one changes the standard melee attack and has one charged special
attack. They use the controls of LM-01 (`Docs/TouchControls.md`): tap ATTACK to strike, hold to
charge, drag to aim, release to fire; the indicator on ATTACK, or keys 1 to 3, selects the
discipline.

**Status:** the code compiles against the Unity 6000.6.0f1 assemblies, and the combat rules pass
their tests outside Unity (see Tests). Nothing has been run inside the Unity Editor or on a
device. Every number below is a first estimate that has never been played.

## What each discipline does

| | Arcane | Flame | Aegis |
| --- | --- | --- | --- |
| Identity | Precision, energy, direction | Heat, aggression, transformation | Force, stability, protection |
| Melee | 3 fast strikes at one target | 3 wide swings at up to 3 or 4 targets, each adds heat | 2 slow, heavy strikes with high stagger |
| Time per swing (start-up + recovery) | 0.27 s, 0.27 s, 0.40 s | 0.38 s, 0.38 s, 0.52 s | 0.52 s, 0.72 s |
| Damage per swing | 9, 10, 15 | 11, 11, 16 | 14, 20 |
| Stagger per swing | 6, 6, 12 | 8, 8, 12 | 30, 45 |
| Movement during a swing | Full speed | 90% to 85% | 70% to 55% |
| Charged attack | **Arcane Arrow** | **Flame Wave** | **Force Pulse** |
| What it is | One projectile, aimed with the camera | A wave along the ground | A short, wide impulse |
| Mana, cooldown | 18, 0.4 s | 26, 1.0 s | 22, 1.5 s |
| Time to full charge | 1.1 s | 1.0 s | 0.9 s |
| Uncharged to fully charged | 22 to 48 damage, 30 to 46 m/s, range 55 m | 14 to 30 damage, reach 5 to 8 m, 75 degrees, up to 6 targets, 45 to 80 heat | 6 to 14 damage, 40 to 90 stagger, reach 3.5 to 5.5 m, 130 degrees, up to 8 targets, pushes 2.5 to 5 m |
| Movement while charging | 85% | 80% | 70% |
| Colour (proposal) | Cool blue-violet | Warm amber | Pale gold |

### Shared rules

- **Tap and hold.** A tap swings; a hold charges. They are never confused (LM-01).
- **Combos.** Each tap performs the next swing. A tap made up to 0.3 s early is remembered and
  performed as soon as the current swing has recovered. After the combo window (0.35 to 0.6 s
  after a swing) the next tap starts from the first swing again. One swing is in progress at a
  time, however fast the taps.
- **Targeting.** A swing goes where the player steers, or where the Mage faces when standing
  still, and turns towards a target that is within reach and within 50 to 60 degrees of that
  direction. Single-target strikes take the nearest target in the arc; area attacks take the
  nearest up to their limit. The same code decides for melee, wave and pulse. Never hit: the
  Mage, allies, the defeated, and anything behind a wall. A swing does not turn towards a target
  it could not hit. Creatures do not shield each other; this now also holds for the enemies' own
  strikes, where a training dummy standing between a Guardian and the Mage used to take the blow.
- **Aiming a charged attack.** The Arcane Arrow goes to what the camera's crosshair points at
  (with the existing aim assist); dragging offsets it by up to 25 degrees sideways and 12 degrees
  up or down. A ring marks where it will strike, tightening as the charge builds. The Flame Wave
  and the Force Pulse go along the ground in the direction of the drag, relative to the camera;
  without a drag they go straight ahead. The area they will cover is outlined on the ground and
  grows with the charge. On keyboard, mouse and gamepad there is no drag: the camera aims.
- **Mana.** One pool, as before (100, regenerating). Melee is free and each target hit returns 2
  to 4 mana. Charged attacks cost mana on release; cancelling costs nothing. A charge is refused
  at once if it cannot be paid for or is on cooldown: the mana bar flashes and ATTACK shows the
  cancellation.
- **Cancelling and interruption.** Releasing on the cancel zone, pressing BLOCK, or a
  system-cancelled touch abandons a charge (LM-01). In addition, a hit of 10 damage or more,
  grabbing a ledge, or defeat breaks a charge; nothing fires, and lifting the finger afterwards
  does nothing.
- **Switching discipline.** At any time. An attack in progress finishes as the discipline it
  started with: a swing lands as that discipline, and a charge being held is released as the
  discipline it began with, at that discipline's cost. The switch applies from the next attack,
  and the next swing starts the new discipline's combo from its first step.
- **Movement.** Charging and swinging never root the Mage. Heavier attacks slow it by the shares
  in the table, and sprinting is off while swinging, charging or blocking.
- **Camera help.** While a swing is aimed at a target and the player is not turning the camera,
  the view drifts towards the target at up to 35 degrees per second. It never snaps, stops the
  moment the camera is touched, and can be switched off (set the rate to 0 on `DisciplineCombat`).

### Heat (Flame)

Flame strikes and the Flame Wave add heat to targets that can be heated. Heat fades after 1.5 s
without more. At 100 the target ignites and burns for 4 s (6 damage per second, in ticks), then
starts again from cold; heat added while it burns is ignored. Burning never staggers, and a
burning enemy stays alert but does not learn where the Mage is from the burn.

| Target | Heat |
| --- | --- |
| Training dummy (straw) | Heats 1.4 times as fast |
| Arcane Wisp | 1.3 times |
| Ruins Guardian (stone) | 0.6 times |
| Objects in the world | Those with a thermal property heat by the same rule (`Docs/WorldMagic.md`) |

### Stagger and push (Aegis)

Every hit now carries a stagger value apart from its damage. Enemies add it to their existing
poise meter, so an Aegis strike (14 damage, 30 stagger) breaks a Ruins Guardian's poise (55) in
two hits where four Arcane strikes do not. The stagger immunity after each stagger still
prevents stun-locking.

The Force Pulse also pushes. An enemy slides along its navigation surface by the distance its
push resistance leaves (Guardian 0.6, Wisp 0), using the same knockback as a stagger. No
Rigidbody is involved, and nothing leaves the walkable area. Training dummies stand on a post and
are not pushed.

Since LM-03 the Aegis swings carry a little force as well (0.6 and 1.0), so they shove an enemy
slightly: a Wisp 0.6 m and 1 m, a Guardian 0.24 m and 0.4 m. Objects in the world with a kinetic,
structural or stability property answer force too.

### Effects on the world

Since LM-03 every attack carries effects apart from its damage, and objects respond according to
their properties. `Docs/WorldMagic.md` describes the model and the example objects.

| Attack | Carries |
| --- | --- |
| Arcane melee | Nothing |
| Arcane Arrow | Arcane charge, 40 to 100 |
| Flame melee, Flame Wave | Heat: 18, 18, 30 per swing; 45 to 80 for the wave |
| Aegis melee, Force Pulse | Force: 0.6 and 1.0 per swing; 2.5 to 5 for the pulse |
| Aegis guard | A brace for unstable objects within 3.5 m, while BLOCK is held |

The Arcane Arrow still lights the Arcane crystals of the Sealed Gateway: a puzzle crystal reads
arcane charge as Arcane magic, heat as Fire and force as Force.

## Block

The protection of block is the same whatever discipline is selected. While BLOCK is held and the
hands are free:

- The guard is up at once; there is no start-up.
- Hits arriving within a 160-degree arc in front of the Mage lose 80% of their damage.
- Hits from behind, and damage without a direction (a burn), are not reduced.
- The Mage turns to face where the camera looks, so the player chooses what is guarded by looking at it.
- Movement is at 55%, and no swing or charge can start. A swing that has not landed is abandoned.
  A swing that has landed still has to recover, so flicking BLOCK does not speed up attacks.

There is no timed parry or counter; none existed before, and none was added. A pale arc on the
ground shows the guarded side and flashes when a hit is stopped.

One thing does depend on the discipline: with **Aegis** selected, the guard also braces unstable
objects within 3.5 m for as long as BLOCK is held (`Docs/WorldMagic.md`). It does nothing to
creatures and changes nothing about the protection.

## Presentation

The Mage has no melee animations (its model has locomotion, jump, climb and one casting
gesture). The attacks therefore have working hooks and temporary, readable feedback:

| Moment | Feedback |
| --- | --- |
| Swing starts | A flat arc in the discipline's colour sweeps out on the ground over the start-up. The Animator trigger named in the swing's data (`Melee`) is set if the Animator has it; otherwise the casting gesture plays as a stand-in |
| Swing hits | A small burst on each target, light camera shake that differs per discipline |
| Charging | The charge ring on ATTACK (LM-01), the aim ring or ground outline |
| Charged release | The casting gesture, a burst at the hands, camera shake; for wave and pulse the area flashes on the ground |
| Burning | A few embers on the target at ignition and on each burn tick |
| Pushed | A small pale burst |
| Blocked hit | A spark at the point of impact, the guard arc flashes, light camera shake |

All effects are pooled, unlit and short: one flat shape per attack and bursts of 6 to 18 soft
particles. There are no lights, no particle collisions, no screen-space effects and no new
shaders. The colours are a proposal from the Art Bible's palette families, not approved swatches.

## Set up

1. Pull `main` on the laptop and let Unity import. The Console must show no red errors.
2. Open each scene you use and run its scene command (**Threefold > Setup > Mage Prototype**, the
   Fantasy Playground, the Combat Arena) or, in a scene that already has the Mage,
   **Threefold > Setup > Disciplines**. Read the report and save the scene.
3. For a dedicated test scene, run **Threefold > Setup > Discipline Range**. It creates
   `Assets/Scenes/DisciplineRange.unity`.
4. Upload the scenes and the new folder `Assets/Threefold/Generated/Disciplines` (see
   `Docs/DevelopmentWorkflow.md`).

The command generates, in `Assets/Threefold/Generated/Disciplines`:

| Generated | What it is |
| --- | --- |
| `Arcane.asset`, `Flame.asset`, `Aegis.asset` | The discipline definitions |
| `ArcaneArrow.asset`, `FlameWave.asset`, `ForcePulse.asset` | The charged abilities |
| `ArcaneArrow.prefab` | The arrow projectile |
| `SweepShape.prefab` | The arc of swings, waves and pulses |
| `*StrikeImpact`, `*ChargedImpact`, `*Release`, `Embers`, `PushDust`, `GuardSpark` prefabs | Particle bursts |
| `AimShape.mat`, `ArcaneArrowCore.mat` | Materials |
| In the scene | `DisciplineCombat` on the Mage; `CombatVisuals` with the aim preview, the guard arc and the feedback |

Existing assets are kept as they are; only empty references are filled. Enemy definitions
generated before this sprint get their push resistance and heat values filled once, unless they
were changed by hand. Until the command has been run in a scene, that scene keeps the Staff
Strike and the placeholder bolt of LM-01; block already reduces damage there.

### The Discipline Range

| Where | Targets | For |
| --- | --- | --- |
| Just ahead of the start | Three dummies abreast | Arcane hits one; Flame's swings hit all three and heat them |
| The field | Five dummies in a wedge, one more behind a wall | Flame Wave and Force Pulse; the one behind the wall must not be hit |
| The lane | Targets at 22, 30 and 38 m | Arcane Arrow at range; the last is the objective construct |
| The left edge | Crystal, barricade, stone barrier, platform, unstable structure, reinforced door | Effects on the world (LM-03, `Docs/WorldMagic.md`) |

Dummies cannot be pushed and have no poise. Try stagger and push on the Guardian and the Wisps in
the Combat Arena.

## Tuning

| To change | Where |
| --- | --- |
| A discipline's swings: damage, stagger, reach, arc, targets, heat, force, mana returned, start-up, recovery, combo window, movement, shake, animation trigger | `Arcane.asset`, `Flame.asset`, `Aegis.asset`: Standard attack |
| What a discipline's guard does to nearby objects | The same assets: Guard |
| Input buffer, how far a swing turns towards a target | The same assets: Standard attack |
| Colour, icon, kind of magic | The same assets: Identity |
| Charged attack: mana cost, cooldown, time to full charge, movement while charging, damage and stagger range, aim limits | `ArcaneArrow.asset`, `FlameWave.asset`, `ForcePulse.asset` |
| Arrow speed, range, size, arcane charge | `ArcaneArrow.asset`: Projectile |
| Wave and pulse reach, arc, target limit, heat, push | `FlameWave.asset`, `ForcePulse.asset`: Area |
| Damage that breaks a charge, speed while blocking, camera help | `Mage`: Discipline Combat |
| Block: share of damage removed, guarded arc | `Mage`: Player Guard |
| How an enemy reacts to heat and push | The enemy's definition asset: Reactions |
| How a dummy reacts to heat | `TrainingDummy.prefab`: Heat |
| Mana pool and regeneration | `Mage`: Mana |

## Architecture

Input, rules, spell behaviour and presentation are separate. As in LM-01, the decisions are in
plain C# with the time passed in, so they are tested without Unity.

| Layer | Files (under `Assets/Threefold/Scripts/Runtime`) | Knows about |
| --- | --- | --- |
| Input | `Input/AttackGesture`, the touch and desktop sources (LM-01) | Nothing about combat |
| Data | `Magic/DisciplineDefinition`, `Magic/DisciplineProfiles`, `Combat/ChargedProfiles`, `Magic/DisciplineDefaults` | Numbers and references only |
| Sequencing (plain C#) | `Magic/DisciplineCombatModel`, `Magic/MeleeComboMachine` | The gesture, the selected discipline, a yes or no for mana and cooldown. Answers with commands |
| Resolution (plain C#) | `Combat/TargetSelection` (who is hit, `BlockRules`, `ChargeScaling`), `Combat/StatusEffects` (`HeatMeter`), `Combat/ChargedAim`, `Magic/CameraAssist` | Geometry and arithmetic |
| Resolution (Unity) | `Combat/AreaStrike` (one physics query, then `TargetSelection`), `Combat/Projectile` | Colliders, `Health`, the effect receiver interface (`IMagicEffectReceiver`, LM-03) |
| Spell behaviour | `Combat/ChargedAbilities` (`ChargedProjectileDefinition`, `ChargedAreaDefinition`), cast through the existing `AbilityCaster` | Cost, cooldown, what the attack does |
| Carrying out | `Magic/DisciplineCombat`, `Combat/PlayerGuard`, `Magic/PlayerDisciplines` | The model's commands and the systems above |
| Presentation | `CombatFeedback/DisciplineFeedback`, `ChargeAimPreview`, `GuardVisual`, `SweepEffect`, `AimShapes` | Events only; changes no rule |

What was reused rather than rebuilt:

- **`AbilityCaster`** enforces mana and cooldown for the charged attacks and raises the events the
  HUD, the camera shake and the casting gesture already listen to. It gained casting an ability
  that is not in a slot, a check without side effects, and an aim preview.
- **`Projectile`** flies the Arcane Arrow. A launch can now carry stagger, heat, a kind of magic
  and a size.
- **`MeleeStrikeDefinition`** (the enemies' strikes) and the Mage's melee share `AreaStrike`.
- **Enemy poise, stagger and knockback** are unchanged; they now read the hit's stagger value and
  accept a push.
- **`Health`** gained damage modifiers, which is how the guard reduces a hit.

Small additions to earlier systems: `ThirdPersonMotor.LimitSpeedThisFrame`, `ThirdPersonCamera.AddYaw`,
`CharacterInputSource.InterruptAttack`, `CharacterAnimationDriver.TrySetTrigger`.

`DisciplineCombat` raises `SwingStarted`, `SwingLanded`, `SwingInterrupted`, `ChargeBegan`,
`ChargeEnded` and `ChargeRefused`, and offers `TryGetChargePreview`. The events of
`PlayerAbilityInput` from LM-01 still fire. While `DisciplineCombat` is enabled, the slot attacks
of `PlayerAbilityInput` (Staff Strike, placeholder bolt) are switched off.

A fourth discipline needs an entry in `MagicDiscipline` and `DisciplineCatalog`, defaults in
`DisciplineDefaults`, and a retuned selector arc (see `Docs/TouchControls.md`). A new kind of
charged attack is a new subclass of `ChargedAbilityDefinition`.

## Tests

Run by `Tools/ci/compile_check.sh` outside Unity, and by Unity's Test Runner in CI once the Unity
job is enabled.

| Test class | Covers |
| --- | --- |
| `DisciplineCombatTests` (52) | All three melee combos step by step, wrap and restart, buffered taps, mashing; all three charged attacks; tap versus hold; charge power and cap; aim passed with the release; charging while moving; cancel zone, block, heavy hit, climbing and defeat; mana spent on release only, refusal without mana or on cooldown, running out of mana; switching discipline between swings, during a swing and during a charge; the mouse-button path; the design intent of the default numbers; the Aegis guard's pulses (LM-03) |
| `CombatRulesTests` (36) | Target selection (reach, arc, height, nearest first, limits, allies, the defeated, the attacker), turning towards a target, heat (build-up, fading, ignition, burn total, immunity), block (arc, direction, mitigation), charge scaling, aim mapping, camera help, stagger apart from damage |
| `EnemyBrainTests` (4 added) | A forceful hit staggers with little damage; precise hits do not; a full Force Pulse staggers a Guardian at once; burn damage never staggers |
| `DisciplineSetupTests` (3) | Needs Unity: the range assembles and validates with all three disciplines; a second run adds nothing; generated assets match the defaults |

The sequencing tests drive the real combat model with the real attack-gesture recognizer and the
default numbers; mana and cooldown are answered by a stand-in with the same rules as the caster.

**Not covered by automated tests:** anything that needs physics or a scene. That includes
`AreaStrike` finding colliders and checking cover, the projectile's flight, push on a real enemy,
a target that cannot be pushed, the aim preview, every effect, and the setup command (until the
Unity job runs). Feel is untested by definition.

## Known limits

- Not yet compiled by Unity, run in the Editor, or played on a device.
- The Mage has no melee or block animation. Swings show as a ground arc and, for want of a clip,
  the casting gesture. Adding clips needs the model rebuilt with Blender from the source
  libraries; the trigger names are already in the data.
- All numbers are first estimates: damage, timing, costs, heat, stagger, push and camera help.
- Melee does not move the Mage (no lunge), and has no hit-stop.
- A swing is resolved in one instant at the end of its start-up, not swept over time.
- Combat timing follows game time and the charge ring on ATTACK follows real time. They agree
  unless the time scale changes, which nothing in the game does yet (no pause, no slow motion).
- Objects in the world respond since LM-03, with limits of their own (`Docs/WorldMagic.md`).
- Enemies do not use the disciplines, heat or block.
- Block has no cost and no limit; with 80% reduction and no stamina it may prove too safe.
- The Staff Strike and Arcane Bolt assets of earlier sprints remain and are unused by the Mage
  once the disciplines are set up.
- There is no sound.
