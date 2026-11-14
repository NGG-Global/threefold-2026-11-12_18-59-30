# World state: persistence and chain reactions

Sprint LM-04 (Living Magic, part 4) decides how long a change to the world lasts, saves what is
meant to last, and lets one magical action set off others within fixed limits. It builds on the
properties and effects of LM-03 (`Docs/WorldMagic.md`).

**Status:** the code compiles against the Unity 6000.6.0f1 assemblies, and the rules pass their
tests outside Unity (see Tests). Nothing has been run inside the Unity Editor or on a device. No
save file has been written by a real build, and no chain reaction has run through real physics.
The budgets below are provisional starting values; none is a measured device limit.

## Three tiers

Every change to the world belongs to one of three tiers. The tier says what happens to it when
the world is reloaded.

| Tier | What it is | Examples | On a reload |
| --- | --- | --- | --- |
| **Immediate** | Something that happens and is over. It is an event, never a state | An impact, a hit flash, a short shove, a burst of particles, a refused attempt | Nothing is stored and nothing is played again |
| **Temporary** | A state that runs out by itself | Burning, heat, arcane charge, a brace, accumulated stress, a glide in progress, an enemy's stagger | Reset to the starting state |
| **Persistent** | A state that is meant to last | An opened gate, a destroyed barrier, a latched mechanism, a solved puzzle, a platform left where it matters | Saved under the object's identifier and restored |

A state is persistent only if two things are true: the object's definition names that part of
its state under **Persisted**, and the object in the scene has a **Persistent Id**. Without an id
an object is as new after every reload.

### What can be saved for a magical object

| Persisted | Keeps | Default |
| --- | --- | --- |
| Destruction | That the structure was destroyed | On |
| Damage | The integrity that is left, so partial damage is kept too | Off |
| Activation | That an arcane mechanism was activated for good (latch) | On |
| Collapse | That the object has come down | On |
| Position | Where a pushed object came to rest | Off |
| Heat | **Exception.** The heat held. The fire itself never survives a reload | Off |
| Charge | **Exception.** The arcane charge held. A timed activation comes back as its charge, not as running | Off |

Heat and charge are temporary by nature. Ticking them is the explicit exception for a puzzle
that depends on them, such as a furnace that must stay hot.

Position is off by default on purpose. A block that can be pushed into a place from which it
cannot be pushed back would stay there for good. Tick it only where the position is the result
that should last.

## Temporary effects

Temporary states are part of the properties (heat, charge, bracing, stress). LM-04 adds the
rules around them.

- **Durations and decay** are values in the definition, as before: burn time, cooling delay and
  rate, charge decay, active time, brace time.
- **Stacking.** What happens when an effect arrives while the same state is running is a policy:

  | State | Policy field | Refresh | Extend | Ignore |
  | --- | --- | --- | --- | --- |
  | Brace | Stability: Brace Stacking | The longer of the two counts (default) | Added to what is left, up to the longest a brace can last | The first brace runs its course |
  | Fire | Thermal: Burn Stacking | The burn starts its time again | One burn time is added, up to twice the burn time | More heat does nothing (default) |
  | Timed activation | Arcane: Active Stacking | The active time starts again | One active time is added, up to twice | More charge does nothing (default) |

  Heat and charge themselves always add up, and each new amount restarts the cooling or decay delay.
- **Expiry before time.** A brace can be given a **Brace Break Force**: a single force of at
  least that strength ends the brace, and is spent on it. Zero, the default, means a brace
  holds against anything. A fire ends when its object is destroyed.
- **Clean-up.** When an object is destroyed, every temporary state on it ends with it: the fire
  goes out, charge is dropped, a brace ends. Nothing is left that needs time to pass.
