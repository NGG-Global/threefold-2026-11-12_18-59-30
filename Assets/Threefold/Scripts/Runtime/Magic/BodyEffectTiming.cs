using System;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>The moment of a strike at which a body effect starts.</summary>
    public enum BodyEffectMoment
    {
        /// <summary>Never.</summary>
        None = 0,

        /// <summary>When the strike begins: the whole wind-up shows it.</summary>
        Windup = 1,

        /// <summary>At the strike's effect moment, two frames before its hit window.</summary>
        Effect = 2,

        /// <summary>When the hit window opens: only the fast part of the strike shows it.</summary>
        HitWindow = 3,
    }

    /// <summary>Where a discipline's charge buildup sits on the body.</summary>
    public enum ChargePlacement
    {
        /// <summary>In the hand or hands the charge animation holds the charge in.</summary>
        Effector = 0,

        /// <summary>Around both forearms: heat builds along the arms.</summary>
        BothForearms = 1,

        /// <summary>In front of the chest, between the hands: energy pressed inwards.</summary>
        Chest = 2,
    }

    /// <summary>
    /// When a discipline's body effects (limb trails, gathering energy, charge buildup, lingering embers)
    /// start, grow and stop, relative to the moments of its attacks. Numbers only: which effect plays is in
    /// the discipline's presentation, and the moments come from the combat rules through the animation cues.
    /// The starting values are in <see cref="MagicVfxDefaults"/>.
    /// </summary>
    [Serializable]
    public sealed class BodyEffectTiming
    {
        [Tooltip("Off: the values below are ignored and the defaults for the discipline are used. The setup command " +
                 "writes the defaults and switches this on.")]
        public bool configured;

        [Header("Melee")]
        [Tooltip("When the trail on the striking hand or foot starts.")]
        public BodyEffectMoment trailFrom = BodyEffectMoment.Effect;

        [Tooltip("On: the trail keeps drawing through the follow-through until the hit window closes. Off: it stops at contact.")]
        public bool trailUntilWindowCloses = true;

        [Tooltip("Strength of the trail when it starts. Below 1 it grows to full strength at the moment of contact, so " +
                 "the effect intensifies as the strike approaches its target.")]
        [Range(0f, 1f)]
        public float trailStartIntensity = 1f;

        [Tooltip("When the oriented swing effect (a ribbon or slash around the body) appears.")]
        public BodyEffectMoment swingFrom = BodyEffectMoment.Effect;

        [Tooltip("Energy gathers at the striking limb from the wind-up until contact.")]
        public bool gatherBeforeStrike;

        [Tooltip("Small embers or motes left where the limb was, after a strike that made contact.")]
        public bool lingerAfterStrike;

        [Tooltip("Strikes with at least this camera shake (MeleeStepProfile.hitTrauma) use the heavy contact effect.")]
        [Min(0f)]
        public float heavyHitTrauma = 0.25f;

        [Header("Charged attack")]
        public ChargePlacement chargePlacement = ChargePlacement.Effector;

        [Tooltip("Strength of the charge effect at the start of a charge. It grows to 1 at full charge.")]
        [Range(0f, 1f)]
        public float chargeStartIntensity = 0.3f;

        [Tooltip("Radius the gathering particles start from at the start of a charge, in metres.")]
        [Min(0f)]
        public float chargeRadiusAtStart = 0.4f;

        [Tooltip("Radius at full charge. Smaller than the start: the energy concentrates.")]
        [Min(0f)]
        public float chargeRadiusAtFull = 0.12f;

        [Header("Stopping")]
        [Tooltip("Seconds an effect is kept after it stops emitting, so trails and particles fade instead of vanishing.")]
        [Min(0.02f)]
        public float drainSeconds = 0.22f;

        [Tooltip("Seconds an effect is kept after its action is cancelled. Short: nothing should linger.")]
        [Min(0.02f)]
        public float cancelDrainSeconds = 0.08f;

        public BodyEffectTiming Clone()
        {
            return (BodyEffectTiming)MemberwiseClone();
        }
    }
}
