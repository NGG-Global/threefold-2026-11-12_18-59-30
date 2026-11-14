using System.Collections.Generic;

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

                        if (kinetic.maxTravel <= kinetic.minTravel)
                        {
                            problems.Add("kinetic: there is no room to travel (the far limit must be beyond the near limit).");
                        }
                    }
                    else if (kinetic.maxRange <= 0f)
                    {
                        problems.Add("kinetic: the range it may move within must be above 0.");
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
                }
            }

            return problems.Count - before;
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
            }

            return reachable;
        }
    }
}