- **Transitions** are announced as in LM-03, once each, in a fixed order.
- **While switched off.** What happens to temporary state while an object's GameObject is
  inactive (out of range, unloaded) is a setting, **When Inactive**:

  | Setting | Meaning |
  | --- | --- |
  | Pause (default) | Time stands still for it. It continues where it was |
  | Catch Up | The time it was away is made up when it returns, in fixed steps, silently: no announcement, no reaction. After two minutes' worth, what is still temporary is dropped, except heat or charge named under Persisted |
  | Reset | Its temporary state is dropped the moment it is switched off. Heat or charge named under Persisted is kept, as a reload would keep it |

  In every case what is persistent is written to the world state when the object is switched off.

### One simulation instead of an update per object

No magical object has an update or a coroutine of its own. An object joins the **world
simulation** when something starts to change on it and leaves when it has come to rest. The
simulation is one component with one update, created by itself the first time it is needed, and
it switches itself off while nothing is changing.

At most **32** objects are simulated at once. Others wait in the order they asked; their state
stands still until a place is free and nothing is lost. Objects marked **Critical** in their
definition never wait.

## Persistent world state

### One store

There is one store for everything the game remembers about the world: the state of puzzle
elements, which `PuzzleSessionState` has kept since the puzzle sprint, and now the persistent
state of magical objects. `PuzzleSessionState` reads and writes the same store; there is no
second save framework beside it. Puzzle keys and object ids are separate name spaces.

Changes are kept **as they happen**. A gate that was opened is open in the store at once, so it
is still open if the player is defeated a moment later. Values that change continuously (the
damage of a fire still burning, a charge that is draining) are written when the state changes,
when the object comes to rest or is switched off, and again just before every save, restart and
checkpoint, so those never work from a stale value. Only a crash can lose the drift since the
last of these.

### Identifiers

A magical object is saved under its **Persistent Id**, a text that must be unique in the game.

- **Threefold > World Magic > Assign Persistent Ids** gives an id to every selected magical
  object that has none. The id is made from the scene's and the object's name and is made
  unique. Nothing is assigned without a selection: which objects keep their state is a decision
  per object, and a practice target is meant to come back.
- An id that exists is never changed by tooling: changing it would orphan what was saved under it.
- Two objects with one id are reported by the validation. At run time the second is refused and
  is simply not saved; the first is unaffected.
- An id whose owner has been destroyed is free again, even if the owner never said so. (Unity does
  not tell an object that was never switched on that it is destroyed; such an object also stops
  listening to the world by itself, so nothing is called on it afterwards.)

### On disk

| Where | Behaviour |
| --- | --- |
| A build (Android, desktop) | The store is read from `world-state.txt` in the app's data folder before the first scene loads, and written back a few seconds after a change and at once when the app is paused or closed |
| The Unity Editor, by default | Kept in memory only. Every Play session starts with nothing saved, as before |
| The Unity Editor with **Threefold > World State > Keep Saved State Between Play Sessions** ticked | As in a build |

The file is plain text, one record per line, written in a fixed order. A save is written to a
new file, flushed to the storage, and then put in place of the old one, which is kept as a
backup. Loading is cautious:

- A save that was written in full but never put in place (the app was ended between the two
  steps) is the newest, and is used. Before, the older backup was loaded instead.
- A damaged or cut-off file falls back to the backup. The Console says so.
- A single unreadable line is left out and the rest is kept.
- A number that is not a number is refused, and a value outside what an object can hold is
  brought within it: an integrity above the maximum, a position beyond the end of the rail.
- **A file this version cannot use in full is never overwritten or deleted by a later save.** A
  file that is not a world state, is from a newer version, is damaged or cannot be read is set
  aside next to the save, under a name ending in `.unreadable`, and the Console says so. Before,
  the next two saves rotated such a file into the backup's place and then deleted it, so a refused
  save from a newer version was lost. If a file can be neither used nor set aside, nothing is
  saved in that session, to keep it from being overwritten.
- What was loaded from anything but a sound main file (the backup, an interrupted save, part of a
  damaged file) is written again as a sound main file a few seconds later.

### Restart, checkpoint, new game

