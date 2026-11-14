# Contextual discovery and feedback

Sprint LM-05 (Living Magic, part 5) is about how the player finds out what magic does to the
world. The world says it through itself: what an object is made of, how it answers when a
discipline is brought near, and what happens when something is tried. There are no outlines on
interactable objects, no floating icons, no tooltips and no instructions.

It builds on the properties of LM-03 (`Docs/WorldMagic.md`) and changes none of their rules.

**Status:** the code compiles against the Unity 6000.6.0f1 assemblies, and the rules pass their
tests outside Unity (see Tests). Nothing has been run inside the Unity Editor or on a device: no
clue, resonance or answer has been seen, heard or felt. How the cues look is a **proposal** made
to fit the Art Bible; it is not an approved look. Every strength and timing below is a first
estimate.

## Three levels

| Level | When | What the player gets | What it means |
| --- | --- | --- | --- |
| 1. Environmental clues | Always, before anything is done | Features of the object itself | What it is made of and how it stands |
| 2. Contextual resonance | A discipline is selected and the object is near or aimed at | The object stirs faintly | This kind of magic could change it. Not: it will work |
| 3. Experimental feedback | Something was tried | The object answers | What actually happened, from its real state |

### Level 1: environmental clues

A clue is a feature the object would have anyway. Which clue an object should carry follows from
its properties.

| Property | Clue | On the examples |
| --- | --- | --- |
| Arcane-reactive | A faint, slow pulse of light | The crystal; the lock in the door's lintel |
| Thermal, flammable | A material that burns | The barricade is wood, with dry straw packed at its foot |
| Thermal with brittleness when heated | Vents, discoloration | Vents on both faces of the stone barrier, with scorched stone around them |
| Kinetic, on an axis | A rail, a groove, a hinge | The platform's rail and its two stops |
| Stability | Cracks, loose debris, a slight sway | Cracks across the structure's blocks, chips at its foot, and its sway |

Only two of these are animated: the pulse, which is a slow breathing of a pale tint at a quarter
strength or less, and the sway. The rest are built parts. Nothing is outlined, and an object
with no property has no clue.

### Level 2: contextual resonance

With a discipline selected, an object nearby may stir: its tint breathes a little faster in the
discipline's colour, and it swells and shrinks by about one per cent. The strength depends on
how directly the player is addressing it:

| Situation | Strength |
| --- | --- |
| Within 9 m, not aimed at | Faint, and fainter with distance |
| Aimed at (within 9 degrees of the view) | Clear |
| Aimed at while charging an attack or holding the guard | Full |

**An object resonates only if an effect the selected discipline really carries could change the
object as it is now.** That is worked out from the discipline's values and the object's
properties and state, not from a table:

| | Arcane | Flame | Aegis |
| --- | --- | --- | --- |
| Arcane crystal | Resonates | | |
| Wooden barricade | | Resonates | Resonates |
| Heat-sensitive stone | | Resonates | Resonates |
| Movable platform | | | Resonates |
| Unstable structure | | | Resonates |
| Reinforced door | Resonates | | Resonates |

It follows the state. A door whose lock is open no longer answers Arcane; something alight no
longer answers Flame; a structure that has come down can no longer be braced; what is destroyed
answers nothing.

Resonance promises a possibility, not success. The door resonates with Aegis because force can
break it, yet a weak pulse does nothing to it. That is what level 3 is for.

### Level 3: experimental feedback

Every attempt is answered in one of six ways. The answer is read from the object after the
effect was applied, so what is shown is what happened.

