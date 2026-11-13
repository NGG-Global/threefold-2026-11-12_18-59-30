# Touch controls

Sprint 2 adds an on-screen control scheme for Android: a floating joystick for movement, a
drag area for the camera, and jump and sprint buttons. Keyboard, mouse and gamepad keep working.

**Status:** the code has passed a syntax check and the repository checks only. It has not been
compiled in Unity and has not run on an Android device.

## Layout

| Control | Where | Behaviour |
| --- | --- | --- |
| Joystick | Left 42% of the screen, lower 80% | Appears under the thumb where it first touches. Follows the thumb if it drifts beyond the stick's travel. Returns to its resting place on release |
| Camera | Right 58% of the screen | Drag to orbit. No visuals |
| Jump | Bottom right | Hold for a full jump, tap for a short one |
| Sprint | Left of Jump | Tap to start sprinting; tap again or release the joystick to stop |
| Interact | Above Sprint, left of Attack | Shown only while something can be used, labelled with what it does ("Open"). Tap to use it. See `Docs/Puzzles.md` |

Sprint is a toggle so that the right thumb stays free for the camera while sprinting. It can be
changed to hold-to-sprint on the `TouchControls` object (Sprint Mode).

Each finger belongs to the control it first touched until it lifts, so the joystick, the camera
and a button can be used at the same time.

## Set up

1. Pull `main` (or the pull request branch) on the laptop and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Mage Prototype**. It now also creates the touch controls. In a scene that already has the Mage, **Threefold > Setup > Touch Controls** adds only the touch part.
3. Save the scene.

The command builds the whole `TouchControls` hierarchy and connects every reference. Nothing
needs to be created or wired by hand. If touch controls already exist they are left untouched;
to rebuild them with the default layout, delete the `TouchControls` object and run the command again.

One existing reference is changed on purpose: the Mage's movement script and the camera are
pointed at a new `CombinedCharacterInput`, which merges the desktop input with the touch input.

## Try it

**In the Editor with the mouse.** Switch on **Threefold > Controls > Preview Touch Controls In
Editor**, then press Play. The controls stay visible and the left mouse button acts as one
finger. The keyboard still works. Switch the menu item off to return to normal desktop testing.

**In the Editor with multi-touch.** Open **Window > General > Device Simulator** and choose a
phone. This also shows notches and rounded corners, so the safe-area handling can be seen.

**On a device.** Build and run for Android from the laptop. The controls appear automatically.

## Which scheme is active

`ControlSchemeSelector` (on the `TouchControls` object) decides:

- On a phone or tablet the game starts with touch controls; elsewhere with desktop controls.
- After that it follows the device used last. Touching the screen shows the controls. Pressing a
  key or using a gamepad hides them.
- While touch controls are shown, clicking does not capture the mouse.

All input sources stay active the whole time, so switching scheme never drops an input. The
mode can be fixed to Always Touch or Always Desktop in the Inspector.

## Tuning

| Setting | Object | Effect |
| --- | --- | --- |
| Radius | `MoveZone` (Virtual Joystick) | Thumb travel for full speed. Smaller is more sensitive |
| Dead Zone | `MoveZone` | Share of travel around the centre that is ignored |
| Response Exponent | `MoveZone` | 1 is linear; higher gives finer control at low speed |
| Follow Thumb | `MoveZone` | Whether the stick follows a drifting thumb |
| Look Sensitivity | `TouchControls` (Touch Controls Input) | Camera turn per swipe. Independent of screen resolution |
| Sprint Mode | `TouchControls` | Toggle or Hold |
| Scale Multiplier | `TouchControls` (Density Canvas Scaler) | Overall size of all controls |

## How it works

| File (under `Assets/Threefold/Scripts/Runtime`) | Responsibility |
| --- | --- |
| `TouchControls/TouchControlsInput.cs` | Reads touches, assigns each finger to a control, and exposes the result as a `CharacterInputSource` |
| `TouchControls/TouchRegion.cs` | Base class for an area one finger can hold |
| `TouchControls/VirtualJoystick.cs`, `JoystickMath.cs` | Floating joystick; dead zone and response curve |
| `TouchControls/TouchLookArea.cs`, `TouchButton.cs` | Camera drag area; button with pressed and highlighted states |
| `Input/CombinedCharacterInput.cs` | Merges several input sources into one |
| `Input/ControlSchemeSelector.cs` | Chooses and applies the touch or desktop scheme |
| `Interface/SafeAreaFitter.cs` | Keeps the controls clear of notches, cut-outs and rounded corners |
| `Interface/DensityCanvasScaler.cs` | Sizes the controls by pixel density, so they are the same physical size on a phone and a tablet |
| `Interface/ProceduralSprites.cs`, `ProceduralImage.cs` | Draw the discs, rings and icons in code. No image files are used |

Design decisions:

- **Touches are read directly from the Input System**, in the same frame and before the movement
  and camera scripts run. The UI event system is not involved, so the scene needs no EventSystem.
- **Movement and camera code is unchanged.** They still read one `CharacterInputSource`.
- **Sizes are in density-independent pixels** (1/160 inch), the Android convention.
- **No new packages and no asset files.** The UI uses the already installed `com.unity.ugui`.

## Tests

Edit Mode tests in `Assets/Threefold/Tests/Editor` cover the joystick dead zone and response
curve, the merging of input sources, the safe-area calculation and the density scaling. They run
in CI once the Unity job is enabled, and in the Editor under **Window > General > Test Runner**.

Not covered by automated tests: finger routing, the generated visuals, scheme switching, and
anything on a real device. These need a run in the Editor or on Android.

## Known limits

- Not yet compiled in Unity or run on a device.
- An attack button was added in Sprint 3; see `Docs/MageCombat.md`.
- The layout is for landscape orientation.
- Existing touch controls are never modified by the setup command, so a layout change in a later
  version requires deleting `TouchControls` and running the command again.
