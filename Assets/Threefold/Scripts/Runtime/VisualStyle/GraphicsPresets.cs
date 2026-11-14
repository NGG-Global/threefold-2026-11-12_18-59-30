using System;

namespace Threefold.VisualStyle
{
    /// <summary>
    /// The three graphics presets the player chooses from, and the rules around them. They are the
    /// visual style's three quality levels (<see cref="VisualQualityPreset"/>) under player-facing
    /// names, each with a frame-rate target:
    ///
    ///   Performance  the lowest level, 30 frames per second: for less capable phones.
    ///   Balanced     the middle level, 60 frames per second: the default on a capable phone.
    ///   Quality      the highest level, 60 frames per second.
    ///
    /// Every preset keeps the illustrated look: the painted materials, the illustrated shader, the
    /// lighting and the contour lines around the characters. What changes is resolution, shadows,
    /// anti-aliasing and post-processing. A target of 60 that the phone cannot keep is lowered to a
    /// steady 30 by the <see cref="FramePacingGuard"/>. Plain C#.
    /// </summary>
    public static class GraphicsPresets
    {
        public const int Performance = 0;
        public const int Balanced = 1;
        public const int Quality = 2;

        /// <summary>
        /// The version of the preset values. Version 2 (LM-08) named the levels, gave each a frame-rate
        /// target and kept the contour lines on the lowest level.
        /// </summary>
        public const int DataVersion = 2;

        /// <summary>Where the player's choice is kept between launches (Unity's PlayerPrefs).</summary>
        public const string PreferenceKey = "Threefold.GraphicsPreset";

        public const int HighFrameRate = 60;
        public const int LowFrameRate = 30;

        private static readonly string[] Names = { "Performance", "Balanced", "Quality" };

        /// <summary>The player-facing name of a level, for the usual three; a level of a longer list keeps its own name.</summary>
        public static string Name(int level, int count, string ownName)
        {
            if (count == Names.Length && level >= 0 && level < Names.Length)
            {
                return Names[level];
            }

            return string.IsNullOrEmpty(ownName) ? $"Level {level + 1}" : ownName;
        }

        /// <summary>The frame-rate target a level has unless it names one: 30 for the lowest, 60 for the others.</summary>
        public static int DefaultFrameRate(int level, int count)
        {
            return count > 1 && level <= 0 ? LowFrameRate : HighFrameRate;
        }

        /// <summary>The frame-rate target of a level: its own if set (above 0), otherwise the default.</summary>
        public static int FrameRate(VisualQualityPreset preset, int level, int count)
        {
            return preset != null && preset.targetFrameRate > 0 ? preset.targetFrameRate : DefaultFrameRate(level, count);
        }

        /// <summary>
        /// Which level a scene starts at: one chosen earlier in this run, then the player's saved
        /// choice, then the device's suggestion, then the middle. Values outside the levels are ignored.
        /// </summary>
        public static int ChooseLevel(int chosenThisRun, int saved, int suggested, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            foreach (int candidate in new[] { chosenThisRun, saved, suggested })
            {
                if (candidate >= 0 && candidate < count)
                {
                    return candidate;
                }
            }

            return Math.Min(Balanced, count - 1);
        }

        /// <summary>
        /// Brings a set of quality levels made before version 2 up to it: the player-facing names, each
        /// level's frame-rate target where none is set, and contour lines on the lowest level, so the
        /// illustrated look holds in every preset. Other values (resolution, shadows, tuned by hand or
        /// not) are kept. Returns true if anything changed.
        /// </summary>
        public static bool Upgrade(VisualQualityPreset[] levels)
        {
            if (levels == null || levels.Length == 0)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < levels.Length; i++)
            {
                VisualQualityPreset level = levels[i];
                if (level == null)
                {
                    continue;
                }

                string name = Name(i, levels.Length, level.displayName);
                if (level.displayName != name)
                {
                    level.displayName = name;
                    changed = true;
                }

                if (level.targetFrameRate <= 0)
                {
                    level.targetFrameRate = DefaultFrameRate(i, levels.Length);
                    changed = true;
                }

                if (i == 0 && !level.outlines)
                {
                    level.outlines = true;
                    changed = true;
                }
            }

