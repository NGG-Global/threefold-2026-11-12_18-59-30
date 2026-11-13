using System;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>
    /// Holds the player's selected discipline (Arcane, Flame, Aegis) and changes it on request from
    /// the input: the touch selector, the number keys, or the gamepad's d-pad. Charged attacks read
    /// the current discipline from here. The rules are in <see cref="DisciplineLoadout"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // After the input sources (-100), before the attack that reads the discipline (0).
    public sealed class PlayerDisciplines : MonoBehaviour
    {
        [SerializeField] private CharacterInputSource inputSource;
        [SerializeField] private MagicDiscipline startingDiscipline = MagicDiscipline.Arcane;

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
