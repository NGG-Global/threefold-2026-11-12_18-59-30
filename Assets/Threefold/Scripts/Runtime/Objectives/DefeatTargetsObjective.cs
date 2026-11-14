using System;
using System.Collections.Generic;
using Threefold.Combat;
using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.Objectives
{
    /// <summary>
    /// An objective that is complete when every listed target has been defeated and every listed goal
    /// is reached. It only watches the targets' health and the goals' state, so it works for any
    /// character class and any kind of target. A goal is any puzzle element, such as the checkpoint at
    /// a level's way out; it counts while it is active.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefeatTargetsObjective : MonoBehaviour
    {
        [Tooltip("Shown to the player.")]
        [SerializeField] private string title = "Defeat the training constructs";

        [Tooltip("Everything that must be defeated.")]
        [SerializeField] private List<Health> targets = new List<Health>();

        [Tooltip("Puzzle elements that must be active, such as the checkpoint at the way out of a level.")]
        [SerializeField] private List<PuzzleElement> goals = new List<PuzzleElement>();

        private int defeatedCount;
        private bool complete;
        private float elapsedSeconds;

        /// <summary>Raised when the number of defeated targets changes. Arguments: defeated, total.</summary>
        public event Action<int, int> Progressed;

        /// <summary>Raised once, when the last target is defeated.</summary>
        public event Action Completed;

        /// <summary>Raised when progress has been cleared for a new attempt.</summary>
        public event Action ProgressReset;

        public string Title => title;
        public IReadOnlyList<Health> Targets => targets;
        public IReadOnlyList<PuzzleElement> Goals => goals;

        /// <summary>Targets and goals together.</summary>
        public int TotalCount => targets.Count + goals.Count;

        /// <summary>Targets defeated and goals reached.</summary>
        public int DefeatedCount => defeatedCount;
        public bool IsComplete => complete;

        /// <summary>Seconds spent on the current attempt. Stops when the objective is complete.</summary>
        public float ElapsedSeconds => elapsedSeconds;

        /// <summary>Sets the title shown to the player. Called by the setup tooling.</summary>
        public void SetTitle(string objectiveTitle)
        {
            title = objectiveTitle;
        }

        /// <summary>Replaces the list of targets. Called by the setup tooling and by tests.</summary>
        public void SetTargets(IEnumerable<Health> newTargets)
        {
            bool listening = isActiveAndEnabled;
            if (listening)
            {
                Unsubscribe();
            }

            targets.Clear();
            if (newTargets != null)
            {
                foreach (Health target in newTargets)
                {
                    if (target != null && !targets.Contains(target))
                    {
                        targets.Add(target);
                    }
                }
            }

            if (listening)
            {
                Subscribe();
            }

            Refresh();
        }

        /// <summary>Replaces the list of goals. Called by the setup tooling and by tests.</summary>
        public void SetGoals(IEnumerable<PuzzleElement> newGoals)
        {
            bool listening = isActiveAndEnabled;
            if (listening)
            {
                Unsubscribe();
            }

            goals.Clear();
            if (newGoals != null)
            {
                foreach (PuzzleElement goal in newGoals)
                {
                    if (goal != null && !goals.Contains(goal))
                    {
                        goals.Add(goal);
                    }
                }
            }

            if (listening)
            {
                Subscribe();
            }

            Refresh();
        }

        /// <summary>
        /// Recounts the defeated targets and reached goals and announces any change. Runs automatically
        /// whenever a target is defeated or a goal changes; public so it can be called by tests.
        /// </summary>
        public void Refresh()
        {
            int defeated = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && !targets[i].IsAlive)
                {
                    defeated++;
                }
            }

            for (int i = 0; i < goals.Count; i++)
            {
                if (goals[i] != null && goals[i].IsActive)
                {
                    defeated++;
                }
            }

            int total = TotalCount;
            if (defeated != defeatedCount)
            {
                defeatedCount = defeated;
                Progressed?.Invoke(defeatedCount, total);
            }

            if (!complete && total > 0 && defeatedCount >= total)
            {
                complete = true;
                Completed?.Invoke();
            }
        }

        /// <summary>
        /// Clears completion and the timer for a new attempt. It does not revive the targets; the
        /// session that restarts the level does that first.
        /// </summary>
        public void ResetProgress()
        {
            complete = false;
            elapsedSeconds = 0f;
            defeatedCount = 0;
            ProgressReset?.Invoke();
            Refresh();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!complete)
            {
                elapsedSeconds += Time.deltaTime;
            }
        }

        private void Subscribe()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].Defeated += Refresh;
                }
            }

            for (int i = 0; i < goals.Count; i++)
            {
                if (goals[i] != null)
                {
                    goals[i].StateChanged += HandleGoalChanged;
                }
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].Defeated -= Refresh;
                }
            }

            for (int i = 0; i < goals.Count; i++)
            {
                if (goals[i] != null)
                {
                    goals[i].StateChanged -= HandleGoalChanged;
                }
            }
        }

        private void HandleGoalChanged(PuzzleElement goal)
        {
            Refresh();
        }
    }
}