| Answer | When | Movement | Also |
| --- | --- | --- | --- |
| **Incompatible** | The object has nothing this kind of magic acts on | Two quick shakes from side to side | A trace of tint. No motes |
| **Insufficient** | The right kind, but too little: a force below the threshold, a brace too weak | A lean with the effect that holds for a moment and eases back | A light tint, a few motes |
| **Progress** | It took effect without crossing a threshold: warmer, more charge, more damage | One soft swell | Tint and swell grow as the threshold comes nearer. A few motes |
| **Transition** | A threshold was crossed: heated, alight, cracked, activated, moving, braced | A sharp kick with the effect that settles | A clear tint; the object's own burst |
| **Completed** | Nothing further to do: opened for good, come down, destroyed | Two slow swells, the second smaller | Full tint; the object's own burst. An attempt on something already finished gets the same movement, small and quiet |
| **Blocked** | The right kind, but held for now: braced, already alight, at its stop | One short dip | A trace of tint. No motes |

Each answer has a movement of its own, so none depends on colour, brightness or sound.

**No numbers.** The player is never shown a threshold or a value. Two insufficient forces of
very different strength are answered identically; how much more is needed is for the player to
find out. Only "progress" grows fuller as a threshold approaches.

**Not too often.**

- What other objects set off (a neighbour's fire), and what enemies do (a stray bolt), is shown
  only when something really changes, and does not count as the player trying something.
- The same answer for the same kind of effect is not repeated within 1.5 s, unless it is a real
  change.
- The Aegis guard reaches everything nearby several times a second without being aimed. Things
  it does nothing to stay still: for the guard's brace only a real change is answered (the
  moment something is braced), and keeping a brace up is not answered at all.
- When the blow that destroys an object is answered, what is left of it moves; the object's
  usual parts are hidden by then.

## Attention escalation

Optional, and on by default. If the player stays within 7 m of something unresolved for 25 s
without trying anything, its own clue slowly becomes up to twice as strong: the pulse brighter,
the sway wider, the trace of tint on a built clue more visible. It rises over 8 s, stays for 6 s
and then rests for 20 s before rising again.

- It **only multiplies what the object already shows**. It adds nothing new, so it cannot name
  a discipline.
- **Trying anything** on the object, even something that does nothing, starts the clock again
  and lets the emphasis fade. Experimenting is never nagged.
- **Progress** removes it at once.
- **Combat** quietens it: for 8 s after the last hit dealt or taken nothing is emphasised, and
  time spent fighting nearby does not count as lingering.
- A **resolved** object is never emphasised.

All timings are on the Discovery object (see Tuning), and the whole rule can be switched off.

## Sound and haptics

Both are optional hooks. Every cue is complete without them.

**Sound.** There are no audio files in the project. `DiscoveryAudio` has a clip slot for each of
seven sounds; an empty slot plays a placeholder tone generated in code by the puzzle sounds of
an earlier sprint. They are stand-ins to make the hooks audible. They are not final audio and
not licensed assets.

| Sound | For | Placeholder |
| --- | --- | --- |
| Resonance | An object begins to resonate noticeably. Quiet | Tick |
| Refusal | Incompatible | Fizzle |
| Resistance | Insufficient | Rumble |
| Progress | Progress | Click |
| Transition | Transition | Chime |
| Completion | Completed | Fanfare |
| Held | Blocked | Fade |

**Haptics.** `DiscoveryHaptics` gives a gamepad a pulse whose strength and length follow the
answer. On a phone or tablet Unity offers only the device's standard vibration, one fixed and
fairly strong buzz; it is therefore **off by default** and, when switched on, used only for
transitions and completions. Neither path has been tried on hardware.

The director also raises C# events (`ResonanceBegan`, `AnswerPresented`) and Unity Events for
anything else that should follow.

## Accessibility

On the Discovery object, under **Options**:

| Option | Effect |
| --- | --- |
| Clue Intensity (0 to 2) | Strength of clues and resonance. 0 switches them off. The answer to an attempt is always shown |
| Reduced Particles | No motes for small answers, and no repeating embers on something that burns. The single burst at a change of state stays |
| Emphasize Motion | Movements half as large again, for players who cannot rely on colour or brightness |
| Strong Resonance | Resonance 60% stronger |

