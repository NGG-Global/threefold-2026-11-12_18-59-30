using System;

namespace Threefold.Magic
{
    /// <summary>
    /// Which discipline is selected, out of a fixed number of slots. Plain C#, so the rules are
    /// tested outside Unity. Wrapped by <see cref="PlayerDisciplines"/> in the scene.
    /// </summary>
    public sealed class DisciplineLoadout
    {
        private int current;

        public DisciplineLoadout(int count, int initial = 0)
        {
            Count = Math.Max(1, count);
            current = initial >= 0 && initial < Count ? initial : 0;
        }

        /// <summary>Raised with the previous and the new slot when the selection changes.</summary>
        public event Action<int, int> Changed;

        public int Count { get; }

        public int Current => current;

        /// <summary>Selects a slot. Returns false, and changes nothing, for an unknown or already selected slot.</summary>
        public bool Select(int slot)
        {
            if (slot < 0 || slot >= Count || slot == current)
            {
                return false;
            }

            int previous = current;
            current = slot;
            Changed?.Invoke(previous, current);
            return true;
        }

        /// <summary>Moves the selection by a number of slots, wrapping around at either end.</summary>
        public bool Cycle(int step)
        {
            if (step == 0 || Count < 2)
            {
                return false;
            }

            int next = ((current + step) % Count + Count) % Count;
            return Select(next);
        }
    }
}
