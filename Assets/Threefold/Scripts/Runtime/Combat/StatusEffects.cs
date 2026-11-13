using System;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>How a target responds to heat. Tuned per kind of target.</summary>
    [Serializable]
    public sealed class HeatSettings
    {
        [Tooltip("Multiplies heat received. 0 makes the target immune; above 1 makes it catch fire easily.")]
        [Min(0f)]
        public float susceptibility = 1f;

        [Tooltip("Heat at which the target ignites.")]
        [Min(1f)]
        public float capacity = 100f;

        [Tooltip("Seconds after the last heat before it starts to fade.")]
        [Min(0f)]
        public float decayDelay = 1.5f;

        [Tooltip("Heat lost per second once it fades.")]
        [Min(0f)]
        public float decayPerSecond = 22f;

        [Tooltip("How long an ignited target burns, in seconds.")]
        [Min(0f)]
        public float burnSeconds = 4f;

        [Tooltip("Damage per second while burning.")]
        [Min(0f)]
        public float burnDamagePerSecond = 6f;

        [Tooltip("Seconds between burn damage ticks.")]
        [Min(0.05f)]
        public float burnTickSeconds = 0.5f;
    }

    /// <summary>
    /// Heat on one target. Flame attacks add heat; it fades after a pause. At capacity the target
    /// ignites and burns for a while, taking damage in ticks, then starts again from cold. Heat
    /// added while burning is ignored, so a target cannot be kept alight without pause.
    /// Plain C#, tested outside Unity.
    /// </summary>
    public sealed class HeatMeter
    {
        private readonly HeatSettings settings;
        private float sinceLastHeat;
        private float burnRemaining;
        private float tickRemaining;

        public HeatMeter(HeatSettings settings)
        {
            this.settings = settings ?? new HeatSettings();
        }

        public HeatSettings Settings => settings;

        public float Heat { get; private set; }

        public bool IsBurning => burnRemaining > 0f;

        /// <summary>0 when cold, 1 at ignition. Stays at 1 while burning.</summary>
        public float Fraction => IsBurning ? 1f : Mathf.Clamp01(Heat / Mathf.Max(settings.capacity, 1f));

        /// <summary>True if the target can be heated at all.</summary>
        public bool CanHeat => settings.susceptibility > 0f;

        /// <summary>Adds heat. Returns true if this ignited the target.</summary>
        public bool Add(float amount)
        {
            if (amount <= 0f || !CanHeat || IsBurning)
            {
                return false;
            }

            sinceLastHeat = 0f;
            Heat += amount * settings.susceptibility;
            if (Heat + 0.0001f < settings.capacity)
            {
                return false;
            }

            Heat = settings.capacity;
            if (settings.burnSeconds <= 0f)
            {
                Heat = 0f;
                return false;
            }

            burnRemaining = settings.burnSeconds;
            tickRemaining = settings.burnTickSeconds;
            return true;
        }

        /// <summary>Advances time. Returns the burn damage that falls due in this step (often 0).</summary>
        public float Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return 0f;
            }

            if (!IsBurning)
            {
                sinceLastHeat += deltaTime;
                if (Heat > 0f && sinceLastHeat >= settings.decayDelay)
                {
                    Heat = Mathf.Max(0f, Heat - settings.decayPerSecond * deltaTime);
                }

                return 0f;
            }

            float damage = 0f;
            float step = Mathf.Min(deltaTime, burnRemaining);
            burnRemaining -= step;
            tickRemaining -= step;

            while (tickRemaining <= 0f)
            {
                damage += settings.burnDamagePerSecond * settings.burnTickSeconds;
                tickRemaining += settings.burnTickSeconds;
            }

            if (burnRemaining <= 0f)
            {
                burnRemaining = 0f;
                Heat = 0f;
                sinceLastHeat = 0f;
            }

            return damage;
        }

        /// <summary>Back to cold, for example when the target is restored.</summary>
        public void Clear()
        {
            Heat = 0f;
            burnRemaining = 0f;
            sinceLastHeat = 0f;
        }
    }

    /// <summary>Something that heats up and can catch fire: an enemy, a straw dummy, later a brazier.</summary>
    public interface IHeatReceiver
    {
        /// <summary>Adds heat from a flame attack. Returns true if it was accepted (the target is not immune).</summary>
        bool ReceiveHeat(float amount, GameObject source);

        float HeatFraction { get; }

        bool IsBurning { get; }
    }

    /// <summary>A push from the Force Pulse or a similar impulse.</summary>
    public readonly struct ForceImpulse
    {
        /// <summary>Direction of the push along the ground.</summary>
        public readonly Vector3 Direction;

        /// <summary>How far an unresisting target is moved, in metres.</summary>
        public readonly float Distance;

        public readonly GameObject Source;

        public ForceImpulse(Vector3 direction, float distance, GameObject source)
        {
            Direction = direction;
            Distance = distance;
            Source = source;
        }
    }

    /// <summary>
    /// Something a force impulse can move. Each receiver decides how: an enemy slides along its
    /// navigation surface, a later crate or pendulum would move in its own way. Nothing gets a
    /// Rigidbody just to be pushed.
    /// </summary>
    public interface IForceReceiver
    {
        void ReceiveForce(in ForceImpulse impulse);
    }

    /// <summary>
    /// Runs a <see cref="HeatMeter"/> for a component that has a <see cref="Health"/>: applies burn
    /// damage and announces ignition. Composed into enemies and dummies rather than added as a
    /// component, so existing prefabs need no change.
    /// </summary>
    public sealed class HeatCarrier
    {
        private readonly HeatMeter meter;
        private Health health;

        public HeatCarrier(HeatSettings settings)
        {
            meter = new HeatMeter(settings);
        }

        public HeatMeter Meter => meter;

        public void Bind(Health target)
        {
            health = target;
        }

        public bool Receive(float amount, GameObject source)
        {
            if (!meter.CanHeat || health == null || !health.IsAlive)
            {
                return false;
            }

            if (meter.Add(amount))
            {
                CombatEvents.RaiseStatus(new StatusEvent(health.gameObject, StatusKind.Ignited, health.AimPoint,
                    meter.Settings.burnSeconds));
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            float burn = meter.Tick(deltaTime);
            if (burn <= 0f || health == null || !health.IsAlive)
            {
                return;
            }

            // No source: a burn hurts and keeps an enemy alert, but it does not tell it where the
            // attacker is standing now.
            Vector3 point = health.AimPoint;
            health.ApplyDamage(new DamageInfo(burn, point, Vector3.zero, null, 0f, SpellElement.Fire));
            CombatEvents.RaiseStatus(new StatusEvent(health.gameObject, StatusKind.BurnTick, point, 0f));
        }

        public void Clear()
        {
            meter.Clear();
        }
    }
}
