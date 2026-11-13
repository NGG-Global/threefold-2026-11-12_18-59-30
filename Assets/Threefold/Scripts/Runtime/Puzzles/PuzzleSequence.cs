using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.Puzzles
{
    /// <summary>One step of a <see cref="PuzzleSequence"/>.</summary>
    [Serializable]
    public sealed class SequenceStep
    {
        [Tooltip("The element whose activation completes this step.")]
        public PuzzleElement element;

        [Tooltip("Latch: done stays done. Hold Until Next Step: the element must stay active until the next step is done.")]
        public StepRule rule = StepRule.Latch;

        public SequenceStep()
        {
        }

        public SequenceStep(PuzzleElement stepElement, StepRule stepRule)
        {
            element = stepElement;
            rule = stepRule;
        }
    }

    /// <summary>
    /// A puzzle that is solved by activating its steps in order. It is active once solved, so a
    /// door can use it as its input. Steps activated out of order do not count, held steps fall
    /// back when released too early (see <see cref="StepRule"/>), and a solved sequence locks
    /// itself and, by default, its active steps, so the outcome stays.
    ///
    /// The rules themselves are in <see cref="SequenceLogic"/>; this component connects them to the
    /// elements in the scene and announces progress for feedback.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleSequence : PuzzleElement
    {
        [Header("Sequence")]
        [SerializeField] private List<SequenceStep> steps = new List<SequenceStep>();

        [Tooltip("Start over when a later step is activated before an earlier one.")]
        [SerializeField] private bool resetOnWrongOrder;

        [Tooltip("When solved, lock the steps that are active, so a lit crystal stays lit.")]
        [SerializeField] private bool lockActiveStepsWhenSolved = true;

        [Tooltip("Further elements to lock when solved, such as the timer, so it stops.")]
        [SerializeField] private List<PuzzleElement> lockWhenSolved = new List<PuzzleElement>();

        [Header("Sequence hooks")]
        [SerializeField] private UnityEvent<int> onStepCompleted = new UnityEvent<int>();
        [SerializeField] private UnityEvent onProgressLost = new UnityEvent();
        [SerializeField] private UnityEvent onSolved = new UnityEvent();

        private SequenceLogic logic;
        private readonly List<PuzzleElement> subscribed = new List<PuzzleElement>();
        private bool started;

        /// <summary>Raised when a step is completed. The argument is the number of completed steps.</summary>
        public event Action<int> StepCompleted;

        /// <summary>Raised when progress falls back. The argument is the number of steps still done.</summary>
        public event Action<int> ProgressLost;

        public event Action Solved;

        /// <summary>Start over when a later step is activated before an earlier one.</summary>
        public bool ResetOnWrongOrder
        {
            get => resetOnWrongOrder;
            set => resetOnWrongOrder = value;
        }

        public IReadOnlyList<SequenceStep> Steps => steps;
        public IReadOnlyList<PuzzleElement> LockWhenSolved => lockWhenSolved;
        public int StepCount => steps.Count;
        public int CompletedSteps => Logic.CompletedSteps;
        public bool IsSolved => IsActive;

        private SequenceLogic Logic
        {
            get
            {
                if (logic == null || logic.StepCount != steps.Count)
                {
                    var rules = new StepRule[steps.Count];
                    for (int i = 0; i < rules.Length; i++)
                    {
                        rules[i] = steps[i] != null ? steps[i].rule : StepRule.Latch;
                    }

                    logic = new SequenceLogic(rules);
                    if (IsActive)
                    {
                        logic.ForceSolved();
                    }
                }

                return logic;
            }
        }

        /// <summary>Replaces the steps. Called by the setup tooling and by tests.</summary>
        public void SetSteps(IEnumerable<SequenceStep> newSteps)
        {
            bool listening = Application.isPlaying && isActiveAndEnabled;
            Unsubscribe();

            steps.Clear();
            if (newSteps != null)
            {
                steps.AddRange(newSteps);
            }

            logic = null;
            if (listening)
            {
                Subscribe();
            }
        }

        /// <summary>Adds an element to lock when solved, unless it is listed already. Called by the setup tooling.</summary>
        public bool AddLockWhenSolved(PuzzleElement element)
        {
            if (element == null || element == this || lockWhenSolved.Contains(element))
            {
                return false;
            }

            lockWhenSolved.Add(element);
            return true;
        }

        /// <summary>Removes entries whose element no longer exists. Called by the setup tooling.</summary>
        /// <returns>The number removed.</returns>
        public int RemoveMissingLockTargets()
        {
            return lockWhenSolved.RemoveAll(element => element == null);
        }

        /// <summary>
        /// Reads the state of every step and updates the progress. Runs whenever a step changes;
        /// public so tests can drive it.
        /// </summary>
        public void Evaluate()
        {
            SequenceLogic rules = Logic;
            if (rules.IsSolved)
            {
                return;
            }

            // Switching off is handled before switching on. Each change normally arrives on its
            // own; when several are found together (in tests, or after re-enabling), a step that
            // switched off is taken into account before one that switched on.
            for (int i = 0; i < steps.Count; i++)
            {
                if (!StepActive(i))
                {
                    Apply(rules.SetStepActive(i, false));
                }
            }

            for (int i = 0; i < steps.Count && !rules.IsSolved; i++)
            {
                if (StepActive(i))
                {
                    Apply(rules.SetStepActive(i, true));
                }
            }
        }

        /// <summary>Clears all progress, unless the sequence is solved.</summary>
        public void ResetProgress()
        {
            if (IsLocked || Logic.IsSolved)
            {
                return;
            }

            int before = logic.CompletedSteps;
            logic.Reset();
            if (before > 0)
            {
                AnnounceLost();
            }
        }

        private void OnEnable()
        {
            Subscribe();

            // The first evaluation waits for Start, when every element has taken over its remembered state.
            if (started)
            {
                Evaluate();
            }
        }

        private void Start()
        {
            started = true;

            // Solved earlier in the session: the elements remember their own locked states, so only
            // the progress needs restoring here.
            if (IsActive)
            {
                Logic.ForceSolved();
            }
            else
            {
                Evaluate();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private bool StepActive(int index)
        {
            SequenceStep step = steps[index];
            return step != null && step.element != null && step.element.IsActive;
        }

        private void Apply(SequenceChange change)
        {
            switch (change)
            {
                case SequenceChange.Progressed:
                    AnnounceStep();
                    break;
                case SequenceChange.ProgressLost:
                    AnnounceLost();
                    break;
                case SequenceChange.WrongOrder:
                    if (resetOnWrongOrder)
                    {
                        ResetProgress();
                    }

                    break;
                case SequenceChange.Solved:
                    AnnounceStep();
                    SetElementActive(true);
                    Lock();
                    LockOutcome();
                    Solved?.Invoke();
                    onSolved.Invoke();
                    break;
            }
        }

        private void AnnounceStep()
        {
            int done = logic.CompletedSteps;
            StepCompleted?.Invoke(done);
            onStepCompleted.Invoke(done);
        }

        private void AnnounceLost()
        {
            ProgressLost?.Invoke(logic.CompletedSteps);
            onProgressLost.Invoke();
        }

        private void LockOutcome()
        {
            if (lockActiveStepsWhenSolved)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (StepActive(i))
                    {
                        steps[i].element.Lock();
                    }
                }
            }

            for (int i = 0; i < lockWhenSolved.Count; i++)
            {
                if (lockWhenSolved[i] != null)
                {
                    lockWhenSolved[i].Lock();
                }
            }
        }

        private void HandleStepChanged(PuzzleElement element)
        {
            Evaluate();
        }

        private void Subscribe()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                PuzzleElement element = steps[i] != null ? steps[i].element : null;
                if (element != null && element != this && !subscribed.Contains(element))
                {
                    element.StateChanged += HandleStepChanged;
                    subscribed.Add(element);
                }
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < subscribed.Count; i++)
            {
                if (subscribed[i] != null)
                {
                    subscribed[i].StateChanged -= HandleStepChanged;
                }
            }

            subscribed.Clear();
        }
    }
}
