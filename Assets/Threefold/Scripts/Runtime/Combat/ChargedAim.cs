using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Turns the aim from the attack gesture (a drag on the ATTACK button, length 0 to 1, X right
    /// and Y up on the screen) into a direction in the world. Plain arithmetic, tested outside Unity.
    /// </summary>
    public static class ChargedAim
    {
        /// <summary>
        /// Ground direction for an area attack. The drag is read relative to the camera: up is away
        /// from the camera, left is left. With no drag the attack goes straight ahead of the camera,
        /// which is also what the mouse and gamepad use.
        /// </summary>
        /// <param name="cameraYawDegrees">The camera's heading; 0 looks along +Z, 90 along +X.</param>
        public static Vector3 GroundDirection(Vector2 aim, float cameraYawDegrees)
        {
            float yaw = cameraYawDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

            if (aim.sqrMagnitude < 0.0001f)
            {
                return forward;
            }

            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            Vector3 direction = right * aim.x + forward * aim.y;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : forward;
        }

        /// <summary>
        /// Offsets for a precise shot aimed with the camera: X is the yaw in degrees (positive turns
        /// right), Y the pitch (positive aims higher). A full drag gives the maximum.
        /// </summary>
        public static Vector2 CameraOffsets(Vector2 aim, float maxYawDegrees, float maxPitchDegrees)
        {
            aim = Vector2.ClampMagnitude(aim, 1f);
            return new Vector2(aim.x * maxYawDegrees, aim.y * maxPitchDegrees);
        }

        /// <summary>Applies yaw and pitch offsets, in degrees, to an aim direction.</summary>
        public static Vector3 ApplyOffsets(Vector3 direction, float yawDegrees, float pitchDegrees)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return direction;
            }

            direction.Normalize();
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + yawDegrees;
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg + pitchDegrees;
            pitch = Mathf.Clamp(pitch, -85f, 85f);

            float yawRadians = yaw * Mathf.Deg2Rad;
            float pitchRadians = pitch * Mathf.Deg2Rad;
            float flat = Mathf.Cos(pitchRadians);
            return new Vector3(Mathf.Sin(yawRadians) * flat, Mathf.Sin(pitchRadians), Mathf.Cos(yawRadians) * flat);
        }

        /// <summary>The heading of a direction along the ground, in degrees; 0 is +Z, 90 is +X.</summary>
        public static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }
}
