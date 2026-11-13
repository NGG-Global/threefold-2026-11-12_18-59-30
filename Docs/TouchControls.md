# Mobile controls

Sprint LM-01 (Living Magic, part 1) replaces the first touch layout with a control scheme built
for simultaneous movement, camera, melee, blocking, jumping, discipline selection and charged
attacks. Keyboard, mouse and gamepad provide the same actions for testing.

**Status:** the code compiles against the Unity 6000.6.0f1 assemblies, and the input rules pass
102 automated tests that run outside Unity (see Tests). Nothing has been run inside the Unity
Editor or on an Android device. Feel values (thresholds, radii, sensitivities) are first
estimates and need tuning on a device.

![Layout on a phone](TouchControls/images/layout_phone.png)

The pictures (also `TouchControls/images/layout_tablet.png`) are drawn by
`Tools/touchcontrols/render_layout.sh` from the same numbers the setup command uses. They are
diagrams, not screenshots.

## What the player does

| Control | Where | Behaviour |
| --- | --- | --- |
| Joystick | Fixed, bottom left | Takes a touch only within 105 dp of its centre; touches elsewhere never move or activate it. Analogue movement with a dead zone. Push outward past the ring to sprint (a thumb that lands beyond the ring runs, and sprints only after crossing it outward); easing below 60% travel or lifting the thumb ends the sprint |
| Camera | Any free part of the screen | Drag to orbit horizontally and vertically. A drag that starts on a button never turns the camera |
| ATTACK | Bottom right, largest | Tap: standard melee strike. Hold about 0.22 s: charge (a ring fills). Drag while charging: aim. Release: fire the charged attack. Release over the red cancel zone that appears above the thumb: cancel |
| Discipline indicator | On the upper-right rim of ATTACK | Shows the selected discipline. Press and drag onto Arcane, Flame or Aegis, then lift; or tap it and then tap a slot. The selected discipline is also shown on ATTACK |
| BLOCK | Up and left of ATTACK | Press, hold to keep blocking, release. Pressing BLOCK cancels an attack press or charge without firing it |
| JUMP | Left of ATTACK | Hold for a full jump, tap for a short one, as before |
| Interact | Above BLOCK | Appears only when something can be used, labelled with what it does |

There is no permanent spell bar: the three disciplines live in a fan that is visible only while
choosing.

### States of the ATTACK button

| State | What the player sees |
| --- | --- |
| Pressed | The button shrinks slightly and brightens |
| Charging | A ring fills around the button in the discipline's colour; the cancel zone appears above the thumb |
| Aiming | A marker inside a thin aim ring shows the direction and strength of the drag |
| Cancel armed | The cancel zone turns red and grows; the charge ring greys out |
| Released | A short flash: white for a tap, the discipline's colour for a charged release |
| Cancelled | A short red flash |

### Rules that keep inputs from contradicting each other

- Each finger belongs to the control it first touched until it lifts. Lifting one finger never
  ends another finger's action.
- A tap (released before the threshold, without dragging more than 14 dp) is never a charge. Once
  a charge has started, releasing it is never a tap. Dragging further than 14 dp before the
  threshold starts the charge at once, so a quick drag to aim is not mistaken for a tap. (If
  that option is switched off, a short press that drags too far does nothing.)
- A touch cancelled by the system (a call, the notification shade, losing focus) cancels the
  charge. It never fires it.
- Block wins over attack: pressing BLOCK cancels an attack in progress, and a new ATTACK press is
  ignored while BLOCK is held. Jumping and moving are allowed during a charge and a block.
- The open discipline fan takes a touch only on its slots. A touch elsewhere (camera, buttons)
  closes it without changing the discipline and still does what it normally does. The joystick
  thumb is excluded, so a discipline can be chosen while moving.

### Keyboard, mouse and gamepad

| Action | Keyboard and mouse | Gamepad |
| --- | --- | --- |
| Move | WASD or arrow keys | Left stick |
| Camera | Mouse (click the Game view to capture it, Esc to release) | Right stick |
| Attack | Left click: tap to strike, hold to charge, release to fire; the camera aims | West button |
| Block | Right mouse button or Q | Left trigger |
| Jump | Space | South button |
| Sprint | Left Shift | Left stick press |
| Disciplines | 1 Arcane, 2 Flame, 3 Aegis | D-pad left and right |
| Interact | E | North button |

Block and the discipline actions were added to `Assets/InputSystem_Actions.inputactions`. Keys 1
and 2 were moved from the template's unused Previous and Next actions to the discipline actions,
so a number key never both selects and steps. If an actions asset lacks these actions, the same
bindings are created in code.

## Set up

