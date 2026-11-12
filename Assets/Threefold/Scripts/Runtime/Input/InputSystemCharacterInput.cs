using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Threefold.InputHandling
{
    /// <summary>
    /// Reads character input from an Input System actions asset (the project's
    /// InputSystem_Actions asset by default). Keyboard, mouse and gamepad work through the
    /// bindings already defined in that asset. Input System on-screen controls drive the same
    /// actions, so a virtual joystick and buttons can be added later without changing this class.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class InputSystemCharacterInput : CharacterInputSource
    {
        [Header("Actions")]
        [Tooltip("Actions asset to read. If empty, the project-wide actions asset is used.")]
        [SerializeField] private InputActionAsset actionsAsset;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string lookActionName = "Look";
        [SerializeField] private string jumpActionName = "Jump";
        [SerializeField] private string sprintActionName = "Sprint";

        [Header("Look")]
        [Tooltip("Look units per second produced by a gamepad stick at full deflection.")]
        [Min(0f)]
        [SerializeField] private float stickLookSpeed = 900f;

        [Tooltip("Desktop only. Click the Game view to capture the mouse, press Esc to release it. " +
                 "Mouse movement turns the camera only while the mouse is captured.")]
        [SerializeField] private bool lockCursorForMouseLook = true;

        private readonly List<InputAction> actionsEnabledHere = new List<InputAction>(4);

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction sprintAction;

        private Vector2 move;
        private Vector2 lookDelta;
        private bool sprintHeld;
        private bool jumpHeld;
        private bool jumpPressedThisFrame;
        private bool cursorLockedHere;

        public override Vector2 Move => move;
        public override Vector2 LookDelta => lookDelta;
        public override bool SprintHeld => sprintHeld;
        public override bool JumpHeld => jumpHeld;
        public override bool JumpPressedThisFrame => jumpPressedThisFrame;

        /// <summary>Actions asset this component reads. Change it only while the component is disabled.</summary>
        public InputActionAsset ActionsAsset
        {
            get => actionsAsset;
            set => actionsAsset = value;
        }

        private void OnEnable()
        {
            if (!ResolveActions())
            {
                enabled = false;
                return;
            }

            EnableIfNeeded(moveAction);
            EnableIfNeeded(lookAction);
            EnableIfNeeded(jumpAction);
            EnableIfNeeded(sprintAction);
        }

        private void OnDisable()
        {
            for (int i = 0; i < actionsEnabledHere.Count; i++)
            {
                actionsEnabledHere[i].Disable();
            }

            actionsEnabledHere.Clear();
            ClearState();
            ReleaseCursor();
        }

        private void Update()
        {
            UpdateCursorLock();

            move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            sprintHeld = sprintAction.IsPressed();
            jumpHeld = jumpAction.IsPressed();
            jumpPressedThisFrame = jumpAction.WasPressedThisFrame();
            lookDelta = ReadLook();
        }

        private Vector2 ReadLook()
        {
            Vector2 raw = lookAction.ReadValue<Vector2>();
            InputControl control = lookAction.activeControl;
            if (control == null)
            {
                return Vector2.zero;
            }

            InputDevice device = control.device;

            // Dragging anywhere on a touchscreen must not turn the camera, otherwise the thumb on a
            // future virtual joystick would also rotate the view. Touch look needs its own source.
            if (device is Touchscreen)
            {
                return Vector2.zero;
            }

            if (device is Mouse)
            {
                bool captured = !lockCursorForMouseLook || Cursor.lockState == CursorLockMode.Locked;
                return captured ? raw : Vector2.zero;
            }

            if (device is Pointer)
            {
                return raw;
            }

            // Sticks report a direction, not a distance, so convert to a per-frame amount.
            return raw * (stickLookSpeed * Time.unscaledDeltaTime);
        }

        private void UpdateCursorLock()
        {
            if (!lockCursorForMouseLook || Application.isMobilePlatform)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                cursorLockedHere = true;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && cursorLockedHere && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReleaseCursor();
            }
        }

        private void ReleaseCursor()
        {
            if (!cursorLockedHere)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorLockedHere = false;
        }

        private bool ResolveActions()
        {
            InputActionAsset asset = actionsAsset != null ? actionsAsset : InputSystem.actions;
            if (asset == null)
            {
                Debug.LogError(
                    $"{nameof(InputSystemCharacterInput)} on '{name}' has no actions asset assigned and the project " +
                    "has no project-wide actions asset. Assign one in the Inspector.", this);
                return false;
            }

            InputActionMap map = asset.FindActionMap(actionMapName, false);
            if (map == null)
            {
                Debug.LogError(
                    $"{nameof(InputSystemCharacterInput)} on '{name}': action map '{actionMapName}' was not found " +
                    $"in '{asset.name}'.", this);
                return false;
            }

            moveAction = FindAction(map, moveActionName);
            lookAction = FindAction(map, lookActionName);
            jumpAction = FindAction(map, jumpActionName);
            sprintAction = FindAction(map, sprintActionName);

            return moveAction != null && lookAction != null && jumpAction != null && sprintAction != null;
        }

        private InputAction FindAction(InputActionMap map, string actionName)
        {
            InputAction action = map.FindAction(actionName, false);
            if (action == null)
            {
                Debug.LogError(
                    $"{nameof(InputSystemCharacterInput)} on '{name}': action '{actionName}' was not found in " +
                    $"action map '{map.name}'.", this);
            }

            return action;
        }

        private void EnableIfNeeded(InputAction action)
        {
            if (action.enabled)
            {
                return;
            }

            action.Enable();
            actionsEnabledHere.Add(action);
        }

        private void ClearState()
        {
            move = Vector2.zero;
            lookDelta = Vector2.zero;
            sprintHeld = false;
            jumpHeld = false;
            jumpPressedThisFrame = false;
        }
    }
}
