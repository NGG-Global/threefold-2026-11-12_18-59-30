using Threefold.VisualStyle;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>How magical combat effects are drawn at one graphics preset and intensity.</summary>
    public readonly struct MagicVfxLevel
    {
        /// <summary>Share of each burst's particles kept, 0 to 1.</summary>
        public readonly float BurstShare;

        /// <summary>Trail lengths (their lifetime) are multiplied by this.</summary>
        public readonly float TrailTimeScale;

        /// <summary>Secondary effects play: lingering embers, gathering force, the shell of an object's inflow.</summary>
        public readonly bool Secondaries;

        /// <summary>Rate of the scripted emitters (waves, ribbons, inflows) is multiplied by this.</summary>
        public readonly float EmitterRateScale;

        public MagicVfxLevel(float burstShare, float trailTimeScale, bool secondaries, float emitterRateScale)
        {
            BurstShare = burstShare;
            TrailTimeScale = trailTimeScale;
            Secondaries = secondaries;
            EmitterRateScale = emitterRateScale;
        }
    }

    /// <summary>
    /// How dense magical combat effects should be at each graphics preset. Plain C# so the shares can be tested
    /// without Unity. Two levels exist: Performance draws fewer particles, shorter trails and no secondary
    /// effects; Balanced and Quality draw the effects as authored. The silhouette of each discipline (its trail,
    /// contact, charge, release and wave) is the same at every level. The player's intensity setting reduces
    /// everything further. Values are proposals until measured on a device.
    /// </summary>
    public static class MagicVfxQuality
    {
        /// <summary>The lowest intensity the setting allows: effects that carry gameplay information stay visible.</summary>
        public const float MinIntensity = 0.25f;

        /// <summary>
        /// Fraction of a burst's particles to keep. Performance keeps a little over half so strikes
        /// still read; Balanced and Quality keep the authored count.
        /// </summary>
        public static float BurstShare(int qualityLevel, int levelCount)
        {
            return IsPerformance(qualityLevel, levelCount) ? 0.55f : 1f;
        }

        /// <summary>True when secondary trails and charge sparkles should play.</summary>
        public static bool AllowSecondaries(int qualityLevel, int levelCount)
        {
            return BurstShare(qualityLevel, levelCount) >= 0.999f;
        }

        /// <summary>The level for a graphics preset and an intensity (0.25 to 1; 1 is as authored).</summary>
        public static MagicVfxLevel For(int qualityLevel, int levelCount, float intensity)
        {
            float scale = ClampIntensity(intensity);
            bool performance = IsPerformance(qualityLevel, levelCount);
            float share = (performance ? 0.55f : 1f) * scale;
            float trail = (performance ? 0.6f : 1f) * Mathf.Lerp(0.6f, 1f, (scale - MinIntensity) / (1f - MinIntensity));
            float rate = (performance ? 0.5f : 1f) * scale;
            return new MagicVfxLevel(share, trail, !performance && scale > 0.5f, rate);
        }

        public static float ClampIntensity(float intensity)
        {
            return Mathf.Clamp(float.IsNaN(intensity) ? 1f : intensity, MinIntensity, 1f);
        }

        private static bool IsPerformance(int qualityLevel, int levelCount)
        {
            // Without levels the effects are drawn as authored.
            return levelCount > 0 && qualityLevel <= GraphicsPresets.Performance;
        }
    }
}