| Operation | What it does | How |
| --- | --- | --- |
| **Restart** | The world takes its saved state again. Persistent state stays; everything temporary is reset; objects without an id are as new. Puzzle elements are left as they are, as before; a latched property trigger stays latched, and one without a latch follows its object | R, the Restart button, `PrototypeSession.Restart()` |
| **Checkpoint** | A copy of the store is taken, and the store can be put back to it; the world follows | `WorldState.SaveCheckpoint()`, `WorldState.RevertToCheckpoint()`. Nothing in the game calls these yet |
| **New game** | Everything saved is forgotten, on disk too, and the level restarts. Files set aside as unreadable are left alone | **Threefold > World State > New Game (Forget Saved State)**, `PrototypeSession.NewGame()` |

Loading is silent. A door that was opened earlier is simply open; a structure that had come down
lies down. Nothing that already happened is announced or played again, and no reaction is sent.

### Initial rules, as asked

| Rule | How it is met |
| --- | --- |
| Completed puzzles remain completed | Puzzle elements with a persistence key, as before, now in the saved store |
| Opened permanent gates remain open | A latched activation is saved (Activation); a latched trigger with a key is saved as a puzzle element |
| Destroyed persistent barriers remain destroyed | Destruction is saved for objects with an id |
| Temporary heat and charge reset on reload | Always, unless named as an exception |
| Exceptions are explicit | Heat and Charge under Persisted in the definition |

## Chain reactions

### How one thing sets off another

An object reacts on others only through **reaction rules** in its definition. A rule says: when
this state starts (On Enter), or again and again while it lasts (While Holding, at an interval),
send this effect, of this magnitude, to everything within this radius and, if asked, to the
object's **Linked Targets**. What the effect does there follows from the target's own
properties, exactly as for an attack. Nothing spreads that is not written down as such a rule.

The primary rules are the properties of LM-03. Together with the reaction rules of the examples
they give the initial rule set:

| Rule | Where it lives |
| --- | --- |
| Heat + flammable material -> ignition | Thermal property: capacity, flammable |
| Heat + structural vulnerability -> increased brittleness | Structural property: brittleness when heated |
| Brittleness + force -> fracture | Structural property: force threshold, force damage |
| Arcane charge + compatible mechanism -> activation | Arcane property: threshold, kind of activation |
| Force + movable object -> controlled displacement | Kinetic property: resistance, minimum force, limits |
| Stabilise + unstable structure -> temporary bracing | Stability property: brace threshold and time |

Every threshold is a value in a definition. There is no rule for a particular puzzle.

### The examples' reactions

| Example | Reaction | Priority |
| --- | --- | --- |
| Wooden barricade | While burning, every second: Heat 18 to everything within 2.6 m | Normal |
| Heat-sensitive stone | When destroyed: Force 2 to everything within 2.5 m | Low |
| Unstable structure | When it collapses: Force 3.5 to everything within 3.5 m | Normal |
| Arcane crystal | When activated: Arcane Charge 75 to its linked targets | Critical |

### The limits

Reactions are not carried out by the object that causes them. Each is offered to a queue, and a
fixed number are carried out per frame.

| Budget | Default | Meaning |
| --- | --- | --- |
| Reaction depth | 3 | How many steps a chain may be from what the player did |
| Reactions per frame | 8 | How many are carried out per frame; the rest wait |
| Queue capacity | 64 | How many of normal priority, and as many of low priority, may wait |
| Active objects | 32 | How many objects are simulated at once |

They are set per scene on the **World Simulation Budgets** component (in the range, on the
`MagicalObjects` group). All four are provisional.

**Depth.** An attack's effects have depth 0. An effect sent by an object that an attack changed
has depth 1, and so on. The depth travels with the effect and stays with the state it causes: a
barricade set alight by an effect of depth 2 sends heat of depth 3, however much later. An
effect deeper than the limit is not sent. This is what ends a slow chain as well as a fast one:
a fire lit by the player reaches three further barricades in a row and stops at the fourth.

