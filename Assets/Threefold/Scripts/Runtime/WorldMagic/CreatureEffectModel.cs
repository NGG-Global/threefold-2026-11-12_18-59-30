using System;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Armour a creature wears: a structure, with the same values and the same rule as a structure in
    /// the world. Force above its threshold wears it down, much faster while the creature is heated if
    /// it is brittle in the heat. While it holds it takes a share of every hit; once broken, what it
    /// covered is exposed.
    /// </summary>
    [Serializable]
    public sealed class ArmourSettings
    {
        [Tooltip("The creature wears armour. Without it, force only pushes and staggers.")]
        public bool enabled;

        [Tooltip("The armour as a structure: integrity, thresholds, force damage, brittleness cold and heated.")]
        public StructuralSettings structure = new StructuralSettings();

        [Tooltip("Share of a hit's damage the armour takes while it is intact.")]
        [Range(0f, 0.9f)]
        public float intactReduction = 0.2f;

        [Tooltip("Share taken while it is weakened.")]
        [Range(0f, 0.9f)]
        public float weakenedReduction = 0.1f;

        [Tooltip("Share taken while it is fractured.")]
        [Range(0f, 0.9f)]
        public float fracturedReduction;

        [Tooltip("Share of a hit's damage added once the armour is broken: what it covered is exposed.")]
        [Range(0f, 1f)]
        public float brokenVulnerability = 0.2f;

        [Tooltip("Breaking the armour knocks the creature off balance: a stagger, unless it was staggered a moment ago.")]
        public bool staggerWhenBroken = true;
    }

    /// <summary>
    /// A magical ward around a creature. It is an arcane mechanism like a crystal: charge builds on it
    /// and drains after a pause, and at its threshold it activates, which for a ward means it is
    /// disrupted. While it holds it takes a share of every hit. A timed activation lets it form again.
    /// </summary>
    [Serializable]
    public sealed class WardSettings
    {
        [Tooltip("The creature has a ward. Without it, arcane charge means nothing to the creature.")]
        public bool enabled;

        [Tooltip("The ward as an arcane mechanism. Its activation is its disruption; with a Timed activation it forms " +
                 "again after the active time.")]
        public ArcaneSettings arcane = new ArcaneSettings
        {
            activationThreshold = 40f, decayDelay = 2f, decayPerSecond = 20f,
            activation = ArcaneActivation.Timed, activeSeconds = 6f, activeStacking = StackPolicy.Ignore,
        };

        [Tooltip("Share of a hit's damage the ward takes while it holds.")]
        [Range(0f, 0.9f)]
        public float reduction = 0.15f;

        [Tooltip("Disrupting the ward knocks the creature off balance: a stagger, unless it was staggered a moment ago.")]
        public bool staggerWhenDisrupted = true;
    }

    /// <summary>
    /// How a creature (an enemy, a training dummy, the player) takes magical effects. It uses the
    /// very same properties and rules as objects in the world, so heat and force mean the same thing
    /// to a Guardian as to a barricade:
    ///
    /// - Heat goes to the thermal property (<see cref="HeatMeter"/>), as on an object.
    /// - Force pushes by the arithmetic of a kinetic object, wears armour down by the arithmetic of a
    ///   structure (brittle in the heat if the armour is), and staggers when it reaches the
    ///   creature's stagger force, as it topples an unstable structure at its collapse force.
    /// - Arcane charge goes to a ward, which is an arcane mechanism; a creature without one refuses it.
    /// - Bracing is refused: a creature has no stability property.
    ///
    /// Each capability is configured per kind of creature; none has all of them. The model decides;
    /// whoever owns it carries the result out (burn damage through health, a push along the
    /// navigation surface, a stagger through the enemy's brain). Plain C#, tested outside Unity.
    /// </summary>
    public sealed class CreatureEffectModel
    {
        private readonly EffectEventLog log = new EffectEventLog();
        private readonly ArmourSettings armourSettings;
        private readonly WardSettings wardSettings;
        private int lastArmourState = -1;
        private int lastWardState = -1;

        /// <param name="heat">How it responds to heat. A susceptibility of 0 makes it immune.</param>
        /// <param name="pushResistance">Share of a force it resists, 0 to 1.</param>
        /// <param name="movable">False for something fixed in place, such as a dummy on a post.</param>
        /// <param name="staggerForce">A single force at least this strong staggers it. 0: force alone never does.</param>
        /// <param name="armour">Optional armour. Null or not enabled: none.</param>
        /// <param name="ward">Optional ward. Null or not enabled: none.</param>
        public CreatureEffectModel(HeatSettings heat, float pushResistance, bool movable, float staggerForce = 0f,
            ArmourSettings armour = null, WardSettings ward = null)
        {
            Thermal = new HeatMeter(heat);
            PushResistance = Mathf.Clamp01(pushResistance);
            Movable = movable;
            StaggerForce = Mathf.Max(0f, staggerForce);

            if (armour != null && armour.enabled)
            {
                armourSettings = armour;
                Armour = new StructuralProperty(armour.structure);
            }

            if (ward != null && ward.enabled)
            {
                wardSettings = ward;
                Ward = new ArcaneProperty(ward.arcane);
            }

            RememberStates();
        }

        /// <summary>The same thermal property an object has.</summary>
        public HeatMeter Thermal { get; }

        /// <summary>The armour, as a structure. Null without armour.</summary>
        public StructuralProperty Armour { get; }

        /// <summary>The ward, as an arcane mechanism: activated means disrupted. Null without a ward.</summary>
        public ArcaneProperty Ward { get; }

        public ArmourSettings ArmourSettings => armourSettings;
        public WardSettings WardSettings => wardSettings;

        public float PushResistance { get; }

        public bool Movable { get; }

        public float StaggerForce { get; }

        /// <summary>True while the ward stands, so it takes its share of hits.</summary>
        public bool WardHolds => Ward != null && Ward.State != ArcaneState.Activated;

        /// <summary>True once the armour is broken.</summary>
        public bool ArmourBroken => Armour != null && Armour.State == StructuralState.Destroyed;

        /// <summary>True if the most recent effect set the creature alight.</summary>
        public bool IgnitedByLastEffect { get; private set; }

        /// <summary>
        /// True if the most recent effect knocked the creature off balance: a force at its stagger force,
        /// its armour breaking, or its ward being disrupted. The owner decides whether a stagger may
        /// happen just now (an enemy that was staggered a moment ago is immune).
        /// </summary>
        public bool StaggeredByLastEffect { get; private set; }

        /// <summary>True if the most recent effect broke the armour.</summary>
        public bool ArmourBrokenByLastEffect { get; private set; }

        /// <summary>True if the most recent effect disrupted the ward.</summary>
        public bool WardDisruptedByLastEffect { get; private set; }

        /// <summary>
        /// Raised for every change of the armour's state (<see cref="WorldPropertyKind.Structural"/>, as
        /// <see cref="StructuralState"/>) and of the ward's (<see cref="WorldPropertyKind.Arcane"/>, as
        /// <see cref="ArcaneState"/>): the property, the state it left and the state it entered. Every
        /// change is announced, so none goes unseen.
        /// </summary>
        public event Action<WorldPropertyKind, int, int> StateChanged;

        /// <summary>Takes one effect.</summary>
        /// <param name="pushDistance">For a force that moves the creature: how far, in metres. Otherwise 0.</param>
        public EffectResponse Apply(in MagicEffect effect, out float pushDistance)
        {
            pushDistance = 0f;
            IgnitedByLastEffect = false;
            StaggeredByLastEffect = false;
            ArmourBrokenByLastEffect = false;
            WardDisruptedByLastEffect = false;

            if ((effect.Targets & EffectTargets.Creatures) == 0)
            {
                return EffectResponse.Filtered;
            }

            if (effect.Magnitude <= 0f)
            {
                return EffectResponse.Resisted;
            }

            switch (effect.Kind)
            {
                case MagicEffectKind.Heat:
                {
                    if (log.CheckAndRecord(effect.EventId, effect.Kind))
                    {
                        return EffectResponse.Duplicate;
                    }

                    if (!Thermal.CanHeat)
                    {
                        return EffectResponse.Resisted;
                    }

                    if (Thermal.IsBurning)
                    {
                        // More heat on something alight does nothing, unless its burn is set to renew:
                        // the same policy, read the same way, as for an object.
                        if (!Thermal.RenewsWhileBurning)
                        {
                            return EffectResponse.Resisted;
                        }

                        Thermal.Add(effect.Magnitude);
                        return EffectResponse.Applied;
                    }

                    IgnitedByLastEffect = Thermal.Add(effect.Magnitude);
                    return EffectResponse.Applied;
                }

                case MagicEffectKind.Force:
                {
                    if (!Movable && Armour == null && StaggerForce <= 0f)
                    {
                        return EffectResponse.NoProperty;
                    }

                    if (log.CheckAndRecord(effect.EventId, effect.Kind))
                    {
                        return EffectResponse.Duplicate;
                    }

                    bool changed = false;

                    // Heat + force = fracture, on armour as on a stone barrier.
                    if (Armour != null && Armour.State != StructuralState.Destroyed)
                    {
                        changed |= CombinationRules.ForceOnStructure(Armour, Thermal, effect.Magnitude) > 0f;
                        if (Armour.State == StructuralState.Destroyed)
                        {
                            ArmourBrokenByLastEffect = true;
                            StaggeredByLastEffect |= armourSettings.staggerWhenBroken;
                        }
                    }

                    // Force against footing: one blow at the stagger force takes the creature off its feet.
                    if (CombinationRules.Overcomes(effect.Magnitude, StaggerForce))
                    {
                        StaggeredByLastEffect = true;
                        changed = true;
                    }

                    if (Movable)
                    {
                        pushDistance = KineticRules.Displacement(effect.Magnitude, PushResistance);
                        if (pushDistance <= 0.01f)
                        {
                            pushDistance = 0f;
                        }
                        else
                        {
                            changed = true;
                        }
                    }

                    AnnounceChanges();
                    return changed ? EffectResponse.Applied : EffectResponse.Resisted;
                }

                case MagicEffectKind.ArcaneCharge:
                {
                    if (Ward == null)
                    {
                        return EffectResponse.NoProperty;
                    }

                    if (log.CheckAndRecord(effect.EventId, effect.Kind))
                    {
                        return EffectResponse.Duplicate;
                    }

                    // Arcane + mechanism = activation: a ward is disrupted at its threshold, as a crystal lights.
                    bool held = WardHolds;
                    bool accepted = Ward.AddCharge(effect.Magnitude);
                    if (held && !WardHolds)
                    {
                        WardDisruptedByLastEffect = true;
                        StaggeredByLastEffect |= wardSettings.staggerWhenDisrupted;
                    }

                    AnnounceChanges();
                    return accepted ? EffectResponse.Applied : EffectResponse.Resisted;
                }

                default:
                    return EffectResponse.NoProperty;
            }
        }

        /// <summary>
        /// Advances what changes with time on the model itself: a ward's charge drains and a disrupted
        /// ward forms again; armour that can burn burns while the creature does. The creature's heat is
        /// advanced by whoever applies its burn damage (<see cref="HeatCarrier"/>).
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Ward?.Tick(deltaTime);

            if (Armour != null && Thermal.IsBurning)
            {
                Armour.Burn(deltaTime);
            }

            AnnounceChanges();
        }

        /// <summary>
        /// The share of a hit's damage that gets through the creature's defences. A hit without a
        /// direction (a burn) passes armour and ward, as it passes a raised guard.
        /// </summary>
        public float DamageMultiplier(bool hitHasDirection)
        {
            if (!hitHasDirection)
            {
                return 1f;
            }

            float multiplier = Armour != null ? ArmourMultiplier(armourSettings, Armour.State) : 1f;
            return multiplier * WardMultiplier(wardSettings, WardHolds);
        }

        /// <summary>What armour in a given state lets through, as a factor on the damage.</summary>
        public static float ArmourMultiplier(ArmourSettings armour, StructuralState state)
        {
            if (armour == null || !armour.enabled)
            {
                return 1f;
            }

            switch (state)
            {
                case StructuralState.Intact: return 1f - Mathf.Clamp(armour.intactReduction, 0f, 0.9f);
                case StructuralState.Weakened: return 1f - Mathf.Clamp(armour.weakenedReduction, 0f, 0.9f);
                case StructuralState.Fractured: return 1f - Mathf.Clamp(armour.fracturedReduction, 0f, 0.9f);
                default: return 1f + Mathf.Clamp01(armour.brokenVulnerability);
            }
        }

        /// <summary>What a ward lets through, as a factor on the damage.</summary>
        public static float WardMultiplier(WardSettings ward, bool holds)
        {
            return ward != null && ward.enabled && holds ? 1f - Mathf.Clamp(ward.reduction, 0f, 0.9f) : 1f;
        }

        public void Restore()
        {
            Thermal.Clear();
            Armour?.Restore();
            Ward?.Restore();
            log.Clear();
            IgnitedByLastEffect = false;
            StaggeredByLastEffect = false;
            ArmourBrokenByLastEffect = false;
            WardDisruptedByLastEffect = false;

            // Put back as a whole: nothing is announced, whoever shows the state looks at it afresh.
            RememberStates();
        }

        private void RememberStates()
        {
            lastArmourState = Armour != null ? (int)Armour.State : -1;
            lastWardState = Ward != null ? (int)Ward.State : -1;
        }

        private void AnnounceChanges()
        {
            if (Armour != null)
            {
                int now = (int)Armour.State;
                if (now != lastArmourState)
                {
                    int before = lastArmourState;
                    lastArmourState = now;
                    StateChanged?.Invoke(WorldPropertyKind.Structural, before, now);
                }
            }

            if (Ward != null)
            {
                int now = (int)Ward.State;
                if (now != lastWardState)
                {
                    int before = lastWardState;
                    lastWardState = now;
                    StateChanged?.Invoke(WorldPropertyKind.Arcane, before, now);
                }
            }
        }
    }
}
