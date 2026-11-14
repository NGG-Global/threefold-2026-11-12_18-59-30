using Threefold.VisualStyle;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// How dense magical combat effects should be at each graphics preset. Plain C# so the shares can
    /// be tested without Unity. The silhouette of each discipline stays the same; only secondary
    /// particle counts change. Values are proposals until measured on a device.
    /// </summary>
    public static class MagicVfxQuality
    {
        /// <summary>
        /// Fraction of a burst's particles to keep. Performance keeps a little over half so strikes
        /// still read; Balanced and Quality keep the authored count.
        /// </summary>
        public static float BurstShare(int qualityLevel, int levelCount)
        {
            if (levelCount <= 0)
            {
                return 1f;
            }

            int preset = qualityLevel <= 0
                ? GraphicsPresets.Performance
                : qualityLevel >= levelCount - 1 && levelCount > 2
                    ? GraphicsPresets.Quality
                    : GraphicsPresets.Balanced;

            switch (preset)
            {
                case GraphicsPresets.Performance:
                    return 0.55f;
                case GraphicsPresets.Quality:
                    return 1f;
                default:
                    return 1f;
            }
        }

        /// <summary>True when secondary trails and charge sparkles should play.</summary>
        public static bool AllowSecondaries(int qualityLevel, int levelCount)
        {
            return BurstShare(qualityLevel, levelCount) >= 0.999f;
        }
    }
}
