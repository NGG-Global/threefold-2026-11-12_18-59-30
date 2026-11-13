using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Threefold.InputHandling
{
    /// <summary>
    /// Reads character input from an Input System actions asset (the project's
    /// InputSystem_Actions asset by default). Keyboard, mouse and gamepad work through the
    /// bindings defined in that asset, and provide the same actions as the touch controls:
    ///
    /// - Attack: click (or gamepad West) for the standard attack; hold for at least the hold
    ///   threshold to charge, release to fire. Aim with the camera. Pressing Block cancels a charge.
    /// - Block: right mouse button, Q, or the gamepad's left trigger. Hold to keep blocking.
    /// - Disciplines: keys 1, 2 and 3 choose directly; the gamepad's d-pad left and right step through them.
    ///
    /// Block and the discipline actions are optional in the asset. If the asset lacks them, the
    /// bindings above are created in code, so an older asset keeps working.
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
        [SerializeField] private string blockActionName = "Block";

        [Tooltip("Actions that choose a discipline directly, in slot order.")]
        [SerializeField] private string[] disciplineSlotActionNames = { "DisciplineSlot1", "DisciplineSlot2", "DisciplineSlot3" };

        [SerializeField] private string previousDisciplineActionName = "Previous";
        [SerializeField] private string nextDisciplineActionName = "Next";

        [Header("Attack gesture")]
        [Tooltip("Hold threshold and charge time for the attack control. Aim settings are not used: the camera aims.")]
        [SerializeField] private AttackGestureSettings attackGesture = new AttackGestureSettings();

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
        private InputAction blockAction;
        private InputAction previousDisciplineAction;
        private InputAction nextDisciplineAction;
        private InputAction[] disciplineSlotActions = new InputAction[0];
        private readonly List<InputAction> actionsCreatedHere = new List<InputAction>(6);
        private AttackGestureRecognizer attackRecognizer;

        private Vector2 move;
        private Vector2 lookDelta;
        private bool sprintHeld;
        private bool jumpHeld;
        private bool jumpPressedThisFrame;
        private bool attackHeld;
        private bool attackPressedThisFrame;
        private bool interactPressedThisFrame;
        private bool attackWasHeld;
        private AttackGestureFrame attackFrame;
        private bool blockHeld;
        private bool blockWasHeld;
        private bool blockPressedThisFrame;
        private bool blockReleasedThisFrame;
        private int disciplineRequest = -1;
        private int disciplineCycle;
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
        public override AttackGestureFrame AttackGesture => attackFrame;
        public override bool BlockHeld => blockHeld;
        public override bool BlockPressedThisFrame => blockPressedThisFrame;
        public override bool BlockReleasedThisFrame => blockReleasedThisFrame;
        public override int DisciplineSlotRequested => disciplineRequest;
        public override int DisciplineCycleRequested => disciplineCycle;

        private AttackGestureRecognizer AttackRecognizer =>
            attackRecognizer ?? (attackRecognizer = new AttackGestureRecognizer(attackGesture));

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

            EnableIfNeeded(blockAction);
            EnableIfNeeded(previousDisciplineAction);
            EnableIfNeeded(nextDisciplineAction);
            for (int i = 0; i < disciplineSlotActions.Length; i++)
            {
                EnableIfNeeded(disciplineSlotActions[i]);
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < actionsEnabledHere.Count; i++)
            {
                actionsEnabledHere[i].Disable();
            }

            actionsEnabledHere.Clear();

            for (int i = 0; i < actionsCreatedHere.Count; i++)
            {
                actionsCreatedHere[i].Dispose();
            }

            actionsCreatedHere.Clear();
            ReleaseCursor();
            AttackRecognizer.Reset();
            ClearState();
        }

        /// <summary>Losing focus abandons a charge instead of firing it when the button state is reset.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                AttackRecognizer.Cancel();
            }
        }

        private void Update()
        {
            UpdateCursorLock();

            move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            sprintHeld = sprintAction.IsPressed();
            jumpHeld = jumpAction.IsPressed();
            jumpPressedThisFrame = jumpAction.WasPressedThisFrame();
            lookDelta = ReadLook();
            ReadBlock();
            ReadAttack();
            ReadAttackGesture();
            ReadDisciplines();
            ReadInteract();
            TrackDeviceKind();
        }

        /// <summary>
        /// Block follows the same device rules as attack: not from the touchscreen (touch has its own
        /// button), and from the mouse only while it is captured.
        /// </summary>
        private void ReadBlock()
        {
            bool held = false;
            if (blockAction != null && blockAction.IsPressed())
            {
                InputControl control = blockAction.activeControl;
                InputDevice device = control != null ? control.device : null;
                bool mouseFree = device is Mouse && lockCursorForMouseLook && Cursor.lockState != CursorLockMode.Locked;
                held = !(device is Touchscreen) && !mouseFree;
            }

            blockHeld = held;
            blockPressedThisFrame = held && !blockWasHeld;
            blockReleasedThisFrame = !held && blockWasHeld;
            blockWasHeld = held;
        }

        /// <summary>Reads the attack button as a gesture: tap, or hold to charge and release to fire.</summary>
        private void ReadAttackGesture()
        {
            AttackGestureRecognizer recognizer = AttackRecognizer;
            recognizer.BeginFrame();
            float now = Time.unscaledTime;

            if (attackHeld && !attackWasHeld)
            {
                if (CombatInputRules.AttackPressAllowed(blockHeld))
                {
                    recognizer.Press(now, Vector2.zero);
                }
            }
            else if (!attackHeld && attackWasHeld)
            {
                recognizer.Release(now, false);
            }

            attackWasHeld = attackHeld;
            recognizer.Update(now);
            CombatInputRules.ApplyBlock(recognizer, blockHeld, blockPressedThisFrame);
            attackFrame = recognizer.Snapshot();
        }

        private void ReadDisciplines()
        {
            disciplineRequest = -1;
            for (int i = 0; i < disciplineSlotActions.Length; i++)
            {
                if (disciplineSlotActions[i] != null && disciplineSlotActions[i].WasPressedThisFrame())
                {
                    disciplineRequest = i;
                }
            }

            disciplineCycle = 0;
            if (previousDisciplineAction != null && previousDisciplineAction.WasPressedThisFrame())
            {
                disciplineCycle -= 1;
            }

            if (nextDisciplineAction != null && nextDisciplineAction.WasPressedThisFrame())
            {
                disciplineCycle += 1;
            }
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
                                 ?? ActiveDevice(attackAction) ?? ActiveDevice(blockAction);
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

            // Leaving mouse look while charging abandons the charge rather than firing it.
            AttackRecognizer.Cancel();

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

            blockAction = FindOrCreate(map, blockActionName, "<Mouse>/rightButton", "<Keyboard>/q", "<Gamepad>/leftTrigger");
            previousDisciplineAction = FindOrCreate(map, previousDisciplineActionName, "<Gamepad>/dpad/left");
            nextDisciplineAction = FindOrCreate(map, nextDisciplineActionName, "<Gamepad>/dpad/right");

            string[] slotNames = disciplineSlotActionNames ?? new string[0];
            disciplineSlotActions = new InputAction[slotNames.Length];
            for (int i = 0; i < slotNames.Length; i++)
            {
                // Number keys 1, 2, 3 ... for the default slots.
                string key = i < 9 ? $"<Keyboard>/{i + 1}" : null;
                disciplineSlotActions[i] = key != null ? FindOrCreate(map, slotNames[i], key) : map.FindAction(slotNames[i], false);
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

        /// <summary>
        /// Finds an optional action in the map, or creates one with default bindings when the asset
        /// does not have it. Created actions belong to this component and are disposed with it.
        /// </summary>
        private InputAction FindOrCreate(InputActionMap map, string actionName, params string[] defaultBindings)
        {
            if (string.IsNullOrEmpty(actionName))
            {
                return null;
            }

            InputAction action = map.FindAction(actionName, false);
            if (action != null)
            {
                return action;
            }

            action = new InputAction(actionName, InputActionType.Button);
            for (int i = 0; i < defaultBindings.Length; i++)
            {
                action.AddBinding(defaultBindings[i]);
            }

            actionsCreatedHere.Add(action);
            return action;
        }

        private void EnableIfNeeded(InputAction action)
        {
            if (action == null || action.enabled)
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
            attackWasHeld = false;
            attackFrame = default;
            blockHeld = false;
            blockWasHeld = false;
            blockPressedThisFrame = false;
            blockReleasedThisFrame = false;
            disciplineRequest = -1;
            disciplineCycle = 0;
        }
    }
}
