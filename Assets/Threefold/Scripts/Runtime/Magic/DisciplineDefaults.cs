using Threefold.Combat;

namespace Threefold.Magic
{
    /// <summary>The numbers a charged attack starts with. The setup command writes them into its ability asset.</summary>
    public sealed class ChargedAttackDefaults
    {
        public string Name;
        public float ManaCost;
        public float CooldownSeconds;
        public ChargeProfile Charge;

        /// <summary>Set for a projectile attack, otherwise null.</summary>
        public ProjectileProfile Projectile;

        /// <summary>Set for an area attack, otherwise null.</summary>
        public AreaProfile Area;
    }

    /// <summary>
    /// The starting values of the three disciplines. The setup command creates the discipline
    /// assets from them; after that the assets are the truth and can be tuned freely. Keeping the
    /// numbers in plain C# lets the tests check the design intent: Arcane is the quickest and most
    /// precise, Flame spreads and heats, Aegis is slow and staggers.
    ///
    /// All values are first estimates. None has been played.
    /// </summary>
    public static class DisciplineDefaults
    {
        public static MeleeComboProfile Melee(MagicDiscipline discipline)
        {
            switch (discipline)
            {
                case MagicDiscipline.Flame:
                    return FlameMelee();
                case MagicDiscipline.Aegis:
                    return AegisMelee();
                default:
                    return ArcaneMelee();
            }
        }

        public static ChargedAttackDefaults Charged(MagicDiscipline discipline)
        {
            switch (discipline)
            {
                case MagicDiscipline.Flame:
                    return FlameWave();
                case MagicDiscipline.Aegis:
                    return ForcePulse();
                default:
                    return ArcaneArrow();
            }
        }

        /// <summary>What a discipline's guard does to nearby objects. Only an Aegis guard braces.</summary>
        public static GuardEffectProfile Guard(MagicDiscipline discipline)
        {
            if (discipline == MagicDiscipline.Aegis)
            {
                // It braces what is near, and sets force against a projectile that strikes it: enough for an
                // enemy's mote (which yields to 0.5), not for anything heavier.
                return new GuardEffectProfile
                {
                    stabilize = 1f, stabilizeSeconds = 1.5f, radius = 3.5f, pulseSeconds = 0.4f,
                    deflectForce = 1f, deflectAssistDegrees = 25f,
                };
            }

            return new GuardEffectProfile();
        }

        /// <summary>
        /// The version of the values a newly generated discipline holds. It is raised when a release
        /// adds values that assets generated earlier do not have; the setup command then adds them once.
        /// Version 1 added the effects on the world: force on Aegis swings, the brace of the Aegis guard
        /// and the charge the Arcane Arrow carries. Version 2 (LM-06) added the force the Aegis guard
        /// sets against projectiles. Version 3 (the martial styles, Docs/MartialStyles.md) added the Aegis
        /// forearm strike as the second of three, the commitment of heavy strikes, Arcane's longer
        /// anticipation, and step names that match the animations.
        /// </summary>
        public const int DataVersion = 3;

        /// <summary>The step names of version 2, by discipline, and the names version 3 gives them.</summary>
        private static readonly string[][] RenamedSteps =
        {
            new[] { "Arcane Jab", "Rising Palm", "Arcane Cut", "Circling Palm" },
            new[] { "Ember Swipe", "Ember Fist", "Ember Backswing", "Ember Hook" },
            new string[0],
        };

