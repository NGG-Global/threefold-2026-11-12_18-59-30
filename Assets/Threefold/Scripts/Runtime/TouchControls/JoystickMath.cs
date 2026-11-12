using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>Pure calculations for the virtual joystick, kept separate so they can be unit tested.</summary>
    public static class JoystickMath
    {
        /// <summary>
        /// Converts a raw stick deflection into a movement value.
        /// </summary>
        /// <param name="raw">Deflection where a length of 1 is the edge of the stick's travel.</param>
        /// <param name="deadZone">Deflection below which the result is zero, from 0 up to (not including) 1.</param>
        /// <param name="responseExponent">
        /// Shape of the response. 1 is linear. Values above 1 give finer control near the centre.
        /// </param>
        /// <returns>
        /// A vector in the direction of <paramref name="raw"/> whose length rises smoothly from 0 at
        /// the dead zone to 1 at full deflection, so there is no jump when the dead zone is left.
        /// </returns>
        public static Vector2 Process(Vector2 raw, float deadZone, float responseExponent)
        {
            float magnitude = raw.magnitude;
            deadZone = Mathf.Clamp(deadZone, 0f, 0.95f);

            if (magnitude <= deadZone || magnitude <= 0f)
            {
                return Vector2.zero;
            }

            float clamped = Mathf.Min(magnitude, 1f);
            float normalized = (clamped - deadZone) / (1f - deadZone);
            float shaped = Mathf.Pow(normalized, Mathf.Max(responseExponent, 0.01f));

            return raw * (shaped / magnitude);
        }

        /// <summary>
        /// Moves the stick's origin just far enough that <paramref name="pointer"/> is no further
        /// than <paramref name="radius"/> away, which lets the stick follow a thumb that drifts.
        /// </summary>
        public static Vector2 FollowOrigin(Vector2 origin, Vector2 pointer, float radius)
        {
            Vector2 offset = pointer - origin;
            float distance = offset.magnitude;

            if (distance <= radius || distance <= 0f)
            {
                return origin;
            }

            return origin + offset * ((distance - radius) / distance);
        }
    }
}
