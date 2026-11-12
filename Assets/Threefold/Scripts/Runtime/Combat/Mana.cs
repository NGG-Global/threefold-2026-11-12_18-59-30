using System;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Mana that abilities spend and that regenerates over time. Regeneration pauses briefly after
    /// each spend, which rewards pacing over holding the attack button down.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Mana : MonoBehaviour
    {
        [SerializeField] private ResourcePool pool = new ResourcePool(100f);

        [Tooltip("Mana restored per second while regenerating.")]
        [Min(0f)]
        [SerializeField] private float regenerationPerSecond = 9f;

        [Tooltip("Seconds after spending mana before regeneration resumes.")]
        [Min(0f)]
        [SerializeField] private float regenerationDelay = 0.9f;

        private float delayRemaining;

        /// <summary>Raised whenever the current or maximum value changes.</summary>
        public event Action Changed;

        /// <summary>Raised when a spend was refused. The argument is the amount that was asked for.</summary>
        public event Action<float> SpendRefused;

        public float Current => pool.Current;
        public float Maximum => pool.Maximum;
        public float Fraction => pool.Fraction;

        /// <summary>Sets the maximum and the regeneration rules. Called by the setup tooling.</summary>
        public void Configure(float maximum, float regenPerSecond, float regenDelay)
        {
            pool.SetMaximum(maximum, true);
            regenerationPerSecond = Mathf.Max(0f, regenPerSecond);
            regenerationDelay = Mathf.Max(0f, regenDelay);
        }

        public bool CanAfford(float amount)
        {
            return amount <= 0f || pool.Current + 0.0001f >= amount;
        }

        /// <summary>Spends the full amount if available. Otherwise spends nothing and raises <see cref="SpendRefused"/>.</summary>
        public bool TrySpend(float amount)
        {
            if (!pool.TrySpend(amount))
            {
                SpendRefused?.Invoke(amount);
                return false;
            }

            if (amount > 0f)
            {
                delayRemaining = regenerationDelay;
                Changed?.Invoke();
            }

            return true;
        }

        /// <returns>The amount actually restored.</returns>
        public float Restore(float amount)
        {
            float added = pool.Add(amount);
            if (added > 0f)
            {
                Changed?.Invoke();
            }

            return added;
        }

        /// <summary>Restores mana to its maximum and ends any regeneration delay.</summary>
        public void Refill()
        {
            delayRemaining = 0f;
            pool.Fill();
            Changed?.Invoke();
        }

        /// <summary>Advances regeneration by the given time. Called every frame; public so it can be tested.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (delayRemaining > 0f)
            {
                delayRemaining -= deltaTime;
                return;
            }

            if (regenerationPerSecond > 0f && !pool.IsFull)
            {
                Restore(regenerationPerSecond * deltaTime);
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
