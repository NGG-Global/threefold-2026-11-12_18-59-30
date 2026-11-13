# Interaction and magical puzzles

Sprint 10 lets the player act on the world in three ways: by moving through it (standing on
plates and platforms), by using things directly (doors, levers, chests), and with magic (spells
that light crystals). The pieces are reusable components that are joined by references, so a
new puzzle is built by connecting parts, not by writing code. An example puzzle, the Sealed
Gateway, stands in the Fantasy Playground.

**Status:** all code compiles against Unity 6000.6.0f1, including with Android player defines,
and the puzzle rules pass 65 tests outside Unity. **Nothing has run in Unity.** The component
tests and the end-to-end test of the assembled puzzle are written but need the Unity engine, so
they have not run either. How the puzzle reads, feels and sounds can only be judged by playing.

## The Sealed Gateway

In the south-east corner of the courtyard stands a small roofed sanctum. Its only way in is a
gateway closed by a stone seal. Three rune lamps on the lintel are dark. Above the gateway a
crystal floats on a bracket, dim.

To open it, in this order:

1. **Reach the lookout.** A dark stone block 2.6 m high stands near the south gate, with a crate
   beside it. It is too high to jump onto: catch its edge from the crate or with a jump, and
   climb up. Standing on top lights the rune ring and the standing stone, a thread of light
   reaches from the stone to the crystal, the crystal brightens and pulses, and the first lamp
   lights.
2. **Strike the crystal with Arcane Bolt** while standing on the lookout. It flares and stays lit,
   the second lamp lights, and the eight stones around the pressure plate light up.
3. **Step onto the pressure plate** in front of the gateway before the stones go out. They go out
   one by one over 6 seconds, each with a tick; the last ones turn warm and blink. The third
   lamp lights, the seal flares and sinks into the ground with a rumble, and a short fanfare
   plays.

Behind the gateway, a stone reliquary can be opened with the interact control; light rises from
inside.

**What goes wrong, and what the player sees:**

| Mistake | What happens |
| --- | --- |
| Striking the crystal from the ground | The bolt fizzles on it with a warm flash and a crackle. Nothing lights |
| Leaving the lookout before striking | The ring and the first lamp go dark again, with a falling tone |
| Too slow to the plate | The crystal's light fades, the lamps go dark, a falling tone. Climb back and try again |
| Stepping on the plate first | The plate sinks, nothing else happens |

The puzzle explains itself through these cues; there is no tutorial text. The three lamps say
three things are needed; the beam says the lookout and the crystal belong together; the stones
around the plate say there is a time limit.

### Once solved

The gateway stays open, the crystal stays lit, the plate stays down and the timer stones stay
lit for the rest of the play session: through the level's restart (R key, Start button, or the
restart button), and when the player goes through the south gate to the Combat Arena and comes
back, which loads the playground again. Leaving Play mode, or closing the
game, forgets it. There is no save system yet, so nothing is written to disk.

## Controls

| Action | Desktop | Gamepad | Touch |
| --- | --- | --- | --- |
| Interact | **E** | North button (Y on Xbox layout) | The interact button, above BLOCK. It appears only while something can be used, labelled with what it does |
| Climb, walk onto plates | As before | As before | As before |
| Cast Arcane Bolt | Hold the left mouse button, release | Hold the west button, release | Hold ATTACK, release (a tap is a melee strike; see `Docs/TouchControls.md`) |

A prompt appears next to the thing in reach: the key and one verb, such as **E Open**. With touch
controls the key is left out and the button carries the verb. Nothing is offered while the Mage
hangs, climbs, vaults or is in the air, or if a wall is in the way.

The keys come from the existing actions asset: its **Interact** action is bound to E and the
gamepad's north button. That action has a Hold interaction; the game reacts to the press itself,
so no holding is needed.

## Build it

On the laptop, after pulling:

1. Let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Fantasy Playground**. It now builds the puzzle as well, adds the
   interactor to the Mage, the prompt to the HUD and the interact button to the touch controls,
   and validates the result.
3. Press Play.

**Threefold > Setup > Sealed Gateway Puzzle** adds only the puzzle (and the interaction parts) to
the open scene, which must already contain the Mage. It is placed at the same position in any
scene.

Both commands can be run again at any time. They add what is missing and fill empty references
only; positions, materials and values changed in the Inspector are kept. A part that was deleted
is rebuilt and reconnected everywhere it was used. To rebuild the puzzle
with the default layout, delete the `SealedGateway Puzzle` object and run the command again.

Afterwards, upload from the laptop (see `Docs/DevelopmentWorkflow.md`): the scene and the new
folder `Assets/Threefold/Generated/Puzzles`.

## Reusable components

All runtime code is under `Assets/Threefold/Scripts/Runtime`. Every puzzle part derives from
`PuzzleElement`, which is either active or not.