        /// <summary>
        /// Gives values generated before version 3 what the martial styles introduced, without losing tuning:
        /// - a step still named as in version 2 takes its new name;
        /// - a step whose start-up and recovery are still version 2's default (the Arcane strikes) takes the new
        ///   split, with the same total time;
        /// - a step without commitment takes the default commitment of the step of the same name;
        /// - an Aegis combo that is still the two strikes of version 2 gets the forearm strike between them.
        /// Damage, stagger, reach, arc and targets of existing steps are never changed.
        /// </summary>
        /// <returns>True if anything changed.</returns>
        public static bool FillMartialStyles(MagicDiscipline discipline, MeleeComboProfile melee)
        {
            if (melee == null || melee.steps == null)
            {
                return false;
            }

            bool changed = false;
            string[] renames = RenamedSteps[(int)discipline % RenamedSteps.Length];
            MeleeComboProfile defaults = Melee(discipline);

            foreach (MeleeStepProfile step in melee.steps)
            {
                if (step == null)
                {
                    continue;
                }

                for (int i = 0; i + 1 < renames.Length; i += 2)
                {
                    if (step.name == renames[i])
                    {
                        step.name = renames[i + 1];
                        changed = true;
                    }
                }

                MeleeStepProfile match = FindStep(defaults, step.name);
                if (match == null)
                {
                    continue;
                }

                if (discipline == MagicDiscipline.Arcane && Approximately(step.recoverySeconds + step.startupSeconds, match.TotalSeconds) &&
                    !Approximately(step.startupSeconds, match.startupSeconds) && IsVersion2ArcaneStartup(step.startupSeconds))
                {
                    step.startupSeconds = match.startupSeconds;
                    step.recoverySeconds = match.recoverySeconds;
                    changed = true;
                }

                if (!step.HasCommitment && match.HasCommitment && step.commitBeforeHitSeconds <= 0f && step.commitAfterHitSeconds <= 0f)
                {
                    step.committedMoveSpeedMultiplier = match.committedMoveSpeedMultiplier;
                    step.commitBeforeHitSeconds = match.commitBeforeHitSeconds;
                    step.commitAfterHitSeconds = match.commitAfterHitSeconds;
                    changed = true;
                }
            }

            if (discipline == MagicDiscipline.Aegis && melee.steps.Length == 2 && melee.steps[0] != null && melee.steps[1] != null &&
                melee.steps[0].name == "Force Palm" && melee.steps[1].name == "Bulwark Slam")
            {
                melee.steps = new[] { melee.steps[0], FindStep(defaults, "Bulwark Forearm"), melee.steps[1] };
                changed = true;
            }

            return changed;
        }

        private static MeleeStepProfile FindStep(MeleeComboProfile combo, string name)
        {
            if (combo?.steps == null)
            {
                return null;
            }

            foreach (MeleeStepProfile step in combo.steps)
            {
                if (step != null && step.name == name)
                {
                    return step;
                }
            }

            return null;
        }

        private static bool IsVersion2ArcaneStartup(float seconds)
        {
            return Approximately(seconds, 0.07f) || Approximately(seconds, 0.1f);
        }

        private static bool Approximately(float a, float b)
        {
            return System.Math.Abs(a - b) < 0.0005f;
        }

        /// <summary>
        /// Gives values generated before version 2 what that version introduced: the guard's deflection.
        /// Only a value that is still zero is filled, so anything tuned since is kept.
        /// </summary>
        /// <returns>True if anything changed.</returns>
        public static bool FillCombinations(MagicDiscipline discipline, GuardEffectProfile guard)
        {
            GuardEffectProfile defaults = Guard(discipline);
            if (guard == null || guard.deflectForce > 0f || !defaults.Deflects)
            {
                return false;
            }

            guard.deflectForce = defaults.deflectForce;
            guard.deflectAssistDegrees = defaults.deflectAssistDegrees;
            return true;
        }

