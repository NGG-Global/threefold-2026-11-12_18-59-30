using Threefold.Combat;
using Threefold.WorldMagic;

namespace Threefold.Enemies
{
    /// <summary>
    /// Default tuning of the two enemies. The setup command writes these values into the enemy
    /// assets, where they can be tuned; the tests use the same values. Ability slots refer to
    /// the abilities the setup command assigns to each enemy's caster.
    ///
    /// Reference points: the Mage has 100 health, walks at 4 m/s, sprints at 7 m/s, and Arcane
    /// Bolt deals 20 damage every 0.35 s for 12 of 100 mana.
    /// </summary>
    public static class EnemyArchetypes
    {
        public const int GuardianSlamSlot = 0;
        public const int GuardianShockwaveSlot = 1;
        public const int WispBoltSlot = 0;

        /// <summary>
        /// The version of the values a newly generated enemy definition holds. Version 1 (LM-06) added
        /// stagger by force, armour and ward.
        /// </summary>
        public const int DataVersion = 1;

        /// <summary>
        /// Gives a definition generated before version 1 what that version introduced, from its
        /// archetype, but only while all of it is still as an older asset has it (no stagger force, no
        /// armour, no ward). If any was set by hand, all are kept. Every other value is left alone.
        /// </summary>
        /// <returns>True if the values were filled in.</returns>
        public static bool FillCombinations(EnemyTuning tuning, EnemyTuning archetype)
        {
            if (tuning == null || archetype == null)
            {
                return false;
            }

            bool untouched = tuning.staggerForce <= 0f && (tuning.armour == null || !tuning.armour.enabled) &&
                             (tuning.ward == null || !tuning.ward.enabled);
            if (!untouched)
            {
                return false;
            }

            tuning.staggerForce = archetype.staggerForce;
            tuning.armour = archetype.armour ?? new ArmourSettings();
            tuning.ward = archetype.ward ?? new WardSettings();
            return true;
        }

        // ---------------------------------------------------------- projectiles

        /// <summary>
        /// How force turns an enemy's mote aside: any force of 0.5 or more (the Aegis guard sets 1, the
        /// Aegis strikes 0.6 and 1, the Force Pulse 2.5 and up), once, and it leaves 30% faster.
        /// </summary>
        public static ProjectileDeflection MoteDeflection => ProjectileDeflection.Designated(0.5f, 1, 1.3f);

        public const float CinderMoteDamage = 10f;
        public const float CinderMoteSpeed = 12f;
        public const float CinderMoteRange = 22f;
        public const float CinderMoteRadius = 0.24f;

        /// <summary>
        /// What a Cinder Wisp's mote carries apart from its damage: heat, for creatures and objects alike.
        /// One sets dry kindling alight; a wooden barricade needs two in quick succession.
        /// </summary>
        public static EffectPayload CinderMoteEffects => new EffectPayload { heat = 45f, targets = EffectTargets.All };

        /// <summary>
        /// What the Guardian's slam carries apart from its damage: force, for objects only. It cracks a
        /// heated stone, shoves a platform and unsettles an unstable structure; it does not shove the Mage.
        /// </summary>
        public static EffectPayload GuardianSlamEffects => new EffectPayload { force = 2.5f, targets = EffectTargets.Objects };

        /// <summary>The Guardian's shockwave: force enough to bring an unstable structure down, for objects only.</summary>
        public static EffectPayload GuardianShockwaveEffects => new EffectPayload { force = 3.5f, targets = EffectTargets.Objects };

        // ------------------------------------------------------------- defences

