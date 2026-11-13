using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A mechanism that stays active for a set time after it is triggered, then expires: a gate
    /// that closes again, a crystal whose charge fades. When it expires it can reset other elements,
    /// which is how a puzzle asks the player to hurry.
    ///
    /// It starts when its trigger element activates, or when <see cref="StartTimer"/> is called
    /// (for example from a Unity Event). Locking it stops the countdown and keeps it active, which
    /// is how a solved puzzle freezes its timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimedMechanism : PuzzleElement
    {
        [Header("Timer")]
        [Tooltip("Optional. The timer starts each time this element activates.")]
        [SerializeField] private PuzzleElement trigger;

        [Tooltip("Seconds the mechanism stays active.")]
        [Min(0.1f)]
        [SerializeField] private float duration = 6f;

        [Tooltip("If triggered again while running, start the full time again.")]
        [SerializeField] private bool restartWhenTriggeredAgain = true;

        [Tooltip("Elements returned to inactive when the time runs out.")]
        [SerializeField] private List<PuzzleElement> resetWhenExpired = new List<PuzzleElement>();

        [Header("Timer hooks")]
        [SerializeField] private UnityEvent onStarted = new UnityEvent();
        [SerializeField] private UnityEvent onExpired = new UnityEvent();

        private CountdownTimer countdown;
        private bool triggerWasActive;
        private bool started;

        /// <summary>Raised when the countdown starts or restarts.</summary>
        public event Action<TimedMechanism> Started;

        /// <summary>Raised when the time runs out.</summary>
        public event Action<TimedMechanism> Expired;

        public PuzzleElement Trigger
        {
            get => trigger;
            set => trigger = value;
        }

        public float Duration
        {
            get => duration;
            set => duration = Mathf.Max(0.1f, value);
        }

        public IReadOnlyList<PuzzleElement> ResetWhenExpired => resetWhenExpired;

        public bool IsRunning => countdown.IsRunning;
        public float Remaining => countdown.Remaining;

        /// <summary>1 when started, 0 when expired.</summary>
        public float RemainingFraction => countdown.RemainingFraction;

        /// <summary>How many of a row of indicator lights should be lit now.</summary>
        public int SegmentsLit(int segments)
        {
            return countdown.SegmentsLit(segments);
        }

        /// <summary>A running timer is not remembered; only a locked (solved) one is.</summary>
        protected override bool PersistsUnlockedState => false;

        /// <summary>Adds an element to reset on expiry, unless it is listed already. Called by the setup tooling.</summary>
        public bool AddResetTarget(PuzzleElement element)
        {
            if (element == null || element == this || resetWhenExpired.Contains(element))
            {
                return false;
            }

            resetWhenExpired.Add(element);
            return true;
        }

        /// <summary>Starts the countdown. Ignored while locked, and while running unless restarting is allowed.</summary>
        public void StartTimer()
        {
            if (IsLocked || (countdown.IsRunning && !restartWhenTriggeredAgain))
            {
                return;
            }

            countdown.Start(duration);
            SetElementActive(true);
            Started?.Invoke(this);
            onStarted.Invoke();
        }

        /// <summary>Stops the countdown and deactivates without resetting anything.</summary>
        public void StopTimer()
        {
            if (IsLocked)
            {
                return;
            }

            countdown.Stop();
            SetElementActive(false);
        }

        /// <summary>Starts the timer if the trigger has activated since the last check. Public so tests can drive it.</summary>
        public void Evaluate()
        {
            bool triggerActive = trigger != null && trigger.IsActive;
            if (triggerActive && !triggerWasActive)
            {
                StartTimer();
            }

            triggerWasActive = triggerActive;
        }

        /// <summary>Advances the countdown. Runs every frame; public so tests can drive it.</summary>
        public void Tick(float deltaTime)
        {
            Evaluate();

            if (!IsLocked && countdown.Tick(deltaTime))
            {
                Expire();
            }
        }

        private void OnEnable()
        {
            if (trigger != null)
            {
                trigger.StateChanged += HandleTriggerChanged;
            }
        }

        private void Start()
        {
            // A trigger that is already active when the scene starts, for example one remembered
            // from earlier in the session, does not start the timer.
            triggerWasActive = trigger != null && trigger.IsActive;
            started = true;
        }

        private void OnDisable()
        {
            if (trigger != null)
            {
                trigger.StateChanged -= HandleTriggerChanged;
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        protected override void OnLocked()
        {
            countdown.Stop();
        }

        private void HandleTriggerChanged(PuzzleElement element)
        {
            if (started)
            {
                Evaluate();
            }
        }

        private void Expire()
        {
            SetElementActive(false);

            for (int i = 0; i < resetWhenExpired.Count; i++)
            {
                if (resetWhenExpired[i] != null)
                {
                    resetWhenExpired[i].ResetState();
                }
            }

            Expired?.Invoke(this);
            onExpired.Invoke();
        }
    }
}