        /// <summary>
        /// Gives values generated before version 1 the effects on the world that version introduced.
        /// Only a value that is still zero is filled, from the defaults of the same swing (by name, else by
        /// position), so anything tuned since is kept. The profiles passed in are changed in place; null ones are skipped.
        /// </summary>
        /// <returns>True if anything changed.</returns>
        public static bool FillWorldEffects(MagicDiscipline discipline, MeleeComboProfile melee, GuardEffectProfile guard,
            ProjectileProfile projectile)
        {
            bool changed = false;

            MeleeComboProfile meleeDefaults = Melee(discipline);
            if (melee != null && melee.steps != null)
            {
                for (int i = 0; i < melee.steps.Length && i < meleeDefaults.StepCount; i++)
                {
                    MeleeStepProfile step = melee.steps[i];

                    // By name where the step still has one of today's names (since version 3 Aegis has a strike
                    // between the two it had), otherwise by position.
                    MeleeStepProfile same = step != null ? FindStep(meleeDefaults, step.name) : null;
                    float force = (same ?? meleeDefaults.steps[i]).force;
                    if (step != null && step.force <= 0f && force > 0f)
                    {
                        step.force = force;
                        changed = true;
                    }
                }
            }

            GuardEffectProfile guardDefaults = Guard(discipline);
            if (guard != null && guard.stabilize <= 0f && guardDefaults.HasEffect)
            {
                guard.stabilize = guardDefaults.stabilize;
                guard.stabilizeSeconds = guardDefaults.stabilizeSeconds;
                guard.radius = guardDefaults.radius;
                guard.pulseSeconds = guardDefaults.pulseSeconds;
                changed = true;
            }

            ProjectileProfile projectileDefaults = Charged(discipline).Projectile;
            if (projectile != null && projectileDefaults != null && projectile.arcaneChargeMin <= 0f &&
                projectile.arcaneChargeMax <= 0f && projectileDefaults.arcaneChargeMax > 0f)
            {
                projectile.arcaneChargeMin = projectileDefaults.arcaneChargeMin;
                projectile.arcaneChargeMax = projectileDefaults.arcaneChargeMax;
                changed = true;
            }

            return changed;
        }

        public static SpellElement Element(MagicDiscipline discipline)
        {
            switch (discipline)
            {
                case MagicDiscipline.Flame:
                    return SpellElement.Fire;
                case MagicDiscipline.Aegis:
                    return SpellElement.Force;
                default:
                    return SpellElement.Arcane;
            }
        }

        // ------------------------------------------------------------ Arcane

        /// <summary>
        /// Fast, narrow, one target at a time: flowing precision (Docs/MartialStyles.md). A rising palm, a circling
        /// palm with the other hand, and a reaching spear hand. Full movement throughout: Arcane flows around its
        /// target. Each start-up is long enough to show a short anticipation (0.1 s; 0.07 s in version 2), with the
        /// same total time per strike, so the rhythm and the damage over time are unchanged.
        /// </summary>
        private static MeleeComboProfile ArcaneMelee()
        {
            return new MeleeComboProfile
            {
                inputBufferSeconds = 0.3f,
                facingAssistDegrees = 60f,
                facingAssistExtraRange = 1.4f,
                steps = new[]
                {
                    new MeleeStepProfile
                    {
                        name = "Rising Palm", damage = 9f, stagger = 6f, reach = 2.4f, arcDegrees = 70f, maxTargets = 1,
                        manaGainPerTarget = 3f, startupSeconds = 0.1f, recoverySeconds = 0.17f, comboWindowSeconds = 0.45f,
                        moveSpeedMultiplier = 1f, hitTrauma = 0.08f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Circling Palm", damage = 10f, stagger = 6f, reach = 2.4f, arcDegrees = 70f, maxTargets = 1,
                        manaGainPerTarget = 3f, startupSeconds = 0.1f, recoverySeconds = 0.17f, comboWindowSeconds = 0.45f,
                        moveSpeedMultiplier = 1f, hitTrauma = 0.08f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Arcane Lance", damage = 15f, stagger = 12f, reach = 2.8f, arcDegrees = 50f, maxTargets = 1,
                        manaGainPerTarget = 4f, startupSeconds = 0.12f, recoverySeconds = 0.28f, comboWindowSeconds = 0.35f,
                        moveSpeedMultiplier = 1f, hitTrauma = 0.14f,
                    },
                },
            };
        }

