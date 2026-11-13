using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// A group of enemies that fight together. When one of them notices the player, the others
    /// join in after short, staggered delays, so a group turns towards the player one by one
    /// instead of all at the same instant. The encounter reports when it starts and when every
    /// member has been defeated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyEncounter : MonoBehaviour
    {
        public enum EncounterState
        {
            Dormant,
            Engaged,
            Cleared,
        }

        [Tooltip("Shown in the Inspector and in logs.")]
        [SerializeField] private string displayName = "Encounter";

        [SerializeField] private List<EnemyController> members = new List<EnemyController>();

        [Tooltip("Members further than this from the one that raised the alert do not hear it, in metres.")]
        [Min(0f)]
        [SerializeField] private float alertRadius = 25f;

        [Tooltip("Shortest delay before an alerted member reacts, in seconds.")]
        [Min(0f)]
        [SerializeField] private float alertDelayMin = 0.25f;

        [Tooltip("Longest delay before an alerted member reacts, in seconds.")]
        [Min(0f)]
        [SerializeField] private float alertDelayMax = 0.9f;

        public string DisplayName => displayName;
        public IReadOnlyList<EnemyController> Members => members;
        public EncounterState State { get; private set; }

        /// <summary>Raised when the first member notices the player.</summary>
        public event Action<EnemyEncounter> Engaged;

        /// <summary>Raised when the last member is defeated.</summary>
        public event Action<EnemyEncounter> Cleared;

        /// <summary>Sets the name and members. Called by the setup tooling.</summary>
        public void Configure(string encounterName, IEnumerable<EnemyController> encounterMembers)
        {
            bool listening = isActiveAndEnabled;
            if (listening)
            {
                Unsubscribe();
            }

            displayName = encounterName;
            members.Clear();
            if (encounterMembers != null)
            {
                foreach (EnemyController member in encounterMembers)
                {
                    if (member != null && !members.Contains(member))
                    {
                        members.Add(member);
                    }
                }
            }

            if (listening)
            {
                Subscribe();
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null)
                {
                    members[i].Alerted += HandleAlerted;
                    members[i].Defeated += HandleDefeated;
                    members[i].Restored += HandleRestored;
                }
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null)
                {
                    members[i].Alerted -= HandleAlerted;
                    members[i].Defeated -= HandleDefeated;
                    members[i].Restored -= HandleRestored;
                }
            }
        }

        private void HandleAlerted(EnemyController source)
        {
            if (State == EncounterState.Dormant)
            {
                State = EncounterState.Engaged;
                Engaged?.Invoke(this);
            }

            Vector3 targetPosition = source.LastKnownTargetPosition;
            Vector3 origin = source.transform.position;
            float delay = alertDelayMin;
            float step = members.Count > 1 ? (alertDelayMax - alertDelayMin) / (members.Count - 1) : 0f;

            for (int i = 0; i < members.Count; i++)
            {
                EnemyController member = members[i];
                if (member == null || member == source || member.IsDefeated)
                {
                    continue;
                }

                if (alertRadius > 0f && (member.transform.position - origin).sqrMagnitude > alertRadius * alertRadius)
                {
                    continue;
                }

                member.ReceiveAllyAlert(targetPosition, delay);
                delay += step;
            }
        }

        private void HandleDefeated(EnemyController member)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null && !members[i].IsDefeated)
                {
                    return;
                }
            }

            if (State != EncounterState.Cleared)
            {
                State = EncounterState.Cleared;
                Cleared?.Invoke(this);
            }
        }

        private void HandleRestored(EnemyController member)
        {
            State = EncounterState.Dormant;
        }
    }
}
