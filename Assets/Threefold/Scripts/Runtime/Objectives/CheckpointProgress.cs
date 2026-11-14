using System;
using UnityEngine;

namespace Threefold.Objectives
{
    /// <summary>
    /// Which checkpoint of a level the player comes back to. Checkpoints are numbered in the order the
    /// level is played; the current one is the furthest reached, so walking back past an earlier one
    /// never moves the player back. What has been reached is kept in the one world state, as a puzzle
    /// record per checkpoint (see <c>Checkpoint</c>), so it is saved with everything else and nothing
    /// new is needed to save it. Plain C#.
    /// </summary>
    public sealed class CheckpointProgress
    {
        private readonly bool[] reached;

        public CheckpointProgress(int count)
        {
            reached = new bool[Math.Max(0, count)];
            Current = -1;
        }

        public int Count => reached.Length;

        /// <summary>The furthest checkpoint reached, or -1 if none has been.</summary>
        public int Current { get; private set; }

        public bool HasCurrent => Current >= 0;

        public bool IsReached(int order)
        {
            return order >= 0 && order < reached.Length && reached[order];
        }

        /// <summary>Notes that a checkpoint was reached. Returns true if it is now the current one, further than before.</summary>
        public bool Reach(int order)
        {
            if (order < 0 || order >= reached.Length)
            {
                return false;
            }

            reached[order] = true;
            if (order <= Current)
            {
                return false;
            }

            Current = order;
            return true;
        }

        /// <summary>Takes what is remembered, for example after a load or after the saved state was replaced.</summary>
        /// <param name="isRemembered">Whether the checkpoint of a given order is remembered as reached.</param>
        public void Restore(Func<int, bool> isRemembered)
        {
            Current = -1;
            for (int i = 0; i < reached.Length; i++)
            {
                reached[i] = isRemembered != null && isRemembered(i);
                if (reached[i])
                {
                    Current = i;
                }
            }
        }

        /// <summary>Forgets every checkpoint: the level starts from its beginning.</summary>
        public void Clear()
        {
            Array.Clear(reached, 0, reached.Length);
            Current = -1;
        }

        /// <summary>The key a checkpoint is remembered under: unique per level as long as the prefix is.</summary>
        public static string KeyFor(string prefix, int order)
        {
            return $"{prefix}Checkpoint/{order}";
        }

        /// <summary>Whether a point lies in a box given in the box's own space by its half extents.</summary>
        public static bool Contains(Vector3 localPoint, Vector3 halfExtents)
        {
            return Mathf.Abs(localPoint.x) <= halfExtents.x && Mathf.Abs(localPoint.y) <= halfExtents.y &&
                   Mathf.Abs(localPoint.z) <= halfExtents.z;
        }
    }

    /// <summary>
    /// When a hazard (molten slag) puts the player back at the last checkpoint. Plain C#.
    /// </summary>
    public static class HazardRules
    {
        /// <summary>
        /// Whether the player is put back after the hazard has dealt its damage: only while still standing.
        /// A hazard that defeats the player leaves the rest to the usual defeat and restart.
        /// </summary>
        public static bool ReturnsPlayer(float healthAfterDamage)
        {
            return healthAfterDamage > 0f;
        }

        /// <summary>Whether enough time has passed since the hazard last acted to act again.</summary>
        public static bool Ready(float now, float lastActed, float cooldown)
        {
            return now - lastActed >= cooldown;
        }
    }
}
