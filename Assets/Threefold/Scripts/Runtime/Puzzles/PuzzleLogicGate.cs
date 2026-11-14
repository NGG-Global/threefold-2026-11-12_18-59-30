using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Combines several elements into one: active while all of them are active (two plates held
    /// down at once), or while any of them is (either of two levers). Use it as the input of a door
    /// or the trigger of a timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleLogicGate : PuzzleElement
    {
        [Header("Gate")]
        [SerializeField] private GateMode mode = GateMode.All;
        [SerializeField] private List<PuzzleElement> inputs = new List<PuzzleElement>();

        private readonly List<PuzzleElement> subscribed = new List<PuzzleElement>();
        private bool started;

        public GateMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public IReadOnlyList<PuzzleElement> Inputs => inputs;

        /// <summary>The gate's state follows its inputs, so only a locked state is remembered.</summary>
        protected override bool PersistsUnlockedState => false;

        /// <summary>Replaces the inputs. Called by the setup tooling and by tests.</summary>
        public void SetInputs(IEnumerable<PuzzleElement> newInputs)
        {
            bool listening = Application.isPlaying && isActiveAndEnabled;
            Unsubscribe();
            inputs.Clear();
            if (newInputs != null)
            {
                foreach (PuzzleElement input in newInputs)
                {
                    if (input != null && input != this && !inputs.Contains(input))
                    {
                        inputs.Add(input);
                    }
                }
            }

            if (listening)
            {
                Subscribe();
            }

            Evaluate();
        }

        /// <summary>Recomputes the state from the inputs. Runs whenever an input changes; public so tests can drive it.</summary>
        public void Evaluate()
        {
            int activeCount = 0;
            int total = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i] == null)
                {
                    continue;
                }

                total++;
                if (inputs[i].IsActive)
                {
                    activeCount++;
                }
            }

            SetElementActive(LogicGateRules.Evaluate(mode, activeCount, total));
        }

        private void OnEnable()
        {
            Subscribe();
            if (started)
            {
                Evaluate();
            }
        }

        private void Start()
        {
            // Waits until every input has taken over its remembered state.
            started = true;
            Evaluate();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        protected override void OnWorldStateReplaced()
        {
            if (started)
            {
                Evaluate();
            }
        }

        private void HandleInputChanged(PuzzleElement element)
        {
            Evaluate();
        }

        private void Subscribe()
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i] != null && !subscribed.Contains(inputs[i]))
                {
                    inputs[i].StateChanged += HandleInputChanged;
                    subscribed.Add(inputs[i]);
                }
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < subscribed.Count; i++)
            {
                if (subscribed[i] != null)
                {
                    subscribed[i].StateChanged -= HandleInputChanged;
                }
            }

            subscribed.Clear();
        }
    }
}
