using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// The geometric and timing decisions of enemy behaviour, separate from Unity's physics and
    /// navigation queries so they can be tested and tuned on their own. Distances are measured
    /// along the ground unless stated otherwise.
    /// </summary>
    public static class EnemyRules
    {
        /// <summary>
        /// Directions tried, in order, when looking for a place to stand around a target, in degrees
        /// from the enemy's current side. The enemy's own side is preferred, so it moves as little
        /// as possible and enemies spread out instead of converging on one spot.
        /// </summary>
        private static readonly float[] StandPointAngles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };

        public static int StandPointCandidateCount => StandPointAngles.Length;

        public static float GetStandPointAngle(int index)
        {
            return StandPointAngles[Mathf.Clamp(index, 0, StandPointAngles.Length - 1)];
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>
        /// True when the target lies within the field of view, judged along the ground. A cone of
        /// 360 degrees sees all around.
        /// </summary>
        public static bool IsInsideViewCone(Vector3 forward, Vector3 toTarget, float coneDegrees)
        {
            if (coneDegrees >= 359.9f)
            {
                return true;
            }

            forward.y = 0f;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(forward, toTarget) <= coneDegrees * 0.5f;
        }

        /// <summary>
        /// Awareness gained per second when a target is seen at the given distance: fast up close,
        /// slow at the edge of sight. Within the close sense range it is infinite: noticed at once.
        /// </summary>
        public static float AwarenessGainRate(float distance, float closeRange, float sightRange, float gainNear,
            float gainFar)
        {
            if (distance <= closeRange)
            {
                return float.PositiveInfinity;
            }

            float span = Mathf.Max(0.01f, sightRange - closeRange);
            float t = Mathf.Clamp01((distance - closeRange) / span);
            return Mathf.Lerp(gainNear, gainFar, t);
        }

        /// <summary>Rotates a direction about the vertical axis. Positive degrees turn clockwise seen from above, as in Unity.</summary>
        public static Vector3 RotateAroundUp(Vector3 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector3(direction.x * cos + direction.z * sin, direction.y, -direction.x * sin + direction.z * cos);
        }

        /// <summary>
        /// A point at <paramref name="distance"/> from <paramref name="centre"/>, on the side of
        /// <paramref name="from"/>, turned by <paramref name="angleDegrees"/> around the centre.
        /// </summary>
        public static Vector3 PointAround(Vector3 centre, Vector3 from, float distance, float angleDegrees)
        {
            Vector3 direction = from - centre;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.back;
            direction = RotateAroundUp(direction, angleDegrees);
            return centre + direction * distance;
        }

        /// <summary>
        /// Chooses the attack to start now: the first one in the list that is off cooldown, allowed
        /// at the current health, has the target in range, and has sight if it needs it.
        /// </summary>
        /// <returns>The index of the attack, or -1 if none can be started.</returns>
        public static int ChooseAttack(IReadOnlyList<EnemyAttackProfile> attacks, IReadOnlyList<float> cooldowns,
            float distance, float healthFraction, bool targetVisible)
        {
            if (attacks == null)
            {
                return -1;
            }

            for (int i = 0; i < attacks.Count; i++)
            {
                EnemyAttackProfile attack = attacks[i];
                if (!IsReady(attack, cooldowns, i, healthFraction))
                {
                    continue;
                }

                if (attack.requiresLineOfSight && !targetVisible)
                {
                    continue;
                }

                if (distance < attack.minRange || distance > attack.maxRange)
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        /// <summary>
        /// The first attack that is off cooldown and allowed at the current health, wherever the
        /// target is. Tells a melee enemy whether closing in is worth it.
        /// </summary>
        public static int FirstReadyAttack(IReadOnlyList<EnemyAttackProfile> attacks, IReadOnlyList<float> cooldowns,
            float healthFraction)
        {
            if (attacks == null)
            {
                return -1;
            }

            for (int i = 0; i < attacks.Count; i++)
            {
                if (IsReady(attacks[i], cooldowns, i, healthFraction))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// True when the chase should be abandoned: the enemy has gone further from home than its
        /// leash and the target is beyond it as well. A target that comes back within reach is
        /// still chased.
        /// </summary>
        public static bool ShouldGiveUpChase(Vector3 home, Vector3 self, Vector3 target, float leashRadius)
        {
            if (leashRadius <= 0f)
            {
                return false;
            }

            return FlatDistance(home, self) > leashRadius && FlatDistance(home, target) > leashRadius;
        }

        /// <summary>The distance a ranged enemy aims for: the middle of its preferred band.</summary>
        public static float PreferredDistance(float minimum, float maximum)
        {
            return maximum > minimum ? (minimum + maximum) * 0.5f : minimum;
        }

        private static bool IsReady(EnemyAttackProfile attack, IReadOnlyList<float> cooldowns, int index, float healthFraction)
        {
            if (attack == null)
            {
                return false;
            }

            if (cooldowns != null && index < cooldowns.Count && cooldowns[index] > 0f)
            {
                return false;
            }

            return healthFraction <= attack.usableBelowHealthFraction + 0.0001f;
        }
    }
}
