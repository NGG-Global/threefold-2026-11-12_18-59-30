namespace Threefold.Feel
{
    /// <summary>
    /// The handful of values that decide how the camera and the touch controls feel, gathered so that they
    /// can be changed on a device while testing (the diagnostics' Feel panel) and kept between launches
    /// (<see cref="FeelSettingsStore"/>). Plain data with its limits: no Unity calls.
    ///
    /// Every default leaves the game exactly as it was before these settings existed. They are first
    /// estimates to be judged by hand; nothing here has been played.
    /// </summary>
    public struct FeelSettings
    {
        public const float MinLookSpeed = 0.5f;
        public const float MaxLookSpeed = 2f;
        public const float MaxShake = 1.5f;

        /// <summary>Multiplies how far a finger's drag turns the camera. 1 is the speed the game had.</summary>
        public float LookSpeed;

        /// <summary>Multiplies every camera shake. 1 is the shake the game had; 0 switches shake off.</summary>
        public float Shake;

        /// <summary>The camera comes round behind the character by itself while he moves. Off: as the game was.</summary>
        public bool AutoRecenter;

        /// <summary>Seconds without look input before the camera starts to come round.</summary>
        public float RecenterDelay;

        /// <summary>The fastest the camera comes round, in degrees per second.</summary>
        public float RecenterSpeed;

        public static FeelSettings Default => new FeelSettings
        {
            LookSpeed = 1f,
            Shake = 1f,
            AutoRecenter = false,
            RecenterDelay = 1.2f,
            RecenterSpeed = 45f,
        };

        // The steps the Feel panel's buttons go through. Each list contains its default.
        private static readonly float[] LookSpeedSteps = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        private static readonly float[] ShakeSteps = { 0f, 0.5f, 1f, 1.5f };
        private static readonly float[] DelaySteps = { 0.6f, 1.2f, 2f, 3f };
        private static readonly float[] SpeedSteps = { 25f, 45f, 70f, 100f };

        /// <summary>The same settings with every value inside its limits; nonsense becomes the default.</summary>
        public FeelSettings Clamped()
        {
            FeelSettings defaults = Default;
            FeelSettings result = this;
            result.LookSpeed = Within(LookSpeed, MinLookSpeed, MaxLookSpeed, defaults.LookSpeed);
            result.Shake = Within(Shake, 0f, MaxShake, defaults.Shake);
            result.RecenterDelay = Within(RecenterDelay, 0.2f, 5f, defaults.RecenterDelay);
            result.RecenterSpeed = Within(RecenterSpeed, 5f, 180f, defaults.RecenterSpeed);
            return result;
        }

        public FeelSettings WithNextLookSpeed() => With(this, look: Next(LookSpeedSteps, LookSpeed));

        public FeelSettings WithNextShake() => With(this, shake: Next(ShakeSteps, Shake));

        public FeelSettings WithNextRecenterDelay() => With(this, delay: Next(DelaySteps, RecenterDelay));

        public FeelSettings WithNextRecenterSpeed() => With(this, speed: Next(SpeedSteps, RecenterSpeed));

        public FeelSettings WithAutoRecenter(bool enabled)
        {
            FeelSettings result = this;
            result.AutoRecenter = enabled;
            return result;
        }

        private static FeelSettings With(FeelSettings source, float? look = null, float? shake = null, float? delay = null,
            float? speed = null)
        {
            FeelSettings result = source;
            result.LookSpeed = look ?? source.LookSpeed;
            result.Shake = shake ?? source.Shake;
            result.RecenterDelay = delay ?? source.RecenterDelay;
            result.RecenterSpeed = speed ?? source.RecenterSpeed;
            return result.Clamped();
        }

        /// <summary>The step after the one nearest to the value, going round to the first after the last.</summary>
        private static float Next(float[] steps, float value)
        {
            int nearest = 0;
            for (int i = 1; i < steps.Length; i++)
            {
                if (Distance(steps[i], value) < Distance(steps[nearest], value))
                {
                    nearest = i;
                }
            }

            return steps[(nearest + 1) % steps.Length];
        }

        private static float Distance(float a, float b)
        {
            return a > b ? a - b : b - a;
        }

        private static float Within(float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }
    }
}
