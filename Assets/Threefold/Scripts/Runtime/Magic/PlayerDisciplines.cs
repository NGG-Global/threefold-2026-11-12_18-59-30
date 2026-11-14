using System;
using System.Collections.Generic;
using Threefold.Combat;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>
    /// Holds the player's disciplines and which one is selected, and changes the selection on request
    /// from the input: the touch selector, the number keys, or the gamepad's d-pad. Combat reads the
    /// selected discipline's melee combo and charged attack from here, and the controls its colour
    /// and icon. The selection rules are in <see cref="DisciplineLoadout"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // After the input sources (-100), before the attack that reads the discipline (0).
    public sealed class PlayerDisciplines : MonoBehaviour, IDisciplineCombatData
    {
        [SerializeField] private CharacterInputSource inputSource;
        [SerializeField] private MagicDiscipline startingDiscipline = MagicDiscipline.Arcane;

        [Tooltip("The discipline assets, in selector order: Arcane, Flame, Aegis. Without them the disciplines can " +
                 "be selected but have no attacks.")]
        [SerializeField] private List<DisciplineDefinition> definitions = new List<DisciplineDefinition>();

        private DisciplineLoadout loadout;

        /// <summary>Raised with the new discipline whenever the selection changes.</summary>
        public event Action<MagicDiscipline> Changed;

        public CharacterInputSource InputSource
        {
            get => inputSource;
            set => inputSource = value;
        }

        public int CurrentSlot => Loadout.Current;

        public MagicDiscipline Current => (MagicDiscipline)Loadout.Current;

        public int Count => Loadout.Count;

        public IReadOnlyList<DisciplineDefinition> Definitions => definitions;

        /// <summary>The selected discipline's asset, or null if none is assigned.</summary>
        public DisciplineDefinition CurrentDefinition => GetDefinition(CurrentSlot);

        /// <summary>The asset of a discipline, or null if none is assigned.</summary>
        public DisciplineDefinition GetDefinition(int slot)
        {
            return slot >= 0 && slot < definitions.Count ? definitions[slot] : null;
        }

        /// <summary>Name, colour and icon of a discipline: from its asset, or the built-in catalogue without one.</summary>
        public DisciplineInfo GetInfo(int slot)
        {
            DisciplineDefinition definition = GetDefinition(slot);
            return definition != null ? definition.Info : DisciplineCatalog.Get(slot);
        }

        /// <summary>Seconds the selected discipline's charged attack takes to charge fully, or 0 if it has none.</summary>
        public float CurrentFullChargeSeconds
        {
            get
            {
                ChargeProfile charge = GetCharge(CurrentSlot);
                return charge != null ? charge.fullChargeSeconds : 0f;
            }
        }

        public MeleeComboProfile GetMelee(int slot)
        {
            DisciplineDefinition definition = GetDefinition(slot);
            return definition != null ? definition.Melee : null;
        }

        public ChargeProfile GetCharge(int slot)
        {
            DisciplineDefinition definition = GetDefinition(slot);
            return definition != null && definition.ChargedAbility != null ? definition.ChargedAbility.Charge : null;
        }

        public float GetGuardPulseSeconds(int slot)
        {
            DisciplineDefinition definition = GetDefinition(slot);
            return definition != null && definition.GuardEffect.HasEffect ? definition.GuardEffect.pulseSeconds : 0f;
        }

        /// <summary>Puts a discipline asset into a slot, extending the list if needed. Called by the setup tooling.</summary>
        public void SetDefinition(int slot, DisciplineDefinition definition)
        {
            if (slot < 0)
            {
                return;
            }

            while (definitions.Count <= slot)
            {
                definitions.Add(null);
            }

            definitions[slot] = definition;
        }

        private DisciplineLoadout Loadout
        {
            get
            {
                if (loadout == null)
                {
                    loadout = new DisciplineLoadout(DisciplineCatalog.Count, (int)startingDiscipline);
                    loadout.Changed += HandleChanged;
                }

                return loadout;
            }
        }

        /// <summary>Connects the input. Called by the setup tooling.</summary>
        public void Bind(CharacterInputSource input)
        {
            inputSource = input;
        }

        public bool Select(MagicDiscipline discipline)
        {
            return Loadout.Select((int)discipline);
        }

        public bool SelectSlot(int slot)
        {
            return Loadout.Select(slot);
        }

        public bool Cycle(int step)
        {
            return Loadout.Cycle(step);
        }

        private void Update()
        {
            if (inputSource == null)
            {
                return;
            }

            // A direct choice wins over stepping when both arrive in the same frame.
            int slot = inputSource.DisciplineSlotRequested;
            if (slot >= 0)
            {
                Loadout.Select(slot);
                return;
            }

            int step = inputSource.DisciplineCycleRequested;
            if (step != 0)
            {
                Loadout.Cycle(step);
            }
        }

        private void HandleChanged(int previous, int current)
        {
            Changed?.Invoke((MagicDiscipline)current);
        }
    }
}
