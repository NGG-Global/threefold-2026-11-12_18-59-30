namespace Threefold.Cameras
{
    /// <summary>How the camera comes round behind the character by itself. Plain data; see <see cref="CameraRecenter"/>.</summary>
    public struct CameraRecenterSettings
    {
        /// <summary>Off: the camera turns only when the player turns it.</summary>
        public bool Enabled;

        /// <summary>How long the player must have left the camera alone, while moving, before it starts to turn.</summary>
        public float DelaySeconds;

        /// <summary>The fastest it turns, in degrees per second. Slow on purpose: it should be felt as help, not seen as a move.</summary>
        public float DegreesPerSecond;

        /// <summary>Below this speed, in metres per second, the character counts as standing and the camera stays.</summary>
        public float MinSpeed;

        /// <summary>Within this many degrees of the character's heading the camera is close enough and stops.</summary>
        public float DeadZoneDegrees;

        /// <summary>
        /// Beyond this many degrees the character is coming towards the camera. Turning then would swing the
        /// view right round and, because movement is steered relative to the camera, bend the path; it stays.
        /// </summary>
        public float MaxAngleDegrees;

        /// <summary>First estimates, never played. Change them from the diagnostics' Feel panel on a device.</summary>
        public static CameraRecenterSettings Default => new CameraRecenterSettings
        {
            Enabled = false,
            DelaySeconds = 1.2f,
            DegreesPerSecond = 45f,
            MinSpeed = 1.5f,
            DeadZoneDegrees = 8f,
            MaxAngleDegrees = 120f,
        };
    }

    /// <summary>
    /// Decides how far the camera turns towards the character's heading in one frame. Plain C#: no Unity
    /// calls, so it is tested outside Unity (CameraFeelTests). ThirdPersonCamera only carries it out.
    ///
    /// The rules, in order: nothing while it is switched off; nothing in a frame with look input, and the
    /// wait starts again; nothing while the character stands; nothing until the player has left the camera
    /// alone for the delay; nothing when the character runs towards the camera; otherwise a turn the short
    /// way round, eased in after the delay and eased out near the heading, that never passes the heading.
    /// </summary>
    public sealed class CameraRecenter
    {
        private const float EaseInSeconds = 0.5f;
        private const float EaseOutDegrees = 20f;

        private float quietSeconds;

        /// <summary>How long the player has left the camera alone while moving. For the diagnostics and the tests.</summary>
        public float QuietSeconds => quietSeconds;

        /// <summary>Forgets the wait, for example when the camera is placed behind the character at once.</summary>
        public void Reset()
        {
            quietSeconds = 0f;
        }

        /// <summary>The turn for this frame, in degrees: positive turns the camera's yaw up, as look input does.</summary>
        /// <param name="cameraYaw">The heading the camera is turning towards, in degrees.</param>
        /// <param name="headingYaw">The way the character faces, in degrees.</param>
        /// <param name="planarSpeed">The character's speed over the ground, in metres per second.</param>
        /// <param name="lookInput">True if the player turned the camera in this frame.</param>
        public float Step(in CameraRecenterSettings settings, float cameraYaw, float headingYaw, float planarSpeed,
            bool lookInput, float deltaSeconds)
        {
            if (!settings.Enabled || lookInput || planarSpeed < settings.MinSpeed || !(deltaSeconds > 0f))
            {
                quietSeconds = 0f;
                return 0f;
            }

            quietSeconds += deltaSeconds;
            if (quietSeconds < settings.DelaySeconds)
            {
                return 0f;
            }

            float difference = DeltaAngle(cameraYaw, headingYaw);
            float size = difference < 0f ? -difference : difference;
            float deadZone = settings.DeadZoneDegrees < 0f ? 0f : settings.DeadZoneDegrees;
            if (size <= deadZone || size > settings.MaxAngleDegrees)
            {
                return 0f;
            }

            float easeIn = Clamp01((quietSeconds - settings.DelaySeconds) / EaseInSeconds);
            float easeOut = Clamp01((size - deadZone) / EaseOutDegrees);

            // A floor on the ease-out, or the last degrees would take for ever.
            float step = settings.DegreesPerSecond * easeIn * (0.15f + 0.85f * easeOut) * deltaSeconds;
            float room = size - deadZone;
            if (step > room)
            {
                step = room;
            }

            if (step < 0f)
            {
                step = 0f;
            }

            return difference < 0f ? -step : step;
        }

        /// <summary>The shortest turn from one heading to another, in degrees, from -180 to 180.</summary>
        public static float DeltaAngle(float from, float to)
        {
            float delta = (to - from) % 360f;
            if (delta > 180f)
            {
                delta -= 360f;
            }
            else if (delta < -180f)
            {
                delta += 360f;
            }

            return delta;
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
