using Threefold.Combat;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// How a creature (an enemy, a training dummy) takes magical effects. It uses the very same
    /// thermal property and push arithmetic as objects in the world, so heat and force mean the
    /// same thing to a Guardian as to a barricade. Creatures have no arcane or stability property:
    /// charge and bracing are refused, not ignored silently.
    ///
    /// The model decides; whoever owns it carries the result out (burn damage through health, a push
    /// along the navigation surface). Plain C#, tested outside Unity.
    /// </summary>
    public sealed class CreatureEffectModel
    {
        private readonly EffectEventLog log = new EffectEventLog();

        /// <param name="heat">How it responds to heat. A susceptibility of 0 makes it immune.</param>
        /// <param name="pushResistance">Share of a force it resists, 0 to 1.</param>
        /// <param name="movable">False for something fixed in place, such as a dummy on a post.</param>
        public CreatureEffectModel(HeatSettings heat, float pushResistance, bool movable)
        {
            Thermal = new HeatMeter(heat);
            PushResistance = Mathf.Clamp01(pushResistance);
            Movable = movable;
        }

        /// <summary>The same thermal property an object has.</summary>
        public HeatMeter Thermal { get; }

        public float PushResistance { get; }

        public bool Movable { get; }

        /// <summary>True if the most recent effect set the creature alight.</summary>
        public bool IgnitedByLastEffect { get; private set; }

        /// <summary>Takes one effect.</summary>
        /// <param name="pushDistance">For a force that moves the creature: how far, in metres. Otherwise 0.</param>
        public EffectResponse Apply(in MagicEffect effect, out float pushDistance)
        {
            pushDistance = 0f;
            IgnitedByLastEffect = false;

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

                    if (!Thermal.CanHeat || Thermal.IsBurning)
                    {
                        return EffectResponse.Resisted;
                    }

                    IgnitedByLastEffect = Thermal.Add(effect.Magnitude);
                    return EffectResponse.Applied;
                }

                case MagicEffectKind.Force:
                {
                    if (!Movable)
                    {
                        return EffectResponse.NoProperty;
                    }

                    if (log.CheckAndRecord(effect.EventId, effect.Kind))
                    {
                        return EffectResponse.Duplicate;
                    }

                    pushDistance = KineticRules.Displacement(effect.Magnitude, PushResistance);
                    if (pushDistance <= 0.01f)
                    {
                        pushDistance = 0f;
                        return EffectResponse.Resisted;
                    }

                    return EffectResponse.Applied;
                }

                default:
                    return EffectResponse.NoProperty;
            }
        }

        public void Restore()
        {
            Thermal.Clear();
            log.Clear();
            IgnitedByLastEffect = false;
        }
    }
}
