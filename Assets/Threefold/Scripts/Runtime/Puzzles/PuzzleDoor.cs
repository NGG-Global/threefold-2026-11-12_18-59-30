using System;
using Threefold.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.Puzzles
{
    /// <summary>How a door is opened.</summary>
    public enum DoorControl
    {
        /// <summary>Open while its input element is active: a gate opened by a puzzle.</summary>
        Input,

        /// <summary>The player opens and closes it with the interact control.</summary>
        Interact,
    }

    /// <summary>
    /// A door, gate or seal. Its leaf slides, sinks or swings between closed and open; the leaf's
    /// colliders move with it. It is active while open or opening, so other elements can follow it.
    ///
    /// A door that is open when the scene starts, for example because its puzzle was solved
    /// earlier in the session, appears open at once instead of opening in front of the player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleDoor : PuzzleElement, IInteractable
    {
        [Header("Door")]
        [SerializeField] private DoorControl control = DoorControl.Input;

        [Tooltip("With Input control: the door is open while this element is active.")]
        [SerializeField] private PuzzleElement input;

        [Tooltip("Open while the input is inactive instead.")]
        [SerializeField] private bool invertInput;

        [SerializeField] private MovingPart leaf = new MovingPart();

        [Min(0.05f)]
        [SerializeField] private float openSeconds = 2.5f;

        [Min(0.05f)]
        [SerializeField] private float closeSeconds = 1.2f;

        [Header("Interaction")]
        [SerializeField] private string openVerb = "Open";
        [SerializeField] private string closeVerb = "Close";

        [Tooltip("Where the prompt appears, in this object's space.")]
        [SerializeField] private Vector3 interactionPointOffset = new Vector3(0f, 1.2f, 0f);

        [Header("Door hooks")]
        [SerializeField] private UnityEvent onFullyOpened = new UnityEvent();
        [SerializeField] private UnityEvent onFullyClosed = new UnityEvent();

        private DoorMotion motion;
        private bool started;

        /// <summary>Raised when the door comes to rest. The argument is true when it is open.</summary>
        public event Action<bool> MovementFinished;

        public DoorControl Control
        {
            get => control;
            set => control = value;
        }

        public PuzzleElement InputElement
        {
            get => input;
            set => input = value;
        }

        /// <summary>Open while the input is inactive instead.</summary>
        public bool InvertInput
        {
            get => invertInput;
            set => invertInput = value;
        }

        public MovingPart Leaf => leaf;

        /// <summary>0 closed, 1 open.</summary>
        public float OpenAmount => motion.Progress;

        public bool IsMoving => motion.IsMoving;
        public bool IsFullyOpen => motion.Progress >= 1f;

        public string InteractionVerb => IsActive ? closeVerb : openVerb;
        public Vector3 InteractionPoint => transform.TransformPoint(interactionPointOffset);
        public Transform InteractionRoot => transform;

        /// <summary>A door the player opened stays open for the session; one driven by a puzzle follows its input.</summary>
        protected override bool PersistsUnlockedState => control == DoorControl.Interact;

        /// <summary>Sets the movement. Called by the setup tooling.</summary>
        public void ConfigureMotion(Transform leafTransform, Vector3 openOffset, Vector3 openRotation, float secondsToOpen,
            float secondsToClose)
        {
            leaf = new MovingPart(leafTransform, openOffset, openRotation);
            openSeconds = Mathf.Max(0.05f, secondsToOpen);
            closeSeconds = Mathf.Max(0.05f, secondsToClose);
        }

        public bool CanInteract(GameObject interactor)
        {
            return control == DoorControl.Interact && !IsLocked && enabled && gameObject.activeInHierarchy;
        }

        public void Interact(GameObject interactor)
        {
            if (CanInteract(interactor))
            {
                SetOpen(!IsActive, false);
            }
        }

        /// <summary>Opens or closes the door, unless it is locked. With <paramref name="instantly"/> it jumps there.</summary>
        public void SetOpen(bool open, bool instantly)
        {
            SetElementActive(open);
            motion.Target = IsActive;

            if (instantly)
            {
                motion.Snap();
            }

            leaf.Apply(motion.Progress);
        }

        /// <summary>With Input control, follows the input. Public so tests can drive it.</summary>
        public void Evaluate(bool instantly)
        {
            if (control != DoorControl.Input)
            {
                return;
            }

            bool want = input != null && (input.IsActive != invertInput);
            if (want != IsActive || instantly)
            {
                SetOpen(want, instantly);
            }
        }

        /// <summary>Moves the leaf. Runs every frame; public so tests can drive it.</summary>
        public void Tick(float deltaTime)
        {
            Evaluate(false);

            if (motion.Tick(deltaTime, openSeconds, closeSeconds))
            {
                bool open = motion.Target;
                MovementFinished?.Invoke(open);
                if (open)
                {
                    onFullyOpened.Invoke();
                }
                else
                {
                    onFullyClosed.Invoke();
                }
            }

            leaf.Apply(motion.Progress);
        }

        protected override void Awake()
        {
            leaf.CaptureRest();
            base.Awake();
        }

        protected override void OnRestored()
        {
            motion.Target = IsActive;
            motion.Snap();
            leaf.Apply(motion.Progress);
        }

        /// <summary>Follows every change of state, including a reset by a timer, so the leaf always matches.</summary>
        protected override void OnStateChanged(bool isActive)
        {
            motion.Target = isActive;
        }

        private void OnEnable()
        {
            if (control == DoorControl.Interact)
            {
                InteractionRegistry.Register(this);
            }

            if (input != null)
            {
                input.StateChanged += HandleInputChanged;
            }
        }

        private void OnDisable()
        {
            InteractionRegistry.Unregister(this);

            if (input != null)
            {
                input.StateChanged -= HandleInputChanged;
            }
        }

        private void Start()
        {
            // Whatever the state is when the scene starts, show it at once and without announcing
            // it, so a gate opened earlier in the session neither moves nor rumbles.
            started = true;
            if (control == DoorControl.Input)
            {
                SetElementActiveSilently(input != null && (input.IsActive != invertInput));
            }

            motion.Target = IsActive;
            motion.Snap();
            leaf.Apply(motion.Progress);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void HandleInputChanged(PuzzleElement element)
        {
            if (started)
            {
                Evaluate(false);
            }
        }
    }
}
