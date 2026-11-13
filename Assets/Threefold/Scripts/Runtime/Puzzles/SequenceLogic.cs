using System;
using System.Collections.Generic;

namespace Threefold.Puzzles
{
    /// <summary>How a completed step of a sequence behaves when its element switches off again.</summary>
    public enum StepRule
    {
        /// <summary>Once done, the step stays done.</summary>
        Latch,

        /// <summary>
        /// The element must stay active until the next step is done. If it switches off before
        /// that, progress falls back to this step. Example: stand on the lookout while you strike
        /// the crystal; the crystal must still be lit when you step on the plate.
        /// </summary>
        HoldUntilNextStep,
    }

    /// <summary>What a change of one step's element did to the sequence.</summary>
    public enum SequenceChange
    {
        None,

        /// <summary>The next step was completed.</summary>
        Progressed,

        /// <summary>A held step was released too early; progress fell back.</summary>
        ProgressLost,

        /// <summary>A step further along than the next one was activated. It does not count.</summary>
        WrongOrder,

        /// <summary>The last step was completed.</summary>
        Solved,
    }

    /// <summary>
    /// The rules of an ordered puzzle, as plain C# so they can be tested without Unity. It is told
    /// when each step's element switches on or off and works out the progress: a step counts only
    /// when its element switches on while it is the next step, held steps fall back when released
    /// early, and once solved nothing changes any more.
    /// </summary>
    public sealed class SequenceLogic
    {
        private readonly StepRule[] rules;
        private readonly bool[] active;
        private int completed;
        private bool solved;

        public SequenceLogic(IReadOnlyList<StepRule> stepRules)
        {
            if (stepRules == null)
            {
                throw new ArgumentNullException(nameof(stepRules));
            }

            rules = new StepRule[stepRules.Count];
            for (int i = 0; i < rules.Length; i++)
            {
                rules[i] = stepRules[i];
            }

            active = new bool[rules.Length];
        }

        public int StepCount => rules.Length;

        /// <summary>How many steps are done, counted from the first.</summary>
        public int CompletedSteps => completed;

        public bool IsSolved => solved;

        public StepRule GetRule(int step)
        {
            return rules[step];
        }

        /// <summary>The last reported state of a step's element.</summary>
        public bool IsStepActive(int step)
        {
            return step >= 0 && step < active.Length && active[step];
        }

        /// <summary>Reports the state of a step's element. Reporting the same state twice changes nothing.</summary>
        public SequenceChange SetStepActive(int step, bool value)
        {
            if (step < 0 || step >= active.Length || active[step] == value)
            {
                return SequenceChange.None;
            }

            active[step] = value;
            if (solved)
            {
                return SequenceChange.None;
            }

            return value ? HandleActivated(step) : HandleDeactivated(step);
        }

        /// <summary>Clears all progress. The known element states are kept, so a step must be activated again to count.</summary>
        public void Reset()
        {
            completed = 0;
            solved = false;
        }

        /// <summary>Marks every step done, for a puzzle that was already solved earlier in the session.</summary>
        public void ForceSolved()
        {
            completed = rules.Length;
            solved = rules.Length > 0;
        }

        private SequenceChange HandleActivated(int step)
        {
            if (step < completed)
            {
                return SequenceChange.None;
            }

            if (step > completed)
            {
                return SequenceChange.WrongOrder;
            }

            completed++;
            if (completed == rules.Length)
            {
                solved = true;
                return SequenceChange.Solved;
            }

            return SequenceChange.Progressed;
        }

        private SequenceChange HandleDeactivated(int step)
        {
            // Only the latest done step can still be undone; the ones before it are confirmed.
            if (step != completed - 1 || rules[step] != StepRule.HoldUntilNextStep)
            {
                return SequenceChange.None;
            }

            completed = step;

            // The step before is no longer confirmed by this one, so if it is held, it must still be held.
            while (completed > 0 && rules[completed - 1] == StepRule.HoldUntilNextStep && !active[completed - 1])
            {
                completed--;
            }

            return SequenceChange.ProgressLost;
        }
    }
}