1. Pull `main` (or the pull request branch) on the laptop and let Unity import. The Console must show no red errors.
2. Open the scene and run **Threefold > Setup > Mage Prototype** (or **Threefold > Setup > Touch Controls** in a scene that already has the Mage). Then run **Threefold > Setup > Combat** if it is not part of the scene command you used, so the Staff Strike is generated.
3. Read the report. Save the scene.
4. Upload the new file `Assets/Threefold/Generated/Combat/StaffStrike.asset` with the scene (see `Docs/DevelopmentWorkflow.md`).

### Scenes built with the earlier layout

The command upgrades existing touch controls in place, once. It changes a part only if that part
still has its earlier default, so anything moved by hand is kept and reported:

| Earlier | After the upgrade |
| --- | --- |
| Camera area on the right 58% of the screen | Whole screen, under the buttons |
| Floating joystick | Fixed joystick at its existing position, with a sprint ring |
| Sprint button | Hidden, not deleted. Sprint is on the joystick ring. Activate `SprintButton` to bring it back; it still works |
| Plain attack button | Same object, now the ATTACK gesture button, moved and enlarged |
| Jump and interact buttons | Moved to the new positions |
| (none) | BLOCK, the discipline indicator and fan, the aim ring and the cancel zone are added |

The Mage gains `PlayerDisciplines` (selected discipline) and `PlayerGuard` (block), and its
caster gains the Staff Strike in slot 1. Arcane Bolt stays in slot 0.

## Try it

- **Editor, mouse as one finger:** switch on **Threefold > Controls > Preview Touch Controls In
  Editor**, then press Play. Only one finger can be simulated this way.
- **Editor, several fingers:** **Window > General > Device Simulator**. It also shows notches and
  rounded corners for the safe area.
- **Device:** build for Android from the laptop. Two- and three-finger use can only be judged here.

## Tuning

| Setting | Object | Default |
| --- | --- | --- |
| Activation Radius, Radius (travel), Dead Zone, Response Exponent | `MoveZone` (Virtual Joystick) | 105 dp, 55 dp, 0.12, 1.4 |
| Sprint Threshold, Sprint Release Deflection | `MoveZone` | 1.45 x travel, 0.6 |
| Look Sensitivity, Horizontal and Vertical Look Scale | `TouchControls` (Touch Controls Input) | 1, 1, 0.8 |
| Camera sensitivity, pitch limits, smoothing, collision | `Main Camera` (Third Person Camera) | Unchanged from earlier sprints |
| Hold Threshold, Tap Slop, Full Charge Seconds | `AttackButton` (Attack Gesture Button, Gesture) | 0.22 s, 14 dp, 0.9 s |
| Aim Radius, Aim Dead Zone, Aim Sensitivity, Max Aim Magnitude | `AttackButton` | 64 dp, 0.2, 1, 1 |
| Cancel Offset, Cancel Radius | `AttackButton` | 150 dp above the press, 36 dp |
| Open Timeout | `DisciplineIndicator` (Discipline Selector) | 3 s |
| Hold threshold for the mouse and gamepad | `Mage` (Input System Character Input, Attack Gesture) | 0.22 s |
| Melee and charged ability slots, aim turn | `Mage` (Player Ability Input) | 1, 0, 35 degrees |
| Overall size of all controls | `TouchControls` (Density Canvas Scaler, Scale Multiplier) | 1 |

Sizes are in density-independent pixels (dp, 1/160 inch), so controls have the same physical size
on a phone and a tablet. Every touch area is at least 48 dp, Android's recommended minimum.

## Architecture

The decisions live in plain C# classes with the time passed in, so they are tested without Unity.
The Unity components are thin: they convert screen positions, call the model and draw the result.

| Plain C# (tested outside Unity) | Responsibility |
| --- | --- |
| `TouchControls/PointerRouter` | Which control owns which finger; priority order; releasing fingers that vanish |
| `TouchControls/FixedJoystickModel` | Activation radius, dead zone, response curve, sprint ring |
| `Input/AttackGesture` (`AttackGestureRecognizer`, `CombatInputRules`) | Idle, Pressed, Charging, Aiming, Released, Cancelled; block priority |
| `TouchControls/DisciplineSelectorModel` | Drag-select, tap-open, cancellation, timeout |
| `Magic/DisciplineLoadout`, `Magic/MagicDiscipline` | Selected discipline; the Arcane, Flame and Aegis catalogue |
| `TouchControls/MobileControlsLayout` | Default positions and sizes, and the rules a layout must meet on every screen |

