using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Where the scripted emitters put their particles: along the front of a travelling wave, along a ribbon
    /// swept around the body, and on paths that converge into a magical object. Plain arithmetic on vectors, so
    /// it is tested outside Unity; the emitters only read it.
    /// </summary>
    public static class MagicVfxGeometry
    {
        /// <summary>
        /// A horizontal direction turned about the vertical axis by <paramref name="degrees"/> (positive turns
        /// from forward towards right, as a positive yaw does in Unity).
        /// </summary>
        public static Vector3 Turn(Vector3 forward, float degrees)
        {
            forward.y = 0f;
            float length = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
            if (length < 0.0001f)
            {
                forward = new Vector3(0f, 0f, 1f);
            }
            else
            {
                forward /= length;
            }

            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector3(forward.x * cos + forward.z * sin, 0f, -forward.x * sin + forward.z * cos);
        }

        /// <summary>
        /// A point on an arc around <paramref name="origin"/>: <paramref name="across"/> 0 is the left edge of the
        /// arc, 0.5 straight ahead, 1 the right edge.
        /// </summary>
        public static Vector3 ArcPoint(Vector3 origin, Vector3 forward, float arcDegrees, float radius, float across)
        {
            float degrees = (Mathf.Clamp01(across) - 0.5f) * Mathf.Clamp(arcDegrees, 0f, 360f);
            return origin + Turn(forward, degrees) * Mathf.Max(0f, radius);
        }

        /// <summary>
        /// The radius of a wave's front <paramref name="progress"/> of the way through its travel (0 to 1). It
        /// leaves quickly and slows towards its reach, as a pressure front does.
        /// </summary>
        public static float FrontRadius(float startRadius, float reach, float progress)
        {
            float t = Mathf.Clamp01(progress);
            float eased = 1f - (1f - t) * (1f - t);
            return Mathf.Lerp(Mathf.Max(0f, startRadius), Mathf.Max(startRadius, reach), eased);
        }

        /// <summary>
        /// Where across its arc a ribbon swept around the body is, <paramref name="progress"/> of the way through
        /// (0 to 1). <paramref name="reverse"/> sweeps from right to left, for a strike played mirrored.
        /// </summary>
        public static float SweepAcross(float progress, bool reverse)
        {
            float t = Mathf.Clamp01(progress);
            float eased = t * t * (3f - 2f * t);
            return reverse ? 1f - eased : eased;
        }

        /// <summary>
        /// How many particles to emit this frame for a steady rate, carrying the fraction over in
        /// <paramref name="carry"/> so a low frame rate emits the same number per second as a high one.
        /// </summary>
        public static int ParticlesThisFrame(float ratePerSecond, float deltaTime, ref float carry)
        {
            if (ratePerSecond <= 0f || deltaTime <= 0f)
            {
                return 0;
            }

            carry += ratePerSecond * deltaTime;
            int count = (int)carry;
            carry -= count;
            return count;
        }

        /// <summary>
        /// A point spread evenly over a sphere (index <paramref name="index"/> of <paramref name="count"/>), for
        /// particles that gather into an object from all round it.
        /// </summary>
        public static Vector3 ShellPoint(Vector3 centre, float radius, int index, int count)
        {
            if (count <= 1)
            {
                return centre + new Vector3(0f, radius, 0f);
            }

            float y = 1f - 2f * (index + 0.5f) / count;
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = index * 2.39996323f; // the golden angle, in radians
            return centre + new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring) * radius;
        }

        /// <summary>
        /// Where a stream of charge flowing into an object starts: the point the magic struck it, pulled back along
        /// the way it came so that the flow is long enough to be seen. Never further than <paramref name="maxLength"/>
        /// from the object's centre.
        /// </summary>
        public static Vector3 InflowStart(Vector3 struckPoint, Vector3 centre, Vector3 arrivalDirection, float minLength,
            float maxLength)
        {
            Vector3 toCentre = centre - struckPoint;
            float distance = toCentre.magnitude;
            if (distance >= minLength)
            {
                return distance <= maxLength ? struckPoint : centre - toCentre / distance * maxLength;
            }

            Vector3 back = arrivalDirection.sqrMagnitude > 0.0001f ? -arrivalDirection.normalized
                : distance > 0.0001f ? -toCentre / distance : Vector3.up;
            return centre + back * minLength;
        }
    }
}