        /// <summary>
        /// The Guardian's stone plates: the same stone as the heat-sensitive barrier (brittleness 0.25 cold,
        /// 8 heated, 4 integrity per unit of force), in thinner plates (threshold 0.5, integrity 60). Cold,
        /// a full Force Pulse removes 4.5 of 60; heated, even an uncharged one breaks them.
        /// </summary>
        public static ArmourSettings GuardianArmour()
        {
            return new ArmourSettings
            {
                enabled = true,
                structure = new StructuralSettings
                {
                    integrity = 60f, weakenedFraction = 0.6f, fracturedFraction = 0.25f,
                    forceThreshold = 0.5f, forceDamage = 4f, brittleness = 0.25f, heatedBrittleness = 8f,
                    burnDamagePerSecond = 0f,
                },
                intactReduction = 0.2f,
                weakenedReduction = 0.1f,
                fracturedReduction = 0f,
                brokenVulnerability = 0.2f,
                staggerWhenBroken = true,
            };
        }

        /// <summary>
        /// The Wisp's ward: an arcane shell that takes 15% of every hit. 40 charge disrupts it for six
        /// seconds (an uncharged Arcane Arrow carries 40); it then forms again. A fully charged arrow
        /// still defeats a Wisp at Normal difficulty in one hit through the ward.
        /// </summary>
        public static WardSettings WispWard()
        {
            return new WardSettings
            {
                enabled = true,
                arcane = new ArcaneSettings
                {
                    susceptibility = 1f, activationThreshold = 40f, decayDelay = 2f, decayPerSecond = 20f,
                    activation = ArcaneActivation.Timed, activeSeconds = 6f, activeStacking = StackPolicy.Ignore,
                },
                reduction = 0.15f,
                staggerWhenDisrupted = true,
            };
        }

        /// <summary>
        /// Ruins Guardian: slow, durable, heavy-hitting. Every attack has a long, clear wind-up
        /// and a long recovery, which is the player's opening. Below 60% health it adds a
        /// shockwave all around itself, so circling it closely stops being safe.
        /// </summary>
        public static EnemyTuning RuinsGuardian()
        {
            return new EnemyTuning
            {
                role = EnemyRole.Melee,
                maxHealth = 200f,
                walkSpeed = 1.4f,
                runSpeed = 2.6f,
                turnSpeed = 150f,
                patrolPauseSeconds = 2f,
                sightRange = 13f,
                sightAngle = 110f,
                closeSenseRange = 2.5f,
                hearingRange = 12f,
                awarenessGainNear = 2.5f,
                awarenessGainFar = 0.5f,
                awarenessDecay = 0.15f,
                suspicionThreshold = 0.35f,
                trackingGraceSeconds = 1f,
                loseTargetSeconds = 6f,
                searchSeconds = 5f,
                leashRadius = 22f,
                alertReactionSeconds = 0.8f,
                preferredMinDistance = 0f,
                preferredMaxDistance = 2.2f,
                waitRingDistance = 4.5f,
                repositionSeconds = 2f,
                poiseThreshold = 55f,
                poiseRecoveryPerSecond = 18f,
                staggerSeconds = 1.3f,
                staggerImmunitySeconds = 2.5f,
                knockbackDistance = 0.4f,

                // Heavy stone: hard to shove, slow to heat. Only a heavy blow (a well-charged Force Pulse, a
                // structure coming down beside it) staggers it by force alone. Stone plates take a share of
                // every hit until heat and force break them.
                pushResistance = 0.6f,
                heat = new HeatSettings { susceptibility = 0.6f },
                staggerForce = 3.5f,
                armour = GuardianArmour(),
                ward = new WardSettings(),
                attacks = new[]
                {
                    new EnemyAttackProfile
                    {
                        name = "Shockwave",
                        abilitySlot = GuardianShockwaveSlot,
                        kind = AttackKind.Melee,
                        minRange = 0f,
                        maxRange = 3f,
                        maxStartAngle = 180f,
                        windupSeconds = 1.3f,
                        trackFraction = 0f,
                        recoverySeconds = 1.4f,
                        cooldownSeconds = 9f,
                        requiresLineOfSight = true,
                        usableBelowHealthFraction = 0.6f,
                    },
                    new EnemyAttackProfile
                    {
                        name = "Slam",
                        abilitySlot = GuardianSlamSlot,
                        kind = AttackKind.Melee,
                        minRange = 0f,
                        maxRange = 2.4f,
                        maxStartAngle = 30f,
                        windupSeconds = 0.95f,
                        trackFraction = 0.55f,
                        recoverySeconds = 1.1f,
                        cooldownSeconds = 2.4f,
                        requiresLineOfSight = true,
                        usableBelowHealthFraction = 1f,
                    },
                },
            };
        }

