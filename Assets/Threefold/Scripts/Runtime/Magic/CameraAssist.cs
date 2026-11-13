using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>
    /// Mild camera help in melee: while the player is attacking and not turning the camera, the
    /// view drifts towards the target at a limited rate. It never snaps, never takes control from
    /// the player, and ignores targets that are far off to the side or already near the centre.
    /// </summary>
    public static class CameraAssist
    {
        /// <summary>Degrees to add to the camera's yaw this frame. 0 when no help is due.</summary>
        /// <param name="cameraYaw">Current heading of the camera.</param>
        /// <param name="targetBearing">Heading from the camera's pivot to the target.</param>
        /// <param name="degreesPerSecond">Fastest drift.</param>
        /// <param name="deadZoneDegrees">A target this close to the centre is left alone.</param>
        /// <param name="maxAngleDegrees">A target further off than this is not followed.</param>
        public static float YawStep(float cameraYaw, float targetBearing, float degreesPerSecond, float deltaTime,
            float deadZoneDegrees, float maxAngleDegrees)
        {
            if (degreesPerSecond <= 0f || deltaTime <= 0f)
            {
                return 0f;
            }

            float difference = Mathf.DeltaAngle(cameraYaw, targetBearing);
            float size = Mathf.Abs(difference);
            if (size <= deadZoneDegrees || size > maxAngleDegrees)
            {
                return 0f;
            }

            // Never past the edge of the dead zone, so the view settles instead of hunting.
            float step = Mathf.Min(degreesPerSecond * deltaTime, size - deadZoneDegrees);
            return Mathf.Sign(difference) * step;
        }
    }
}