**No loops, no duplicates.** A reaction to an attack keeps the attack's identity. A target takes
each kind of effect once per identity, so an effect cannot come back to where it started, and a
target reached by two paths is acted on once. The queue also refuses to hold the same effect of
the same event for the same target twice. If the second copy is more urgent than the one that
waits (a critical one behind a low one), the waiting one gives way instead, so a puzzle never
loses its effect to a weaker copy that happened to be offered first.

**Walls.** A reaction by distance is stopped by scenery between the two objects, by the same
test an attack's area uses. Creatures do not shield what stands behind them. A reaction to a
linked target is not affected by distance or walls.

**Order.** Critical reactions first, then normal, then low; within a priority, first offered,
first carried out. Objects are advanced in the order they joined the simulation, and a reaction
goes to linked targets in the order listed, then to what is in reach, nearest first. The same
situation therefore plays out the same way every time. (Two targets at exactly the same distance
are taken in the order the physics engine reports them, which is not guaranteed.)

**When there is too much to do.** What cannot be carried out in a frame waits for the next. When
a queue is full, a normal or low reaction is refused, counted, and reported in the Console.
**A critical reaction is never refused for lack of room**: the queue grows for it, and that is
counted too. Mark as critical the reactions a puzzle depends on.

**A critical reaction is never lost without a word.** It can still not happen for three reasons
that are part of the design: it would be deeper than the chain may go, its target had already
taken the same effect of the same event by another path, or its target was destroyed, unloaded or
switched off before it arrived. Each of these is reported in the Console at once, naming the
target and the reason, so a puzzle that depends on it can be fixed. Before, these were dropped
silently. A reload is the one exception: what was on its way belonged to the world as it was, and
is dropped with the rest of the temporary state (the Console notes how many critical ones).

**Objects that are gone.** A target is checked again at the moment of delivery. An object that
is destroyed, unloaded or switched off leaves the simulation, and everything on its way to it is
given up, so nothing is called on it afterwards. What it had already sent out still happens.

**Reach.** At most 16 targets take a reaction sent by distance. When more stand within reach,
the nearest 16 are kept, whatever order the physics engine reports them in.

## In the Discipline Range

The setup adds to the lane of LM-03:

- **Persistent ids** on the examples that keep something: barricade and stone (destruction),
  platform (position), unstable structure (collapse, destruction), door (destruction, damage,
  activation).
  The crystal's activation is timed and therefore temporary; it has no id.
- **A row of five barricades** behind the lane, each within reach of the next one's fire and of
  no other. Light the first: it and the next three burn down, and the fifth stays untouched.
  They have no ids, so a restart brings them back.
- **A link** from the crystal to the reinforced door. When the crystal activates, the door's
  lock receives 75 charge, half of what it needs, so one full Arcane Arrow at the lock opens it
  instead of two. The lock's charge starts to drain after 3 s, which leaves about 5 s for the arrow.
- The **budgets** component.

The six examples in the lane stand too far apart to set each other off, with one exception: if
the platform has been pushed to its far stop, the unstable structure next to it shoves it back
along its rail when it comes down. Reach is measured from the centre of the object that reacts
to the nearest point of the other.

## Set up

1. Pull `main` on the laptop and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Discipline Range**, read the report and save the scene. It updates
   the example definitions once (what is saved, their reactions; other values are kept, and so
   are these if you have already set any of them by hand), gives the examples their ids, links
   the crystal, and places the chain row and the budgets.
3. Run **Threefold > Validate > Open Scene**.
4. Upload the scene and the changed assets in `Assets/Threefold/Generated/WorldMagic`.

To see persistence in the Editor, tick **Threefold > World State > Keep Saved State Between
Play Sessions**, destroy something, stop Play and press Play again. **Show What Is Saved**
prints the store. **New Game** clears it.

For a new object of your own: tick what is to be kept under **Persisted** in its definition,
select the object and run **Assign Persistent Ids**, add reaction rules under **Reactions**, and
list **Linked Targets** on the object if a rule goes to them.

## Tuning

