using System;

namespace Threefold.DevTools
{
    /// <summary>A summary of recent frame times.</summary>
    public readonly struct FrameTimingSummary
    {
        public readonly int Samples;
        public readonly float AverageMs;
        public readonly float WorstMs;

        /// <summary>The average of the slowest twentieth of the frames: what a stutter feels like.</summary>
        public readonly float SlowestMs;

        public FrameTimingSummary(int samples, float averageMs, float worstMs, float slowestMs)
        {
            Samples = samples;
            AverageMs = averageMs;
            WorstMs = worstMs;
            SlowestMs = slowestMs;
        }

        public float AverageFps => AverageMs > 0f ? 1000f / AverageMs : 0f;
        public float SlowestFps => SlowestMs > 0f ? 1000f / SlowestMs : 0f;
    }

    /// <summary>
    /// Frame times over a fixed window, for the development diagnostics. It records the unscaled
    /// frame time Unity reports; it is not a profiler and measures nothing about where the time goes.
    /// Adding a frame allocates nothing; a summary sorts a copy kept for the purpose. Plain C#.
    /// </summary>
    public sealed class FrameTimingStats
    {
        private readonly float[] window;
        private readonly float[] sorted;
        private int next;
        private int count;

        public FrameTimingStats(int capacity = 120)
        {
            capacity = Math.Max(1, capacity);
            window = new float[capacity];
            sorted = new float[capacity];
        }

        public int Capacity => window.Length;
        public int Count => count;

        /// <summary>Adds one frame's duration, in seconds. Values that cannot be a frame (zero, negative, not a number) are ignored.</summary>
        public void Add(float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds))
            {
                return;
            }

            window[next] = seconds * 1000f;
            next = (next + 1) % window.Length;
            if (count < window.Length)
            {
                count++;
            }
        }

        public void Clear()
        {
            next = 0;
            count = 0;
        }

        public FrameTimingSummary Summarize()
        {
            if (count == 0)
            {
                return new FrameTimingSummary(0, 0f, 0f, 0f);
            }

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sorted[i] = window[i];
                sum += window[i];
            }

            Array.Sort(sorted, 0, count);
            int slowCount = Math.Max(1, count / 20);
            float slowSum = 0f;
            for (int i = count - slowCount; i < count; i++)
            {
                slowSum += sorted[i];
            }

            return new FrameTimingSummary(count, sum / count, sorted[count - 1], slowSum / slowCount);
        }
    }
}
