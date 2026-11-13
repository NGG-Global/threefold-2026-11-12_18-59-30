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

        /// <summary>Fast, narrow, one target at a time. Three quick strikes; the third reaches further.</summary>
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
                        name = "Arcane Jab", damage = 9f, stagger = 6f, reach = 2.4f, arcDegrees = 70f, maxTargets = 1,
                        manaGainPerTarget = 3f, startupSeconds = 0.07f, recoverySeconds = 0.2f, comboWindowSeconds = 0.45f,
                        moveSpeedMultiplier = 1f, hitTrauma = 0.08f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Arcane Cut", damage = 10f, stagger = 6f, reach = 2.4f, arcDegrees = 70f, maxTargets = 1,
                        manaGainPerTarget = 3f, startupSeconds = 0.07f, recoverySeconds = 0.2f, comboWindowSeconds = 0.45f,
                        moveSpeedMultiplier = 1f, hitTrauma = 0.08f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Arcane Lance", damage = 15f, stagger = 12f, reach = 2.8f, arcDegrees = 50f, maxTargets = 1,
                        manaGainPerTarget = 4f, startupSeconds = 0.1f, recoverySeconds = 0.3f, comboWindowSeconds = 0.35f,
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
                    scaleMin = 0.9f, scaleMax = 1.5f, noiseRadius = 10f,
                },
            };
        }

        // ------------------------------------------------------------- Flame

        /// <summary>Medium pace, wide swings that catch several targets and heat them.</summary>
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
                        name = "Ember Swipe", damage = 11f, stagger = 8f, reach = 2.3f, arcDegrees = 110f, maxTargets = 3,
                        heat = 18f, manaGainPerTarget = 2f, startupSeconds = 0.12f, recoverySeconds = 0.26f,
                        comboWindowSeconds = 0.5f, moveSpeedMultiplier = 0.9f, hitTrauma = 0.12f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Ember Backswing", damage = 11f, stagger = 8f, reach = 2.3f, arcDegrees = 110f, maxTargets = 3,
                        heat = 18f, manaGainPerTarget = 2f, startupSeconds = 0.12f, recoverySeconds = 0.26f,
                        comboWindowSeconds = 0.5f, moveSpeedMultiplier = 0.9f, hitTrauma = 0.12f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Flare", damage = 16f, stagger = 12f, reach = 2.5f, arcDegrees = 140f, maxTargets = 4,
                        heat = 30f, manaGainPerTarget = 2f, startupSeconds = 0.16f, recoverySeconds = 0.36f,
                        comboWindowSeconds = 0.4f, moveSpeedMultiplier = 0.85f, hitTrauma = 0.18f,
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

        /// <summary>Slow, deliberate, heavy on stagger. Two strikes; the second is a wide slam.</summary>
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
                        name = "Force Palm", damage = 14f, stagger = 30f, reach = 2.3f, arcDegrees = 90f, maxTargets = 2,
                        manaGainPerTarget = 3f, startupSeconds = 0.18f, recoverySeconds = 0.34f, comboWindowSeconds = 0.6f,
                        moveSpeedMultiplier = 0.7f, hitTrauma = 0.2f,
                    },
                    new MeleeStepProfile
                    {
                        name = "Bulwark Slam", damage = 20f, stagger = 45f, reach = 2.6f, arcDegrees = 120f, maxTargets = 3,
                        manaGainPerTarget = 3f, startupSeconds = 0.26f, recoverySeconds = 0.46f, comboWindowSeconds = 0.5f,
                        moveSpeedMultiplier = 0.55f, hitTrauma = 0.3f,
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
