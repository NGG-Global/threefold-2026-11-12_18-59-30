using System;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>How an enemy fights: up close, or from a distance.</summary>
    public enum EnemyRole
    {
        Melee,
        Ranged,
    }

    /// <summary>Kind of attack, used to limit how many of each may happen at once.</summary>
    public enum AttackKind
    {
        Melee,
        Ranged,
    }

    /// <summary>
    /// When and how an enemy uses one of its abilities. What the ability does (damage, reach,
    /// projectile) is in the ability asset; this decides range, telegraph and pacing.
    /// </summary>
    [Serializable]
    public sealed class EnemyAttackProfile
    {
        [Tooltip("Shown in the Inspector only.")]
        public string name = "Attack";

        [Tooltip("Slot of the ability on the enemy's Ability Caster.")]
        [Min(0)]
        public int abilitySlot;

        public AttackKind kind = AttackKind.Melee;

        [Tooltip("Closest distance to the target at which this attack is started, in metres.")]
        [Min(0f)]
        public float minRange;

        [Tooltip("Furthest distance to the target at which this attack is started, in metres.")]
        [Min(0.1f)]
        public float maxRange = 2.4f;

        [Tooltip("The enemy turns until the target is within this angle of its facing before it starts. 180 needs no turning.")]
        [Range(0f, 180f)]
        public float maxStartAngle = 35f;

        [Tooltip("Telegraph: seconds between the warning and the hit. This is the player's time to react.")]
        [Min(0.05f)]
        public float windupSeconds = 0.9f;

        [Tooltip("Share of the wind-up during which the enemy keeps turning or aiming at the target. After it, the attack is locked and can be dodged.")]
        [Range(0f, 1f)]
        public float trackFraction = 0.6f;

        [Tooltip("Seconds the enemy stays still after the hit. The player's opening.")]
        [Min(0f)]
        public float recoverySeconds = 0.8f;

        [Tooltip("Seconds before this attack can be used again.")]
        [Min(0f)]
        public float cooldownSeconds = 2.5f;

        [Tooltip("Only started while the target is in sight.")]
        public bool requiresLineOfSight = true;

        [Tooltip("Only used once the enemy's health is at or below this share. 1 means always.")]
        [Range(0f, 1f)]
        public float usableBelowHealthFraction = 1f;

        public EnemyAttackProfile Clone()
        {
            return (EnemyAttackProfile)MemberwiseClone();
        }
    }

    /// <summary>
    /// Every number that shapes one kind of enemy: senses, movement, reactions and attacks. It
    /// is plain data, so the decision logic that reads it can be tested without Unity. Difficulty
    /// is applied by making a scaled copy (<see cref="Scaled"/>); the original is never changed.
    /// </summary>
    [Serializable]
    public sealed class EnemyTuning
    {
        [Header("Role")]
        public EnemyRole role = EnemyRole.Melee;

        [Header("Health")]
        [Min(1f)]
        public float maxHealth = 100f;

        [Header("Movement")]
        [Tooltip("Speed while patrolling, investigating and returning, in metres per second.")]
        [Min(0.1f)]
        public float walkSpeed = 1.6f;

        [Tooltip("Speed while chasing, in metres per second.")]
        [Min(0.1f)]
        public float runSpeed = 3.2f;

        [Tooltip("Turning speed, in degrees per second. A slow turn lets the player get round the enemy.")]
        [Min(10f)]
        public float turnSpeed = 240f;

        [Tooltip("Seconds spent at each patrol point.")]
        [Min(0f)]
        public float patrolPauseSeconds = 1.5f;

        [Header("Senses")]
        [Tooltip("How far the enemy can see, in metres.")]
        [Min(0.5f)]
        public float sightRange = 14f;

        [Tooltip("Width of the field of view, in degrees.")]
        [Range(10f, 360f)]
        public float sightAngle = 120f;

        [Tooltip("Within this distance a visible target is noticed at once, whichever way the enemy faces.")]
        [Min(0f)]
        public float closeSenseRange = 2.5f;

        [Tooltip("Noises within this distance are investigated, in metres.")]
        [Min(0f)]
        public float hearingRange = 12f;

        [Tooltip("Awareness gained per second when the target is seen up close. 1 is full awareness.")]
        [Min(0f)]
        public float awarenessGainNear = 2.5f;

        [Tooltip("Awareness gained per second when the target is seen at the edge of sight.")]
        [Min(0f)]
        public float awarenessGainFar = 0.6f;

        [Tooltip("Awareness lost per second while the target is out of sight and the enemy is not fighting.")]
        [Min(0f)]
        public float awarenessDecay = 0.2f;

        [Tooltip("Awareness at which the enemy becomes suspicious and comes to look.")]
        [Range(0.05f, 0.95f)]
        public float suspicionThreshold = 0.35f;

        [Tooltip("Seconds after losing sight during which the enemy still follows where the target went.")]
        [Min(0f)]
        public float trackingGraceSeconds = 0.75f;

        [Tooltip("Seconds without sight of the target before the enemy gives up the chase and searches.")]
        [Min(0.5f)]
        public float loseTargetSeconds = 5f;

        [Tooltip("Seconds spent looking around at a point of interest before returning.")]
        [Min(0f)]
        public float searchSeconds = 4f;

        [Tooltip("The enemy does not chase further than this from where it started, in metres. 0 means no limit.")]
        [Min(0f)]
        public float leashRadius = 24f;

        [Tooltip("Pause between noticing the target and moving, in seconds. Gives the player a moment to see they were spotted.")]
        [Min(0f)]
        public float alertReactionSeconds = 0.6f;

        [Header("Positioning")]
        [Tooltip("Ranged: closest the enemy wants to be to the target. Melee: unused.")]
        [Min(0f)]
        public float preferredMinDistance;

        [Tooltip("Ranged: furthest the enemy wants to be from the target. Melee: unused.")]
        [Min(0f)]
        public float preferredMaxDistance = 2.2f;

        [Tooltip("Melee: distance at which the enemy waits while others attack.")]
        [Min(1f)]
        public float waitRingDistance = 4.5f;

        [Tooltip("Seconds between choices of a new place to stand.")]
        [Min(0.2f)]
        public float repositionSeconds = 1.5f;

        [Header("Reactions")]
        [Tooltip("Damage taken in quick succession that interrupts the enemy. 0 means it is never staggered.")]
        [Min(0f)]
        public float poiseThreshold = 50f;

        [Tooltip("How quickly accumulated damage is forgotten, per second.")]
        [Min(0f)]
        public float poiseRecoveryPerSecond = 20f;

        [Tooltip("How long a stagger lasts, in seconds.")]
        [Min(0.05f)]
        public float staggerSeconds = 1f;

        [Tooltip("After a stagger, seconds during which the enemy cannot be staggered again. Prevents stun-locking.")]
        [Min(0f)]
        public float staggerImmunitySeconds = 2f;

        [Tooltip("How far a stagger pushes the enemy back, in metres.")]
        [Min(0f)]
        public float knockbackDistance = 0.5f;

        [Tooltip("Share of a force push (such as the Force Pulse) that this enemy resists. 0 is moved the full " +
                 "distance; 1 cannot be moved.")]
        [Range(0f, 1f)]
        public float pushResistance;

        [Tooltip("How this enemy responds to heat from flame attacks. Set susceptibility to 0 for an enemy that cannot burn.")]
        public HeatSettings heat = new HeatSettings();

        [Header("Attacks")]
        [Tooltip("In order of preference: the first one whose conditions are met is used.")]
        public EnemyAttackProfile[] attacks = new EnemyAttackProfile[0];

        /// <summary>A deep copy, so a scaled version never changes the asset it came from.</summary>
        public EnemyTuning Clone()
        {
            var copy = (EnemyTuning)MemberwiseClone();
            copy.attacks = new EnemyAttackProfile[attacks != null ? attacks.Length : 0];
            for (int i = 0; i < copy.attacks.Length; i++)
            {
                copy.attacks[i] = attacks[i] != null ? attacks[i].Clone() : null;
            }

            return copy;
        }

        /// <summary>A copy with the difficulty's multipliers applied.</summary>
        public EnemyTuning Scaled(EnemyDifficultySettings difficulty)
        {
            EnemyTuning copy = Clone();
            if (difficulty == null)
            {
                return copy;
            }

            copy.maxHealth = Mathf.Max(1f, maxHealth * difficulty.healthMultiplier);
            copy.walkSpeed = Mathf.Max(0.1f, walkSpeed * difficulty.speedMultiplier);
            copy.runSpeed = Mathf.Max(0.1f, runSpeed * difficulty.speedMultiplier);
            copy.awarenessGainNear = awarenessGainNear * difficulty.awarenessMultiplier;
            copy.awarenessGainFar = awarenessGainFar * difficulty.awarenessMultiplier;

            for (int i = 0; i < copy.attacks.Length; i++)
            {
                EnemyAttackProfile attack = copy.attacks[i];
                if (attack == null)
                {
                    continue;
                }

                attack.windupSeconds = Mathf.Max(0.05f, attack.windupSeconds * difficulty.telegraphMultiplier);
                attack.cooldownSeconds = Mathf.Max(0f, attack.cooldownSeconds * difficulty.cooldownMultiplier);
            }

            return copy;
        }
    }

    /// <summary>
    /// A difficulty level: multipliers for every enemy, ranged accuracy, and the encounter pacing
    /// that keeps fights readable (how many enemies may attack at once and how far apart).
    /// </summary>
    [Serializable]
    public sealed class EnemyDifficultySettings
    {
        public string displayName = "Normal";

        [Header("Enemies")]
        [Min(0.1f)] public float healthMultiplier = 1f;
        [Min(0f)] public float damageMultiplier = 1f;
        [Min(0.1f)] public float speedMultiplier = 1f;

        [Tooltip("How quickly enemies notice the player.")]
        [Min(0f)] public float awarenessMultiplier = 1f;

        [Tooltip("Length of attack telegraphs. Above 1 gives the player more time to react.")]
        [Min(0.1f)] public float telegraphMultiplier = 1f;

        [Tooltip("Time between an enemy's attacks.")]
        [Min(0.1f)] public float cooldownMultiplier = 1f;

        [Header("Ranged accuracy")]
        [Tooltip("Share of the player's movement that ranged enemies anticipate.")]
        [Range(0f, 1f)] public float aimLead = 0.5f;

        [Tooltip("Random scatter of enemy shots, in degrees.")]
        [Range(0f, 15f)] public float aimSpreadDegrees = 2f;

        [Header("Encounter pacing")]
        [Tooltip("Most enemies attacking at the same moment.")]
        [Min(1)] public int maxSimultaneousAttackers = 2;

        [Tooltip("Most melee enemies attacking at the same moment.")]
        [Min(1)] public int maxSimultaneousMelee = 1;

        [Tooltip("Least time between the start of two attacks, so telegraphs do not overlap.")]
        [Min(0f)] public float minSecondsBetweenAttacks = 0.5f;

        [Tooltip("An enemy outside the camera's view waits at least this long before it may attack.")]
        [Min(0f)] public float offscreenAttackDelay = 1.5f;

        public static EnemyDifficultySettings Easy()
        {
            return new EnemyDifficultySettings
            {
                displayName = "Easy",
                healthMultiplier = 0.8f,
                damageMultiplier = 0.7f,
                speedMultiplier = 0.9f,
                awarenessMultiplier = 0.75f,
                telegraphMultiplier = 1.3f,
                cooldownMultiplier = 1.35f,
                aimLead = 0f,
                aimSpreadDegrees = 4f,
                maxSimultaneousAttackers = 1,
                maxSimultaneousMelee = 1,
                minSecondsBetweenAttacks = 1f,
                offscreenAttackDelay = 2.5f,
            };
        }

        public static EnemyDifficultySettings Normal()
        {
            return new EnemyDifficultySettings();
        }

        public static EnemyDifficultySettings Hard()
        {
            return new EnemyDifficultySettings
            {
                displayName = "Hard",
                healthMultiplier = 1.25f,
                damageMultiplier = 1.3f,
                speedMultiplier = 1.1f,
                awarenessMultiplier = 1.35f,
                telegraphMultiplier = 0.8f,
                cooldownMultiplier = 0.8f,
                aimLead = 0.85f,
                aimSpreadDegrees = 1f,
                maxSimultaneousAttackers = 3,
                maxSimultaneousMelee = 2,
                minSecondsBetweenAttacks = 0.25f,
                offscreenAttackDelay = 0.8f,
            };
        }
    }
}