        /// <summary>
        /// Arcane Wisp: fragile and mobile. It keeps 6 to 11 m away, slips back when the player
        /// closes in, and fires slow, dodgeable bolts after a visible charge. Every hit staggers it.
        /// </summary>
        public static EnemyTuning ArcaneWisp()
        {
            return new EnemyTuning
            {
                role = EnemyRole.Ranged,
                maxHealth = 40f,
                walkSpeed = 2.4f,
                runSpeed = 4.2f,
                turnSpeed = 360f,
                patrolPauseSeconds = 1.2f,
                sightRange = 17f,
                sightAngle = 160f,
                closeSenseRange = 3f,
                hearingRange = 14f,
                awarenessGainNear = 3f,
                awarenessGainFar = 0.8f,
                awarenessDecay = 0.25f,
                suspicionThreshold = 0.3f,
                trackingGraceSeconds = 0.75f,
                loseTargetSeconds = 5f,
                searchSeconds = 4f,
                leashRadius = 20f,
                alertReactionSeconds = 0.5f,
                preferredMinDistance = 6f,
                preferredMaxDistance = 11f,
                waitRingDistance = 8f,
                repositionSeconds = 1.6f,
                poiseThreshold = 1f,
                poiseRecoveryPerSecond = 0f,
                staggerSeconds = 0.45f,
                staggerImmunitySeconds = 1f,
                knockbackDistance = 1.2f,

                // Light and airborne: thrown the full distance, and quick to catch fire. An arcane ward takes a
                // little of every hit until arcane charge disrupts it.
                pushResistance = 0f,
                heat = new HeatSettings { susceptibility = 1.3f },
                staggerForce = 1.5f,
                armour = new ArmourSettings(),
                ward = WispWard(),
                attacks = new[]
                {
                    new EnemyAttackProfile
                    {
                        name = "Arcane Mote",
                        abilitySlot = WispBoltSlot,
                        kind = AttackKind.Ranged,
                        minRange = 2.5f,
                        maxRange = 15f,
                        maxStartAngle = 25f,
                        windupSeconds = 0.8f,
                        trackFraction = 0.7f,
                        recoverySeconds = 0.4f,
                        cooldownSeconds = 2.2f,
                        requiresLineOfSight = true,
                        usableBelowHealthFraction = 1f,
                    },
                },
            };
        }

        /// <summary>
        /// Cinder Wisp: an Arcane Wisp of fire. It moves and fights like one but fires motes that carry
        /// heat, and it cannot be heated itself. It has no ward. Its motes can be turned aside by force,
        /// into whatever burns, or back at it.
        /// </summary>
        public static EnemyTuning CinderWisp()
        {
            EnemyTuning tuning = ArcaneWisp();
            tuning.heat = new HeatSettings { susceptibility = 0f };
            tuning.ward = new WardSettings();
            tuning.armour = new ArmourSettings();
            tuning.staggerForce = 1.5f;
            tuning.attacks = new[]
            {
                new EnemyAttackProfile
                {
                    name = "Cinder Mote",
                    abilitySlot = WispBoltSlot,
                    kind = AttackKind.Ranged,
                    minRange = 2.5f,
                    maxRange = 15f,
                    maxStartAngle = 25f,
                    windupSeconds = 0.9f,
                    trackFraction = 0.7f,
                    recoverySeconds = 0.4f,
                    cooldownSeconds = 2.6f,
                    requiresLineOfSight = true,
                    usableBelowHealthFraction = 1f,
                },
            };
            return tuning;
        }
    }
}