            return changed;
        }
    }

    /// <summary>
    /// Lowers a 60 frames-per-second target to a steady 30 when the phone cannot keep 60, because an
    /// even 30 feels better than a frame rate that wavers between 40 and 60. It watches the time each
    /// frame takes in windows of a few seconds, after a warm-up that skips the hitches of loading, and
    /// steps down only after two windows in a row run clearly slower than the target. A frame longer
    /// than two target frames counts as two, so an isolated hitch (a shader compiled on first use, a
    /// first spawn) cannot decide a window or use up the warm-up; only sustained slowness does. It never
    /// steps back up by itself, so the frame rate does not swing; choosing a preset again gives 60
    /// another chance. Plain C#.
    /// </summary>
    public sealed class FramePacingGuard
    {
        private readonly float windowSeconds;
        private readonly float warmUpSeconds;
        private readonly float tolerance;
        private readonly int windowsNeeded;

        private float warmUpLeft;
        private float windowTime;
        private int windowFrames;
        private int slowWindows;

        /// <param name="windowSeconds">Length of one window of observation.</param>
        /// <param name="warmUpSeconds">Time ignored after a start, a change of scene or of preset.</param>
        /// <param name="tolerance">How much slower than the target, on average over a window, counts as slow: 1.15 is 15%.</param>
        /// <param name="windowsNeeded">Slow windows in a row before stepping down.</param>
        public FramePacingGuard(float windowSeconds = 5f, float warmUpSeconds = 3f, float tolerance = 1.15f, int windowsNeeded = 2)
        {
            this.windowSeconds = Math.Max(0.5f, windowSeconds);
            this.warmUpSeconds = Math.Max(0f, warmUpSeconds);
            this.tolerance = Math.Max(1f, tolerance);
            this.windowsNeeded = Math.Max(1, windowsNeeded);
            Requested = GraphicsPresets.HighFrameRate;
            Rewarm();
        }

        /// <summary>The target the preset asks for.</summary>
        public int Requested { get; private set; }

        /// <summary>True once the guard has lowered the target, until a preset is chosen again.</summary>
        public bool FellBack { get; private set; }

        /// <summary>The target to use now.</summary>
        public int Current => FellBack ? Math.Min(Requested, GraphicsPresets.LowFrameRate) : Requested;

        /// <summary>The average frame time of the last complete window, in milliseconds; 0 before the first.</summary>
        public float LastWindowMs { get; private set; }

        /// <summary>A preset was chosen: its target is honoured afresh, and any earlier step-down is forgotten.</summary>
        public void Request(int targetFrameRate)
        {
            Requested = targetFrameRate > 0 ? targetFrameRate : GraphicsPresets.HighFrameRate;
            FellBack = false;
            Rewarm();
        }

        /// <summary>Starts the warm-up again without forgetting a step-down: for a change of scene.</summary>
        public void Rewarm()
        {
            warmUpLeft = warmUpSeconds;
            windowTime = 0f;
            windowFrames = 0;
            slowWindows = 0;
        }

        /// <summary>Counts one frame. Returns true on the frame the guard steps the target down.</summary>
        public bool Observe(float deltaSeconds)
        {
            if (FellBack || Requested <= GraphicsPresets.LowFrameRate || !(deltaSeconds > 0f) || float.IsInfinity(deltaSeconds))
            {
                return false;
            }

            // Capped at two target frames: a long hitch counts like a merely slow frame.
            float counted = Math.Min(deltaSeconds, 2f / Requested);
            if (warmUpLeft > 0f)
            {
                warmUpLeft -= counted;
                return false;
            }

            windowTime += counted;
            windowFrames++;
            if (windowTime < windowSeconds)
            {
                return false;
            }

            float averageSeconds = windowTime / windowFrames;
            LastWindowMs = averageSeconds * 1000f;
            windowTime = 0f;
            windowFrames = 0;

            bool slow = averageSeconds > tolerance / Requested;
            slowWindows = slow ? slowWindows + 1 : 0;
            if (slowWindows < windowsNeeded)
            {
                return false;
            }

            FellBack = true;
            return true;
        }
    }
}
