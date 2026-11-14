using Threefold.Combat;
using Threefold.Persistence;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>The example objects of the combination court (LM-06), apart from the six of LM-03.</summary>
    public enum CombinationExample
    {
        RuneLockedSlide = 0,
        CrumblingWalkway = 1,
        DryKindling = 2,
    }

    /// <summary>
    /// The starting values of the three objects the combination court adds. Like the six examples of
    /// LM-03 they are nothing but property values: no object has a script of its own, and each
    /// combination it shows follows from the properties it has. The setup command creates their
    /// definition assets from these; after that the assets are the truth.
    ///
    /// All values are first estimates. None has been played.
    /// </summary>
    public static class CombinationDefaults
    {
        public static int Count => 3;

        /// <summary>Length of the crumbling walkway along its slope, in metres. The court builds it this long.</summary>
        public const float WalkwayLength = 7f;

        /// <summary>Height of the ledge the walkway leads up to, in metres: beyond what the Mage can climb or catch.</summary>
        public const float WalkwayRise = 3.8f;

        public static string Name(CombinationExample example)
        {
            switch (example)
            {
                case CombinationExample.RuneLockedSlide: return "Rune-Locked Slide";
                case CombinationExample.CrumblingWalkway: return "Crumbling Walkway";
                default: return "Dry Kindling";
            }
        }

        public static string Description(CombinationExample example)
        {
            switch (example)
            {
                case CombinationExample.RuneLockedSlide:
                    return "Arcane-reactive and kinetic. A rune lock holds the block on its rail: no force moves it until " +
                           "arcane charge activates the lock. While the lock is lit, force slides the block; when it goes " +
                           "dark, the block stays where it was left.";
                case CombinationExample.CrumblingWalkway:
                    return "Stability. It gives way under anyone's weight within a moment. Braced by an Aegis guard it " +
                           "holds, for as long as the brace lasts; holding the guard longer builds a longer brace.";
                default:
                    return "Thermal and structural. Dry brush catches fire at a touch of heat and burns away in a few " +
                           "seconds, heating whatever stands close by.";
            }
        }

        public static MagicalPropertySettings Settings(CombinationExample example)
        {
            switch (example)
            {
                case CombinationExample.RuneLockedSlide: return RuneLockedSlide();
                case CombinationExample.CrumblingWalkway: return CrumblingWalkway();
                default: return DryKindling();
            }
        }

        /// <summary>
        /// ARCANE + MECHANISM = ACTIVATION, then FORCE. 80 charge lights the lock for ten seconds (a full
        /// Arcane Arrow, or two uncharged ones in quick succession); more charge while lit starts the
        /// ten seconds again. While lit, a Force Pulse slides the block along its five-metre rail: two
        /// metres for an uncharged pulse, four for a full one. Aegis strikes are below its minimum force.
        /// Unlit, nothing moves it, so the order is the object's own: the lock must be lit first. Where
        /// it was slid is where it is found again.
        /// </summary>
        private static MagicalPropertySettings RuneLockedSlide()
        {
            return new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings
                {
                    susceptibility = 1f, activationThreshold = 80f, decayDelay = 2f, decayPerSecond = 15f,
                    activation = ArcaneActivation.Timed, activeSeconds = 10f, activeStacking = StackPolicy.Refresh,
                },
                kinetic = true,
                kineticSettings = new KineticSettings
                {
                    resistance = 0.2f, minimumForce = 1.5f, mode = KineticMode.Axis, localAxis = Vector3.forward,
                    minTravel = 0f, maxTravel = 5f, maxStepPerPush = 5f, moveSpeed = 3f,
                    movableOnlyWhen = PropertyCondition.Activated,
                },
                persisted = PersistedState.Position,
            };
        }

        /// <summary>
        /// STABILISE + INSTABILITY = BRACING. Anyone standing on it adds two stress a second; at 1.5 it
        /// gives way, so nobody gets across in time. Braced, it takes no stress at all. Each pulse of the
        /// Aegis guard adds its 1.5 s to the brace, up to eight seconds, so holding the guard at its foot
        /// for a few seconds buys a crossing; keeping the guard up while walking renews it on the way. A
        /// heavy blow (4.5) also brings it down. It comes back on a restart.
        /// </summary>
        private static MagicalPropertySettings CrumblingWalkway()
        {
            return new MagicalPropertySettings
            {
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 4.5f, stressLimit = 1.5f, stressRecoveryPerSecond = 0.6f,
                    unstableWhenStructure = StructuralTrigger.Never, collapseWhenStructure = StructuralTrigger.Never,
                    braceThreshold = 0.5f, braceSeconds = 2f, maxBraceSeconds = 8f, braceStacking = StackPolicy.Extend,
                    braceBreakForce = 0f, loadStressPerSecond = 2f,
                },
                persisted = PersistedState.None,
            };
        }

        /// <summary>
        /// A small flammable thing: 20 heat sets it alight (one fiery mote, two Flame strikes), it burns
        /// away in four seconds, and while it burns it sends 25 heat a second to whatever is within 2.5 m:
        /// enough to set a wooden barricade beside it alight. Force scatters it. It is back after a restart.
        /// </summary>
        private static MagicalPropertySettings DryKindling()
        {
            return new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings
                {
                    susceptibility = 1.5f, capacity = 30f, heatedFraction = 0.4f, flammable = true,
                    decayDelay = 2f, decayPerSecond = 10f, burnSeconds = 5f, burnDamagePerSecond = 0f, burnTickSeconds = 0.5f,
                },
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 20f, weakenedFraction = 0.6f, fracturedFraction = 0.25f,
                    forceThreshold = 0.5f, forceDamage = 10f, brittleness = 1f, burnDamagePerSecond = 5f,
                },
                persisted = PersistedState.None,
                emissions = new[]
                {
                    new ReactionEmission
                    {
                        when = PropertyCondition.Burning, timing = EmissionTiming.WhileHolding, interval = 1f,
                        effect = MagicEffectKind.Heat, magnitude = 25f, radius = 2.5f, targets = EffectTargets.All,
                        priority = ReactionPriority.Normal,
                    },
                },
            };
        }
    }
}
