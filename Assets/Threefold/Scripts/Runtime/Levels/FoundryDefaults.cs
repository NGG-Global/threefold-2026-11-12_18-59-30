using Threefold.Combat;
using Threefold.Persistence;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Levels
{
    /// <summary>The magical objects the Broken Foundry adds to those of LM-03 and LM-06.</summary>
    public enum FoundryObject
    {
        DormantCrystal = 0,
        Furnace = 1,
        CrumblingGantry = 2,
    }

    /// <summary>
    /// The starting values of the three kinds of object the Broken Foundry adds. Everything else in the
    /// level reuses a definition that already exists: the damaged barricade is the wooden barricade of
    /// LM-03, the slag plug is its heat-sensitive stone, the loose scaffolds are its unstable structure,
    /// and the rune lift and the counterweight are the rune-locked slide of LM-06. Like those, these
    /// are nothing but property values: no object has a script of its own. The setup command creates
    /// the definition assets from these; after that the assets are the truth.
    ///
    /// All values are first estimates. None has been played.
    /// </summary>
    public static class FoundryDefaults
    {
        public static int Count => 3;

        public static string Name(FoundryObject kind)
        {
            switch (kind)
            {
                case FoundryObject.DormantCrystal: return "Dormant Crystal";
                case FoundryObject.Furnace: return "Foundry Furnace";
                default: return "Crumbling Gantry";
            }
        }

        public static string Description(FoundryObject kind)
        {
            switch (kind)
            {
                case FoundryObject.DormantCrystal:
                    return "Arcane-reactive. Arcane charge builds up and drains after a pause; at full charge it wakes " +
                           "and stays awake, which is saved. Heat and force do nothing to it.";
                case FoundryObject.Furnace:
                    return "Thermal, and it cannot burn. Flame stokes it; once hot it stays hot for a while and sends " +
                           "heat out of its mouth every second to whatever stands close, creatures included. It cools " +
                           "by itself, so its heat is temporary.";
                default:
                    return "Stability. It gives way under anyone's weight within a moment. Braced by an Aegis guard it " +
                           "holds for as long as the brace lasts. Once down, a hoist winds it back up a few seconds " +
                           "after nobody stands on it, so a failed crossing can be tried again.";
            }
        }

        public static MagicalPropertySettings Settings(FoundryObject kind)
        {
            switch (kind)
            {
                case FoundryObject.DormantCrystal: return DormantCrystal();
                case FoundryObject.Furnace: return Furnace();
                default: return CrumblingGantry();
            }
        }

        /// <summary>
        /// The opening mechanism. A fully charged Arcane Arrow wakes it at once; three uncharged arrows do
        /// if each follows the last within the pause before the charge drains. Once awake it stays awake
        /// (Latch), and that is saved, so the foundry gate it opens stays open.
        /// </summary>
        private static MagicalPropertySettings DormantCrystal()
        {
            return new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings
                {
                    susceptibility = 1f, activationThreshold = 100f, decayDelay = 2.5f, decayPerSecond = 15f,
                    activation = ArcaneActivation.Latch,
                },
                persisted = PersistedState.Activation,
                critical = true,
            };
        }

        /// <summary>
        /// A furnace that holds heat. 60 heat makes it hot: a full Flame Wave, a full Flame combo, or an
        /// uncharged wave and one strike. It holds up to 120 and starts to cool six seconds after the last
        /// heat, four a second, so a well-stoked furnace stays hot for about twenty seconds. While hot it
        /// sends 22 heat a second out of its mouth to everything within 3.5 m: a Ruins Guardian standing
        /// there is heated in about four seconds (which makes its stone plates brittle) and catches fire
        /// in about eight; so would the Mage. Its heat is temporary and is not saved.
        /// </summary>
        private static MagicalPropertySettings Furnace()
        {
            return new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings
                {
                    susceptibility = 1f, capacity = 120f, heatedFraction = 0.5f, flammable = false,
                    decayDelay = 6f, decayPerSecond = 4f, burnSeconds = 0f, burnDamagePerSecond = 0f, burnTickSeconds = 0.5f,
                },
                persisted = PersistedState.None,
                emissions = new[]
                {
                    new ReactionEmission
                    {
                        when = PropertyCondition.Heated, timing = EmissionTiming.WhileHolding, interval = 1f,
                        effect = MagicEffectKind.Heat, magnitude = FurnaceHeatPerSecond, radius = FurnaceHeatRadius,
                        targets = EffectTargets.All, priority = ReactionPriority.Normal,
                    },
                },
            };
        }

        /// <summary>Heat the furnace sends each second while hot.</summary>
        public const float FurnaceHeatPerSecond = 22f;

        /// <summary>How far the furnace's heat reaches from its mouth, in metres.</summary>
        public const float FurnaceHeatRadius = 3.5f;

        /// <summary>Seconds a collapsed gantry waits, with nobody on it, before the hoist has it standing again.</summary>
        public const float GantryRecoverSeconds = 5f;

        /// <summary>
        /// STABILISE + INSTABILITY = BRACING, as the crumbling walkway of LM-06, with two differences.
        /// Weight brings it down twice as fast (4 stress a second against 1.5: under 0.4 s), because a
        /// level bridge could otherwise be crossed with two quick sprinting jumps: a jump spends about
        /// 0.7 s in the air, so the bridge is gone before the first one lands. And it recovers: five
        /// seconds after it came down, counted while nobody stands on it, it stands again. That makes a
        /// failed crossing something to try again, never a dead end, and since such a collapse is
        /// temporary nothing about it is saved. A held Aegis guard builds a brace of up to eight seconds,
        /// as before; braced, weight does nothing to it.
        /// </summary>
        private static MagicalPropertySettings CrumblingGantry()
        {
            return new MagicalPropertySettings
            {
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 4.5f, stressLimit = 1.5f, stressRecoveryPerSecond = 0.6f,
                    unstableWhenStructure = StructuralTrigger.Never, collapseWhenStructure = StructuralTrigger.Never,
                    braceThreshold = 0.5f, braceSeconds = 2f, maxBraceSeconds = 8f, braceStacking = StackPolicy.Extend,
                    braceBreakForce = 0f, loadStressPerSecond = 4f, recoverSeconds = GantryRecoverSeconds,
                },
                persisted = PersistedState.None,
            };
        }
    }
}
