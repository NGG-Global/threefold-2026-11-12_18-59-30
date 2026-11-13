using UnityEngine;

namespace Threefold.Interaction
{
    /// <summary>How far, at what angle and at what height the player can reach an interactable.</summary>
    public readonly struct InteractionReach
    {
        /// <summary>Greatest horizontal distance from the character, in metres.</summary>
        public readonly float Distance;

        /// <summary>Greatest angle from the character's facing, in degrees.</summary>
        public readonly float MaxAngle;

        /// <summary>Within this horizontal distance the angle does not matter, so a thing right beside the character can be used.</summary>
        public readonly float CloseDistance;

        /// <summary>Lowest and highest point that can be reached, relative to the feet, in metres.</summary>
        public readonly float MinHeight;
        public readonly float MaxHeight;

        public InteractionReach(float distance, float maxAngle, float closeDistance, float minHeight, float maxHeight)
        {
            Distance = distance;
            MaxAngle = maxAngle;
            CloseDistance = closeDistance;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
        }
    }

    /// <summary>
    /// The choice of what to interact with, as plain functions so it can be tested without Unity
    /// running: something within reach, roughly in front of the character, nearer and more
    /// central things first.
    /// </summary>
    public static class InteractionRules
    {
        /// <summary>
        /// Scores an interaction point. Lower is better: the horizontal distance, plus up to one
        /// reach length for an object at the edge of the allowed angle.
        /// </summary>
        /// <param name="feet">The character's position, at its feet.</param>
        /// <param name="forward">The way the character faces. Only its horizontal part is used.</param>
        /// <param name="point">The interaction point.</param>
        /// <returns>False if the point cannot be reached.</returns>
        public static bool TryScore(Vector3 feet, Vector3 forward, Vector3 point, in InteractionReach reach, out float score)
        {
            score = float.PositiveInfinity;

            Vector3 offset = point - feet;
            if (offset.y < reach.MinHeight || offset.y > reach.MaxHeight)
            {
                return false;
            }

            var flat = new Vector2(offset.x, offset.z);
            float distance = flat.magnitude;
            if (distance > reach.Distance)
            {
                return false;
            }

            float angle = 0f;
            var facing = new Vector2(forward.x, forward.z);
            if (distance > 0.0001f && facing.sqrMagnitude > 0.0001f)
            {
                angle = Vector2.Angle(facing, flat);
            }

            if (distance > reach.CloseDistance && angle > reach.MaxAngle)
            {
                return false;
            }

            score = distance + angle / 180f * reach.Distance;
            return true;
        }
    }
}
