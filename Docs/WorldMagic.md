# Magical world properties

Sprint LM-03 (Living Magic, part 3) lets the world respond to the three disciplines of LM-02
(`Docs/Disciplines.md`). An object is given **properties** (it can heat, it has a structure, it
can be moved, it holds arcane charge, it can be unstable) and an attack carries **effects** (heat,
arcane charge, force, stabilise). What happens follows from the two. There is no script per
object and no table of "this spell on that object".

**Status:** the code compiles against the Unity 6000.6.0f1 assemblies, and the rules pass their
tests outside Unity (see Tests). Nothing has been run inside the Unity Editor or on a device: the
example prefabs have not been generated or seen, and no effect has been delivered through real
physics. Every number below is a first estimate that has never been played.

## Effects

An attack can carry four kinds of effect. Each is one small value (`MagicEffect`), delivered to
whatever the attack reaches that can receive effects (`IMagicEffectReceiver`).

| Effect | Magnitude means | Acts on |
| --- | --- | --- |
| Heat | Heat units. An object ignites, or counts as heated, at a share of its capacity | Thermal |
| Arcane Charge | Charge units. An object activates at its threshold | Arcane-reactive |
| Force | The metres an unresisting target would be moved | Kinetic, structural, stability |
| Stabilise | The strength of a brace, with a duration in seconds | Stability |

Every effect also carries its direction, the point it arrived at, its source, the discipline it
came from, a filter (creatures, objects or both) and an **event identity**. One swing, one wave
or one arrow is one event: if it reaches the same target twice (two colliders of one object, a
projectile that touches twice) the second arrival is refused as a duplicate. A target remembers
its last eight events.

