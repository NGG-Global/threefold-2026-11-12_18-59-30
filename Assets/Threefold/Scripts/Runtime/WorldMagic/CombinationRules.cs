using Threefold.Combat;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// The relationships between effects and properties that hold wherever the two meet: on an object
    /// in the world, on a creature's armour, on a projectile in flight. The disciplines combine
    /// through these and nothing else, so a combination is found by reasoning about what things are,
    /// never by remembering a recipe:
    ///
    /// - HEAT + FORCE = FRACTURE. Heat makes a structure that is brittle in the heat more brittle;
    ///   force above its threshold then removes integrity. A heat-sensitive stone and a Guardian's
    ///   stone armour follow the same arithmetic.
    /// - ARCANE + MECHANISM = ACTIVATION. Charge builds to a threshold and the mechanism activates. A
    ///   mechanism may hold a part that only force can move once it is activated, and a creature's
    ///   ward is a mechanism too: its activation is its disruption.
    /// - FORCE + PROJECTILE = DEFLECTION. A force at least as strong as a projectile demands turns it
    ///   aside; it keeps what it carries.
    /// - STABILISE + INSTABILITY = BRACING. A brace stops what force and weight would otherwise bring
    ///   down, for as long as it lasts.
    /// - FORCE against FOOTING. One force at or above a threshold takes something off its feet: it
    ///   topples an unstable structure and staggers a creature.
    ///
    /// Plain C#, tested outside Unity.
    /// </summary>
    public static class CombinationRules
    {
        /// <summary>
        /// Whether a structure that belongs with this thermal property is in the heat: heated or
        /// burning. While it is, a structure with a brittleness for the heat uses it.
        /// </summary>
        public static bool IsHeated(HeatMeter thermal)
        {
            return thermal != null && thermal.State >= ThermalState.Heated;
        }

        /// <summary>
        /// A force on a structure, with the brittleness the heat gives it at this moment. The one rule
        /// for a stone barrier, a door and a creature's armour.
        /// </summary>
        /// <returns>The integrity removed.</returns>
        public static float ForceOnStructure(StructuralProperty structure, HeatMeter thermal, float magnitude)
        {
            return structure != null ? structure.ApplyForce(magnitude, IsHeated(thermal)) : 0f;
        }

        /// <summary>
        /// Whether one force is strong enough to take something off its footing: to stagger a creature
        /// or to turn a projectile aside. A threshold of 0 means force alone never does.
        /// </summary>
        public static bool Overcomes(float magnitude, float threshold)
        {
            return threshold > 0f && magnitude > 0f && magnitude + 0.0001f >= threshold;
        }
    }
}
