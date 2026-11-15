using Threefold.WorldMagic;

namespace Threefold.CombatFeedback
{
    /// <summary>How the arrival of magic at an object is shown.</summary>
    public enum WorldArrivalLook
    {
        None = 0,

        /// <summary>Arcane charge flows into the object from where it struck, and gathers in it.</summary>
        ChargeInflow = 1,

        /// <summary>Fire licks the object where the heat struck it.</summary>
        Heat = 2,

        /// <summary>A pressure streak along the push, at the object.</summary>
        Force = 3,

        /// <summary>A lattice of force closes round the object and holds it.</summary>
        Brace = 4,
    }

    /// <summary>
    /// Which arrival is shown when a magical effect reaches an object. It follows from the effect and from the
    /// object's answer, never from the kind of object: an object that took the effect shows its arrival; one
    /// that has no property for it, filtered it, already took it from the same attack, or resisted it, shows
    /// nothing here (its own state visuals, configured in its definition, show what changed). Plain C#.
    /// </summary>
    public static class WorldReactionVfx
    {
        public static WorldArrivalLook Choose(MagicEffectKind kind, EffectResponse response)
        {
            if (response != EffectResponse.Applied)
            {
                return WorldArrivalLook.None;
            }

            switch (kind)
            {
                case MagicEffectKind.ArcaneCharge:
                    return WorldArrivalLook.ChargeInflow;
                case MagicEffectKind.Heat:
                    return WorldArrivalLook.Heat;
                case MagicEffectKind.Force:
                    return WorldArrivalLook.Force;
                case MagicEffectKind.Stabilize:
                    return WorldArrivalLook.Brace;
                default:
                    return WorldArrivalLook.None;
            }
        }

        /// <summary>
        /// Whether a reaction passed on from another object (a chain) shows an arrival too. Only the first link,
        /// the player's own attack, does: a chain shows its result through the objects' own state visuals, so a
        /// long chain does not fill the screen.
        /// </summary>
        public static bool ShowsArrival(int depth)
        {
            return depth <= 0;
        }
    }
}
