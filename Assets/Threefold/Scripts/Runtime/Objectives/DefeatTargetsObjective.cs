using System;
using System.Collections.Generic;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Objectives
{
    /// <summary>
    /// An objective that is complete when every listed target has been defeated. It only watches
    /// the targets' health, so it works for any character class and any kind of target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefeatTargetsObjective : MonoBehaviour
    {
        [Tooltip("Shown to the player.")]
        [SerializeField] private string title = "Defeat the training constructs";

        [Tooltip("Everything that must be defeated.")]
        [SerializeField] private List<Health> targets = new List<Health>();

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
        public int TotalCount => targets.Count;
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

        /// <summary>
        /// Recounts the defeated targets and announces any change. Runs automatically whenever a
        /// target is defeated; public so it can be called by tests.
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

            if (defeated != defeatedCount)
            {
                defeatedCount = defeated;
                Progressed?.Invoke(defeatedCount, targets.Count);
            }

            if (!complete && targets.Count > 0 && defeatedCount >= targets.Count)
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
        }
    }
}