**Damage is separate.** Damage goes to `Health` as before; effects go to properties. One attack
delivers both: a Flame swing damages a Wisp and heats it, and heats the barricade behind it
without "damaging" it. An object with properties needs no health, and a hit for 0 damage (the
Aegis guard's brace) still carries its effect.

### What each attack carries

| Attack | Effect | Uncharged to fully charged |
| --- | --- | --- |
| Arcane melee | None | |
| **Arcane Arrow** | Arcane Charge | 40 to 100 |
| **Flame melee** | Heat | 18, 18, 30 per swing |
| **Flame Wave** | Heat | 45 to 80 |
| **Aegis melee** | Force | 0.6 (Force Palm), 1.0 (Bulwark Slam) |
| **Force Pulse** | Force | 2.5 to 5 |
| **Aegis guard** (BLOCK held with Aegis selected) | Stabilise, objects only | Strength 1 for 1.5 s, renewed every 0.4 s, within 3.5 m |

The values are in the discipline assets (see Tuning). The guard's brace is the "defensive
interaction": holding BLOCK with Aegis selected steadies unstable things nearby for as long as
it is held. The protection BLOCK gives is unchanged and still the same for every discipline.

## Properties

An object has any combination of the five. Each is a small state machine in plain C#; the states
change only through an effect or the passing of time, so the same inputs always give the same
result.

### Thermal

`Unheated -> Heated -> Burning`, and back to `Unheated` by cooling or burning out.

Heat accumulates by the effect's magnitude times the object's susceptibility. After a pause
without more heat it cools at a steady rate. At the **heated share** of its capacity the object
counts as heated, which other properties can depend on. At full capacity a **flammable** object
ignites and burns for a set time; one that is not flammable stays at capacity, heated, until it
cools. Heat added while burning is ignored.

This is the heat rule of LM-02, now shared: enemies, training dummies and objects use the same
`HeatMeter`.

### Structural

`Intact -> Weakened -> Fractured -> Destroyed`, by loss of integrity.

Force above the object's **force threshold** removes integrity: the excess, times its force
damage, times its **brittleness**. A separate brittleness applies **while the object is heated**,
which is how something becomes breakable only when hot. Fire removes integrity for as long as a
thermal property burns. The states are reached at set shares of the integrity. A destroyed object
is finished: it takes no further effect, stops burning, switches its colliders off and stops
updating.

### Kinetic

`Resting -> Moving -> Resting`.

A force moves the object by its magnitude less the share its **resistance** takes; a force below
its **minimum force** does not move it at all. The movement is controlled, not simulated: either
along one **axis** between two limits (a platform on a rail), or across the ground within a
radius of where it started. The object glides to its new place at a set speed and stops short of
anything in its way; a push that is blocked outright counts as resisted and announces nothing. It
needs no Rigidbody.

### Arcane-reactive

`Uncharged -> Charged -> Activated`.

Charge accumulates and, after a pause, drains. At the **activation threshold** the object
activates in one of three ways: **Latch** (stays activated), **Timed** (activated for a set time,
then uncharged again) or **While Charged** (activated until its charge has drained). Activation
is what mechanisms listen to (see Mechanisms).

### Stability

`Stable`, `Unstable -> Braced -> Unstable`, and `Collapsed`.

An unstable object comes down under one force at or above its **collapse force**, or when lighter
forces add up to its **stress limit** before the stress has faded. A Stabilise effect of
sufficient strength **braces** it for a time: while braced it does not collapse and cannot be
moved. A collapse that became due while braced happens when the brace ends. The collapse itself
is authored (a pose the object falls into), not simulated.

### How the properties act on each other

These couplings are the only rules between properties, applied in a fixed order after every
change:

| When | Then |
| --- | --- |
| A thermal property burns | The structure loses integrity each second |
| The object is heated | The structure uses its heated brittleness |
| The structure reaches a chosen state | The object becomes unstable, or collapses |
| The object is braced, has collapsed or is destroyed | Force does not move it, and a glide already under way stops where it is |
| One force would both bring the object down and push it | It comes down; it is not also pushed |
| The structure is destroyed | The fire goes out and nothing further is processed |

### What an object answers

Every effect gets one of six answers, which feedback and tests can read:

| Answer | Meaning |
| --- | --- |
| Applied | A property changed |
| Resisted | The object has the property, but the effect was too weak, or the object is braced, burning already, or at its limit |
| No Property | The object has nothing this kind of effect acts on. A crystal does not heat; a barricade does not charge |
| Filtered | The effect was meant for creatures only, or objects only |
| Duplicate | The same event had already been processed |
| Spent | The object is destroyed |

## The six examples

Generated by **Threefold > Setup > Magical Objects** from the values in `WorldMagicDefaults`. The
behaviour in this table is what the tests check against the disciplines' default numbers.

| Example | Properties | Responds to | What happens |
| --- | --- | --- | --- |
| **Arcane crystal** | Arcane-reactive | Arcane Arrow | A full arrow activates it at once; three uncharged arrows do if they follow each other. It lights a beacon for 10 s, then goes dark and can be charged again. Left alone, its charge drains after 2 s |
| **Wooden barricade** | Thermal, structural | Flame; heavy force | A full Flame combo, or a charged Flame Wave, sets it alight; it burns down in about 5 s and leaves charred stubs. An uncharged wave only heats it. Two full Force Pulses also break it; Aegis strikes are too light |
| **Heat-sensitive stone** | Thermal, structural | Flame, then force | Cold, a full Force Pulse removes under 4% of its integrity. A full Flame combo or a full Flame Wave heats it; a full Force Pulse then shatters it. It never burns, and it cools in about 5 s, so the blow has to follow the heat |
| **Movable platform** | Kinetic | Force Pulse | Slides along its rail: 2 m for an uncharged pulse, 4 m for a full one, at most 6 m from its start. Aegis strikes are below its minimum force |
| **Unstable structure** | Stability, structural | Force; Aegis guard | A full Force Pulse topples it; so do two uncharged ones in quick succession. With the Aegis guard held within 3.5 m it turns gold and stands against any pulse; the brace ends 1.5 s after the guard is released |
| **Reinforced door** | Structural, arcane-reactive | Arcane Arrow; heavy force | Two ways through. Two full arrows charge its lock and it opens for good. Or two full Force Pulses fracture it and it gives way; a third destroys the leaf. Fire and anything lighter than a well-charged pulse do nothing |

No example answers every discipline. The crystal ignores heat and force; the platform ignores
heat and charge; nothing but the unstable structure answers a brace.

In the range a low **gate** stands behind the crystal and is open while the crystal is activated.
It has no magic of its own: it is an ordinary puzzle door whose input is the crystal.

## Enemies and training dummies

Creatures receive the same effects through the same interface, and their heat and push use the
same arithmetic as objects (`CreatureEffectModel`): heat means to a Ruins Guardian what it means
to a barricade, and a force moves a Wisp as far as an object with the same resistance. A creature
has no arcane or stability property, so charge and bracing are answered "No Property", exactly as
an object without those properties answers.

What changed for creatures in this sprint:

- **Aegis melee now shoves enemies slightly**, because the swings carry force: a Wisp 0.6 m and
  1 m, a Guardian (which resists 60%) 0.24 m and 0.4 m. Before, only the Force Pulse pushed.
  When the same strike also staggers the enemy, the larger of the two movements applies (the
  stagger's knockback), never their sum and never the smaller.
- Burning, ignition, stagger, poise and the Force Pulse's push are otherwise as in LM-02.

## Making a new object

No script is needed.

1. **Create the definition.** In the Project window: **Create > Threefold > Magical Object
   Definition**. Tick the properties the object has and set their values. Or duplicate one of the
   six generated definitions and change it. Several objects can share one definition.
2. **Make the object magical.** Select the object in the scene (and, with Ctrl+click, the
   definition) and run **Threefold > World Magic > Make Selection Magical**. It adds
   `MagicalObject` and `PropertyStateVisuals`, lists the object's colliders and renderers, and
   assigns the definition. The object needs at least one collider that is not a trigger; that is
   how attacks find it.
3. **Say what is seen.** On `PropertyStateVisuals`, per condition (Heated, Burning, Weakened,
   Fractured, Destroyed, Charged, Activated, Moving, At Travel Limit, Unstable, Braced,
   Collapsed): a tint, parts to show or hide (the whole mesh, the rubble), a burst to play. Heat
   and charge colour the listed renderers gradually by themselves.
4. **For something that moves,** set **Move Target** on `MagicalObject` to the part that slides.
   The axis in the definition is in that part's own space.
5. **Validate.** **Threefold > Validate > Magical Objects**.

### Mechanisms

`PropertyStateTrigger` turns a condition on an object's properties into a puzzle element
(`Docs/Puzzles.md`). Everything puzzles already offer can then follow it without code: a door
that is open while a crystal is activated, a timer that starts when a barrier is destroyed, a
sequence of three objects that must burn. Set the object, the conditions (any one of several) and
whether it latches; then use the trigger as a door's **Input** or a timer's **Trigger**. The
reinforced door is built this way: one trigger on "Activated or Fractured", latched, as the input
of an ordinary `PuzzleDoor`.

`MagicalObject` also has an **On Destroyed** Unity Event and C# events for every change of state
and every effect received.

## Set up

1. Pull `main` on the laptop and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Discipline Range**. It adds the six examples and the gate in a lane
   along the left edge of the range, and updates the discipline assets (below). Read the report
   and save the scene. The discipline assets are shared by every scene, so this one run updates
   them everywhere.
3. Optional: to have the examples in another scene, open it and run
   **Threefold > Setup > Magical Objects**. It asks before placing anything.
4. Upload the scene, the changed discipline assets and the new folder
   `Assets/Threefold/Generated/WorldMagic` (see `Docs/DevelopmentWorkflow.md`).

Generated in `Assets/Threefold/Generated/WorldMagic`:

| Generated | What it is |
| --- | --- |
| `ArcaneCrystalDefinition.asset` and five more `*Definition.asset` | The property values of each example |
| `ArcaneCrystal.prefab`, `WoodenBarricade.prefab`, `HeatSensitiveStone.prefab`, `MovablePlatform.prefab`, `UnstableStructure.prefab`, `ReinforcedDoor.prefab` | The examples, built from primitive shapes |
| `CrystalGate.prefab` | A gate that is open while its input is active |
| `Example*.mat` | Plain materials |
| `ObjectEmbers`, `ObjectDust`, `ObjectBrace`, `ObjectCharge` prefabs | Particle bursts |

Existing definitions and prefabs are kept as they are, so tuned values are not lost. To
regenerate with defaults, delete the folder.

**Discipline assets generated in LM-02 are updated once.** They predate the effects on the world,
so the setup fills in what they lack: force on the Aegis swings, the brace of the Aegis guard and
the charge of the Arcane Arrow. Only values that are still zero are filled; everything tuned
since is kept, and an updated asset is not touched for this again. Until the setup has run, the
Arcane Arrow still charges crystals (it falls back to its damage as charge), but Aegis swings
carry no force and the Aegis guard does not brace.

### Where the examples stand in the range

Along the left edge, 8.5 m left of the centre line, facing the centre. From the start: crystal
(with its gate behind it), barricade, stone barrier, platform (its rail runs 6 m away from the
start), unstable structure (it falls away from the centre), reinforced door.

## Tuning

| To change | Where |
| --- | --- |
| How an example responds: every threshold, rate and limit | Its `*Definition.asset` |
| What an attack carries: heat and force of each swing | `Flame.asset`, `Aegis.asset`: Standard attack |
| Charge of the Arcane Arrow | `ArcaneArrow.asset`: Projectile |
| Heat of the Flame Wave, force of the Force Pulse (its push) | `FlameWave.asset`, `ForcePulse.asset`: Area |
| The Aegis guard's brace: strength, duration, radius, interval | `Aegis.asset`: Guard |
| What an example shows per state | `PropertyStateVisuals` on its prefab |
| What stops a pushed object | `MagicalObject`: Obstacle Layers |
| How an enemy takes heat and force | The enemy's definition asset: Reactions |

## Validation

**Threefold > Validate > Magical Objects** checks every magical object in the open scene and
every definition in the project. It also runs as part of **Threefold > Validate > Open Scene** and
at the end of the setup commands. It reports:

- An object without a definition, or without a solid collider.
- An object that also has `Health` (creatures answer through their own scripts).
- A pushable object whose moving part is marked static or carries no collider.
- A definition with no property, or with values under which a property can never do anything: a
  structure nothing can damage, brittleness-when-heated on something that cannot heat, fire
  damage on something that cannot burn, something flammable with no time to burn, a platform with
  no room to travel, a crystal that cannot charge, stability that nothing can unsettle.
- A trigger or a visual that waits for a state the object can never reach, such as "Burning" on
  stone.

## Performance

- **Event-driven.** Nothing looks for objects each frame. An effect reaches an object only when
  an attack's own physics query (the one it already makes to find targets) or a projectile's
  impact finds its collider.
- **Asleep by default.** A `MagicalObject` is switched off while at rest, so it has no update at
  all. It wakes when an effect arrives and sleeps again once it has cooled, drained, stopped
  moving and is no longer braced. `PropertyStateVisuals` sleeps the same way, except while an
  unstable object sways.
- **No Rigidbody is required.** Pushed objects are moved by their transform and stopped by one
  box sweep per push. The example parts that move carry a kinematic Rigidbody, as the puzzle
  doors do, so that physics treats their colliders as moving; the magic does not depend on it.
- **No allocation per effect.** Effects and payloads are values; queries write into buffers that
  are reused; an object's event memory is a fixed ring of eight. Tints use a property block, so
  no material is copied.
- An area attack reaches at most 16 objects besides its creature targets.

Not measured: none of this has been profiled, in the Editor or on a device.

## How it is built

| File (under `Assets/Threefold/Scripts`) | Role |
| --- | --- |
| `Runtime/WorldMagic/MagicEffect.cs` | The effect, the payload an attack carries, the receiver interface, event identity |
| `Runtime/WorldMagic/WorldProperties.cs` | Structural, kinetic, arcane and stability properties. Plain C# |
| `Runtime/Combat/StatusEffects.cs` | The thermal property (`HeatMeter`), shared with creatures. Plain C# |
| `Runtime/WorldMagic/MagicalPropertySet.cs` | One object's properties, the couplings between them, conditions. Plain C# |
| `Runtime/WorldMagic/CreatureEffectModel.cs` | How a creature takes effects, by the same rules. Plain C# |
| `Runtime/WorldMagic/MagicalObjectRules.cs` | Checks on property values, and which states are reachable. Plain C# |
| `Runtime/WorldMagic/WorldMagicDefaults.cs` | The starting values of the six examples. Plain C# |
| `Runtime/WorldMagic/MagicalObjectDefinition.cs` | The ScriptableObject that holds an object's properties |
| `Runtime/WorldMagic/MagicalObject.cs` | The component: receives effects, advances time while awake, moves, cleans up |
| `Runtime/WorldMagic/PropertyStateTrigger.cs` | A property condition as a puzzle element |
| `Runtime/WorldMagic/PropertyStateVisuals.cs` | Tints, parts, bursts and motion per condition |
| `Runtime/Combat/AreaStrike.cs`, `Projectile.cs` | Delivery: damage to health, effects to receivers, one event per attack |
| `Editor/WorldMagic/MagicalObjectSetup.cs` | Generates the examples; Make Selection Magical |
| `Editor/WorldMagic/MagicalObjectValidator.cs` | The validation |

The rules never touch a Unity object, so they are tested outside Unity like the enemy brain and
the combat model.

### Changes to earlier systems

- The three receiver interfaces of LM-02 (`IHeatReceiver`, `IForceReceiver`, `ISpellReceiver`)
  are replaced by `IMagicEffectReceiver`. Enemies, training dummies and the puzzle crystals
  (`SpellActivator`) implement it. A `SpellActivator` reads an effect as its kind of magic
  (charge as Arcane, heat as Fire, force as Force), so the Sealed Gateway behaves as before.
- `AreaStrike` (melee, wave, pulse, enemy strikes) and `Projectile` deliver damage and effects
  separately, under one event identity per attack. A projectile that carries no explicit effect,
  such as an enemy's bolt, carries one derived from its kind of magic.
- **Arcane swings no longer reach puzzle crystals.** In LM-02 any swing with a kind of magic
  reached the activators in its area, so an Arcane swing could light an Arcane crystal standing
  within reach, and a crystal of another kind flashed its refusal. Arcane swings carry no effect
  now, so neither happens. The Arcane Arrow, Flame swings, the Flame Wave, Aegis swings and the
  Force Pulse reach activators as before. This follows from the design (melee Arcane carries
  nothing); it is listed because it changes how an existing puzzle part behaves.
- A puzzle crystal takes one attack as one spell, even if the attack carries two kinds of effect.
- A melee step has a **force** value; a discipline has a **guard** section; the Arcane Arrow has
  a charge range. `DisciplineCombatModel` issues a guard pulse while BLOCK is held.
- `HeatSettings` gained the heated share and "flammable"; `HeatMeter` reports the three states.

## Tests

Run by `Tools/ci/compile_check.sh` outside Unity, and by Unity's Test Runner in CI once the Unity
job is enabled.

| Test class | Covers |
| --- | --- |
| `WorldPropertyTests` (54) | Each property alone: heating, cooling, ignition, burn-out, immunity; integrity, thresholds, brittleness; resistance, minimum force, axis limits, range, glide; charge, decay, the three kinds of activation; collapse by force and by stress, bracing, a pending collapse. Several properties together and each coupling. Exact thresholds. Incompatible effects. The same event twice. Destruction and what is left; restoring. Determinism and the order of announcements. A blow that topples or destroys does not also push; a brace or a collapse stops a glide; a blocked push changes and announces nothing |
| `MagicEffectDeliveryTests` (40) | What each attack of each discipline carries with the default numbers. Each of the six examples against those attacks, including what it must ignore. A creature and an object answering the same effect by the same rules. The delivery routine reporting whether anything changed; a push against a knockback under way; one attack as one spell to a puzzle crystal |
| `MagicalObjectRulesTests` (20) | Every validation rule; the examples are valid; no example answers every effect; no object reaches a state the rules call unreachable; the one-time update of older discipline values |
| `DisciplineCombatTests` (4 added, 52) | The guard pulse: at once when BLOCK goes down, then at its interval; again at once when BLOCK is raised anew; only for a discipline whose guard has an effect; not while climbing or defeated |
| `MagicalObjectSetupTests` (7) | Needs Unity: the examples generate complete and valid; each prefab answers only through its properties; a destroyed object stops blocking and can be restored; the range holds the examples and a second run adds nothing; Make Selection Magical; the update of an older discipline asset |

**Not covered by automated tests:** anything that needs physics or a scene. That includes an
attack finding an object's collider and checking cover, a projectile delivering its effect, a
pushed object stopping at an obstacle, the sleep and wake of the components, every visual, and
the setup command until the Unity job runs.

## Known limits

- Not yet compiled by Unity, run in the Editor, or played on a device.
- The example prefabs are built from cubes, capsules and spheres with plain materials. They show
  behaviour, not the approved art direction.
- All numbers are first estimates.
- A character standing on the platform is not carried when it slides. It only moves when pushed.
- A pushed object is stopped by one box sweep with its overall bounds. An object with a very
  irregular shape, or one pushed around a corner, is not handled precisely. What the object
  already touches when the push starts does not stop it, so a pushable object should not be
  placed touching a wall that lies in its path.
- A pushable object remembers its resting place in the world. It should not be made a child of
  something that itself moves.
- Collapse is a pose the object moves into. Nothing is thrown, and a collapsing object does not
  hurt what it lands on.
- Fire does not spread from one object to another, and a burning object does not heat creatures
  next to it.
- An object's state is not saved between sessions, except through a latched trigger with a
  persistence key (`Docs/Puzzles.md`).
- Enemies do not use effects against objects on purpose; an enemy's fire bolt would heat a
  barricade it happens to hit.
- Aim assist draws the Arcane Arrow to an arcane-reactive object that is not yet activated, the
  reinforced door's lock included. Whether that helps or distracts is untested.
- There is no sound.
