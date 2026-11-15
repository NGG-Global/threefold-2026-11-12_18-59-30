using System;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace Threefold.Tests
{
    /// <summary>
    /// Measures what a stretch of code allocates on the managed heap, for the tests that require the per-frame
    /// rules to allocate nothing.
    ///
    /// Two counters exist, and each runtime has only one that works:
    ///  - .NET (the runner of Tools/ci/compile_check.sh) counts the bytes a thread allocated.
    ///    Unity's Mono has the same method, but there it never moves.
    ///  - Unity records every managed allocation under the profiler marker "GC.Alloc". That is the counter
    ///    Unity's own test constraint (Is.Not.AllocatingGCMemory) reads, in the same way as here.
    ///
    /// <see cref="End"/> therefore returns bytes on .NET and a number of allocations in Unity. Every test
    /// compares it with zero, so the unit does not matter. Where neither counter works the test is
    /// inconclusive, never passed.
    /// </summary>
    internal static class AllocationMeter
    {
        private enum Counter
        {
            Unknown,
            Bytes,
            UnityRecorder,
            None,
        }

        private const string UnityMarker = "GC.Alloc";

        private static Counter counter = Counter.Unknown;
        private static object probe;
        private static long bytesAtBegin;
        private static Recorder recorder;

        /// <summary>Makes the test inconclusive on a runtime where no counter counts.</summary>
        public static void Require()
        {
            if (counter == Counter.Unknown)
            {
                counter = Find();
            }

            if (counter == Counter.None)
            {
                Assert.Inconclusive("Allocations cannot be counted here: GC.GetAllocatedBytesForCurrentThread does not move and " +
                                    $"Unity's '{UnityMarker}' recorder is not available or records nothing.");
            }
        }

        /// <summary>Starts counting. Call again to start afresh.</summary>
        public static void Begin()
        {
            Require();
            if (counter == Counter.Bytes)
            {
                bytesAtBegin = GC.GetAllocatedBytesForCurrentThread();
            }
            else
            {
                BeginRecording();
            }
        }

        /// <summary>Stops counting and returns what was allocated since <see cref="Begin"/>: bytes, or allocations in Unity.</summary>
        public static long End()
        {
            if (counter == Counter.Bytes)
            {
                return GC.GetAllocatedBytesForCurrentThread() - bytesAtBegin;
            }

            return EndRecording();
        }

        private static Counter Find()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            probe = new object[8];
            if (GC.GetAllocatedBytesForCurrentThread() != before)
            {
                return Counter.Bytes;
            }

            try
            {
                // The recorder counts only if it sees an allocation that certainly happened.
                BeginRecording();
                probe = new object[8];
                return EndRecording() > 0 ? Counter.UnityRecorder : Counter.None;
            }
            catch (Exception)
            {
                // Outside Unity the recorder does not exist.
                return Counter.None;
            }
        }

        private static void BeginRecording()
        {
            if (recorder == null)
            {
                recorder = Recorder.Get(UnityMarker);
            }

            // Switching it off first discards what it recorded before, its own creation included.
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
        }

        private static long EndRecording()
        {
            recorder.enabled = false;
            recorder.CollectFromAllThreads();
            return recorder.isValid ? recorder.sampleBlockCount : 0;
        }
    }
}
