using System;
using Threefold.Characters;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// How the player takes magical effects from the world: the explicit rules for hazards. The player
    /// is a creature like any other (<see cref="CreatureEffectModel"/>), with its own values:
    ///
    /// - Heat from fires and fiery shots warms the player and, at full heat, sets the player alight for
    ///   a short burn.
    /// - Force from a structure coming down or a stone shattering shoves the player a little.
    /// - Arcane charge and bracing mean nothing to the player and are refused.
    ///
    /// The player's own attacks never reach the player (an attack never strikes its user), and enemy
    /// strikes carry their force to objects only. What reaches the player is therefore what the world
    /// sends: reactions of objects, and the heat of an enemy's fiery shot. A raised guard takes the
    /// share of an effect from the front that it takes of a hit's damage.
    /// </summary>
    [Serializable]
    public sealed class PlayerHazardSettings
    {
        [Tooltip("How the player responds to heat from the world.")]
        public HeatSettings heat = PlayerHazardRules.DefaultHeat();

        [Tooltip("Share of a force from the world that the player resists. 1: never shoved.")]
        [Range(0f, 1f)]
        public float pushResistance = 0.5f;

        [Tooltip("Seconds a shove takes.")]
        [Min(0.05f)]
        public float shoveSeconds = 0.25f;
    }

    /// <summary>The player's hazard rules that need no Unity object. Plain C#, tested outside Unity.</summary>
    public static class PlayerHazardRules
    {
        /// <summary>
        /// The player warms at 0.6 of the heat sent, ignites at 100 and burns for 2.5 s at 5 damage a
        /// second. Standing within reach of a burning barricade (18 heat a second) sets the player alight
        /// after about ten seconds; three fiery motes in quick succession do it at once.
        /// </summary>
        public static HeatSettings DefaultHeat()
        {
            return new HeatSettings
            {
                susceptibility = 0.6f, capacity = 100f, heatedFraction = 0.5f, flammable = true,
                decayDelay = 1.5f, decayPerSecond = 25f, burnSeconds = 2.5f, burnDamagePerSecond = 5f, burnTickSeconds = 0.5f,
                burnStacking = StackPolicy.Ignore,
            };
        }

        /// <summary>
        /// An effect arriving at a guarded player: if the guard covers the side it comes from, it loses
        /// the share a guarded hit loses. A brace and an effect from behind or without a direction are
        /// not reduced.
        /// </summary>
        public static float Guarded(float magnitude, bool guarding, Vector3 facing, Vector3 travel, float arcDegrees,
            float reduction)
        {
            if (!guarding || magnitude <= 0f || !BlockRules.Covers(facing, travel, arcDegrees))
            {
                return magnitude;
            }

            return BlockRules.Mitigate(magnitude, reduction);
        }
    }

    /// <summary>The player's receiver of magical effects from the world. See <see cref="PlayerHazardSettings"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEffectReceiver : MonoBehaviour, IMagicEffectReceiver, IRestorable
    {
        [SerializeField] private PlayerHazardSettings settings = new PlayerHazardSettings();

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private Health health;

        [Tooltip("Optional. A raised guard takes its share of effects from the front. Found on this object if empty.")]
        [SerializeField] private PlayerGuard guard;

        [Tooltip("Optional. Shoves move the character through it. Found on this object if empty.")]
        [SerializeField] private ThirdPersonMotor motor;

        private CreatureEffectModel model;
        private HeatCarrier carrier;

        /// <summary>Raised for every effect that arrives, with what became of it. For feedback and tests in the scene.</summary>
        public event Action<MagicEffect, EffectResponse> EffectReceived;

        public PlayerHazardSettings Settings => settings;

        public float HeatFraction => model != null ? model.Thermal.Fraction : 0f;

        public bool IsBurning => model != null && model.Thermal.IsBurning;

        public EffectResponse ReceiveEffect(in MagicEffect effect)
        {
            EnsureModel();
            if (health == null || !health.IsAlive)
            {
                return EffectResponse.Spent;
            }

            MagicEffect arriving = effect;
            if (guard != null && guard.IsBlocking && effect.Kind != MagicEffectKind.Stabilize)
            {
                float magnitude = PlayerHazardRules.Guarded(effect.Magnitude, true, transform.forward, effect.Direction,
                    guard.GuardArcDegrees, guard.DamageReduction);
                arriving = new MagicEffect(effect.Kind, magnitude, effect.Direction, effect.Duration, effect.Point, effect.Source,
                    effect.Discipline, effect.Targets, effect.EventId, effect.Depth);
            }

            EffectResponse response = model.Apply(arriving, out float pushDistance);
            if (model.IgnitedByLastEffect)
            {
                carrier.AnnounceIgnition();
            }

            if (pushDistance > 0f && motor != null)
            {
                motor.Shove(arriving.Direction * pushDistance, settings.shoveSeconds);
                CombatEvents.RaiseStatus(new StatusEvent(gameObject, StatusKind.Pushed, health.AimPoint, settings.shoveSeconds));
            }

            EffectReceived?.Invoke(arriving, response);
            return response;
        }

        /// <summary>Puts the player's heat back to cold, for a restart.</summary>
        public void Restore()
        {
            EnsureModel();
            model.Restore();
        }

        private void Awake()
        {
            EnsureModel();
        }

        private void Update()
        {
            carrier?.Tick(Time.deltaTime);
        }

        private void EnsureModel()
        {
            if (health == null) health = GetComponent<Health>();
            if (guard == null) guard = GetComponent<PlayerGuard>();
            if (motor == null) motor = GetComponent<ThirdPersonMotor>();

            if (model != null)
            {
                return;
            }

            settings ??= new PlayerHazardSettings();
            model = new CreatureEffectModel(settings.heat, settings.pushResistance, true);
            carrier = new HeatCarrier(model.Thermal);
            carrier.Bind(health);
        }
    }
}
