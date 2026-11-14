using System;
using System.Collections.Generic;
using System.Globalization;

namespace Threefold.DevTools
{
    /// <summary>
    /// What the development diagnostics know about the last few seconds, for deciding which warnings
    /// to show. Counts marked "since the last look" are differences between two refreshes.
    /// </summary>
    public struct PerformanceSnapshot
    {
        public float AverageMs;
        public float SlowestMs;

        /// <summary>The frame rate aimed for now; 0 or less if none is set.</summary>
        public int TargetFps;

        /// <summary>True once a 60 target was lowered to 30 for the session.</summary>
        public bool FellBack;

        /// <summary>Main-thread CPU time of the last frame Unity reported, in milliseconds; 0 if not available.</summary>
        public float CpuMs;

        /// <summary>GPU time of the last frame Unity reported, in milliseconds; 0 if not available.</summary>
        public float GpuMs;

        /// <summary>Garbage collections since the last look, and the seconds that covered.</summary>
        public int Collections;
        public float CollectionSeconds;

        public int ReactionsWaiting;
        public long DroppedSinceLastLook;
        public long CriticalOverflowSinceLastLook;

        public int SimulatedObjects;
        public int SimulationLimit;
        public int SimulationWaiting;

        public int Enemies;
        public int Projectiles;
        public int ProjectileLimit;
    }

    /// <summary>
    /// The warnings the development diagnostics show. The thresholds are rules of thumb for testing,
    /// not measured budgets, and each warning says what it saw. Plain C#.
    /// </summary>
    public static class PerformanceWarnings
    {
        /// <summary>How much slower than the target, on average, before frames count as slow: 10%.</summary>
        public const float SlowFrameTolerance = 1.1f;

        /// <summary>A frame among the slowest 5% that takes this many target frames is a visible stutter.</summary>
        public const float StutterFactor = 2f;

        /// <summary>More garbage collections than this per ten seconds suggests something allocates every frame.</summary>
        public const int CollectionsPerTenSeconds = 1;

        /// <summary>Adds a line for each warning that applies. Returns how many were added.</summary>
        public static int Collect(in PerformanceSnapshot s, List<string> into)
        {
            if (into == null)
            {
                return 0;
            }

            int before = into.Count;
            CultureInfo invariant = CultureInfo.InvariantCulture;

            if (s.TargetFps > 0 && s.AverageMs > 0f)
            {
                float budgetMs = 1000f / s.TargetFps;
                if (s.AverageMs > budgetMs * SlowFrameTolerance)
                {
                    into.Add(string.Format(invariant, "Frames take {0:0.0} ms on average; {1} fps needs {2:0.0} ms.",
                        s.AverageMs, s.TargetFps, budgetMs) + Bound(s));
                }

                if (s.SlowestMs > budgetMs * StutterFactor)
                {
                    into.Add(string.Format(invariant, "Stutter: the slowest 5% of frames take {0:0.0} ms ({1:0.0} target frames).",
                        s.SlowestMs, s.SlowestMs / budgetMs));
                }
            }

            if (s.FellBack)
            {
                into.Add("This device could not keep 60 fps; the target was lowered to 30 for this session.");
            }

            if (s.CollectionSeconds > 0f && s.Collections > 0)
            {
                float perTen = s.Collections * 10f / s.CollectionSeconds;
                if (perTen > CollectionsPerTenSeconds)
                {
                    into.Add(string.Format(invariant, "{0} garbage collections in {1:0} s: something may allocate every frame.",
                        s.Collections, s.CollectionSeconds));
                }
            }

            if (s.CriticalOverflowSinceLastLook > 0)
            {
                into.Add($"{s.CriticalOverflowSinceLastLook} critical reaction(s) went beyond the queue's capacity (taken, not lost).");
            }

            if (s.DroppedSinceLastLook > 0)
            {
                into.Add($"{s.DroppedSinceLastLook} non-critical reaction(s) refused because the queue was full.");
            }

            if (s.SimulationLimit > 0 && s.SimulatedObjects >= s.SimulationLimit)
            {
                into.Add($"Simulation at its limit of {s.SimulationLimit} objects; {s.SimulationWaiting} waiting their turn.");
            }

            if (s.ProjectileLimit > 0 && s.Projectiles >= s.ProjectileLimit)
            {
                into.Add($"{s.Projectiles} projectiles in flight, at the expected most of {s.ProjectileLimit}.");
            }

            return into.Count - before;
        }

        /// <summary>Which side seems to hold the frame back, when Unity reports both timings.</summary>
        private static string Bound(in PerformanceSnapshot s)
        {
            if (s.CpuMs <= 0f || s.GpuMs <= 0f)
            {
                return string.Empty;
            }

            return s.GpuMs > s.CpuMs * 1.2f ? " The GPU takes longer: rendering cost (resolution, shadows, overdraw)."
                : s.CpuMs > s.GpuMs * 1.2f ? " The CPU takes longer: scripts, physics, animation or draw submission."
                : string.Empty;
        }

        /// <summary>A byte count in mebibytes, or "not available" for 0 or less.</summary>
        public static string Megabytes(long bytes)
        {
            return bytes > 0
                ? (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
                : "not available";
        }

        /// <summary>The difference between two counter readings; 0 if the counter was reset in between.</summary>
        public static long Since(long now, long before)
        {
            return Math.Max(0L, now - before);
        }
    }
}