        /// <summary>A substantial shot aimed with the camera. Costs mana and has a short cooldown, so it is not a basic attack.</summary>
        private static ChargedAttackDefaults ArcaneArrow()
        {
            return new ChargedAttackDefaults
            {
                Name = "Arcane Arrow",
                ManaCost = 18f,
                CooldownSeconds = 0.4f,
                Charge = new ChargeProfile
                {
                    fullChargeSeconds = 1.1f, moveSpeedMultiplier = 0.85f,
                    targeting = ChargedTargetingMode.CameraAim, maxAimYawDegrees = 25f, maxAimPitchDegrees = 12f,
                    damageMin = 22f, damageMax = 48f, staggerMin = 15f, staggerMax = 40f,
                    element = SpellElement.Arcane, releaseTrauma = 0.18f,
                },
                Projectile = new ProjectileProfile
                {
                    speedMin = 30f, speedMax = 46f, range = 55f, radiusMin = 0.16f, radiusMax = 0.24f,
                    scaleMin = 0.9f, scaleMax = 1.5f, noiseRadius = 10f, arcaneChargeMin = 40f, arcaneChargeMax = 100f,
                },
            };
        }

        // ------------------------------------------------------------- Flame

        /// <summary>
        /// Medium pace, wide swings that catch several targets and heat them: explosive aggression
        /// (Docs/MartialStyles.md). A driving straight punch, a lunging hook and a spinning rising sweep. Each slows
        /// the Mage a little, and more for a moment around its hit (the commitment), so the strikes feel thrown
        /// with the whole body while the controls stay responsive before and after.
        /// </summary>
        private static MeleeComboProfile FlameMelee()
        {
            return new MeleeComboProfile
            {
                inputBufferSeconds = 0.3f,
                facingAssistDegrees = 50f,
                facingAssistExtraRange = 1.2f,
                steps = new[]
                {
                    new MeleeStepProfile
                    {
                        name = "Ember Fist", damage = 11f, stagger = 8f, reach = 2.3f, arcDegrees = 110f, maxTargets = 3,
                        heat = 18f, manaGainPerTarget = 2f, startupSeconds = 0.12f, recoverySeconds = 0.26f,
                        comboWindowSeconds = 0.5f, moveSpeedMultiplier = 0.9f, hitTrauma = 0.12f,
                        committedMoveSpeedMultiplier = 0.6f, commitBeforeHitSeconds = 0.04f, commitAfterHitSeconds = 0.1f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Ember Hook", damage = 11f, stagger = 8f, reach = 2.3f, arcDegrees = 110f, maxTargets = 3,
                        heat = 18f, manaGainPerTarget = 2f, startupSeconds = 0.12f, recoverySeconds = 0.26f,
                        comboWindowSeconds = 0.5f, moveSpeedMultiplier = 0.9f, hitTrauma = 0.12f,
                        committedMoveSpeedMultiplier = 0.35f, commitBeforeHitSeconds = 0.06f, commitAfterHitSeconds = 0.14f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Flare", damage = 16f, stagger = 12f, reach = 2.5f, arcDegrees = 140f, maxTargets = 4,
                        heat = 30f, manaGainPerTarget = 2f, startupSeconds = 0.16f, recoverySeconds = 0.36f,
                        comboWindowSeconds = 0.4f, moveSpeedMultiplier = 0.85f, hitTrauma = 0.18f,
                        committedMoveSpeedMultiplier = 0.15f, commitBeforeHitSeconds = 0.1f, commitAfterHitSeconds = 0.18f,
                    },
                },
            };
        }