Movement is part of every cue with every option. Colour, particles, sound and haptics are
supplements.

**Lower graphics presets.** The director follows the game's own quality setting, the one the
visual style's controller holds, if the scene has it: its lowest level counts as Low, its
highest as High. Without it the tier set on the director applies (Medium by default). Only
particles depend on the tier: on Low, small answers have no motes. The movement, its size and length, the tint, the sound and the haptic
pulse of an answer are the same on every level. Nothing relies on bloom or on post-processing,
which the lower presets switch off.

There is no options screen in the game yet. Until there is one these options are set in the
Inspector, or from code through `DiscoveryDirector.Settings`.

## How it looks, against the Art Bible

- Tint: a wash towards a pale colour through the object's existing material, at most half way on
  its body and fully only on the small parts that show charge. No emission, no bloom.
- Movement: a shift of at most about 12 cm and a change of size of at most 5%, lasting under a second.
- Particles: three motes for a small answer (none on the lowest quality level), and the bursts
  the objects already had for changes of state.
- Not used: outlines or highlights on interactable objects, floating icons, overlays or text,
  full-screen effects, extra transparent layers.

Whether these strengths read well in the approved style, and on a phone screen, has to be judged
in Unity and on a device. Until then this is a proposal.

## Set up

1. Pull `main` on the laptop and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Discipline Range**, read the report and save the scene. It adds the
   `Discovery` object, and gives the six example prefabs their clue component and built clues.
   Prefabs generated by an earlier sprint are updated in place; nothing on them is removed.
3. In another scene that already has the Mage, run **Threefold > Setup > Discovery**.
4. Run **Threefold > Validate > Open Scene**.
5. Upload the scene and the changed prefabs in `Assets/Threefold/Generated/WorldMagic`.

For an object of your own, **Threefold > World Magic > Make Selection Magical** now also adds
`DiscoveryClues`. Give it a **Motion Target**: a child that holds only the object's mesh. It must
not be the part the object itself moves (a sliding block, a door leaf).

## Tuning

| To change | Where |
| --- | --- |
| The player's options | `Discovery`: Discovery Director, Options |
| Resonance: radius, strengths, aiming angle, fade | `Discovery`: Discovery Director, Resonance |
| Escalation: on or off, delay, rise, hold, rest, how much, quiet after combat, radius | `Discovery`: Discovery Director, Attention |
| How far around the player objects are looked for, how often, how many are attended to | `Discovery`: Discovery Director, Nearby Objects |
| How soon the same small answer may repeat; which effects are answered as incompatible | `Discovery`: Discovery Director, Answers |
| An object's idle clue: strength, colour; when it counts as resolved | The object: Discovery Clues |
| Sounds, volume | `Discovery`: Discovery Audio |
| Haptic strength, device vibration | `Discovery`: Discovery Haptics |

## Validation

**Threefold > Validate > Magical Objects**, and the scene validation, now also report:

- Discovery clues that belong to no magical object.
- Clues without a moving part (its answers would rely on colour alone), or whose moving part is
  the object itself, the part the object places, or not part of the object.
- A scene with clues but no discovery director, or a director without its player, camera or
  disciplines.

## Performance

- **One update.** Only the director has one. `DiscoveryClues` has none, and the haptic hook is
  timed by the director's step.
- **A bounded number of objects.** Objects with clues put themselves on a list when they are
  switched on. The director looks through that list 64 objects per frame, keeps the nearest 8
  within 12 m, and starts again a quarter of a second after it has been through all of them.
  Clues and resonance are worked out for those 8 only, each frame, by arithmetic. No physics is
  involved, so what else stands around does not matter, and a level with six hundred objects
  costs the same per frame as one with eight.
- **Answers are event-driven.** An object reports an attempt when it happens. Up to six answers
  play at once, at any distance.
- **No allocation** in the rules that run per frame; this is tested.
- Tints go through the property block the object already used; no material is copied.