| Component | What it is | Active while |
| --- | --- | --- |
| `Puzzles/PresenceZone` | A box that notices the player (optionally other characters or heavy physics objects) | Something that counts is inside |
| `Puzzles/PressurePlate` | A presence zone with a top that sinks | Something stands on it |
| `Puzzles/SpellActivator` | A crystal, brazier or seal that answers chosen kinds of magic. Can be receptive only while another element is active | Struck (Latch), or alternately on and off (Toggle) |
| `Puzzles/InteractiveSwitch` | A lever or chest used with the interact control. Toggle or one-shot; can require another element | Switched on |
| `Puzzles/TimedMechanism` | Starts when its trigger activates (or from a Unity Event); can reset other elements when it runs out | Running |
| `Puzzles/PuzzleSequence` | Steps that must happen in order | Solved |
| `Puzzles/PuzzleLogicGate` | All or any of several elements | Its inputs say so |
| `Puzzles/PuzzleDoor` | A door, gate or seal. Opened by an input element, or by the player with the interact control | Open or opening |

`Interaction/IInteractable` is the interface for anything the player can use. `PuzzleDoor` and
`InteractiveSwitch` implement it; any other component can, and registers itself with
`InteractionRegistry` while enabled. `Interaction/Interactor` on the Mage picks the nearest
reachable one in front of the character with nothing in the way, and
`Interaction/InteractionPrompt` shows it.

### Sequence rules

Each step of a `PuzzleSequence` has a rule:

- **Latch:** once done, stays done.
- **Hold Until Next Step:** the element must stay active until the next step is done; if it
  switches off earlier, progress falls back to that step.

A step counts only when its element switches on while it is the next step, so standing on the
plate before the crystal is lit does nothing, and stepping off and on again is needed. Steps out
of order are ignored, or, with **Reset On Wrong Order**, start the sequence over. When solved, the
sequence locks itself, its active steps and anything listed under **Lock When Solved**, so the
outcome cannot be undone.

The Sealed Gateway uses: lookout (hold), crystal (hold), plate (latch); the timer starts on the
crystal and resets it; the timer is locked on success.

### Remembering state

Give an element a **Persistence Key** and its state is remembered for the session in
`Puzzles/PuzzleSessionState`. Elements whose state comes from the world (zones, plates, spell
activators, timers, gates) are remembered only once locked, that is, once their puzzle is
solved. A lever the player left switched on is remembered as it is. Keys must be unique in the
whole game; the scene validation reports duplicates within a scene.

### Spells

`Combat/SpellElement` names the kind of magic a spell carries: Arcane, and, reserved for later
spells, Fire and Frost. Each projectile prefab carries one (Arcane by default, which covers the
existing Arcane Bolt prefab without regenerating it). On impact, a projectile tells any
`ISpellReceiver` it hits. A receptive, unlit `SpellActivator` is also an aim-assist target, so a
thumb on a touchscreen finds the crystal as easily as a training dummy.

### Feedback hooks

Every element raises C# events (`StateChanged`, `Rejected`, and more on sequences, timers and
doors) and has Unity Events (**On Activated**, **On Deactivated**, **On Rejected**, and so on) to
connect sounds and effects in the Inspector. Ready-made presentation components listen to them:

| Component | Shows |
| --- | --- |
| `Puzzles/PuzzleGlow` | Dim while dormant, a slow pulse while waiting, bright while active, a warm flash when refused. Drives emission and an optional light |
| `Puzzles/SequenceIndicator` | One lamp per step |
| `Puzzles/TimerIndicator` | Stones going out one by one, with ticks |
| `Puzzles/PuzzleBeam` | A thread of light between two points while an element is active |
| `Puzzles/PuzzleAudio` | Sounds for on, off, refused, and for sequences each step, lost progress and the solution |

**Audio.** There are no audio files in the project. Every sound slot accepts a clip; an empty slot
plays a simple placeholder generated in code (`Puzzles/PuzzleSounds`: a bell, a falling tone, a
crackle, a rumble, an arpeggio, a knock and a tick; step chimes rise with each step). They are
meant to make the hooks audible until real sounds exist, not as final audio. They are built when
the scene starts, so building one never interrupts play. These are the first sounds in the game.

## Making another puzzle

1. Place the parts and add the components: zones, plates, activators, switches.
2. Connect them: a door's **Input** to the element that opens it, a timer's **Trigger**, a
   sequence's **Steps**, an activator's **Receptive While**.
3. Add a `PuzzleGlow` and `PuzzleAudio` to whatever the player should see and hear react.
4. Give the elements whose outcome must last a **Persistence Key**.
5. Run **Threefold > Validate > Open Scene**.

For anything built repeatedly, write a setup command like `Editor/Puzzles/SealedGatewaySetup.cs`.
Its layout is expressed relative to one site position, so the same command can place the puzzle
elsewhere.

## How it is built

