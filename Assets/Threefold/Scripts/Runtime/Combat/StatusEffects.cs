using System;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>The thermal state of a target. The order is the order of the transitions.</summary>
    public enum ThermalState
    {
        Unheated = 0,

        /// <summary>Warm enough to behave differently: a heat-sensitive stone turns brittle.</summary>
        Heated = 1,

        /// <summary>Alight, taking burn damage in ticks until the burn ends.</summary>
        Burning = 2,
    }

    /// <summary>
    /// How a target responds to heat. Tuned per kind of target. Enemies, training dummies and
    /// objects in the world all use these settings and the same <see cref="HeatMeter"/>.
    /// </summary>
    [Serializable]
    public sealed class HeatSettings
    {
        [Tooltip("Multiplies heat received. 0 makes the target immune; above 1 makes it catch fire easily.")]
        [Min(0f)]
        public float susceptibility = 1f;

        [Tooltip("Heat at which the target ignites, or, if it cannot burn, the most heat it holds.")]
        [Min(1f)]
        public float capacity = 100f;

        [Tooltip("Share of the capacity at which the target counts as heated.")]
        [Range(0.05f, 1f)]
        public float heatedFraction = 0.5f;

        [Tooltip("Whether the target ignites at its capacity. A target that cannot burn only becomes heated.")]
        public bool flammable = true;

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

        [Tooltip("What more heat does to a target that is already burning. Ignore: nothing, so it cannot be kept " +
                 "alight without pause. Refresh: the burn starts its time again. Extend: the burn lasts one burn time " +
                 "longer, up to twice the burn time in all.")]
        public StackPolicy burnStacking = StackPolicy.Ignore;
    }

    /// <summary>
    /// What happens when an effect arrives while the same temporary state is already running.
    /// </summary>
    public enum StackPolicy
    {
        /// <summary>The state starts its time again. It never lasts longer than one full time.</summary>
        Refresh = 0,

        /// <summary>The time is added to what is left, up to a stated limit.</summary>
        Extend = 1,

        /// <summary>Nothing: the state runs its course.</summary>
        Ignore = 2,
    }

    /// <summary>
    /// Heat on one target: the thermal property. Heat effects add heat; it fades after a pause.
    /// The states are Unheated, Heated (at a share of the capacity) and Burning. At capacity a
    /// flammable target ignites and burns for a while, taking damage in ticks, then starts again
    /// from cold; one that cannot burn stays heated at its capacity until it cools. Heat added
    /// while burning is ignored, so a target cannot be kept alight without pause.
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

        /// <summary>Unheated, Heated or Burning.</summary>
        public ThermalState State
        {
            get
            {
                if (IsBurning)
                {
                    return ThermalState.Burning;
                }

                return Heat > 0f && Heat + 0.0001f >= settings.capacity * settings.heatedFraction
                    ? ThermalState.Heated
                    : ThermalState.Unheated;
            }
        }

        /// <summary>True while nothing will change without new heat: cold and not burning.</summary>
        public bool IsAtRest => !IsBurning && Heat <= 0f;

        /// <summary>0 when cold, 1 at ignition. Stays at 1 while burning.</summary>
        public float Fraction => IsBurning ? 1f : Mathf.Clamp01(Heat / Mathf.Max(settings.capacity, 1f));

        /// <summary>True if the target can be heated at all.</summary>
        public bool CanHeat => settings.susceptibility > 0f;

        /// <summary>True if more heat does anything to the target while it burns (see <see cref="HeatSettings.burnStacking"/>).</summary>
        public bool RenewsWhileBurning => settings.burnStacking != StackPolicy.Ignore;

        /// <summary>Adds heat. Returns true if this ignited the target.</summary>
        public bool Add(float amount)
        {
            if (amount <= 0f || !CanHeat)
            {
                return false;
            }

            if (IsBurning)
            {
                // Already alight: what more heat does is a matter of policy. It never ignites twice.
                if (settings.burnStacking == StackPolicy.Refresh)
                {
                    burnRemaining = settings.burnSeconds;
                }
                else if (settings.burnStacking == StackPolicy.Extend)
                {
                    burnRemaining = Mathf.Min(burnRemaining + settings.burnSeconds, settings.burnSeconds * 2f);
                }

                return false;
            }

            sinceLastHeat = 0f;
            Heat += amount * settings.susceptibility;
            if (Heat + 0.0001f < settings.capacity)
            {
                return false;
            }

            Heat = settings.capacity;
            if (!settings.flammable)
            {
                // It cannot burn: it holds its full heat until it cools.
                return false;
            }

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

        /// <summary>
        /// Sets the heat held, for a saved state that names heat as an exception. The target is not
        /// alight afterwards, whatever the amount: a fire does not survive a reload.
        /// </summary>
        public void SetHeat(float amount)
        {
            burnRemaining = 0f;
            sinceLastHeat = 0f;
            Heat = float.IsNaN(amount) ? 0f : Mathf.Clamp(amount, 0f, settings.capacity);
        }
    }

    /// <summary>
    /// Carries a creature's heat out through its <see cref="Health"/>: applies burn damage in ticks and
    /// announces ignition and burning, for a <see cref="HeatMeter"/> that someone else decides for.
    /// Composed into enemies and dummies rather than added as a component, so existing prefabs need
    /// no change.
    /// </summary>
    public sealed class HeatCarrier
    {
        private readonly HeatMeter meter;
        private Health health;

        public HeatCarrier(HeatMeter meter)
        {
            this.meter = meter ?? new HeatMeter(null);
        }

        public HeatMeter Meter => meter;

        public void Bind(Health target)
        {
            health = target;
        }

        /// <summary>Tells whatever shows status effects that the creature has just caught fire.</summary>
        public void AnnounceIgnition()
        {
            if (health != null)
            {
                CombatEvents.RaiseStatus(new StatusEvent(health.gameObject, StatusKind.Ignited, health.AimPoint,
                    meter.Settings.burnSeconds));
            }
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