        /// <summary>A directional wave along the ground that damages and heats up to six targets.</summary>
        private static ChargedAttackDefaults FlameWave()
        {
            return new ChargedAttackDefaults
            {
                Name = "Flame Wave",
                ManaCost = 26f,
                CooldownSeconds = 1f,
                Charge = new ChargeProfile
                {
                    fullChargeSeconds = 1f, moveSpeedMultiplier = 0.8f,
                    targeting = ChargedTargetingMode.GroundDirection,
                    damageMin = 14f, damageMax = 30f, staggerMin = 10f, staggerMax = 22f,
                    element = SpellElement.Fire, releaseTrauma = 0.22f,
                },
                Area = new AreaProfile
                {
                    reachMin = 5f, reachMax = 8f, arcDegrees = 75f, verticalReach = 2f, maxTargets = 6,
                    heatMin = 45f, heatMax = 80f, pushMin = 0f, pushMax = 0f, noiseRadius = 14f,
                },
            };
        }

        // ------------------------------------------------------------- Aegis

        /// <summary>
        /// Slow, deliberate, heavy on stagger: grounded power (Docs/MartialStyles.md). A sinking palm thrust, a
        /// forearm strike driven by the hips, and a two-handed slam. Each roots the Mage for a moment around its
        /// hit (the slam completely), so the weight shows; outside that the Mage still moves, slowly.
        /// </summary>
        private static MeleeComboProfile AegisMelee()
        {
            return new MeleeComboProfile
            {
                inputBufferSeconds = 0.35f,
                facingAssistDegrees = 50f,
                facingAssistExtraRange = 1f,
                steps = new[]
                {
                    new MeleeStepProfile
                    {
                        name = "Force Palm", damage = 14f, stagger = 30f, force = 0.6f, reach = 2.3f, arcDegrees = 90f, maxTargets = 2,
                        manaGainPerTarget = 3f, startupSeconds = 0.18f, recoverySeconds = 0.34f, comboWindowSeconds = 0.6f,
                        moveSpeedMultiplier = 0.7f, hitTrauma = 0.2f,
                        committedMoveSpeedMultiplier = 0.2f, commitBeforeHitSeconds = 0.1f, commitAfterHitSeconds = 0.16f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Bulwark Forearm", damage = 12f, stagger = 32f, force = 0.7f, reach = 2.2f, arcDegrees = 100f, maxTargets = 2,
                        manaGainPerTarget = 3f, startupSeconds = 0.2f, recoverySeconds = 0.38f, comboWindowSeconds = 0.55f,
                        moveSpeedMultiplier = 0.65f, hitTrauma = 0.22f,
                        committedMoveSpeedMultiplier = 0.25f, commitBeforeHitSeconds = 0.1f, commitAfterHitSeconds = 0.16f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Bulwark Slam", damage = 20f, stagger = 45f, force = 1f, reach = 2.6f, arcDegrees = 120f, maxTargets = 3,
                        manaGainPerTarget = 3f, startupSeconds = 0.26f, recoverySeconds = 0.46f, comboWindowSeconds = 0.5f,
                        moveSpeedMultiplier = 0.55f, hitTrauma = 0.3f,
                        committedMoveSpeedMultiplier = 0f, commitBeforeHitSeconds = 0.16f, commitAfterHitSeconds = 0.24f,
                    },
                },
            };
        }

        /// <summary>A short, wide impulse that staggers and shoves what it can; little damage.</summary>
        private static ChargedAttackDefaults ForcePulse()
        {
            return new ChargedAttackDefaults
            {
                Name = "Force Pulse",
                ManaCost = 22f,
                CooldownSeconds = 1.5f,
                Charge = new ChargeProfile
                {
                    fullChargeSeconds = 0.9f, moveSpeedMultiplier = 0.7f,
                    targeting = ChargedTargetingMode.GroundDirection,
                    damageMin = 6f, damageMax = 14f, staggerMin = 40f, staggerMax = 90f,
                    element = SpellElement.Force, releaseTrauma = 0.28f,
                },
                Area = new AreaProfile
                {
                    reachMin = 3.5f, reachMax = 5.5f, arcDegrees = 130f, verticalReach = 2f, maxTargets = 8,
                    heatMin = 0f, heatMax = 0f, pushMin = 2.5f, pushMax = 5f, noiseRadius = 12f,
                },
            };
        }
    }
}
