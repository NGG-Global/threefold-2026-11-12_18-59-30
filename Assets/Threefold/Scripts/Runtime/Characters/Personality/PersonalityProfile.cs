using System;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>
    /// How a discipline carries the body when nothing else is asked of it: posture, breathing, what the hands and
    /// shoulders do, how the weight shifts and how the head looks around. Every value is a small rotation added on
    /// top of the animation that is playing (the stance loops and the idle); none replaces it. Angles are degrees in
    /// the character's own axes: pitch is positive forwards (chin down), yaw positive to the character's right.
    /// Differences between disciplines are these numbers only (<see cref="PersonalityDefaults"/>).
    /// </summary>
    [Serializable]
    public sealed class PersonalityProfile
    {
        [Header("Posture")]
        [Tooltip("How far the upper body leans forward, in degrees, shared between the spine and the chest.")]
        [Range(-6f, 10f)] public float forwardLeanDegrees;

        [Tooltip("Positive: chest lifted and shoulders drawn back (open, squared). Negative: shoulders rolled forward.")]
        [Range(-5f, 5f)] public float chestOpenDegrees;

        [Tooltip("Head pitch on top of the lean. Positive: chin down. Negative: chin up.")]
        [Range(-6f, 6f)] public float headPitchDegrees;

        [Header("Breathing")]
        [Range(4f, 30f)] public float breathsPerMinute = 12f;

        [Tooltip("How far the chest rises with each breath, in degrees. The shoulders follow by a third.")]
        [Range(0f, 3f)] public float breathDepthDegrees = 1f;

        [Header("Hands")]
        [Tooltip("Radius of the small circle each arm traces, in degrees at the shoulder. 1.5 degrees moves a hand about 1.5 cm.")]
        [Range(0f, 3f)] public float handCircleDegrees;

        [Range(0f, 40f)] public float handCirclesPerMinute = 8f;

        [Header("Restlessness")]
        [Tooltip("Uneven movement of the shoulders and hands, in degrees.")]
        [Range(0f, 3f)] public float restlessDegrees;

        [Range(0f, 90f)] public float restlessPerMinute = 30f;

        [Header("Weight")]
        [Tooltip("Sideways sway of the spine as the weight shifts, in degrees.")]
        [Range(0f, 3f)] public float weightShiftDegrees;

        [Range(0f, 30f)] public float weightShiftsPerMinute = 6f;

        [Header("Looking around")]
        [Tooltip("Shortest time between two looks, in seconds.")]
        [Min(0.2f)] public float lookIntervalMin = 3f;

        [Tooltip("Longest time between two looks, in seconds.")]
        [Min(0.2f)] public float lookIntervalMax = 6f;

        [Range(0f, 25f)] public float lookYawDegrees = 12f;
        [Range(0f, 8f)] public float lookPitchDegrees = 3f;

        [Tooltip("Roughly how long the head takes to turn to a look, in seconds. Short is sharp, long is deliberate.")]
        [Range(0.08f, 1.5f)] public float lookTurnSeconds = 0.4f;

        [Tooltip("How long a look is held before the head returns, in seconds.")]
        [Range(0.1f, 4f)] public float lookHoldSeconds = 1f;

        [Header("Face (only on a model with face bones)")]
        [Tooltip("Shortest time between two blinks, in seconds.")]
        [Min(0.5f)] public float blinkIntervalMin = 3f;

        [Tooltip("Longest time between two blinks, in seconds.")]
        [Min(0.5f)] public float blinkIntervalMax = 6f;

        [Tooltip("How long a blink takes, closing and opening, in seconds.")]
        [Range(0.06f, 0.4f)] public float blinkSeconds = 0.15f;

        [Tooltip("Where the brows rest, in millimetres. Negative: lowered, focused. 0: neutral.")]
        [Range(-3f, 3f)] public float browMillimetres;

        [Header("Changing discipline")]
        [Tooltip("Seconds over which the body moves to this discipline's bearing when it is selected.")]
        [Range(0.1f, 1.5f)] public float blendSeconds = 0.4f;

        public PersonalityProfile Clone()
        {
            return (PersonalityProfile)MemberwiseClone();
        }
    }

    /// <summary>The reactions every discipline shares, and how hard they may move the body.</summary>
    [Serializable]
    public sealed class PersonalityReactionTuning
    {
        [Tooltip("Flinch of the chest from the hardest hit, in degrees. The head moves half as much again.")]
        [Range(0f, 6f)] public float impactDegrees = 4f;

        [Tooltip("How fast the flinch swings back, in cycles per second.")]
        [Range(1f, 6f)] public float impactFrequency = 3.2f;

        [Tooltip("0 swings for a long time, 1 returns without overshoot.")]
        [Range(0.2f, 1f)] public float impactDamping = 0.6f;

        [Tooltip("Lean back of the chest when a fully charged attack leaves the hands, in degrees.")]
        [Range(0f, 5f)] public float castBraceDegrees = 2.5f;

        [Tooltip("How fast the brace settles, in cycles per second.")]
        [Range(1f, 6f)] public float castFrequency = 2.4f;

        [Range(0.2f, 1f)] public float castDamping = 0.75f;

        [Tooltip("How far the brows rise at the hardest hit, in millimetres.")]
        [Range(0f, 4f)] public float browRaiseMillimetres = 3f;

        [Tooltip("How far the brows lower during a strike or a charge, in millimetres.")]
        [Range(0f, 3f)] public float browFocusMillimetres = 1.5f;

        [Tooltip("A hit at least this strong (0 to 1) makes the eyes blink.")]
        [Range(0f, 1f)] public float blinkOnImpactStrength = 0.5f;

        [Tooltip("Damage of one hit that gives the full flinch.")]
        [Min(1f)] public float fullImpactDamage = 25f;
    }

    /// <summary>The three disciplines' profiles and the shared reactions: what a <see cref="PersonalityPoseModel"/> runs on.</summary>
    [Serializable]
    public sealed class PersonalityTuning
    {
        public PersonalityProfile arcane = PersonalityDefaults.Arcane();
        public PersonalityProfile flame = PersonalityDefaults.Flame();
        public PersonalityProfile aegis = PersonalityDefaults.Aegis();
        public PersonalityReactionTuning reactions = new PersonalityReactionTuning();

        /// <summary>The profile of a discipline slot (0 Arcane, 1 Flame, 2 Aegis); Arcane for anything else.</summary>
        public PersonalityProfile Get(int slot)
        {
            PersonalityProfile profile = slot == 1 ? flame : slot == 2 ? aegis : arcane;
            return profile ?? arcane;
        }
    }
}
