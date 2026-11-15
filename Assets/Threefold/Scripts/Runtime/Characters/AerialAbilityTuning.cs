using System;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>The kinds of aerial ability. Which discipline has which is data, set on the component.</summary>
    public enum AerialAbilityKind
    {
        None = 0,

        /// <summary>Arcane: a strong upward impulse, more height than a jump.</summary>
        Ascension = 1,

        /// <summary>Flame: propulsion downward while Jump is held, from a limited fuel budget.</summary>
        Glide = 2,

        /// <summary>Aegis: a short, fast horizontal dash.</summary>
        Dash = 3,
    }

    /// <summary>Rules shared by every aerial ability.</summary>
    [Serializable]
    public sealed class AerialRulesTuning
    {
        [Tooltip("Frames the character must stand on ground in a row before a landing counts. One frame of ground " +
                 "(brushing a ledge, a flicker on a slope) does not give the ability back.")]
        [Min(1)]
        public int landingConfirmFrames = 2;

        [Tooltip("Seconds the character must stand on ground before a landing counts, as well as the frames.")]
        [Min(0f)]
        public float landingConfirmSeconds = 0.04f;

        [Tooltip("A jump started from a ledge (jumping off backwards) makes the ability usable, but gives back an " +
                 "ability already spent only if this is on.")]
        public bool ledgeJumpRestoresAbility;

        [Tooltip("Seconds after a hit of at least the stagger damage during which no aerial ability can start.")]
        [Min(0f)]
        public float staggerSeconds = 0.35f;

        [Tooltip("Damage in one hit that staggers the Mage for aerial abilities.")]
        [Min(0f)]
        public float staggerDamage = 10f;
    }

    /// <summary>Arcane Ascension.</summary>
    [Serializable]
    public sealed class AscensionTuning
    {
        [Tooltip("Height the impulse rises above the point where it starts, in metres. A jump rises 1.5 m.")]
        [Min(0.1f)]
        public float height = 3f;

        [Tooltip("Seconds of gathering, held in place in the air, before the impulse. 0 starts it at once.")]
        [Min(0f)]
        public float gatherSeconds = 0.06f;

        [Tooltip("Horizontal speed kept when the impulse starts, 0 to 1. Below 1 the leap is mostly vertical; air " +
                 "steering afterwards is the normal one.")]
        [Range(0f, 1f)]
        public float horizontalSpeedKept = 0.85f;

        [Tooltip("Seconds after the start during which the hands are busy (no attack starts; a swing in progress is abandoned).")]
        [Min(0f)]
        public float handsBusySeconds = 0.25f;
    }

    /// <summary>Flame Fire Glide.</summary>
    [Serializable]
    public sealed class GlideTuning
    {
        [Tooltip("Seconds of propulsion per airborne sequence. Releasing Jump keeps what is left.")]
        [Min(0.05f)]
        public float fuelSeconds = 1.6f;

        [Tooltip("Fastest fall while propelling, in metres per second.")]
        [Min(0.1f)]
        public float maxFallSpeed = 2.4f;

        [Tooltip("Share of gravity while falling slower than the cap. Low: the fall levels out quickly.")]
        [Range(0f, 1f)]
        public float fallGravityScale = 0.35f;

        [Tooltip("Upward deceleration that brings a faster fall down to the cap, in metres per second squared. High " +
                 "enough to feel responsive, short of a sudden stop.")]
        [Min(0f)]
        public float brakeAcceleration = 45f;

        [Tooltip("Share of gravity while still rising. 1 or more: propulsion never adds height.")]
        [Min(1f)]
        public float riseGravityScale = 1f;

        [Tooltip("Top horizontal speed while propelling, in metres per second. Sprinting is 7.")]
        [Min(0f)]
        public float speed = 7.5f;

        [Tooltip("How quickly the joystick steers while propelling, in metres per second squared.")]
        [Min(0f)]
        public float steerAcceleration = 16f;

        [Tooltip("Fuel, in seconds, needed to start propelling again after a release.")]
        [Min(0f)]
        public float minimumToResume = 0.1f;
    }

    /// <summary>Aegis Force Dash.</summary>
    [Serializable]
    public sealed class DashTuning
    {
        [Tooltip("Distance of a dash that nothing stops, in metres.")]
        [Min(0.1f)]
        public float distance = 4.2f;

        [Tooltip("Seconds a dash lasts.")]
        [Min(0.02f)]
        public float seconds = 0.18f;

        [Tooltip("Joystick deflection, 0 to 1, above which the dash goes where the joystick points; below it, where " +
                 "the Mage faces.")]
        [Range(0f, 1f)]
        public float inputThreshold = 0.3f;

        [Tooltip("Vertical speed held during the dash, in metres per second: a little lift, no climb.")]
        public float verticalSpeed = 0.4f;

        [Tooltip("Horizontal speed kept after the dash, in metres per second. No more than sprinting, so speed never " +
                 "builds up from one dash to the next.")]
        [Min(0f)]
        public float exitSpeed = 6f;

        [Tooltip("A wall met at more than this angle from glancing (0 is head-on, 90 grazing) stops the dash; a more " +
                 "glancing wall turns it along the wall.")]
        [Range(0f, 90f)]
        public float stopAngle = 50f;

        public float Speed => distance / Mathf.Max(0.02f, seconds);
    }

    /// <summary>The starting values. The component's serialized values are the truth afterwards.</summary>
    public static class AerialAbilityDefaults
    {
        public static AerialRulesTuning Rules() => new AerialRulesTuning();
        public static AscensionTuning Ascension() => new AscensionTuning();
        public static GlideTuning Glide() => new GlideTuning();
        public static DashTuning Dash() => new DashTuning();
    }
}
