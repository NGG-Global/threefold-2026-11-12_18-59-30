using System;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// A bounded quantity such as health or mana. Plain data with no Unity lifecycle, so the same
    /// rules serve every character class and can be unit tested directly.
    /// </summary>
    [Serializable]
    public sealed class ResourcePool
    {
        [Min(1f)]
        [SerializeField] private float maximum = 100f;

        private float current;
        private bool initialised;

        public ResourcePool()
        {
        }

        public ResourcePool(float maximum)
        {
            this.maximum = Mathf.Max(1f, maximum);
        }

        public float Maximum => maximum;

        public float Current
        {
            get
            {
                EnsureInitialised();
                return current;
            }
        }

        /// <summary>Current amount as a share of the maximum, from 0 to 1.</summary>
        public float Fraction => maximum > 0f ? Current / maximum : 0f;

        public bool IsEmpty => Current <= 0f;

        public bool IsFull => Current >= maximum;

        /// <summary>Changes the maximum. The current amount is capped to it.</summary>
        public void SetMaximum(float value, bool refill)
        {
            EnsureInitialised();
            maximum = Mathf.Max(1f, value);
            current = refill ? maximum : Mathf.Min(current, maximum);
        }

        public void Fill()
        {
            initialised = true;
            current = maximum;
        }

        /// <summary>Removes up to the given amount.</summary>
        /// <returns>The amount actually removed.</returns>
        public float Remove(float amount)
        {
            EnsureInitialised();
            float removed = Mathf.Clamp(amount, 0f, current);
            current -= removed;
            return removed;
        }

        /// <summary>Adds up to the given amount, without exceeding the maximum.</summary>
        /// <returns>The amount actually added.</returns>
        public float Add(float amount)
        {
            EnsureInitialised();
            float added = Mathf.Clamp(amount, 0f, maximum - current);
            current += added;
            return added;
        }

        /// <summary>Removes the full amount if enough is available, otherwise removes nothing.</summary>
        public bool TrySpend(float amount)
        {
            EnsureInitialised();
            if (amount <= 0f)
            {
                return true;
            }

            // The small tolerance stops a value like 11.9999 from blocking a cost of 12.
            if (current + 0.0001f < amount)
            {
                return false;
            }

            current = Mathf.Max(0f, current - amount);
            return true;
        }

        private void EnsureInitialised()
        {
            if (!initialised)
            {
                initialised = true;
                current = maximum;
            }
        }
    }
}
