using Threefold.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Threefold.TouchControls
{
    /// <summary>
    /// Character input from on-screen touch controls: a floating joystick, a camera look area,
    /// jump, sprint and attack buttons, and an interact button that appears only while there is
    /// something to interact with.
    ///
    /// Each finger is assigned to the control it first touched and stays with it until lifted,
    /// so the joystick, the camera and the buttons can all be used at the same time. Touches are
    /// read directly from the Input System every frame, before gameplay scripts run, so there is
    /// no added frame of delay.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class TouchControlsInput : CharacterInputSource
    {
        public enum SprintButtonMode
        {
            /// <summary>Sprint only while the button is held.</summary>
            Hold,

            /// <summary>Tap to start sprinting, tap again or release the joystick to stop.</summary>
            Toggle,
        }

        [Header("Controls")]
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField] private TouchLookArea lookArea;
        [SerializeField] private TouchButton jumpButton;
        [SerializeField] private TouchButton sprintButton;
        [SerializeField] private TouchButton attackButton;

        [Tooltip("Optional. Shown only while there is something to interact with.")]
        [SerializeField] private TouchButton interactButton;

        [Tooltip("Optional. Shows what the interact button will do, such as 'Open'.")]
        [SerializeField] private Text interactLabel;

        [Tooltip("Optional. Used to show and hide all touch controls together.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Behaviour")]
        [Tooltip("Toggle frees the right thumb for the camera while sprinting.")]
        [SerializeField] private SprintButtonMode sprintMode = SprintButtonMode.Toggle;

        [Tooltip("Look units per pixel of finger travel on a screen that is 1080 pixels high. " +
                 "The value is scaled with screen height, so the same swipe turns the camera equally on every device.")]
        [Min(0f)]
        [SerializeField] private float lookSensitivity = 1f;

        [Tooltip("In the Editor and on desktop, the left mouse button acts as one finger when no touch is active.")]
        [SerializeField] private bool mouseActsAsFinger = true;

        [Tooltip("Opacity change per second when the controls are shown or hidden.")]
        [Min(0f)]
        [SerializeField] private float fadeSpeed = 6f;

        private const float ReferenceScreenHeight = 1080f;
        private const int MousePointerId = int.MinValue + 1;
        private const int MaxRegions = 6;

        private readonly TouchRegion[] regions = new TouchRegion[MaxRegions];
        private readonly bool[] regionSeen = new bool[MaxRegions];
        private int regionCount;

        private Vector2 move;
        private Vector2 lookDelta;
        private bool sprintHeld;
        private bool sprintLatched;
        private bool jumpHeld;
        private bool jumpPressedThisFrame;
        private bool attackHeld;
        private bool attackPressedThisFrame;
        private bool interactPressedThisFrame;
        private bool visible = true;
        private bool enhancedTouchEnabledHere;

        public override Vector2 Move => move;
        public override Vector2 LookDelta => lookDelta;
        public override bool SprintHeld => sprintHeld;
        public override bool JumpHeld => jumpHeld;
        public override bool JumpPressedThisFrame => jumpPressedThisFrame;
        public override bool AttackHeld => attackHeld;
        public override bool AttackPressedThisFrame => attackPressedThisFrame;
        public override bool InteractPressedThisFrame => interactPressedThisFrame;

        public VirtualJoystick Joystick => joystick;
        public TouchButton JumpButton => jumpButton;
        public TouchButton SprintButton => sprintButton;
        public TouchButton AttackButton => attackButton;
        public TouchButton InteractButton => interactButton;

        /// <summary>Whether the controls are shown and respond to touch.</summary>
        public bool Visible => visible;

        public float LookSensitivity
        {
            get => lookSensitivity;
            set => lookSensitivity = Mathf.Max(0f, value);
        }

        public SprintButtonMode SprintMode
        {
            get => sprintMode;
            set => sprintMode = value;
        }

        /// <summary>Connects the controls. Called by the setup tooling.</summary>
        public void Bind(VirtualJoystick moveJoystick, TouchLookArea cameraLookArea, TouchButton jump,
            TouchButton sprint, CanvasGroup controlsGroup)
        {
            joystick = moveJoystick;
            lookArea = cameraLookArea;
            jumpButton = jump;
            sprintButton = sprint;
            canvasGroup = controlsGroup;
            CollectRegions();
        }

        /// <summary>Connects the attack button. Called by the setup tooling.</summary>
        public void BindAttackButton(TouchButton attack)
        {
            attackButton = attack;
            CollectRegions();
        }

        /// <summary>Connects the interact button and its label. Called by the setup tooling.</summary>
        public void BindInteractButton(TouchButton interact, Text label)
        {
            interactButton = interact;
            interactLabel = label;
            CollectRegions();
        }

        /// <summary>
        /// Shows the interact button, labelled with what it will do, while there is something to
        /// interact with, and hides it otherwise. Called every frame by the interaction prompt.
        /// </summary>
        public void SetInteractAvailable(bool available, string verb)
        {
            if (interactButton == null)
            {
                return;
            }

            if (available && interactLabel != null && interactLabel.text != verb)
            {
                interactLabel.text = verb;
            }

            if (interactButton.gameObject.activeSelf != available)
            {
                interactButton.gameObject.SetActive(available);
            }
        }

        /// <summary>Shows or hides the controls. Hidden controls ignore touches and report no input.</summary>
        public void SetVisible(bool show, bool immediate = false)
        {
            visible = show;

            if (!show)
            {
                ReleaseAll();
                ClearState();
            }

            if (canvasGroup != null && immediate)
            {
                canvasGroup.alpha = show ? 1f : 0f;
            }
        }

        private void OnEnable()
        {
            CollectRegions();

            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
                enhancedTouchEnabledHere = true;
            }

            if (canvasGroup != null)
            {
                // Touches are routed by this component, not by the UI event system.
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.alpha = visible ? 1f : 0f;
            }
        }

        private void OnDisable()
        {
            ReleaseAll();
            ClearState();

            if (enhancedTouchEnabledHere)
            {
                EnhancedTouchSupport.Disable();
                enhancedTouchEnabledHere = false;
            }
        }

        private void Update()
        {
            UpdateFade();

            if (!visible)
            {
                return;
            }

            RoutePointers();
            ReadControls();
        }

        // ------------------------------------------------------------ routing

        private void RoutePointers()
        {
            for (int i = 0; i < regionCount; i++)
            {
                regionSeen[i] = false;
            }

            ReadOnlyArray<Touch> touches = Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                Touch touch = touches[i];
                TouchPhase phase = touch.phase;
                Vector2 position = touch.screenPosition;

                if (phase == TouchPhase.Began)
                {
                    BeginPointer(touch.touchId, position);
                }
                else if (phase == TouchPhase.Moved || phase == TouchPhase.Stationary)
                {
                    MovePointer(touch.touchId, position);
                }
                else if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
                {
                    MovePointer(touch.touchId, position);
                    EndPointer(touch.touchId);
                }
            }

            if (touches.Count == 0)
            {
                RouteMouse();
            }

            // A finger that disappeared without an end event must not leave a control stuck.
            for (int i = 0; i < regionCount; i++)
            {
                if (regions[i].IsCaptured && !regionSeen[i])
                {
                    regions[i].Release();
                }
            }
        }

        private void RouteMouse()
        {
            if (!mouseActsAsFinger || Application.isMobilePlatform)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || Cursor.lockState == CursorLockMode.Locked)
            {
                return;
            }

            Vector2 position = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                BeginPointer(MousePointerId, position);
            }
            else if (mouse.leftButton.isPressed)
            {
                MovePointer(MousePointerId, position);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                MovePointer(MousePointerId, position);
                EndPointer(MousePointerId);
            }
        }

        private void BeginPointer(int pointerId, Vector2 position)
        {
            // Regions are ordered by priority, so a button wins over the look area beneath it.
            for (int i = 0; i < regionCount; i++)
            {
                TouchRegion region = regions[i];
                if (!region.IsCaptured && region.isActiveAndEnabled && region.Contains(position))
                {
                    region.Capture(pointerId, position);
                    regionSeen[i] = true;
                    return;
                }
            }
        }

        private void MovePointer(int pointerId, Vector2 position)
        {
            int index = FindRegion(pointerId);
            if (index < 0)
            {
                return;
            }

            regions[index].Drag(position);
            regionSeen[index] = true;
        }

        private void EndPointer(int pointerId)
        {
            int index = FindRegion(pointerId);
            if (index < 0)
            {
                return;
            }

            regions[index].Release();
            regionSeen[index] = true;
        }

        private int FindRegion(int pointerId)
        {
            for (int i = 0; i < regionCount; i++)
            {
                if (regions[i].PointerId == pointerId)
                {
                    return i;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------- output

        private void ReadControls()
        {
            move = joystick != null ? joystick.Value : Vector2.zero;

            lookDelta = Vector2.zero;
            if (lookArea != null && Screen.height > 0)
            {
                float pixelsToLookUnits = ReferenceScreenHeight / Screen.height * lookSensitivity;
                lookDelta = lookArea.ConsumeDelta() * pixelsToLookUnits;
            }

            jumpHeld = jumpButton != null && jumpButton.IsHeld;
            jumpPressedThisFrame = jumpButton != null && jumpButton.WasPressedThisFrame;

            attackHeld = attackButton != null && attackButton.IsHeld;
            attackPressedThisFrame = attackButton != null && attackButton.WasPressedThisFrame;

            interactPressedThisFrame = interactButton != null && interactButton.isActiveAndEnabled
                                       && interactButton.WasPressedThisFrame;

            sprintHeld = ReadSprint();
        }

        private bool ReadSprint()
        {
            if (sprintButton == null)
            {
                return false;
            }

            if (sprintMode == SprintButtonMode.Hold)
            {
                sprintLatched = false;
                sprintButton.Highlighted = false;
                return sprintButton.IsHeld;
            }

            if (sprintButton.WasPressedThisFrame)
            {
                sprintLatched = !sprintLatched;
            }

            // Letting go of the joystick ends the sprint, so the next movement starts at walking pace.
            if (joystick != null && !joystick.IsCaptured)
            {
                sprintLatched = false;
            }

            sprintButton.Highlighted = sprintLatched;
            return sprintLatched;
        }

        // ------------------------------------------------------------ helpers

        private void CollectRegions()
        {
            regionCount = 0;

            // Priority order: buttons first, then the joystick, then the look area.
            AddRegion(attackButton);
            AddRegion(interactButton);
            AddRegion(jumpButton);
            AddRegion(sprintButton);
            AddRegion(joystick);
            AddRegion(lookArea);
        }

        private void AddRegion(TouchRegion region)
        {
            if (region != null && regionCount < MaxRegions)
            {
                regions[regionCount] = region;
                regionSeen[regionCount] = false;
                regionCount++;
            }
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < regionCount; i++)
            {
                if (regions[i] != null)
                {
                    regions[i].Release();
                }
            }
        }

        private void ClearState()
        {
            move = Vector2.zero;
            lookDelta = Vector2.zero;
            sprintHeld = false;
            sprintLatched = false;
            jumpHeld = false;
            jumpPressedThisFrame = false;
            attackHeld = false;
            attackPressedThisFrame = false;
            interactPressedThisFrame = false;

            if (sprintButton != null)
            {
                sprintButton.Highlighted = false;
            }
        }

        private void UpdateFade()
        {
            if (canvasGroup == null)
            {
                return;
            }

            float target = visible ? 1f : 0f;
            canvasGroup.alpha = fadeSpeed > 0f
                ? Mathf.MoveTowards(canvasGroup.alpha, target, fadeSpeed * Time.unscaledDeltaTime)
                : target;
        }
    }
}
