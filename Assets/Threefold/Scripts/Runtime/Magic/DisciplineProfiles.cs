using System;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>One swing of a discipline's melee combo. All times are in seconds, distances in metres.</summary>
    [Serializable]
    public sealed class MeleeStepProfile
    {
        public string name = "Strike";

        [Header("Hit")]
        [Min(0f)]
        public float damage = 10f;

        [Tooltip("Poise damage: how hard the hit pushes towards a stagger. Independent of the damage.")]
        [Min(0f)]
        public float stagger = 8f;

        [Min(0.1f)]
        public float reach = 2.4f;

        [Tooltip("Width of the struck area in degrees, centred on the swing direction.")]
        [Range(10f, 360f)]
        public float arcDegrees = 90f;

        [Tooltip("Targets more than this far above or below the feet are not hit.")]
        [Min(0.1f)]
        public float verticalReach = 1.8f;

        [Tooltip("Most targets one swing can hit. The nearest are chosen.")]
        [Min(1)]
        public int maxTargets = 1;

        [Tooltip("Heat added to each target that can be heated.")]
        [Min(0f)]
        public float heat;

        [Tooltip("Force of the blow on things that can be moved or broken: the metres an unresisting target is shoved.")]
        [Min(0f)]
        public float force;

        [Tooltip("Mana returned for each target hit. Melee feeds the charged attacks.")]
        [Min(0f)]
        public float manaGainPerTarget = 2f;

        [Header("Timing")]
        [Tooltip("From the tap to the moment the hit lands.")]
        [Min(0f)]
        public float startupSeconds = 0.1f;

        [Tooltip("After the hit, before the next swing can start.")]
        [Min(0f)]
        public float recoverySeconds = 0.25f;

        [Tooltip("After the recovery, how long a new tap still continues the combo instead of restarting it.")]
        [Min(0f)]
        public float comboWindowSeconds = 0.45f;

        [Tooltip("Movement speed during the swing, relative to normal. 1 does not slow the character.")]
        [Range(0f, 1f)]
        public float moveSpeedMultiplier = 1f;

        [Header("Presentation")]
        [Tooltip("Camera shake added when the swing connects, 0 to 1.")]
        [Range(0f, 1f)]
        public float hitTrauma = 0.12f;

        [Tooltip("Animator trigger set when the swing starts. Ignored if the Animator has no such parameter.")]
        public string animationTrigger = "Melee";

        /// <summary>Seconds from the tap until the next swing can start.</summary>
        public float TotalSeconds => startupSeconds + recoverySeconds;

        /// <summary>The magical effects the swing carries, apart from its damage.</summary>
        public EffectPayload Effects => new EffectPayload { heat = heat, force = force, targets = EffectTargets.All };
    }

    /// <summary>A discipline's standard attack: the swings in order, and how forgiving the input is.</summary>
    [Serializable]
    public sealed class MeleeComboProfile
    {
        public MeleeStepProfile[] steps = { new MeleeStepProfile() };

        [Tooltip("A tap made this long before a swing can start is still performed. Makes quick taps reliable.")]
        [Min(0f)]
        public float inputBufferSeconds = 0.3f;

        [Tooltip("A swing turns towards a target that is at most this many degrees off the wanted direction. 0 switches it off.")]
        [Range(0f, 180f)]
        public float facingAssistDegrees = 55f;

        [Tooltip("How far beyond the swing's reach a target may be and still be turned towards.")]
        [Min(0f)]
        public float facingAssistExtraRange = 1.2f;

        public int StepCount => steps != null ? steps.Length : 0;

        public MeleeStepProfile GetStep(int index)
        {
            return steps != null && index >= 0 && index < steps.Length ? steps[index] : null;
        }
    }

    /// <summary>
    /// What a discipline's guard does to the world around the character, beyond protecting it. The
    /// protection itself is the same for every discipline. An Aegis guard braces unstable things nearby.
    /// </summary>
    [Serializable]
    public sealed class GuardEffectProfile
    {
        [Tooltip("Strength of the brace given to nearby objects while guarding. 0 means the guard does not brace.")]
        [Min(0f)]
        public float stabilize;

        [Tooltip("Seconds each brace lasts. It is renewed for as long as the guard is held.")]
        [Min(0f)]
        public float stabilizeSeconds = 1.5f;

        [Tooltip("How far from the character objects are braced, in metres.")]
        [Min(0f)]
        public float radius = 3.5f;

        [Tooltip("Seconds between braces while the guard is held.")]
        [Min(0.1f)]
        public float pulseSeconds = 0.4f;

        public bool HasEffect => stabilize > 0f && radius > 0f;

        /// <summary>The effects each pulse of the guard carries. Objects only: a brace means nothing to a creature.</summary>
        public EffectPayload Effects => new EffectPayload
        {
            stabilize = stabilize, stabilizeSeconds = stabilizeSeconds, targets = EffectTargets.Objects,
        };
    }
}
