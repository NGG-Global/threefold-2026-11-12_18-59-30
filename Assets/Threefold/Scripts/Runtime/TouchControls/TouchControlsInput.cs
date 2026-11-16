using System.Collections.Generic;
using Threefold.InputHandling;
using Threefold.Magic;
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
    /// Character input from the on-screen touch controls: a fixed joystick on the left, camera drag
    /// on any free part of the screen, and ATTACK (tap, hold to charge, drag to aim), BLOCK and JUMP
    /// on the right, with a compact discipline selector on the ATTACK button and an interact button
    /// that appears only when there is something to use.
    ///
    /// Each finger belongs to the control it first touched until it lifts (see <see cref="PointerRouter"/>),
    /// so moving, turning the camera and acting all work at once, and lifting one finger never ends
    /// another finger's action. Touches are read directly from the Input System every frame, before
    /// gameplay scripts run, so there is no added frame of delay.
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

        /// <summary>The layout this version of the setup command builds. Older controls are upgraded in place.</summary>
        public const int CurrentLayoutVersion = 2;

        [Header("Controls")]
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField] private TouchLookArea lookArea;
        [SerializeField] private TouchButton jumpButton;

        [Tooltip("ATTACK: tap, hold to charge, drag to aim, release to fire.")]
        [SerializeField] private AttackGestureButton attackGesture;

        [SerializeField] private TouchButton blockButton;

        [Tooltip("Optional. The discipline indicator on the ATTACK button and its fan.")]
        [SerializeField] private DisciplineSelector disciplineSelector;

        [Tooltip("Optional. Shown only while there is something to interact with.")]
        [SerializeField] private TouchButton interactButton;

        [Tooltip("Optional. Shows what the interact button will do, such as 'Open'.")]
        [SerializeField] private Text interactLabel;

        [Tooltip("Optional. Used to show and hide all touch controls together.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Earlier layout")]
        [Tooltip("Optional. The sprint button of the first layout. Hidden by the upgrade; it still works if shown again.")]
        [SerializeField] private TouchButton sprintButton;

        [Tooltip("The plain attack button of the first layout. Used only until the setup command upgrades it.")]
        [SerializeField] private TouchButton attackButton;

        [Header("Disciplines")]
        [Tooltip("Optional. The discipline shown on the ATTACK button and the indicator is read from here.")]
        [SerializeField] private PlayerDisciplines disciplines;

        [Header("Behaviour")]
        [Tooltip("Pushing the joystick past its ring sprints.")]
        [SerializeField] private bool sprintFromJoystick = true;

        [Tooltip("For the sprint button, if it is shown. Toggle frees the right thumb for the camera while sprinting.")]
        [SerializeField] private SprintButtonMode sprintMode = SprintButtonMode.Toggle;

        [Tooltip("Look units per pixel of finger travel on a screen that is 1080 pixels high. " +
                 "The value is scaled with screen height, so the same swipe turns the camera equally on every device.")]
        [Min(0f)]
        [SerializeField] private float lookSensitivity = 1f;

        [Tooltip("Multiplies horizontal camera drag.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float horizontalLookScale = 1f;

        [Tooltip("Multiplies vertical camera drag. The camera's own pitch limits still apply.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float verticalLookScale = 0.8f;

        [Tooltip("In the Editor and on desktop, the left mouse button acts as one finger when no touch is active.")]
        [SerializeField] private bool mouseActsAsFinger = true;

        [Tooltip("Opacity change per second when the controls are shown or hidden.")]
        [Min(0f)]
        [SerializeField] private float fadeSpeed = 6f;

        [Tooltip("Gesture settings used only by the plain attack button of the first layout.")]
        [SerializeField] private AttackGestureSettings legacyAttackGesture = new AttackGestureSettings();

        [HideInInspector]
        [SerializeField] private int layoutVersion;

        private const float ReferenceScreenHeight = 1080f;
        private const int MousePointerId = int.MinValue + 1;

        private readonly PointerRouter router = new PointerRouter();
        private readonly List<IPointerTarget> priority = new List<IPointerTarget>(12);

        private AttackGestureRecognizer legacyRecognizer;
        private bool legacyAttackWasHeld;

        private Vector2 move;
        private Vector2 lookDelta;
        private bool sprintHeld;
        private bool sprintLatched;
        private bool jumpHeld;
        private bool jumpPressedThisFrame;
        private bool attackHeld;
        private bool attackPressedThisFrame;
        private AttackGestureFrame attackFrame;
        private bool blockHeld;
        private bool blockPressedThisFrame;
        private bool blockReleasedThisFrame;
        private int disciplineRequest = -1;
        private int displayedDiscipline;
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
        public override AttackGestureFrame AttackGesture => attackFrame;
        public override bool BlockHeld => blockHeld;
        public override bool BlockPressedThisFrame => blockPressedThisFrame;
        public override bool BlockReleasedThisFrame => blockReleasedThisFrame;
        public override int DisciplineSlotRequested => disciplineRequest;
        public override bool InteractPressedThisFrame => interactPressedThisFrame;

        public VirtualJoystick Joystick => joystick;
        public TouchLookArea LookArea => lookArea;
        public TouchButton JumpButton => jumpButton;
        public TouchButton SprintButton => sprintButton;
        public TouchButton BlockButton => blockButton;
        public TouchButton InteractButton => interactButton;
        public AttackGestureButton AttackGestureButton => attackGesture;
        public DisciplineSelector DisciplineSelector => disciplineSelector;
        public PlayerDisciplines Disciplines => disciplines;

        /// <summary>The plain attack button of the first layout, until it is upgraded.</summary>
        public TouchButton LegacyAttackButton => attackButton;

        /// <summary>True if there is an attack control of either kind.</summary>
        public bool HasAttackControl => attackGesture != null || attackButton != null;

        public int LayoutVersion => layoutVersion;

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

        /// <summary>Abandons the press or charge on ATTACK; the button shows the cancellation.</summary>
        public override void InterruptAttack()
        {
            if (attackGesture != null)
            {
                attackGesture.Recognizer.Cancel();
            }

            if (legacyRecognizer != null)
            {
                legacyRecognizer.Cancel();
            }
        }

        /// <summary>Connects the core controls. Called by the setup tooling.</summary>
        public void Bind(VirtualJoystick moveJoystick, TouchLookArea cameraLookArea, TouchButton jump,
            TouchButton sprint, CanvasGroup controlsGroup)
        {
            joystick = moveJoystick;
            lookArea = cameraLookArea;
            jumpButton = jump;
            sprintButton = sprint;
            canvasGroup = controlsGroup;
            CollectTargets();
        }

        /// <summary>Connects the plain attack button of the first layout. Kept for scenes built before the upgrade.</summary>
        public void BindAttackButton(TouchButton attack)
        {
            attackButton = attack;
            CollectTargets();
        }

        /// <summary>Connects ATTACK, BLOCK and the discipline selector, and records the layout version. Called by the setup tooling.</summary>
        public void BindActionControls(AttackGestureButton attack, TouchButton block, DisciplineSelector selector)
        {
            attackGesture = attack;
            blockButton = block;
            disciplineSelector = selector;
            if (attack != null)
            {
                attackButton = null;
            }

            CollectTargets();
        }

        /// <summary>Connects the interact button and its label. Called by the setup tooling.</summary>
        public void BindInteractButton(TouchButton interact, Text label)
        {
            interactButton = interact;
            interactLabel = label;
            CollectTargets();
        }

        /// <summary>Connects the discipline state shown on the controls. Called by the setup tooling.</summary>
        public void BindDisciplines(PlayerDisciplines source)
        {
            disciplines = source;
        }

        /// <summary>Records that the controls match the given layout. Called by the setup tooling.</summary>
        public void MarkLayoutVersion(int version)
        {
            layoutVersion = version;
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
                CancelEverything();
            }

            if (canvasGroup != null && immediate)
            {
                canvasGroup.alpha = show ? 1f : 0f;
            }
        }

        private void OnEnable()
        {
            CollectTargets();

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
            CancelEverything();

            if (enhancedTouchEnabledHere)
            {
                EnhancedTouchSupport.Disable();
                enhancedTouchEnabledHere = false;
            }
        }

        /// <summary>
        /// Losing focus (a notification shade, a call, the app switcher) aborts every touch. Nothing
        /// fires: a charge is cancelled, not released.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                CancelEverything();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                CancelEverything();
            }
        }

        private void Update()
        {
            UpdateFade();

            if (!visible)
            {
                return;
            }

            float now = Time.unscaledTime;
            BeginFrame();
            RoutePointers();
            ReadControls(now);
        }

        private void LateUpdate()
        {
            if (!visible)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            displayedDiscipline = disciplines != null ? disciplines.CurrentSlot : displayedDiscipline;
            DisciplineInfo info = disciplines != null
                ? disciplines.GetInfo(displayedDiscipline)
                : DisciplineCatalog.Get(displayedDiscipline);

            if (attackGesture != null)
            {
                // The charge ring fills in the time the selected discipline's charged attack needs.
                float chargeSeconds = disciplines != null ? disciplines.CurrentFullChargeSeconds : 0f;
                if (chargeSeconds > 0f)
                {
                    attackGesture.Gesture.fullChargeSeconds = chargeSeconds;
                }

                attackGesture.SetDiscipline(info.Color, info.Icon);
                attackGesture.RefreshVisuals(deltaTime);
            }

            if (disciplineSelector != null)
            {
                disciplineSelector.SetCurrent(displayedDiscipline);
                disciplineSelector.RefreshVisuals(deltaTime);
            }
        }

        // ------------------------------------------------------------ routing

        private void BeginFrame()
        {
            if (attackGesture != null)
            {
                attackGesture.BeginFrame();

                // While block is held, a new attack press is not started (see CombatInputRules).
                attackGesture.PressAllowed = CombatInputRules.AttackPressAllowed(blockButton != null && blockButton.IsHeld);
            }

            if (legacyRecognizer != null)
            {
                legacyRecognizer.BeginFrame();
            }

            if (disciplineSelector != null)
            {
                disciplineSelector.BeginFrame();
            }
        }

        private void RoutePointers()
        {
            router.BeginFrame();

            ReadOnlyArray<Touch> touches = Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                Touch touch = touches[i];
                TouchPhase phase = touch.phase;
                Vector2 position = touch.screenPosition;

                if (phase == TouchPhase.Began)
                {
                    router.Begin(touch.touchId, position);
                }
                else if (phase == TouchPhase.Moved || phase == TouchPhase.Stationary)
                {
                    router.Move(touch.touchId, position);
                }
                else if (phase == TouchPhase.Ended)
                {
                    router.End(touch.touchId, position, false);
                }
                else if (phase == TouchPhase.Canceled)
                {
                    router.End(touch.touchId, position, true);
                }
            }

            if (touches.Count == 0)
            {
                RouteMouse();
            }

            router.EndFrame();
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

            bool pressed = mouse.leftButton.wasPressedThisFrame;
            bool released = mouse.leftButton.wasReleasedThisFrame;

            // A quick click can go down and up within one frame: begin and end it in the same frame.
            if (pressed)
            {
                router.Begin(MousePointerId, position);
            }

            if (released)
            {
                router.End(MousePointerId, position, false);
            }
            else if (!pressed && mouse.leftButton.isPressed)
            {
                router.Move(MousePointerId, position);
            }
        }

        /// <summary>A touch that started on any control other than the selector closes an open fan, and still works.</summary>
        private void HandlePointerBegan(int pointerId, Vector2 position, IPointerTarget owner)
        {
            if (disciplineSelector == null || !disciplineSelector.IsOpen)
            {
                return;
            }

            bool selectorTouch = ReferenceEquals(owner, disciplineSelector) ||
                                 ReferenceEquals(owner, disciplineSelector.Fan);

            // The joystick thumb is excluded, so a discipline can be chosen while moving.
            if (!selectorTouch && !ReferenceEquals(owner, joystick))
            {
                disciplineSelector.PressOutside();
            }
        }

        // ------------------------------------------------------------- output

        private void ReadControls(float now)
        {
            move = joystick != null ? joystick.Value : Vector2.zero;

            lookDelta = Vector2.zero;
            if (lookArea != null && Screen.height > 0)
            {
                // The feel settings' look speed is 1 unless it was changed from the diagnostics' Feel panel.
                float pixelsToLookUnits = ReferenceScreenHeight / Screen.height * lookSensitivity *
                                          Threefold.Feel.FeelSettingsStore.Current.LookSpeed;
                Vector2 delta = lookArea.ConsumeDelta() * pixelsToLookUnits;
                lookDelta = new Vector2(delta.x * horizontalLookScale, delta.y * verticalLookScale);
            }

            jumpHeld = jumpButton != null && jumpButton.IsHeld;
            jumpPressedThisFrame = jumpButton != null && jumpButton.WasPressedThisFrame;

            blockHeld = blockButton != null && blockButton.IsHeld;
            blockPressedThisFrame = blockButton != null && blockButton.WasPressedThisFrame;
            blockReleasedThisFrame = blockButton != null && blockButton.WasReleasedThisFrame;

            ReadAttack(now);

            if (disciplineSelector != null)
            {
                disciplineSelector.Tick(now);
                disciplineRequest = disciplineSelector.SelectedThisFrame;
                if (disciplineRequest >= 0)
                {
                    displayedDiscipline = disciplineRequest;
                }
            }
            else
            {
                disciplineRequest = -1;
            }

            interactPressedThisFrame = interactButton != null && interactButton.isActiveAndEnabled
                                       && interactButton.WasPressedThisFrame;

            sprintHeld = ReadSprint();
        }

        private void ReadAttack(float now)
        {
            AttackGestureRecognizer recognizer = null;

            if (attackGesture != null)
            {
                recognizer = attackGesture.Recognizer;
                attackGesture.Tick(now);
            }
            else if (attackButton != null)
            {
                // A scene built before the upgrade: the plain button is read as a gesture without aim.
                recognizer = legacyRecognizer ?? (legacyRecognizer = new AttackGestureRecognizer(legacyAttackGesture));
                bool held = attackButton.IsHeld;
                if (held && !legacyAttackWasHeld && CombatInputRules.AttackPressAllowed(blockHeld))
                {
                    recognizer.Press(now, Vector2.zero);
                }
                else if (!held && legacyAttackWasHeld)
                {
                    recognizer.Release(now, false);
                }

                legacyAttackWasHeld = held;
                recognizer.Update(now);
            }

            if (recognizer == null)
            {
                attackFrame = default;
                attackHeld = false;
                attackPressedThisFrame = false;
                return;
            }

            CombatInputRules.ApplyBlock(recognizer, blockHeld, blockPressedThisFrame);

            bool wasActive = attackHeld;
            attackFrame = recognizer.Snapshot();
            attackHeld = recognizer.IsActive;
            attackPressedThisFrame = attackHeld && !wasActive;
        }

        private bool ReadSprint()
        {
            bool fromJoystick = sprintFromJoystick && joystick != null && joystick.Sprinting;

            if (sprintButton == null || !sprintButton.isActiveAndEnabled)
            {
                sprintLatched = false;
                return fromJoystick;
            }

            if (sprintMode == SprintButtonMode.Hold)
            {
                sprintLatched = false;
                sprintButton.Highlighted = false;
                return fromJoystick || sprintButton.IsHeld;
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
            return fromJoystick || sprintLatched;
        }

        // ------------------------------------------------------------ helpers

        private void CollectTargets()
        {
            router.PointerBegan -= HandlePointerBegan;
            router.PointerBegan += HandlePointerBegan;

            priority.Clear();

            // Priority order: the open fan, the selector, the action buttons, the joystick, then the camera.
            if (disciplineSelector != null)
            {
                Add(disciplineSelector.Fan);
                Add(disciplineSelector);
            }

            Add(attackGesture);
            Add(attackButton);
            Add(blockButton);
            Add(interactButton);
            Add(jumpButton);
            Add(sprintButton);
            Add(joystick);
            Add(lookArea);

            router.SetTargets(priority);
        }

        private void Add(TouchRegion region)
        {
            if (region != null)
            {
                priority.Add(region);
            }
        }

        /// <summary>Releases every finger as cancelled and clears all output, so nothing fires or stays held.</summary>
        private void CancelEverything()
        {
            if (attackGesture != null)
            {
                attackGesture.Recognizer.Cancel();
            }

            if (legacyRecognizer != null)
            {
                legacyRecognizer.Cancel();
            }

            router.ReleaseAll(true);

            if (disciplineSelector != null)
            {
                disciplineSelector.CancelAll();
            }

            legacyAttackWasHeld = false;
            ClearState();
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
            attackFrame = default;
            blockHeld = false;
            blockPressedThisFrame = false;
            blockReleasedThisFrame = false;
            disciplineRequest = -1;
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
