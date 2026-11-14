# Living Magic

Living Magic is how the Mage's three disciplines act on a world that answers through its
properties. This page is the overview of what LM-01 to LM-08 built and where each part is
documented in detail. It describes what the code does; what has been confirmed in Unity or on a
device is listed in `Docs/LivingMagicTesting.md`.

## Implemented systems

| Sprint | System | Detail |
| --- | --- | --- |
| LM-01, LM-02 | Touch controls with tap, hold-to-charge and drag-to-aim; the Arcane, Flame and Aegis disciplines with melee combos, charged attacks and block | `Docs/TouchControls.md`, `Docs/Disciplines.md` |
| LM-03 | Magical world properties: thermal, structural, kinetic, arcane-reactive, stability; six example objects | `Docs/WorldMagic.md` |
| LM-04 | World state (what lasts, what resets) and chain reactions through one bounded scheduler | `Docs/WorldState.md` |
| LM-05 | Contextual discovery: clues, resonance and experimental feedback, without icons or numbers | `Docs/Discovery.md` |
| LM-06 | Emergent combinations shared by objects and creatures; deflection; bracing | `Docs/Combinations.md` |
| LM-07 | The Broken Foundry: a level with three solutions to its exit, checkpoints, a hazard and diagnostics | `Docs/BrokenFoundry.md` |
| LM-08 | Graphics presets with frame-rate targets, frame pacing guard, graphics menu, extended diagnostics, allocation tests, EditMode tests on the build runner | `Docs/MobilePerformance.md`, `Docs/LivingMagicTesting.md` |

The rules are plain C# with no Unity calls, so they are tested outside Unity: `DisciplineCombatModel`,
`MeleeComboMachine`, `AttackGestureRecognizer`, `MagicalPropertySet`, `CombinationRules`,
`ReactionScheduler`, `DiscoveryRules`, `FoundryRoutes`, `GraphicsPresets`, `FramePacingGuard`,
among others. The components only carry out what those rules decide.

## Input gestures

| Gesture | Result |
| --- | --- |
| Joystick, bottom left | Move; push past the ring to sprint |
| Drag on any free part of the screen | Turn the camera |
| Tap ATTACK | The selected discipline's melee strike (a combo when repeated) |
| Hold ATTACK about 0.22 s | Charge the discipline's charged attack; a ring fills |
| Drag while charging | Aim |
| Release | Fire the charged attack; release over the red zone above the thumb cancels |
| Drag from the discipline indicator onto a slot, or tap it then a slot | Choose Arcane, Flame or Aegis |
| Hold BLOCK | Block (the Aegis guard braces and can turn projectiles) |
| JUMP, Interact | Jump; use what is in front of the Mage (shown only when there is something) |
| Graphics button, top left | Choose Performance, Balanced or Quality |
| Three-finger tap (development builds) | Show or hide the diagnostics |

Keyboard and mouse: WASD to move, the mouse to look, left click to strike or hold to charge, right
click or Q to block, 1 to 3 for the disciplines, Space to jump, Left Shift to sprint, E to interact,
R to restart, F3 for the diagnostics (development builds), F6 to F8 for the style test keys. Gamepads
are supported too. Details, and the rules that keep inputs from contradicting each other, are in
`Docs/TouchControls.md`.

## Effects and magical properties

An attack carries effects besides its damage: **Heat**, **Arcane Charge**, **Force** and
**Stabilise**. Damage goes to `Health`; effects go to properties. One swing, wave or arrow is one
event, so it acts on a target once.

| Property | States | Answers to |
| --- | --- | --- |
| Thermal | Unheated, Heated, Burning | Heat |
| Structural | Intact, Weakened, Fractured, Destroyed | Force (more when heated), burning |
| Kinetic | Resting, Moving | Force (a controlled glide, no physics body) |
| Arcane-reactive | Uncharged, Charged, Activated (latch, timed or while charged) | Arcane Charge |
| Stability | Stable, Unstable, Braced, Collapsed | Force, weight, Stabilise |

A new kind of object is a `MagicalObjectDefinition` asset that combines these, not new code.
Creatures take effects through `CreatureEffectModel` with the same property classes: heat, push,
stagger force, armour and ward, each enemy kind with some of them.

## Combination rules

| Combination | Rule |
| --- | --- |
| Heat + force = fracture | A heated structure uses its heated brittleness, so force breaks it |
| Arcane + mechanism = activation | Charge to a threshold activates; a part may be locked until then |
| Force + projectile = deflection | A designated projectile is turned once by enough force from the other side |
| Stabilise + instability = bracing | A brace holds up what force and weight would bring down, while it lasts |
| Force against footing | One force above a threshold takes an unstable structure or a creature off its feet |

No rule looks at the order of spells, a pair of disciplines or a kind of object; order matters
only through the object's state at the time. Details in `Docs/Combinations.md`.

## Chain reactions

An object that changes may offer effects to others through reaction rules in its definition. They
go to one queue (`ReactionScheduler`): depth 3, 8 per frame, critical first and never refused,
duplicates of one event refused. At most 32 objects with temporary state are advanced per frame
by one `WorldSimulation`. These budgets are provisional.

## Persistence

| Tier | Examples | On reload |
| --- | --- | --- |
| Immediate | An impact, a shove, a burst | Nothing stored |
| Temporary | Heat, charge, a brace, a glide, stagger | Reset |
| Persistent | An opened gate, a destroyed barrier, a latched mechanism, a reached checkpoint, a defeated Guardian | Saved and restored silently |

A state is saved only if the definition names it under Persisted and the object has a Persistent
Id. One store, `WorldState.Current`. Restart, checkpoint, forget one level and new game are
described in `Docs/WorldState.md`. The graphics preset is a player preference in `PlayerPrefs`,
not world state.

## Performance budgets

See `Docs/MobilePerformance.md`: 60 fps on Balanced and Quality, 30 on Performance, a guard that
lowers an unkept 60 to 30, reaction and simulation budgets, and zero allocation per frame in the
magic's rules (checked by test). None of these has been measured on a device.

## Verified test results

See `Docs/LivingMagicTesting.md` for what ran, where, and with what result, and for the manual
device checklist.

## Known limitations

- Nothing from LM-01 to LM-08 has been measured on a device: frame rate, input latency, heat, the
  look of the presets, touch comfort.
- The 118 tests that need the Unity engine run only on the owner's laptop runner (from LM-08); the
  GitHub-hosted Unity job has no licence.
- There are no Play Mode tests. Frame-by-frame flows (movement through a level, a chain reaction
  in a scene, the camera) are covered by no automated test.
- The reaction and simulation budgets are provisional. Objects that never come to rest could keep
  others waiting if more than 32 were active at once.
- The frame pacing guard's thresholds (5-second windows, 15%) and the preset values are proposals.
- Optimized Frame Pacing is off until the owner turns it on (see `Docs/MobilePerformance.md`).

## Future extension points

| To add | Where |
| --- | --- |
| A new magical object | A `MagicalObjectDefinition` asset; reaction rules in the definition |
| A new effect kind | `MagicEffectKind`, the properties that answer it, `CombinationRules` |
| A new discipline or charged attack | `DisciplineDefaults`; a new subclass of `ChargedAbilityDefinition`, **in a file of its own name** |
| A new enemy capability | `CreatureEffectModel` settings on the enemy definition |
| A new level | A layout class like `FoundryLayout`, a route model like `FoundryRoutes`, a setup command |
| A new graphics preset | Another entry in the style controller's quality levels; `GraphicsPresets` names three, others keep their own names |
| A new diagnostics warning | `PerformanceWarnings.Collect`, with a test |
