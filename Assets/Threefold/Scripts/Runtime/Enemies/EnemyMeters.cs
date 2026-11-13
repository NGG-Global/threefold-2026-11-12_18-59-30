using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// How sure an enemy is that the player is there, from 0 (unaware) to 1 (certain). Seeing the
    /// player raises it, faster when the player is close; losing sight lets it fall again. A
    /// glimpse at the edge of vision therefore makes an enemy suspicious before it attacks,
    /// which gives the player a chance to break line of sight.
    /// </summary>
    public sealed class AwarenessMeter
    {
        public float Value { get; private set; }

        public bool IsFull => Value >= 1f;

        /// <summary>Raises awareness for a target seen at the given distance during the given time.</summary>
        public void Observe(float deltaTime, float distance, EnemyTuning tuning, float multiplier = 1f)
        {
            float rate = EnemyRules.AwarenessGainRate(distance, tuning.closeSenseRange, tuning.sightRange,
                tuning.awarenessGainNear, tuning.awarenessGainFar);

            if (float.IsPositiveInfinity(rate))
            {
                Value = 1f;
                return;
            }

            Value = Mathf.Min(1f, Value + rate * Mathf.Max(0f, multiplier) * Mathf.Max(0f, deltaTime));
        }

        public void Decay(float deltaTime, float ratePerSecond)
        {
            Value = Mathf.Max(0f, Value - Mathf.Max(0f, ratePerSecond) * Mathf.Max(0f, deltaTime));
        }

        public void Fill()
        {
            Value = 1f;
        }

        public void Clear()
        {
            Value = 0f;
        }

        /// <summary>Raises awareness to at least the given value; never lowers it.</summary>
        public void RaiseTo(float value)
        {
            Value = Mathf.Max(Value, Mathf.Clamp01(value));
        }
    }

    /// <summary>
    /// Decides when damage interrupts an enemy. Damage taken in quick succession adds up; when it
    /// passes the threshold the enemy is staggered. After a stagger the enemy cannot be staggered
    /// again for a while, so it cannot be locked in place by constant fire.
    /// </summary>
    public sealed class PoiseMeter
    {
        private float accumulated;
        private float immunityRemaining;

        public float Threshold { get; set; }
        public float RecoveryPerSecond { get; set; }
        public float ImmunitySeconds { get; set; }

        public float Accumulated => accumulated;
        public bool IsImmune => immunityRemaining > 0f;

        public void Configure(float threshold, float recoveryPerSecond, float immunitySeconds)
        {
            Threshold = Mathf.Max(0f, threshold);
            RecoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
            ImmunitySeconds = Mathf.Max(0f, immunitySeconds);
        }

        /// <summary>Adds damage. Returns true if it breaks the enemy's poise, which means a stagger.</summary>
        public bool Absorb(float damage)
        {
            if (Threshold <= 0f || damage <= 0f || immunityRemaining > 0f)
            {
                return false;
            }

            accumulated += damage;
            if (accumulated + 0.0001f < Threshold)
            {
                return false;
            }

            accumulated = 0f;
            immunityRemaining = ImmunitySeconds;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (immunityRemaining > 0f)
            {
                immunityRemaining -= deltaTime;
            }

            accumulated = Mathf.Max(0f, accumulated - RecoveryPerSecond * deltaTime);
        }

        public void Reset()
        {
            accumulated = 0f;
            immunityRemaining = 0f;
        }
    }
}
