# Mage Prototype: movement, camera and input

This document covers the first playable foundation for the Mage: a third-person character
controller, an orbit camera, an input layer, and an Editor command that sets up the scene.

## How to set up and test

1. Pull the branch on the development PC and let Unity finish importing.
2. Check the Console window. It must show no red errors before continuing.
3. In the top menu, choose **Threefold > Setup > Mage Prototype**.
   - If `Assets/Scenes/MagePrototype.unity` exists, the command opens it (Unity first offers to save the scene that is open).
   - If it does not exist, the command asks before applying the setup to the scene that is open.
4. In the summary dialog, choose **Save scene**.
5. Press **Play**, then click inside the Game view to capture the mouse.

| Control | Action |
| --- | --- |
| W A S D or arrow keys | Move, relative to the camera |
| Mouse | Orbit the camera |
| Left Shift (hold) | Sprint |
| Space | Jump. Hold for a full jump, tap for a short one |
| Esc | Release the mouse |

A gamepad also works through the bindings already in `Assets/InputSystem_Actions.inputactions`.

## What the setup command does

The command adds only what is missing and fills only references that are empty. Running it
again changes nothing that is already in place, and values tuned in the Inspector are kept.
Everything it does can be reverted with Edit > Undo.

- `PrototypeGround`: kept if present, otherwise created as a 50 m plane.
- `PrototypeObstacles`: a step, a jump box, a wall and a ramp for testing. Created once.
- `Directional Light`: created only if the scene has none.
- `Mage`: found by its movement script or by name, otherwise created with a placeholder capsule body.
- `CharacterController`, movement script and input script: added to the Mage if missing.
- `Main Camera`: the existing main camera is reused, otherwise one is created. The third-person camera script is added to it.

## Code layout

All code is under `Assets/Threefold/Scripts`.

| File | Responsibility |
| --- | --- |
| `Runtime/Input/CharacterInputSource.cs` | Device-independent input contract: move, look, sprint, jump |
| `Runtime/Input/InputSystemCharacterInput.cs` | Reads that input from the Input System actions asset. Handles mouse capture on desktop |
| `Runtime/Characters/ThirdPersonMotor.cs` | Movement, rotation, jumping, gravity and ground detection on a `CharacterController` |
| `Runtime/Cameras/ThirdPersonCamera.cs` | Orbit follow camera with smoothing, pitch limits and clipping avoidance |
| `Editor/MagePrototypeSetup.cs` | The menu command |

The movement and camera scripts never read a device. They read a `CharacterInputSource`, so the
control scheme can change without touching gameplay code.

## Tuning

Select the `Mage` object to tune movement, and `Main Camera` to tune the camera. Each field has a
tooltip. The most useful ones:

- **Movement:** Walk Speed, Sprint Speed, Acceleration, Deceleration, Rotation Smooth Time.
- **Jump:** Jump Height, Gravity, Coyote Time, Jump Buffer Time, Jump Cut Multiplier.
- **Camera:** Distance, Horizontal and Vertical Sensitivity, Min and Max Pitch, Follow Smooth Time.

## Extending

- **Virtual joystick and action buttons:** the Input System's on-screen controls (`OnScreenStick`,
  `OnScreenButton`) act as a virtual gamepad and drive the existing actions. No gameplay code changes.
- **Touch camera:** touchscreen drag is deliberately ignored for look, so a thumb on a joystick
  does not turn the camera. Touch look needs a dedicated screen region that feeds `LookDelta`,
  implemented as a new `CharacterInputSource` or as an extension of the existing one.
- **Animation:** `ThirdPersonMotor` exposes `HorizontalSpeed`, `VerticalSpeed`, `IsGrounded`,
  `IsSprinting`, and the `Jumped` and `Landed` events for an Animator driver.
- **Spellcasting:** set `ThirdPersonMotor.MovementInputEnabled` to false to root the character
  during a cast. New actions such as Attack are already defined in the actions asset.

## Known limits

- The scripts were written and syntax-checked outside Unity. They have not been compiled or
  run in the Unity Editor yet; the first import on the development PC is the real test.
- The Mage body is a placeholder capsule. No model or animation is included.
- There are no touch controls yet, so the prototype is playable in the Editor and with a gamepad only.
