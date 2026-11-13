using Threefold.Interaction;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Something the player uses by hand: a lever, a wheel, a chest. A toggle switches on and off
    /// with each use; a one-shot switch, such as a chest lid, can be used once. It can require
    /// another element to be active first; until then it is offered but refuses, with a rattle, so
    /// the player learns that something else is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractiveSwitch : PuzzleElement, IInteractable
    {
        [Header("Switch")]
        [SerializeField] private SwitchMode mode = SwitchMode.Toggle;

        [Tooltip("The word shown in the prompt, such as 'Pull' or 'Open'.")]
        [SerializeField] private string verb = "Pull";

        [Tooltip("Optional. The word shown while a toggle is on. If empty, the verb is used.")]
        [SerializeField] private string offVerb = string.Empty;

        [Tooltip("Optional. The switch works only while this element is active.")]
        [SerializeField] private PuzzleElement requires;

        [Tooltip("Where the prompt appears, in this object's space.")]
        [SerializeField] private Vector3 interactionPointOffset = new Vector3(0f, 1f, 0f);

        [Header("Movement")]
        [SerializeField] private MovingPart movingPart = new MovingPart();

        [Min(0.05f)]
        [SerializeField] private float moveSeconds = 0.5f;

        private DoorMotion motion;

        public SwitchMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public string Verb
        {
            get => verb;
            set => verb = value ?? string.Empty;
        }

        public PuzzleElement Requires
        {
            get => requires;
            set => requires = value;
        }

        public MovingPart MovingPart => movingPart;

        public string InteractionVerb => IsActive && !string.IsNullOrEmpty(offVerb) ? offVerb : verb;
        public Vector3 InteractionPoint => transform.TransformPoint(interactionPointOffset);
        public Transform InteractionRoot => transform;

        public override bool IsReady => !IsLocked && (requires == null || requires.IsActive);

        /// <summary>Sets the switch up. Called by the setup tooling.</summary>
        public void Configure(SwitchMode switchMode, string promptVerb, Vector3 promptOffset, Transform part,
            Vector3 moveBy, Vector3 turnBy, float seconds)
        {
            mode = switchMode;
            verb = promptVerb ?? string.Empty;
            interactionPointOffset = promptOffset;
            movingPart = new MovingPart(part, moveBy, turnBy);
            moveSeconds = Mathf.Max(0.05f, seconds);
        }

        public bool CanInteract(GameObject interactor)
        {
            if (!enabled || !gameObject.activeInHierarchy || IsLocked)
            {
                return false;
            }

            return mode == SwitchMode.Toggle || !IsActive;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            Use();
        }

        /// <summary>Uses the switch as the player would. Public so that tests and Unity Events can use it.</summary>
        public SwitchResponse Use()
        {
            bool conditionMet = requires == null || requires.IsActive;
            SwitchResponse response = SwitchRules.Use(mode, IsActive, IsLocked, conditionMet);

            switch (response)
            {
                case SwitchResponse.TurnedOn:
                    SetElementActive(true);
                    break;
                case SwitchResponse.TurnedOff:
                    SetElementActive(false);
                    break;
                case SwitchResponse.Rejected:
                    RaiseRejected();
                    break;
            }

            motion.Target = IsActive;
            return response;
        }

        /// <summary>Moves the part. Runs every frame; public so tests can drive it.</summary>
        public void Tick(float deltaTime)
        {
            motion.Target = IsActive;
            motion.Tick(deltaTime, moveSeconds, moveSeconds);
            movingPart.Apply(motion.Progress);
        }

        protected override void Awake()
        {
            movingPart.CaptureRest();
            base.Awake();
        }

        protected override void OnRestored()
        {
            motion.Target = IsActive;
            motion.Snap();
            movingPart.Apply(motion.Progress);
        }

        private void OnEnable()
        {
            InteractionRegistry.Register(this);
        }

        private void OnDisable()
        {
            InteractionRegistry.Unregister(this);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
