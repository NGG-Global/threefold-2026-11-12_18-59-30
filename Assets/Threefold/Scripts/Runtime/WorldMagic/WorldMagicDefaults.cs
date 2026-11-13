using Threefold.Combat;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>The example objects the setup command generates.</summary>
    public enum ExampleObject
    {
        ArcaneCrystal = 0,
        WoodenBarricade = 1,
        HeatSensitiveStone = 2,
        MovablePlatform = 3,
        UnstableStructure = 4,
        ReinforcedDoor = 5,
    }

    /// <summary>
    /// The starting values of the six example objects. The setup command creates their definition
    /// assets from these; after that the assets are the truth and can be tuned freely. Keeping the
    /// numbers in plain C# lets the tests check, with the disciplines' own default attacks, that each
    /// example really responds the way it is meant to.
    ///
    /// All values are first estimates. None has been played.
    /// </summary>
    public static class WorldMagicDefaults
    {
        public static int Count => 6;

        public static string Name(ExampleObject example)
        {
            switch (example)
            {
                case ExampleObject.ArcaneCrystal: return "Arcane Crystal";
                case ExampleObject.WoodenBarricade: return "Wooden Barricade";
                case ExampleObject.HeatSensitiveStone: return "Heat-Sensitive Stone";
                case ExampleObject.MovablePlatform: return "Movable Platform";
                case ExampleObject.UnstableStructure: return "Unstable Structure";
                default: return "Reinforced Door";
            }
        }

        /// <summary>What the example shows, in a sentence or two. Stored as the notes of its definition.</summary>
        public static string Description(ExampleObject example)
        {
            switch (example)
            {
                case ExampleObject.ArcaneCrystal:
                    return "Arcane-reactive. Arcane Arrows charge it; at full charge it activates for a time, then needs " +
                           "charging again. Charge drains if it is left alone. Heat and force do nothing to it.";
                case ExampleObject.WoodenBarricade:
                    return "Thermal and structural. Flame heats it until it catches fire; the fire consumes it. Force " +
                           "breaks it slowly. Arcane charge does nothing to it.";
                case ExampleObject.HeatSensitiveStone:
                    return "Thermal and structural. Cold, force barely marks it. Heated by Flame it becomes brittle and a " +
                           "strong push shatters it. It never burns.";
                case ExampleObject.MovablePlatform:
                    return "Kinetic. Force slides it along its rail, between its two stops. Weak pushes do not move it. " +
                           "Heat and arcane charge do nothing to it.";
                case ExampleObject.UnstableStructure:
                    return "Stability and structural. A strong push, or several weaker ones, brings it down. An Aegis " +
                           "guard held nearby braces it: while braced it neither falls nor moves.";
                default:
                    return "Structural and arcane-reactive. Only heavy force damages it; fractured, it gives way. Charging " +
                           "its lock with Arcane Arrows opens it instead. Heat does nothing to it.";
            }
        }

        public static MagicalPropertySettings Settings(ExampleObject example)
        {
            switch (example)
            {
                case ExampleObject.ArcaneCrystal: return ArcaneCrystal();
                case ExampleObject.WoodenBarricade: return WoodenBarricade();
                case ExampleObject.HeatSensitiveStone: return HeatSensitiveStone();
                case ExampleObject.MovablePlatform: return MovablePlatform();
                case ExampleObject.UnstableStructure: return UnstableStructure();
                default: return ReinforcedDoor();
            }
        }

        /// <summary>
        /// Arcane-reactive only. Arcane charge builds up and drains after a pause; at 100 the crystal
        /// activates for ten seconds and then goes dark, so it can be tried again. A fully charged
        /// Arcane Arrow activates it at once. Heat and force do nothing to it.
        /// </summary>
        private static MagicalPropertySettings ArcaneCrystal()
        {
            return new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings
                {
                    activationThreshold = 100f, decayDelay = 2f, decayPerSecond = 15f,
                    activation = ArcaneActivation.Timed, activeSeconds = 10f,
                },
            };
        }

        /// <summary>
        /// Thermal and structural. Wood heats quickly and catches fire; while it burns its structure is
        /// consumed, and one burn destroys it. A strong force also breaks it, in two full pulses. Arcane
        /// charge does nothing to it.
        /// </summary>
        private static MagicalPropertySettings WoodenBarricade()
        {
            return new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings
                {
                    susceptibility = 1.2f, capacity = 60f, heatedFraction = 0.4f, flammable = true,
                    decayDelay = 2f, decayPerSecond = 12f, burnSeconds = 6f, burnDamagePerSecond = 0f, burnTickSeconds = 0.5f,
                },
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 60f, weakenedFraction = 0.6f, fracturedFraction = 0.25f,
                    forceThreshold = 1f, forceDamage = 10f, brittleness = 1f, burnDamagePerSecond = 12f,
                },
            };
        }

        /// <summary>
        /// Thermal and structural, and the two depend on each other. The stone does not burn, and cold
        /// force barely marks it. Heated, it turns brittle: one full Force Pulse shatters it. It cools
        /// again in a few seconds, so the heat and the blow have to follow each other.
        /// </summary>
        private static MagicalPropertySettings HeatSensitiveStone()
        {
            return new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings
                {
                    susceptibility = 1f, capacity = 100f, heatedFraction = 0.6f, flammable = false,
                    decayDelay = 3f, decayPerSecond = 10f, burnSeconds = 0f, burnDamagePerSecond = 0f,
                },
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 100f, weakenedFraction = 0.6f, fracturedFraction = 0.25f,
                    forceThreshold = 1.5f, forceDamage = 4f, brittleness = 0.25f, heatedBrittleness = 8f,
                },
            };
        }

        /// <summary>
        /// Kinetic only. It slides along one axis, up to six metres from where it starts, by what a
        /// force leaves after its resistance. Light blows do not move it at all. Heat and charge do nothing.
        /// </summary>
        private static MagicalPropertySettings MovablePlatform()
        {
            return new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings
                {
                    resistance = 0.2f, minimumForce = 1.5f, mode = KineticMode.Axis, localAxis = Vector3.forward,
                    minTravel = 0f, maxTravel = 6f, maxStepPerPush = 4f, moveSpeed = 4f,
                },
            };
        }

        /// <summary>
        /// Stability and structural. It stands, but only just: a full Force Pulse brings it down at
        /// once, and lighter force adds up until it falls. Braced by an Aegis guard it holds against
        /// anything for as long as the brace lasts.
        /// </summary>
        private static MagicalPropertySettings UnstableStructure()
        {
            return new MagicalPropertySettings
            {
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 3.5f, stressLimit = 5f, stressRecoveryPerSecond = 0.5f,
                    unstableWhenStructure = StructuralTrigger.Weakened, collapseWhenStructure = StructuralTrigger.Fractured,
                    braceThreshold = 0.5f, braceSeconds = 2f, maxBraceSeconds = 6f,
                },
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 80f, weakenedFraction = 0.6f, fracturedFraction = 0.25f,
                    forceThreshold = 1f, forceDamage = 8f, brittleness = 1f,
                },
            };
        }

        /// <summary>
        /// Structural and arcane-reactive: two ways through. Its structure ignores fire and any force
        /// short of a well-charged Force Pulse; two full pulses breach it. Its arcane lock opens it
        /// instead, once it holds 150 charge, and stays open.
        /// </summary>
        private static MagicalPropertySettings ReinforcedDoor()
        {
            return new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 150f, weakenedFraction = 0.6f, fracturedFraction = 0.3f,
                    forceThreshold = 3.5f, forceDamage = 40f, brittleness = 1f,
                },
                arcane = true,
                arcaneSettings = new ArcaneSettings
                {
                    activationThreshold = 150f, decayDelay = 3f, decayPerSecond = 10f, activation = ArcaneActivation.Latch,
                },
            };
        }
    }
}
