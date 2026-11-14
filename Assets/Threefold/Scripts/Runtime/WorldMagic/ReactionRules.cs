using System;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>When a reaction rule sends its effect out.</summary>
    public enum EmissionTiming
    {
        /// <summary>Once, at the moment the condition starts to hold.</summary>
        OnEnter = 0,

        /// <summary>Again and again at an interval, for as long as the condition holds. The first after one interval.</summary>
        WhileHolding = 1,
    }

    /// <summary>How much a reaction matters when there is more to do than a frame allows.</summary>
    public enum ReactionPriority
    {
        /// <summary>Flavour. Handled last, and the first to be given up when the queue is full.</summary>
        Low = 0,

        Normal = 1,

        /// <summary>A puzzle depends on it. Handled first and never given up.</summary>
        Critical = 2,
    }

    /// <summary>What happens to an object's temporary state while the object is switched off (out of range, unloaded).</summary>
    public enum InactiveBehaviour
    {
        /// <summary>Time stands still for it. It continues where it was when it is switched on again.</summary>
        Pause = 0,

        /// <summary>The time it was away is made up when it is switched on again: it has cooled, drained, burnt down.</summary>
        CatchUp = 1,

        /// <summary>Its temporary state is dropped the moment it is switched off.</summary>
        Reset = 2,
    }

    /// <summary>
    /// A secondary reaction: an effect an object sends out because of the state it is in. A burning
    /// barricade heats what stands near it; a structure that comes down shoves what is around it; an
    /// activated crystal charges the mechanisms linked to it. This is how one action can set off a
    /// chain, and the only way: nothing spreads that is not written down as a rule like this.
    ///
    /// The effect goes to everything that can receive effects within the radius, and to the object's
    /// linked targets if asked. What it does there follows from the target's own properties, exactly
    /// as for an attack.
    /// </summary>
    [Serializable]
    public sealed class ReactionEmission
    {
        [Tooltip("The state that sets the reaction off. Any one of several.")]
        public PropertyCondition when = PropertyCondition.Burning;

        public EmissionTiming timing = EmissionTiming.OnEnter;

        [Tooltip("While Holding: seconds between effects.")]
        [Min(0.1f)]
        public float interval = 1f;

        [Header("What is sent")]
        public MagicEffectKind effect = MagicEffectKind.Heat;

        [Min(0f)]
        public float magnitude = 10f;

        [Tooltip("Stabilise only: seconds the brace lasts. 0 lets the target use its own.")]
        [Min(0f)]
        public float duration;

        [Header("Who receives it")]
        [Tooltip("Everything that can receive effects within this distance. 0: nothing by distance.")]
        [Min(0f)]
        public float radius = 2.5f;

        [Tooltip("Also the objects listed as Linked Targets on the object in the scene.")]
        public bool toLinkedTargets;

        public EffectTargets targets = EffectTargets.All;

        [Tooltip("Critical for a reaction a puzzle depends on: it is handled first and never dropped.")]
        public ReactionPriority priority = ReactionPriority.Normal;

        public bool ReachesAnything => (radius > 0f || toLinkedTargets) && magnitude > 0f && when != PropertyCondition.None;
    }

    /// <summary>One reaction that has fallen due on an object and waits to be sent to its targets.</summary>
    public readonly struct EmissionRequest
    {
        /// <summary>Index of the rule in the object's settings.</summary>
        public readonly int Rule;

        /// <summary>The depth the effect will carry: one more than what caused the object's state.</summary>
        public readonly int Depth;

        /// <summary>
        /// The identity the effect will carry. A reaction to an attack keeps the attack's identity, so a
        /// target reached by two paths takes it once; a repeating reaction has an identity of its own each time.
        /// </summary>
        public readonly int EventId;

        public EmissionRequest(int rule, int depth, int eventId)
        {
            Rule = rule;
            Depth = depth;
            EventId = eventId;
        }
    }
}
