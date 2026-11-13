using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>What a character can do with an obstacle in front of it.</summary>
    public enum ObstacleAction
    {
        /// <summary>Low enough to walk over, or nothing to do.</summary>
        None,

        /// <summary>Jump over it in one movement.</summary>
        Vault,

        /// <summary>Climb up onto it.</summary>
        Mantle,

        /// <summary>Catch the edge and hang from it.</summary>
        Hang,

        /// <summary>Too high to climb from the ground.</summary>
        TooHigh,
    }

    /// <summary>
    /// The geometric decisions of traversal, separate from physics queries so they can be tested
    /// and tuned on their own. Heights are measured from the character's feet.
    /// </summary>
    public static class TraversalRules
    {
        /// <summary>Decides between vaulting over and climbing onto an obstacle met on the ground.</summary>
        /// <param name="height">Height of the obstacle's top above the feet.</param>
        /// <param name="thickness">Depth of the obstacle, or a negative value if it continues (a platform).</param>
        /// <param name="speed">Current horizontal speed.</param>
        public static ObstacleAction ClassifyObstacle(float height, float thickness, float speed, float stepHeight,
            float vaultMaxHeight, float mantleMaxHeight, float vaultMaxThickness, float vaultMinSpeed)
        {
            if (height <= stepHeight)
            {
                return ObstacleAction.None;
            }

            if (height > mantleMaxHeight)
            {
                return ObstacleAction.TooHigh;
            }

            bool thin = thickness >= 0f && thickness <= vaultMaxThickness;
            if (thin)
            {
                // Too tall to vault: climb onto it instead.
                if (height > vaultMaxHeight)
                {
                    return ObstacleAction.Mantle;
                }

                // A thin, low obstacle is vaulted at speed. Walking slowly, nothing happens: the
                // player is not carried over something they were only walking up to.
                return speed >= vaultMinSpeed ? ObstacleAction.Vault : ObstacleAction.None;
            }

            return ObstacleAction.Mantle;
        }

        /// <summary>What happens when a ledge is met in the air.</summary>
        public static ObstacleAction ClassifyAirLedge(float height, float stepHeight, float grabMinHeight, float grabMaxHeight)
        {
            if (height <= stepHeight || height > grabMaxHeight)
            {
                return ObstacleAction.None;
            }

            // Below hand height the character climbs straight on; higher, it catches the ledge and hangs.
            return height < grabMinHeight ? ObstacleAction.Mantle : ObstacleAction.Hang;
        }

        /// <summary>True if a surface is steep enough to be a wall.</summary>
        public static bool IsWall(Vector3 normal, float maxTiltDegrees)
        {
            return Mathf.Abs(normal.y) <= Mathf.Sin(maxTiltDegrees * Mathf.Deg2Rad);
        }

        /// <summary>True if a surface is level enough to stand on.</summary>
        public static bool IsWalkable(Vector3 normal, float maxSlopeDegrees)
        {
            return normal.y >= Mathf.Cos(maxSlopeDegrees * Mathf.Deg2Rad);
        }

        /// <summary>How strongly a direction points into a wall: 1 straight at it, -1 straight away.</summary>
        public static float Toward(Vector3 direction, Vector3 wallNormal)
        {
            direction.y = 0f;
            wallNormal.y = 0f;
            if (direction.sqrMagnitude < 1e-6f || wallNormal.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }

            return Vector3.Dot(direction.normalized, -wallNormal.normalized);
        }

        /// <summary>Where the feet are while hanging from a ledge corner.</summary>
        public static Vector3 HangPosition(Vector3 corner, Vector3 wallNormal, float radius, float wallGap, float hangDepth)
        {
            return corner + Flat(wallNormal) * (radius + wallGap) + Vector3.down * hangDepth;
        }

        /// <summary>Where the feet are after climbing onto a ledge.</summary>
        public static Vector3 StandPosition(Vector3 corner, Vector3 wallNormal, float radius, float inset)
        {
            return corner - Flat(wallNormal) * (radius + inset) + Vector3.up * 0.02f;
        }

        /// <summary>Smooth start and stop, 0 to 1.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>A point along a polyline at a share of its total length.</summary>
        public static Vector3 AlongPath(Vector3[] points, int count, float share)
        {
            if (count <= 0)
            {
                return Vector3.zero;
            }

            if (count == 1)
            {
                return points[0];
            }

            float total = 0f;
            for (int i = 1; i < count; i++)
            {
                total += Vector3.Distance(points[i - 1], points[i]);
            }

            if (total <= 1e-5f)
            {
                return points[count - 1];
            }

            float remaining = Mathf.Clamp01(share) * total;
            for (int i = 1; i < count; i++)
            {
                float length = Vector3.Distance(points[i - 1], points[i]);
                if (remaining <= length || i == count - 1)
                {
                    return Vector3.Lerp(points[i - 1], points[i], length > 1e-5f ? Mathf.Clamp01(remaining / length) : 1f);
                }

                remaining -= length;
            }

            return points[count - 1];
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.zero;
        }
    }
}