Not measured: none of this has been profiled.

## How it is built

Discovery reads the world's state and never changes it. No file under `Runtime/WorldMagic`
depends on it.

| File (under `Assets/Threefold/Scripts`) | Role |
| --- | --- |
| `Runtime/Discovery/DiscoveryRules.cs` | Everything that is decided, in plain C#: what a discipline offers, what could respond, resonance strength, the six answers, how an answer is shown, the escalation, which objects are attended to |
| `Runtime/Discovery/DiscoveryDirector.cs` | The one update: looks up nearby objects, shows clues and resonance, plays answers |
| `Runtime/Discovery/DiscoveryClues.cs` | Per object: listens to its events, classifies attempts, holds the part that moves |
| `Runtime/Discovery/DiscoveryAudio.cs`, `DiscoveryHaptics.cs` | The optional hooks |
| `Editor/Discovery/DiscoverySetup.cs` | Adds the director to a scene |
| `Editor/WorldMagic/MagicalObjectSetup.cs` | Clue component and built clues on the example prefabs |

### Changes to earlier systems

- `MagicalPropertySet.CouldRespondTo` says whether a kind of effect could change the object as
  it is now: with the brittleness it has at this moment, the brace it is under, the heat it
  holds. It reads state and changes nothing.
- `MagicalObject.IsApplyingEffect` tells a change caused by an effect from one that came with time.
- `PropertyStateVisuals` accepts an accent colour and a scale for its sway from outside, so that
  one component writes a renderer's colour.
- The setup of the Mage's rig now also adds the Discovery object.

## Tests

Run by `Tools/ci/compile_check.sh` outside Unity, and by Unity's Test Runner in CI once the Unity
job is enabled.

| Test class | Covers |
| --- | --- |
| `DiscoveryTests` (32) | What each discipline offers, from its values. Resonance on compatible targets, for every example and discipline. No false indicators: wherever "could respond" is denied, the strongest attempt does nothing, in every state the examples reach; and where it is granted, a strong enough attempt works. Resonance following state, and its strength when near, aimed at and preparing. Each of the six answers, including insufficient power. Several properties on one object. Which changes count as a transition. What is shown for other objects' reactions, and repetition. Every answer with a movement and sound of its own; the same answer on every quality level; the options; no number revealed. Clues from properties. Escalation: delay, rise, hold, rest, attempts, progress, combat, leaving. An attempt on something already finished; a held guard; force judged on the object as it is now; the wider sway following the clue option; the game's quality levels. The nearest few of six hundred, looked through a few per frame. No allocation per frame |
| `DiscoverySetupTests` (4) | One runs without Unity: every sound has a placeholder of its own. Three need Unity: the range's director and its connections; clue component, moving part and built clues on every example prefab; clues without a moving part or without a director reported |

**Not covered by automated tests:** everything the components do in Play mode. That includes the
director finding objects, telling what is aimed at, showing clues, resonance and answers, the
sound and the haptic pulse, and how any of it looks.

## Known limits

- Not yet compiled by Unity, run in the Editor, or played on a device. Nothing here has been seen.
- The look is a proposal against the Art Bible, not an approved one. The built clues are
  primitive shapes like the examples themselves.
- "Aimed at" means near the centre of the view. The aim offset from dragging on ATTACK is not
  taken into account, and an object behind a wall can resonate, unseen.
- There is no options screen; the accessibility options are set in the Inspector or from code,
  and are not saved between sessions.
- Sounds are generated placeholders. Device vibration is coarse and off by default; haptics are
  untried on hardware.
- Escalation treats every unresolved object near the player alike. It cannot know which one the
  player is stuck on, and it is not remembered across a reload.
- Changes that come with time (a fire burning something down, a brace running out) are shown by
  the object's own visuals, not answered as feedback.
- Enemies and training dummies have no clues, resonance or answers; they keep the hit feedback
  they had.
- The director attends to one player.
