using Threefold.Combat;

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

                // Heavy stone: hard to shove, slow to heat.
                pushResistance = 0.6f,
                heat = new HeatSettings { susceptibility = 0.6f },
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

                // Light and airborne: thrown the full distance, and quick to catch fire.
                pushResistance = 0f,
                heat = new HeatSettings { susceptibility = 1.3f },
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
    }
}
