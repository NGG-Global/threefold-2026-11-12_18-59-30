using System.Collections.Generic;
using Threefold.Persistence;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Checks on a set of property values, apart from any Unity object, so the validation the editor
    /// shows can be tested outside Unity. They find values that make an object silently do nothing:
    /// a structure nothing can damage, a platform with no room to travel, a brittleness that depends
    /// on heat the object cannot hold. Plain C#.
    /// </summary>
    public static class MagicalObjectRules
    {
        /// <summary>Appends one line per problem with the settings.</summary>
        /// <returns>The number of problems found.</returns>
        public static int Validate(MagicalPropertySettings settings, List<string> problems)
        {
            int before = problems.Count;

            if (settings == null)
            {
                problems.Add("has no property settings.");
                return problems.Count - before;
            }

            if (settings.PropertyCount == 0)
            {
                problems.Add("has no property switched on, so it responds to nothing.");
                return problems.Count - before;
            }

            bool canHeat = settings.thermal && settings.thermalSettings != null && settings.thermalSettings.susceptibility > 0f;
            bool canBurn = canHeat && settings.thermalSettings.flammable && settings.thermalSettings.burnSeconds > 0f;

            if (settings.thermal)
            {
                if (settings.thermalSettings == null || settings.thermalSettings.susceptibility <= 0f)
                {
                    problems.Add("thermal: susceptibility is 0, so it never heats.");
                }
                else if (settings.thermalSettings.capacity <= 0f)
                {
                    problems.Add("thermal: capacity must be above 0.");
                }
                else if (settings.thermalSettings.flammable && settings.thermalSettings.burnSeconds <= 0f)
                {
                    problems.Add("thermal: it is flammable but its burn time is 0, so it never burns; at full heat it " +
                                 "only goes cold again. Give it a burn time or switch flammable off.");
                }
            }

            if (settings.structural)
            {
                StructuralSettings structure = settings.structuralSettings;
                if (structure == null || structure.integrity <= 0f)
                {
                    problems.Add("structural: integrity must be above 0.");
                }
                else
                {
                    if (structure.fracturedFraction >= structure.weakenedFraction)
                    {
                        problems.Add("structural: the fractured threshold must be below the weakened threshold.");
                    }

                    bool forceDamages = structure.forceDamage > 0f && structure.brittleness > 0f;
                    bool heatedForceDamages = structure.forceDamage > 0f && structure.heatedBrittleness > 0f && canHeat;
                    bool fireDamages = structure.burnDamagePerSecond > 0f && canBurn;
                    if (!forceDamages && !heatedForceDamages && !fireDamages)
                    {
                        problems.Add("structural: nothing can damage it (no force damage, and no fire damage on something that burns).");
                    }

                    if (structure.heatedBrittleness > 0f && !canHeat)
                    {
                        problems.Add("structural: brittleness when heated is set, but the object has no thermal property that heats.");
                    }

                    if (structure.burnDamagePerSecond > 0f && !canBurn)
                    {
                        problems.Add("structural: fire damage is set, but the object cannot burn (no flammable thermal " +
                                     "property with a burn time).");
                    }
                }
            }

            if (settings.kinetic)
            {
                KineticSettings kinetic = settings.kineticSettings;
                if (kinetic == null)
                {
                    problems.Add("kinetic: has no values.");
                }
                else
                {
                    if (kinetic.resistance >= 1f)
                    {
                        problems.Add("kinetic: resistance is 1, so no force moves it.");
                    }

                    if (kinetic.moveSpeed <= 0f || kinetic.maxStepPerPush <= 0f)
                    {
                        problems.Add("kinetic: move speed and the step per push must be above 0.");
                    }

                    if (kinetic.mode == KineticMode.Axis)
                    {
                        if (kinetic.localAxis.sqrMagnitude < 0.0001f)
                        {
                            problems.Add("kinetic: the axis has no direction.");
                        }
                        else if (kinetic.localAxis.x * kinetic.localAxis.x + kinetic.localAxis.z * kinetic.localAxis.z <
                                 KineticRules.MinimumAlong * KineticRules.MinimumAlong * kinetic.localAxis.sqrMagnitude)
                        {
                            problems.Add("kinetic: the axis points up or down. A push is horizontal, so unless the object " +
                                         "is tilted, no push moves it along that axis.");
                        }

                        if (kinetic.maxTravel <= kinetic.minTravel)
                        {
                            problems.Add("kinetic: there is no room to travel (the far limit must be beyond the near limit).");
                        }
                    }
                    else if (kinetic.maxRange <= 0f)
                    {
                        problems.Add("kinetic: the range it may move within must be above 0.");
                    }

                    // A part held by a mechanism must be one the object can ever release.
                    PropertyCondition unlock = kinetic.movableOnlyWhen;
                    if ((unlock & MagicalPropertySet.KineticConditions) != 0)
                    {
                        problems.Add("kinetic: 'movable only when' names the movement itself (Moving, At Travel Limit), " +
                                     "which cannot unlock it. Name a state of another property, such as Activated.");
                    }

                    PropertyCondition releasable = unlock & ~MagicalPropertySet.KineticConditions;
                    if (releasable != PropertyCondition.None && (releasable & Reachable(settings)) == 0)
                    {
                        problems.Add($"kinetic: it moves only when '{releasable}', which the object can never reach with its " +
                                     "properties, so nothing ever moves it.");
                    }
                }
            }

            if (settings.arcane)
            {
                ArcaneSettings arcane = settings.arcaneSettings;
                if (arcane == null || arcane.susceptibility <= 0f)
                {
                    problems.Add("arcane: susceptibility is 0, so it never charges.");
                }
                else
                {
                    if (arcane.activationThreshold <= 0f)
                    {
                        problems.Add("arcane: the activation threshold must be above 0.");
                    }

                    if (arcane.activation == ArcaneActivation.Timed && arcane.activeSeconds <= 0f)
                    {
                        problems.Add("arcane: a timed activation needs a time above 0.");
                    }
                }
            }

            if (settings.stability)
            {
                StabilitySettings stability = settings.stabilitySettings;
                if (stability == null)
                {
                    problems.Add("stability: has no values.");
                }
                else
                {
                    bool unsettledByStructure = settings.structural && stability.unstableWhenStructure != StructuralTrigger.Never;
                    bool broughtDownByStructure = settings.structural && stability.collapseWhenStructure != StructuralTrigger.Never;

                    if (!stability.startsUnstable && !unsettledByStructure && !broughtDownByStructure)
                    {
                        problems.Add("stability: it starts stable and nothing can unsettle it, so the property never does anything.");
                    }

                    if (!settings.structural && (stability.unstableWhenStructure != StructuralTrigger.Never ||
                                                 stability.collapseWhenStructure != StructuralTrigger.Never))
                    {
                        problems.Add("stability: it follows the state of a structure, but the object has no structural property.");
                    }

                    if (stability.maxBraceSeconds <= 0f || stability.braceSeconds <= 0f)
                    {
                        problems.Add("stability: brace times must be above 0, or it can never be braced.");
                    }

                    if (stability.loadStressPerSecond > 0f && stability.stressLimit <= 0f)
                    {
                        problems.Add("stability: weight adds stress, but the stress limit is 0, so weight can never bring it down.");
                    }

                    if (stability.recoverSeconds > 0f && (settings.persisted & PersistedState.Collapse) != 0)
                    {
                        problems.Add("stability: it stands again by itself after coming down, so its collapse is temporary; " +
                                     "Collapse under Persisted is contradictory and is never saved.");
                    }
                }
            }

            ValidatePersistence(settings, problems);
            ValidateEmissions(settings, problems);

            return problems.Count - before;
        }

        /// <summary>
        /// Parts named to be saved that the object does not have. Only the deliberate choices are
        /// checked (damage, position and the two exceptions); the standard three are harmless on any object.
        /// </summary>
        private static void ValidatePersistence(MagicalPropertySettings settings, List<string> problems)
        {
            PersistedState saved = settings.persisted;
            if ((saved & PersistedState.Damage) != 0 && !settings.structural)
            {
                problems.Add("persistence: damage is to be saved, but the object has no structural property.");
            }

            if ((saved & PersistedState.Position) != 0 && !settings.kinetic)
            {
                problems.Add("persistence: the position is to be saved, but the object cannot be moved.");
            }

            if ((saved & PersistedState.Heat) != 0 && !settings.thermal)
            {
                problems.Add("persistence: heat is to be saved, but the object has no thermal property.");
            }

            if ((saved & PersistedState.Charge) != 0 && !settings.arcane)
            {
                problems.Add("persistence: charge is to be saved, but the object has no arcane property.");
            }
        }

        /// <summary>Reaction rules that can never fire, reach nothing, or would run for ever.</summary>
        private static void ValidateEmissions(MagicalPropertySettings settings, List<string> problems)
        {
            if (settings.emissions == null)
            {
                return;
            }

            PropertyCondition reachable = Reachable(settings);
            for (int i = 0; i < settings.emissions.Length; i++)
            {
                ReactionEmission rule = settings.emissions[i];
                string label = $"reaction {i + 1}";
                if (rule == null)
                {
                    problems.Add($"{label}: the entry is empty.");
                    continue;
                }

                if (rule.when == PropertyCondition.None)
                {
                    problems.Add($"{label}: it has no condition, so it never fires.");
                }
                else if ((rule.when & reachable) == 0)
                {
                    problems.Add($"{label}: it waits for '{rule.when}', which the object can never reach with its properties.");
                }

                if (rule.magnitude <= 0f)
                {
                    problems.Add($"{label}: its magnitude is 0, so it does nothing.");
                }

                if (rule.radius <= 0f && !rule.toLinkedTargets)
                {
                    problems.Add($"{label}: it has no radius and does not go to linked targets, so it reaches nothing.");
                }

                if (rule.targets == EffectTargets.None)
                {
                    problems.Add($"{label}: it is meant for neither creatures nor objects.");
                }
                else if (rule.effect == MagicEffectKind.Stabilize && (rule.targets & EffectTargets.Objects) == 0)
                {
                    problems.Add($"{label}: a brace means nothing to creatures; it has to include objects.");
                }

                if (rule.timing == EmissionTiming.WhileHolding)
                {
                    // Only conditions that end by themselves make a sensible repeating reaction.
                    const PropertyCondition Lasting = PropertyCondition.Weakened | PropertyCondition.Fractured |
                                                      PropertyCondition.Destroyed | PropertyCondition.Collapsed |
                                                      PropertyCondition.AtTravelLimit;
                    if ((rule.when & PropertyCondition.Destroyed) != 0 && (rule.when & ~PropertyCondition.Destroyed) == 0)
                    {
                        problems.Add($"{label}: it repeats while the object is destroyed, but a destroyed object sends nothing. Use On Enter.");
                    }
                    else if ((rule.when & Lasting) != 0)
                    {
                        problems.Add($"{label}: it repeats while '{rule.when & Lasting}' holds, a state that does not end by itself. " +
                                     "The object would be simulated for good. Use On Enter, or a state that passes.");
                    }
                }
            }
        }

        /// <summary>
        /// What happens to a state when the world is reloaded, for an object with these settings and an
        /// identifier: persistent if it is saved, temporary if it is reset. (Immediate things, such as
        /// an impact or a burst, are events and never states.)
        /// </summary>
        public static PersistenceTier TierOf(PropertyCondition condition, MagicalPropertySettings settings)
        {
            if (settings == null)
            {
                return PersistenceTier.Temporary;
            }

            PersistedState saved = settings.persisted;
            bool kept;
            switch (condition)
            {
                case PropertyCondition.Heated:
                    kept = (saved & PersistedState.Heat) != 0;
                    break;

                case PropertyCondition.Weakened:
                case PropertyCondition.Fractured:
                    kept = (saved & PersistedState.Damage) != 0;
                    break;

                case PropertyCondition.Destroyed:
                    kept = (saved & (PersistedState.Destruction | PersistedState.Damage)) != 0;
                    break;

                case PropertyCondition.Charged:
                    kept = (saved & PersistedState.Charge) != 0;
                    break;

                case PropertyCondition.Activated:
                {
                    ArcaneActivation mode = settings.arcaneSettings != null ? settings.arcaneSettings.activation : ArcaneActivation.Latch;
                    kept = ((saved & PersistedState.Activation) != 0 && mode == ArcaneActivation.Latch) ||
                           ((saved & PersistedState.Charge) != 0 && mode == ArcaneActivation.WhileCharged);
                    break;
                }

                case PropertyCondition.AtTravelLimit:
                    kept = (saved & PersistedState.Position) != 0;
                    break;

                case PropertyCondition.Collapsed:
                    kept = (saved & PersistedState.Collapse) != 0;
                    break;

                default:
                    // Burning, moving, braced and unstable never outlast a reload.
                    kept = false;
                    break;
            }

            return kept ? PersistenceTier.Persistent : PersistenceTier.Temporary;
        }

        /// <summary>True if an object with these settings has a property that this kind of effect acts on.</summary>
        public static bool Answers(MagicalPropertySettings settings, MagicEffectKind kind)
        {
            if (settings == null)
            {
                return false;
            }

            switch (kind)
            {
                case MagicEffectKind.Heat: return settings.thermal;
                case MagicEffectKind.ArcaneCharge: return settings.arcane;
                case MagicEffectKind.Force: return settings.structural || settings.kinetic || settings.stability;
                case MagicEffectKind.Stabilize: return settings.stability;
                default: return false;
            }
        }

        /// <summary>
        /// The conditions an object with these settings can ever meet. Something that waits for a
        /// condition outside this set (a trigger on "Burning" for an object that cannot burn) waits for ever.
        /// </summary>
        public static PropertyCondition Reachable(MagicalPropertySettings settings)
        {
            PropertyCondition reachable = PropertyCondition.None;
            if (settings == null)
            {
                return reachable;
            }

            if (settings.thermal && settings.thermalSettings != null && settings.thermalSettings.susceptibility > 0f)
            {
                reachable |= PropertyCondition.Heated;

                // The same condition the heat rule uses: flammable, and with a time to burn for.
                if (settings.thermalSettings.flammable && settings.thermalSettings.burnSeconds > 0f)
                {
                    reachable |= PropertyCondition.Burning;
                }
            }

            if (settings.structural)
            {
                reachable |= PropertyCondition.Weakened | PropertyCondition.Fractured | PropertyCondition.Destroyed;
            }

            if (settings.kinetic)
            {
                reachable |= PropertyCondition.Moving | PropertyCondition.AtTravelLimit;
            }

            if (settings.arcane)
            {
                reachable |= PropertyCondition.Charged | PropertyCondition.Activated;
            }

            if (settings.stability)
            {
                reachable |= PropertyCondition.Unstable | PropertyCondition.Braced | PropertyCondition.Collapsed;
                if (settings.stabilitySettings != null && settings.stabilitySettings.loadStressPerSecond > 0f)
                {
                    reachable |= PropertyCondition.Loaded;
                }
            }

            return reachable;
        }
    }
}
