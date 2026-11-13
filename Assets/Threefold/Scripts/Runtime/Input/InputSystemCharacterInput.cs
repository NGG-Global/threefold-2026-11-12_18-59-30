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
        [SerializeField] private string attackActionName = "Attack";
        [SerializeField] private string interactActionName = "Interact";

        [Tooltip("Binding group of the keyboard and mouse in the actions asset. Used to name the interact key in prompts.")]
        [SerializeField] private string keyboardBindingGroup = "Keyboard&Mouse";

        [Tooltip("Binding group of the gamepad in the actions asset. Used to name the interact button in prompts.")]
        [SerializeField] private string gamepadBindingGroup = "Gamepad";

        [Header("Look")]
        [Tooltip("Look units per second produced by a gamepad stick at full deflection.")]
        [Min(0f)]
        [SerializeField] private float stickLookSpeed = 900f;

        [Tooltip("Desktop only. Click the Game view to capture the mouse, press Esc to release it. " +
                 "Mouse movement turns the camera only while the mouse is captured.")]
        [SerializeField] private bool lockCursorForMouseLook = true;

        private readonly List<InputAction> actionsEnabledHere = new List<InputAction>(6);

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private InputAction attackAction;
        private InputAction interactAction;

        private Vector2 move;
        private Vector2 lookDelta;
        private bool sprintHeld;
        private bool jumpHeld;
        private bool jumpPressedThisFrame;
        private bool attackHeld;
        private bool attackPressedThisFrame;
        private bool interactPressedThisFrame;
        private bool usingGamepad;
        private string keyboardInteractLabel;
        private string gamepadInteractLabel;
        private bool suppressAttackUntilRelease;
        private bool cursorLockedHere;
        private bool mouseCaptureEnabled = true;

        public override Vector2 Move => move;
        public override Vector2 LookDelta => lookDelta;
        public override bool SprintHeld => sprintHeld;
        public override bool JumpHeld => jumpHeld;
        public override bool JumpPressedThisFrame => jumpPressedThisFrame;
        public override bool AttackHeld => attackHeld;
        public override bool AttackPressedThisFrame => attackPressedThisFrame;
        public override bool InteractPressedThisFrame => interactPressedThisFrame;

        /// <summary>True if a gamepad was used more recently than the keyboard and mouse.</summary>
        public bool UsingGamepad => usingGamepad;

        /// <summary>
        /// The name of the key or button that interacts, for whichever of keyboard or gamepad was used
        /// last, such as "E". Empty if the actions asset has no interact action.
        /// </summary>
        public string InteractBindingLabel
        {
            get
            {
                if (interactAction == null)
                {
                    return string.Empty;
                }

                // Display strings allocate, so each is built once. The game reacts to the press itself,
                // so a Hold interaction is not mentioned.
                if (usingGamepad)
                {
                    return gamepadInteractLabel ?? (gamepadInteractLabel =
                        interactAction.GetBindingDisplayString(InputBinding.MaskByGroup(gamepadBindingGroup),
                        InputBinding.DisplayStringOptions.DontIncludeInteractions));
                }

                return keyboardInteractLabel ?? (keyboardInteractLabel =
                    interactAction.GetBindingDisplayString(InputBinding.MaskByGroup(keyboardBindingGroup),
                        InputBinding.DisplayStringOptions.DontIncludeInteractions));
            }
        }

        /// <summary>Actions asset this component reads. Change it only while the component is disabled.</summary>
        public InputActionAsset ActionsAsset
        {
            get => actionsAsset;
            set => actionsAsset = value;
        }

        /// <summary>
        /// Whether clicking may capture the mouse for camera look. Turned off while touch controls
        /// are in use, so that the pointer stays free to operate them. Turning it off releases
        /// a captured mouse.
        /// </summary>
        public bool MouseCaptureEnabled
        {
            get => mouseCaptureEnabled;
            set
            {
                mouseCaptureEnabled = value;
                if (!value)
                {
                    ReleaseCursor();
                }
            }
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

            if (attackAction != null)
            {
                EnableIfNeeded(attackAction);
            }

            if (interactAction != null)
            {
                EnableIfNeeded(interactAction);
            }
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
            ReadAttack();
            ReadInteract();
            TrackDeviceKind();
        }

        private void ReadInteract()
        {
            interactPressedThisFrame = false;

            // WasPressedThisFrame follows the button itself, so the Hold interaction on this action
            // in the default actions asset does not delay it.
            if (interactAction == null || !interactAction.WasPressedThisFrame())
            {
                return;
            }

            // Touch has its own interact button.
            InputControl control = interactAction.activeControl;
            if (control != null && control.device is Touchscreen)
            {
                return;
            }

            interactPressedThisFrame = true;
        }

        /// <summary>Remembers whether the gamepad or the keyboard and mouse was used last, for prompts.</summary>
        private void TrackDeviceKind()
        {
            InputDevice device = ActiveDevice(moveAction) ?? ActiveDevice(jumpAction) ?? ActiveDevice(interactAction)
                                 ?? ActiveDevice(attackAction);
            if (device is Gamepad)
            {
                usingGamepad = true;
            }
            else if (device is Keyboard || device is Mouse)
            {
                usingGamepad = false;
            }
        }

        private static InputDevice ActiveDevice(InputAction action)
        {
            if (action == null || !action.IsPressed())
            {
                return null;
            }

            InputControl control = action.activeControl;
            return control != null ? control.device : null;
        }

        private void ReadAttack()
        {
            attackHeld = false;
            attackPressedThisFrame = false;

            if (attackAction == null)
            {
                return;
            }

            bool held = attackAction.IsPressed();
            if (!held)
            {
                suppressAttackUntilRelease = false;
                return;
            }

            InputControl control = attackAction.activeControl;
            InputDevice device = control != null ? control.device : null;

            // A tap anywhere on a touchscreen must not attack; touch has its own attack button.
            if (device is Touchscreen)
            {
                return;
            }

            if (device is Mouse)
            {
                // The click that captures the mouse, and clicks while it is free, are not attacks.
                bool captured = !lockCursorForMouseLook || Cursor.lockState == CursorLockMode.Locked;
                if (!captured || suppressAttackUntilRelease)
                {
                    return;
                }
            }

            attackHeld = true;
            attackPressedThisFrame = attackAction.WasPressedThisFrame();
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
            if (!lockCursorForMouseLook || !mouseCaptureEnabled || Application.isMobilePlatform)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                cursorLockedHere = true;
                suppressAttackUntilRelease = true;
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

            // Attack is optional: without it the character can still move.
            attackAction = map.FindAction(attackActionName, false);
            if (attackAction == null)
            {
                Debug.LogWarning(
                    $"{nameof(InputSystemCharacterInput)} on '{name}': action '{attackActionName}' was not found in " +
                    $"action map '{map.name}'. Attacking from keyboard, mouse and gamepad is unavailable.", this);
            }

            // Interact is optional too.
            interactAction = map.FindAction(interactActionName, false);
            if (interactAction == null)
            {
                Debug.LogWarning(
                    $"{nameof(InputSystemCharacterInput)} on '{name}': action '{interactActionName}' was not found in " +
                    $"action map '{map.name}'. Interacting from keyboard and gamepad is unavailable.", this);
            }

            keyboardInteractLabel = null;
            gamepadInteractLabel = null;

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
            attackHeld = false;
            attackPressedThisFrame = false;
            interactPressedThisFrame = false;
            suppressAttackUntilRelease = false;
        }
    }
}