| Unity component | Responsibility |
| --- | --- |
| `TouchControls/TouchControlsInput` | Reads touches every frame before gameplay, feeds the router, publishes the result as a `CharacterInputSource` |
| `TouchControls/VirtualJoystick`, `TouchLookArea`, `TouchButton` | Joystick, camera drag area, BLOCK, JUMP and interact |
| `TouchControls/AttackGestureButton` | ATTACK: feeds the recognizer, shows each state, places the cancel zone |
| `TouchControls/DisciplineSelector`, `DisciplineFan` | Indicator and fan |
| `Input/InputSystemCharacterInput` | Keyboard, mouse and gamepad, through the same recognizer and rules |
| `Input/CombinedCharacterInput` | Merges touch and desktop into the one source gameplay reads |
| `Combat/PlayerAbilityInput` | Tap to melee; charge events; the placeholder charged attack |
| `Combat/PlayerGuard` | Block press, hold and release |
| `Magic/PlayerDisciplines` | Applies discipline requests from any input |
| `Editor/TouchControlsSetup` | Builds and upgrades the controls; no Inspector work |

`CharacterInputSource` gained `AttackGesture`, `BlockHeld`, `BlockPressedThisFrame`,
`BlockReleasedThisFrame`, `DisciplineSlotRequested` and `DisciplineCycleRequested`. They are
virtual with neutral defaults, so other input sources keep working. Movement, camera and
traversal code is unchanged.

### Connecting the Living Magic abilities (LM-02)

`PlayerAbilityInput` raises three events. They are the connection points for the discipline
abilities:

| Event | Payload |
| --- | --- |
| `ChargeStarted` | The discipline selected when the charge began |
| `ChargedAttackReleased` | `ChargedAttackRequest`: discipline at release, aim (length 0 to 1, screen X right and Y up; zero means the camera's aim) and charge progress (0 to 1) |
| `AttackCancelled` | None |

Every `ChargeStarted` is followed by exactly one `ChargedAttackReleased` or `AttackCancelled`,
including when the charge ends because the touch controls were hidden, the app lost focus, or the
Mage grabbed a ledge (a charge released while hanging or climbing is reported as cancelled).

Sprint LM-02 connected the disciplines (`Docs/Disciplines.md`). On a Mage that has
`DisciplineCombat`, taps and charged releases are performed by the selected discipline, and the
Staff Strike and the placeholder bolt of this sprint are switched off; the events above still
fire. In a scene where the discipline setup has not been run, the placeholder remains: a tap is
the Staff Strike and a charged release fires the Arcane Bolt, turned sideways by up to 35 degrees
by the aim.

## Tests

Run by `Tools/ci/compile_check.sh` outside Unity, and by Unity's Test Runner in CI once the Unity
job is enabled:

| Test class | Covers |
| --- | --- |
| `FixedJoystickTests` (15) | Activation radius, fixed centre, dead zone, analogue range, release, sprint ring, its hysteresis, and no sprint for a thumb that lands beyond the ring |
| `AttackGestureTests` (32) | Tap versus hold, jitter tolerance, drag-to-charge, aim dead zone, sensitivity and limits, charge progress, cancel zone, system cancel, block priority, mouse-button path, merging two sources |
| `MultiTouchRoutingTests` (24) | Joystick and camera together, camera touches never resetting movement, drags that start on buttons, two- and three-finger use, release order, vanished fingers, focus loss, discipline selection while moving, the fan not intercepting other touches |
| `DisciplineSelectionTests` (17) | Drag-select, tap-open, every cancellation path, timeout, nearest slot; loadout selection and cycling |
| `MobileControlsLayoutTests` (14) | The layout on ten safe-area sizes from a small 16:9 phone to a 12.9" tablet: inside the safe area, no overlaps, 48 dp touch areas, ATTACK largest, cancel zone out of aim reach; that the validator catches broken layouts; safe-area insets; desktop bindings for every action |
| `MobileControlsSetupTests` (2) | Needs Unity: the assembled scene has the new controls; an earlier-layout scene is upgraded in place and a second run adds nothing |

The routing tests use stand-in controls built on the same models as the real components, on the
real layout numbers. They do not execute the Unity components themselves.

**Not covered by automated tests:** the Unity components' screen-to-canvas conversion, the
visuals, the Input System's touch delivery on a device, the setup command (until the Unity job
runs), and feel.

## Known limits

- Not yet compiled by Unity, run in the Editor, or played on a device. Device testing has not
  been done.
- What the attacks and the block do is described in `Docs/Disciplines.md` (Sprint LM-02). The
  charge ring on ATTACK fills in the time the selected discipline's charged attack needs.
- Holding the attack control no longer fires repeatedly; holding now charges.
- A tap is recognised when the finger lifts, so the melee strike starts as the finger lifts rather
  than when it lands. This is what keeps taps and charges apart.
- The selector can still be opened with a third finger during a charge; the fan and the cancel
  zone then overlap on screen. They do not interfere, because each belongs to its own finger.
- The fan arc is tuned for three disciplines. A fourth slot would leave the screen on small phones
  until the arc is retuned (the layout tests report this).
- Discipline colours and the procedural icons are a proposal, not approved Art Bible swatches.
- The layout is for landscape orientation.