| To change | Where |
| --- | --- |
| What an object keeps, what happens while it is switched off, whether it is critical | Its definition: Persistence |
| What an object sets off | Its definition: Reactions |
| Stacking and break force | Its definition: Thermal, Arcane, Stability |
| Depth, reactions per frame, queue capacity, active objects, seconds between saves | `MagicalObjects`: World Simulation Budgets |
| Which objects a reaction is sent to by link, and which layers it searches | The object: Reactions |

## Validation

**Threefold > Validate > Magical Objects**, and the scene validation, now also report:

- A persistent id used by more than one object.
- An id on an object whose definition saves nothing.
- Persisted parts the object does not have: damage without a structure, position on something
  that cannot move, heat or charge without the property.
- A reaction that can never fire, has no magnitude, reaches nothing, or would repeat for ever.
- A reaction that goes to linked targets only on an object that lists none; a linked target that
  is missing or cannot receive effects; linked targets that no reaction uses.

## Performance

- Logic and presentation are apart. An object's state is in its properties; `PropertyStateVisuals`
  only shows it. While none of an object's renderers is drawn, colours, sway and bursts are not
  computed, and the state is shown again the moment the object is seen.
- One update for all temporary state, none while the world is at rest.
- Reactions are found by one physics query per reaction sent, into a shared buffer, and only when
  a rule fires: on a change of state, or once per interval. Nothing is scanned per frame.
- Nothing propagates physically. There is no Rigidbody in the chain; a reaction is an effect
  with a radius, bounded in depth and in number per frame.
- Saving allocates (it builds a text) and writes a file. It happens a few seconds after a change
  and on pause, not per frame. The time it takes on a device has not been measured.

Not measured: none of this has been profiled.

## How it is built

| File (under `Assets/Threefold/Scripts`) | Role |
| --- | --- |
| `Runtime/Persistence/WorldStateStore.cs` | The store, the record of an object, tiers, snapshots. Plain C# |
| `Runtime/Persistence/WorldStateText.cs` | The text format, reading with care, the file with its backup. Plain C# |
| `Runtime/Persistence/WorldState.cs` | The session: restart, checkpoint, new game, save, load. Plain C# |
| `Runtime/Persistence/PersistentIdRegistry.cs` | Which object owns which id; duplicates. Plain C# |
| `Runtime/Persistence/WorldStateBootstrap.cs`, `WorldStateSaver.cs` | Where the file is, loading at start, writing after changes and on pause |
| `Runtime/Puzzles/PuzzleSessionState.cs` | Unchanged for its callers; now reads and writes the store |
| `Runtime/WorldMagic/MagicalPropertySet.cs` | Capture and load of an object's state, clearing temporary state, catching up, reaction rules and depth. Plain C# |
| `Runtime/WorldMagic/ReactionRules.cs` | A reaction rule, priorities, behaviour while inactive. Plain C# |
| `Runtime/WorldMagic/ReactionScheduler.cs` | The bounded queue and the set of simulated objects, with the budgets. Plain C# |
| `Runtime/WorldMagic/WorldSimulation.cs`, `WorldSimulationBudgets.cs` | The one update; the budgets per scene |
| `Runtime/WorldMagic/MagicalObject.cs` | Joins the simulation, sends reactions, saves and loads |
| `Editor/WorldMagic/WorldStateMenu.cs` | The World State menu |

### Changes to earlier systems

- `MagicalObject` no longer has its own update and is no longer switched off while at rest; the
  world simulation advances it. `IsAwake` says whether it is being simulated.
- `MagicalObject.Restore()` now also forgets what was saved for the object. To take the saved
  state, reload the world.
- A restart now reloads every magical object: temporary state is reset, and objects without an
  id are as new. Before, a restart left magical objects as they were.
- A `PropertyStateTrigger` shows the state its scene starts in without announcing it.
- Puzzle elements with a key follow the store when it is replaced as a whole (checkpoint, new
  game, load). A restart leaves them as they are, as before.
- A magical effect carries its reaction depth.
- Heat on something already burning is still ignored by default; it is now a policy, and it
  means the same for enemies and training dummies as for objects.
- `AreaStrike.IsBehindCover` is public, so reactions use the same cover test as attacks.

