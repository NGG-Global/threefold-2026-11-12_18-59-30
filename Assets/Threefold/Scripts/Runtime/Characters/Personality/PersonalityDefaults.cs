namespace Threefold.Characters
{
    /// <summary>
    /// The starting bearing of the three disciplines. A component that has been set up keeps its own copy, which is
    /// the truth afterwards. First estimates: none has been seen on the character in Unity.
    /// </summary>
    public static class PersonalityDefaults
    {
        /// <summary>Relaxed and observant: upright, even breathing, the hands tracing slow circles, calm looks around.</summary>
        public static PersonalityProfile Arcane()
        {
            return new PersonalityProfile
            {
                forwardLeanDegrees = 0f,
                chestOpenDegrees = 2f,
                headPitchDegrees = -1f,
                breathsPerMinute = 11f,
                breathDepthDegrees = 1.1f,
                handCircleDegrees = 1.6f,
                handCirclesPerMinute = 9f,
                restlessDegrees = 0.15f,
                restlessPerMinute = 20f,
                weightShiftDegrees = 0.6f,
                weightShiftsPerMinute = 5f,
                lookIntervalMin = 3.5f,
                lookIntervalMax = 7f,
                lookYawDegrees = 14f,
                lookPitchDegrees = 4f,
                lookTurnSeconds = 0.5f,
                lookHoldSeconds = 1.4f,
                blinkIntervalMin = 3f,
                blinkIntervalMax = 6f,
                blinkSeconds = 0.16f,
                browMillimetres = 0f,
                blendSeconds = 0.45f,
            };
        }

        /// <summary>Restless and ready: leaning in, quick breath, shoulders and hands never quite still, short sharp checks.</summary>
        public static PersonalityProfile Flame()
        {
            return new PersonalityProfile
            {
                forwardLeanDegrees = 5f,
                chestOpenDegrees = -1f,
                headPitchDegrees = -2.5f, // the eyes stay on the target while the body leans in
                breathsPerMinute = 19f,
                breathDepthDegrees = 1.6f,
                handCircleDegrees = 0.3f,
                handCirclesPerMinute = 14f,
                restlessDegrees = 1.4f,
                restlessPerMinute = 50f,
                weightShiftDegrees = 1.1f,
                weightShiftsPerMinute = 13f,
                lookIntervalMin = 1.6f,
                lookIntervalMax = 3.5f,
                lookYawDegrees = 18f,
                lookPitchDegrees = 3f,
                lookTurnSeconds = 0.2f,
                lookHoldSeconds = 0.5f,
                blinkIntervalMin = 4.5f,
                blinkIntervalMax = 9f,
                blinkSeconds = 0.10f,
                browMillimetres = -1.5f,
                blendSeconds = 0.3f,
            };
        }

        /// <summary>Grounded and sure: chest squared, the slowest and deepest breath, hardly any sway, slow deliberate looks.</summary>
        public static PersonalityProfile Aegis()
        {
            return new PersonalityProfile
            {
                forwardLeanDegrees = 1f,
                chestOpenDegrees = 3.5f,
                headPitchDegrees = 1f,
                breathsPerMinute = 8f,
                breathDepthDegrees = 1.4f,
                handCircleDegrees = 0.2f,
                handCirclesPerMinute = 5f,
                restlessDegrees = 0.05f,
                restlessPerMinute = 10f,
                weightShiftDegrees = 0.25f,
                weightShiftsPerMinute = 3f,
                lookIntervalMin = 6f,
                lookIntervalMax = 11f,
                lookYawDegrees = 9f,
                lookPitchDegrees = 2f,
                lookTurnSeconds = 0.8f,
                lookHoldSeconds = 2.2f,
                blinkIntervalMin = 4f,
                blinkIntervalMax = 8f,
                blinkSeconds = 0.20f,
                browMillimetres = 0f,
                blendSeconds = 0.6f,
            };
        }

        public static PersonalityTuning Create()
        {
            return new PersonalityTuning();
        }
    }
}