| File (under `Assets/Threefold/Scripts`) | Role |
| --- | --- |
| `Runtime/Puzzles/SequenceLogic.cs`, `PuzzleRules.cs` | The rules as plain C#: sequence progress, countdown, debouncing, door movement, gates, spell and switch decisions |
| `Runtime/Puzzles/*` (others) | The components listed above |
| `Runtime/Interaction/*` | Interactable interface and registry, choice of target, interactor, prompt |
| `Runtime/Combat/SpellElement.cs`, `AimAssistTargets.cs` | Kinds of magic, spell receivers, extra aim-assist targets |
| `Editor/Puzzles/SealedGatewaySetup.cs` | The menu command that builds and wires the example |
| `Editor/Puzzles/PuzzleAssets.cs` | Generated meshes and materials for the puzzle props |
| `Editor/Puzzles/PuzzleValidator.cs` | Puzzle and interaction checks, part of the scene validation |
| `Editor/InteractionSetup.cs` | Interactor, prompt and touch button, part of every rig setup |

**Changes to earlier systems**

- `CharacterInputSource` has `InteractPressedThisFrame` (false unless a source provides it). The
  desktop input reads the Interact action and can name its key; the combined input merges it;
  the touch controls have the interact button.
- `Projectile` notifies spell receivers and carries a spell element.
- `CameraAimProvider` also considers registered aim-assist targets.
- The Fantasy Playground keeps grass and moss out of the puzzle's area, and its setup builds the
  puzzle.

Presence is checked with a box query every frame rather than with trigger messages, because
traversal switches the character's collider off and on, and Unity does not reliably report
leaving a trigger when a collider is switched off. A character hanging from the lookout's edge is
therefore not on the lookout.

## Layout checks

The positions were checked by calculation against the playground's existing objects, not by
looking at the scene:

| Check | Result |
| --- | --- |
| Overlap with existing walls, jump course, columns, traversal course, gate arch | None. The sanctum stands 5 cm inside the perimeter walls; the lookout 1.15 m from the south wall |
| A bolt from any standing spot on the lookout to the crystal (bolt radius 0.18 m, cast 1.25 m above the feet) | Clear of the sanctum, cornice, bracket and stele by at least 0.37 m |
| A player on the crate counts as on the lookout | No: the detection box starts above the head of someone standing on the crate |
| Distance from the lookout to the plate | About 7 m, against a 6 s timer |
| Getting into the sanctum without the gateway | Not possible: it is roofed. Its roof can be reached from the lookout with a running jump, which is harmless |

## Tests

| File | Covers | Runs |
| --- | --- | --- |
| `PuzzleLogicTests.cs` | Sequence rules including the gateway with a failed and a successful attempt, countdown and indicator stones, debouncing, door movement and easing, gates, spell and switch decisions, session memory, generated sounds | Outside Unity: 58 passed |
| `InteractionRulesTests.cs` | Reach, angle, height and preference | Outside Unity: 7 passed |
| `CombinedCharacterInputTests.cs` | Interact merged from several sources (one test added) | Needs Unity |
| `PuzzleComponentTests.cs` | The components together with real physics: detection, the full sequence, fizzling, wrong spell, too slow, wrong order, locking, session memory, chest, lever, interactive door and its reset, gate. One test first checks that physics queries see the stand-in player in Edit Mode, which the others rely on | Needs Unity (Edit Mode); not run |
| `SealedGatewayAssemblyTests.cs` | Builds the playground and checks the puzzle's wiring, detection on the lookout and plate, not on the crate, the bolt's path to the crystal, the seal blocking and then clearing the way, and solves it with the real Mage | Needs Unity, batch mode only; not run |

Not covered by automated tests: how anything looks or sounds, the prompt's position on screen,
the touch button, and the timing of the puzzle in real play.

## Verified, and not verified

| Check | Result |
| --- | --- |
| Compiles against Unity 6000.6.0f1, also with Android player defines | Passed (`Tools/ci/compile_check.sh`) |
| Repository checks | Passed |
| Puzzle and interaction rules outside Unity | 65 tests passed |
| Independent code review | Done. It found seven defects (among them: a solved seal rumbling again on reload, a hand-opened door not closing on reset, re-running the setup not reconnecting a rebuilt part); all were fixed |
| **Anything in Unity: the setup command, the puzzle, the prompt, the sounds** | **Not verified** |
| **Edit Mode and batch-mode tests** | **Not run** |
| **Touch interaction on a device** | **Not verified** |

## Known limits

- One example puzzle. No moving platforms, pushable blocks (plates already accept heavy physics
  objects, but nothing pushable exists), or puzzles that need several players.
- Prompts show one interactable at a time, the nearest one in front.
- A door that closes on the player is not prevented from doing so. The Sealed Gateway never
  closes once open.
- Placeholder sounds only; no music or ambience.
- The glow uses material property blocks, which take those few renderers out of the SRP Batcher.
  About 20 renderers are affected; measure on a phone if frame time is tight.
- Two extra point lights (crystal and reliquary), off until needed, without shadows.
- The gamepad key name in the prompt comes from the Input System's binding display string and
  has not been seen; it may read differently from "Y".