## Tests

Run by `Tools/ci/compile_check.sh` outside Unity, and by Unity's Test Runner in CI once the Unity
job is enabled.

| Test class | Covers |
| --- | --- |
| `WorldStateTests` (30) | The store and its revision; snapshots; the text format written and read back, in a fixed order, with awkward ids; refusal of foreign and newer files; damaged and cut-off text; the file with its backup; repeated saving and loading; restart, checkpoint and new game; objects asked for their present state before a save, a restart and a checkpoint; duplicate ids; an id freed when its owner is gone; an interrupted save used; a file from a newer version and a damaged file set aside and never overwritten; saving stopped when a file can be neither used nor set aside |
| `MagicalPersistenceTests` (34) | Each persistent part restored, on each example; temporary state reset; the heat and charge exceptions; loading again and again; loading is silent; records that do not fit; rules between properties after a load; tiers; the three stacking policies; a breakable brace; clean-up on destruction; clearing temporary state; catching up; reaction rules firing once per entry and at their interval; reaction depth; a latch through a restart and through a replaced state; the update of an older definition keeping what was set by hand; the burn policy on a creature; heat and charge exceptions kept when temporary state is cleared or time is made up |
| `ChainReactionTests` (28) | The queue: order, budget per frame, priorities, overflow, critical never refused, depth limit, duplicates, a more urgent copy taking the place of a waiting one, every reaction given up reported, a critical reaction turned away by its target reported, targets that are gone, cancelling, no nesting. The simulated set: budget, waiting, critical, order, leaving during a step. Whole chains with the examples: a fire along a row to the depth limit, a row of structures toppling, no way back, two paths acting once, a crystal feeding a linked lock, destruction in the middle of a chain, forty simultaneous critical reactions, low priority giving way, and a chain saved half-way and loaded |
| `WorldStateSetupTests` (5) | Needs Unity: the range's ids, chain row and link; duplicate ids reported; assigning ids; the update of an older definition; an id with nothing to save and a broken link |

The chain tests use a small stand-in world that does what the component does in a scene (finds
what is in reach, nearest first, and offers each effect to the queue). The rules, the queue and
the budgets are the real ones.

**Not covered by automated tests:** everything the components do in Play mode. That includes a
magical object saving and loading through the store in a running scene, the simulation's update,
the physics query that finds a reaction's targets (and keeping the nearest 16), the Console
reports of lost critical reactions, an object destroyed without ever being switched on, the file
being written and flushed on a device and on pause, the visuals suspending when unseen, and the
Editor menu commands.

## Known limits

- Not yet compiled by Unity, run in the Editor, or played on a device.
- The four budgets are provisional. They have not been measured on any device.
- On a device there is no control yet to clear the saved world; in the Editor there is the menu
  command. Until a control exists, a device build keeps what was saved until the app's data is cleared.
- Nothing in the game takes a checkpoint yet. The operations exist and are tested.
- The player's own state (position, health, mana, discipline) is not saved, and neither are
  enemies. A restart puts them back as before.
- After a new game or a return to a checkpoint, puzzle switches, doors and logic gates follow
  the store, but a sequence that was solved and a timer that was running keep their inner
  progress until the scene is loaded again.
- Creatures receive reactions (a Wisp next to a burning barricade heats up) but send none.
- An object that waits for a place in the simulation shows no change until it gets one.
- A persistent id made from names changes meaning if the object is renamed and the id is then
  cleared and assigned again. Ids are never changed by themselves.
- If two saved versions of the game disagree about an object's limits, the saved value is brought
  within the current limits; it is not migrated.
- Save data is not encrypted or protected against editing.
- Whether the flush to storage before a save is put in place reaches the device's storage on
  Android has not been checked on hardware.
- The search for a reaction's targets reads at most 48 colliders per query. In a place crowded
  with scenery on the reaction layers, some objects within reach may not be seen.
- Two scenes that share a name produce the same ids for objects that share a name. The validation
  checks one scene at a time.
