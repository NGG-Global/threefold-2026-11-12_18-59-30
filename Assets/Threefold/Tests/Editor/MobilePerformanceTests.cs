using System;
using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.DevTools;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// LM-08: the graphics presets and their frame-rate targets, the guard that lowers an unkept 60 to a
    /// steady 30, the warnings of the development diagnostics, and the allocations of the magical
    /// simulation's per-frame work. Allocations are counted with the runtime's own counter for the
    /// current thread; they say nothing about time, which only a device can measure. The counter is
    /// read into a local before asserting: Assert.AreEqual(0L, ...) boxes its first argument, and that
    /// box would otherwise be counted. Unity's Mono runtime is reported to return 0 from that counter
    /// whatever is allocated, so each allocation test first checks that the counter moves and is
    /// inconclusive where it does not: the guarantee comes from the .NET runner of compile_check.sh.
    /// </summary>
    public sealed class MobilePerformanceTests
    {
        private const float Frame60 = 1f / 60f;
        private int nextEvent = 90000;
        private static object probe;

        /// <summary>Makes the test inconclusive on a runtime whose allocation counter does not count.</summary>
        private static void RequireAllocationCounter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            probe = new object[8];
            long after = GC.GetAllocatedBytesForCurrentThread();
            if (after == before)
            {
                Assert.Inconclusive("This runtime does not count allocations (GC.GetAllocatedBytesForCurrentThread did not move). " +
                                    "The allocation result comes from the .NET runner of Tools/ci/compile_check.sh.");
            }
        }

        // ------------------------------------------------------------ presets

        [Test]
        public void ThePresetsHavePlayerFacingNames()
        {
            Assert.AreEqual("Performance", GraphicsPresets.Name(0, 3, "Low"));
            Assert.AreEqual("Balanced", GraphicsPresets.Name(1, 3, "Medium"));
            Assert.AreEqual("Quality", GraphicsPresets.Name(2, 3, "High"));
            Assert.AreEqual("Ultra", GraphicsPresets.Name(3, 4, "Ultra"), "A level of a longer list keeps its own name.");
            Assert.AreEqual("Level 2", GraphicsPresets.Name(1, 2, null));
        }

        [Test]
        public void TheLowestPresetAimsFor30AndTheOthersFor60UnlessTheyNameATarget()
        {
            Assert.AreEqual(30, GraphicsPresets.DefaultFrameRate(0, 3));
            Assert.AreEqual(60, GraphicsPresets.DefaultFrameRate(1, 3));
            Assert.AreEqual(60, GraphicsPresets.DefaultFrameRate(2, 3));
            Assert.AreEqual(60, GraphicsPresets.DefaultFrameRate(0, 1), "A single level is not the lowest of several.");

            Assert.AreEqual(30, GraphicsPresets.FrameRate(new VisualQualityPreset(), 0, 3), "0 means the level's default.");
            Assert.AreEqual(45, GraphicsPresets.FrameRate(new VisualQualityPreset { targetFrameRate = 45 }, 0, 3));
            Assert.AreEqual(60, GraphicsPresets.FrameRate(null, 1, 3));

            Assert.AreEqual(30, GraphicsPresets.FrameRate(VisualQualityPreset.Low(), 0, 3));
            Assert.AreEqual(60, GraphicsPresets.FrameRate(VisualQualityPreset.Medium(), 1, 3));
            Assert.AreEqual(60, GraphicsPresets.FrameRate(VisualQualityPreset.High(), 2, 3));
        }

        [Test]
        public void TheStartingPresetIsThisRunsChoiceThenTheSavedOneThenTheDevicesSuggestion()
        {
            Assert.AreEqual(2, GraphicsPresets.ChooseLevel(2, 0, 1, 3), "A choice made in this run comes first.");
            Assert.AreEqual(0, GraphicsPresets.ChooseLevel(-1, 0, 2, 3), "Then the player's saved choice.");
            Assert.AreEqual(2, GraphicsPresets.ChooseLevel(-1, -1, 2, 3), "Then what the device suggests.");
            Assert.AreEqual(1, GraphicsPresets.ChooseLevel(-1, 7, 9, 3), "Values outside the levels are ignored; Balanced remains.");
            Assert.AreEqual(0, GraphicsPresets.ChooseLevel(-1, -1, -1, 1));
            Assert.AreEqual(0, GraphicsPresets.ChooseLevel(1, 1, 1, 0));
        }

        [Test]
        public void UpgradingOlderLevelsNamesThemSetsTheirTargetsAndKeepsTheLinesOnPerformance()
        {
            VisualQualityPreset low = VisualQualityPreset.Low();
            low.displayName = "Low";
            low.targetFrameRate = 0;
            low.outlines = false;
            low.renderScale = 0.65f; // tuned by hand
            VisualQualityPreset medium = VisualQualityPreset.Medium();
            medium.displayName = "Medium";
            medium.targetFrameRate = 0;
            VisualQualityPreset high = VisualQualityPreset.High();
            high.displayName = "High";
            high.targetFrameRate = 45; // chosen by hand
            var levels = new[] { low, medium, high };

            Assert.IsTrue(GraphicsPresets.Upgrade(levels));
            Assert.AreEqual("Performance", low.displayName);
            Assert.AreEqual("Balanced", medium.displayName);
            Assert.AreEqual("Quality", high.displayName);
            Assert.AreEqual(30, low.targetFrameRate);
            Assert.AreEqual(60, medium.targetFrameRate);
            Assert.AreEqual(45, high.targetFrameRate, "A target that was set is kept.");
            Assert.IsTrue(low.outlines, "The contour lines are part of the illustrated look in every preset.");
            Assert.AreEqual(0.65f, low.renderScale, 0.0001f, "Other values are kept.");

            Assert.IsFalse(GraphicsPresets.Upgrade(levels), "A second upgrade changes nothing.");
            Assert.IsFalse(GraphicsPresets.Upgrade(null));
            Assert.IsFalse(GraphicsPresets.Upgrade(new VisualQualityPreset[] { null }));
        }

        [Test]
        public void EveryPresetKeepsTheIllustratedLook()
        {
            foreach (VisualQualityPreset preset in new[] { VisualQualityPreset.Low(), VisualQualityPreset.Medium(), VisualQualityPreset.High() })
            {
                Assert.IsTrue(preset.outlines, $"{preset.displayName} keeps the contour lines.");
                Assert.AreNotEqual(StyleShadows.Off, preset.shadows, $"{preset.displayName} keeps the sun's shadows.");
            }
        }

        // ------------------------------------------------------- frame pacing

        private static int Feed(FramePacingGuard guard, float seconds, float frameSeconds)
        {
            int stepDowns = 0;
            for (float t = 0f; t < seconds; t += frameSeconds)
            {
                if (guard.Observe(frameSeconds))
                {
                    stepDowns++;
                }
            }

            return stepDowns;
        }

        [Test]
        public void A60TargetThatIsKeptIsNeverLowered()
        {
            var guard = new FramePacingGuard();
            guard.Request(60);
            Assert.AreEqual(0, Feed(guard, 60f, Frame60 * 1.05f), "Within the tolerance.");
            Assert.AreEqual(60, guard.Current);
            Assert.IsFalse(guard.FellBack);
            Assert.Greater(guard.LastWindowMs, 0f);
        }

        [Test]
        public void A60TargetThatCannotBeKeptStepsDownToASteady30AfterTwoSlowWindows()
        {
            var guard = new FramePacingGuard(windowSeconds: 5f, warmUpSeconds: 3f, tolerance: 1.15f, windowsNeeded: 2);
            guard.Request(60);

            // Warm-up (3 s) and one slow window (5 s): not yet.
            Assert.AreEqual(0, Feed(guard, 8.2f, 1f / 45f));
            Assert.IsFalse(guard.FellBack, "One slow window is not enough.");

            Assert.AreEqual(1, Feed(guard, 5.2f, 1f / 45f), "The second slow window steps down, once.");
            Assert.IsTrue(guard.FellBack);
            Assert.AreEqual(30, guard.Current);
            Assert.AreEqual(60, guard.Requested, "What the preset asked for is remembered.");
            Assert.AreEqual(1000f / 45f, guard.LastWindowMs, 0.5f);

            Assert.AreEqual(0, Feed(guard, 30f, 1f / 20f), "It never steps again, or back up, by itself.");
            Assert.AreEqual(30, guard.Current);
        }

        [Test]
        public void HitchesWhileLoadingAreIgnored()
        {
            var guard = new FramePacingGuard();
            guard.Request(60);
            Assert.AreEqual(0, Feed(guard, 2.5f, 0.5f), "A few long frames inside the warm-up.");
            Assert.AreEqual(0, Feed(guard, 30f, Frame60));
            Assert.IsFalse(guard.FellBack);
        }

        [Test]
        public void IsolatedHitchesNeverDecideAWindow()
        {
            var guard = new FramePacingGuard(windowSeconds: 5f, warmUpSeconds: 3f);
            guard.Request(60);
            Feed(guard, 3.1f, Frame60);

            for (int window = 0; window < 4; window++)
            {
                // A shader compiled on first use and a few first spawns, in every window.
                Assert.IsFalse(guard.Observe(3f));
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsFalse(guard.Observe(0.3f));
                }

                Assert.AreEqual(0, Feed(guard, 5f, Frame60));
            }

            Assert.IsFalse(guard.FellBack, "Only sustained slowness lowers the target.");
        }

        [Test]
        public void ALongLoadingFrameDoesNotUseUpTheWarmUp()
        {
            var guard = new FramePacingGuard(windowSeconds: 1f, warmUpSeconds: 3f, windowsNeeded: 1);
            guard.Request(60);
            guard.Rewarm();
            Assert.IsFalse(guard.Observe(5f), "The frame that loaded the scene.");
            Assert.AreEqual(0, Feed(guard, 2f, 1f / 30f), "Still warming up: the long frame counted as two target frames.");
            Assert.AreEqual(1, Feed(guard, 3f, 1f / 30f), "Sustained slowness after the warm-up still steps down.");
        }

        [Test]
        public void ASlowWindowFollowedByAGoodOneDoesNotStepDown()
        {
            var guard = new FramePacingGuard(windowSeconds: 5f, warmUpSeconds: 0f);
            guard.Request(60);
            Assert.AreEqual(0, Feed(guard, 5.1f, 1f / 40f));
            Assert.AreEqual(0, Feed(guard, 5.1f, Frame60));
            Assert.AreEqual(0, Feed(guard, 5.1f, 1f / 40f));
            Assert.IsFalse(guard.FellBack, "The slow windows must follow each other.");
        }

        [Test]
        public void ChoosingAPresetAgainGivesItsTargetAnotherChanceButAChangeOfSceneDoesNot()
        {
            var guard = new FramePacingGuard(windowSeconds: 1f, warmUpSeconds: 0f);
            guard.Request(60);
            Feed(guard, 3f, 1f / 30f);
            Assert.IsTrue(guard.FellBack);

            guard.Rewarm();
            Assert.IsTrue(guard.FellBack, "A change of scene keeps the step-down.");
            Assert.AreEqual(30, guard.Current);

            guard.Request(60);
            Assert.IsFalse(guard.FellBack);
            Assert.AreEqual(60, guard.Current);
        }

        [Test]
        public void A30TargetIsNotWatchedAndNonsenseFrameTimesAreIgnored()
        {
            var guard = new FramePacingGuard(windowSeconds: 1f, warmUpSeconds: 0f);
            guard.Request(30);
            Assert.AreEqual(0, Feed(guard, 10f, 0.2f));
            Assert.AreEqual(30, guard.Current);

            guard.Request(60);
            foreach (float nonsense in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                for (int i = 0; i < 100; i++)
                {
                    Assert.IsFalse(guard.Observe(nonsense));
                }
            }

            Assert.IsFalse(guard.FellBack);
            Assert.AreEqual(0f, guard.LastWindowMs);

            guard.Request(0);
            Assert.AreEqual(60, guard.Requested, "No target means the high one.");
        }

        [Test]
        public void TheGuardAllocatesNothingPerFrame()
        {
            RequireAllocationCounter();
            var guard = new FramePacingGuard(windowSeconds: 0.5f, warmUpSeconds: 0f, windowsNeeded: 1000);
            guard.Request(60);
            guard.Observe(Frame60);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++)
            {
                guard.Observe(i % 7 == 0 ? 1f / 30f : Frame60);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }

        // ---------------------------------------------------------- warnings

        private static PerformanceSnapshot Healthy()
        {
            return new PerformanceSnapshot
            {
                AverageMs = 16.4f, SlowestMs = 20f, TargetFps = 60, CpuMs = 9f, GpuMs = 11f, Collections = 0, CollectionSeconds = 10f,
                ReactionsWaiting = 3, SimulatedObjects = 5, SimulationLimit = 32, Enemies = 2, Projectiles = 3, ProjectileLimit = 24,
            };
        }

        [Test]
        public void AHealthyFrameGivesNoWarnings()
        {
            var warnings = new List<string>();
            Assert.AreEqual(0, PerformanceWarnings.Collect(Healthy(), warnings));
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(0, PerformanceWarnings.Collect(Healthy(), null));
        }

        [Test]
        public void EachProblemIsNamedWithWhatWasSeen()
        {
            PerformanceSnapshot slow = Healthy();
            slow.AverageMs = 24f;
            slow.GpuMs = 21f;
            slow.CpuMs = 10f;
            var warnings = new List<string>();
            Assert.AreEqual(1, PerformanceWarnings.Collect(slow, warnings));
            StringAssert.Contains("24.0 ms", warnings[0]);
            StringAssert.Contains("16.7 ms", warnings[0]);
            StringAssert.Contains("GPU", warnings[0], "When Unity reports both, the slower side is named.");

            PerformanceSnapshot many = Healthy();
            many.SlowestMs = 40f;
            many.FellBack = true;
            many.Collections = 3;
            many.CriticalOverflowSinceLastLook = 2;
            many.DroppedSinceLastLook = 5;
            many.SimulatedObjects = 32;
            many.SimulationWaiting = 4;
            many.Projectiles = 24;
            warnings.Clear();
            Assert.AreEqual(7, PerformanceWarnings.Collect(many, warnings), string.Join("\n", warnings));
            StringAssert.Contains("Stutter", warnings[0]);
            StringAssert.Contains("lowered to 30", warnings[1]);
            StringAssert.Contains("3 garbage collections", warnings[2]);
            StringAssert.Contains("not lost", warnings[3], "A critical overflow is taken, never dropped.");
            StringAssert.Contains("5 non-critical", warnings[4]);
            StringAssert.Contains("limit of 32", warnings[5]);
            StringAssert.Contains("24 projectiles", warnings[6]);
        }

        [Test]
        public void WithoutATargetOrTimingsNothingIsGuessed()
        {
            PerformanceSnapshot unknown = Healthy();
            unknown.TargetFps = 0;
            unknown.AverageMs = 50f;
            unknown.SlowestMs = 90f;
            var warnings = new List<string>();
            Assert.AreEqual(0, PerformanceWarnings.Collect(unknown, warnings), "No target: the frame time is shown, not judged.");

            Assert.AreEqual("not available", PerformanceWarnings.Megabytes(0));
            Assert.AreEqual("1.5 MB", PerformanceWarnings.Megabytes(1572864));
            Assert.AreEqual(3L, PerformanceWarnings.Since(10, 7));
            Assert.AreEqual(0L, PerformanceWarnings.Since(2, 7), "A counter that was reset is not a negative count.");
        }

        // ------------------------------------------- allocations of the magic

        private sealed class Sink : IMagicEffectReceiver
        {
            public int Received;

            public EffectResponse ReceiveEffect(in MagicEffect effect)
            {
                Received++;
                return EffectResponse.Applied;
            }
        }

        private sealed class Restless : ISimulated
        {
            public float Time;
            public bool Critical;

            public void Simulate(float deltaTime) => Time += deltaTime;
            public bool IsSimulationAtRest => false;
            public bool IsSimulationCritical => Critical;
        }

        private MagicEffect Effect(MagicEffectKind kind, float magnitude, int depth = 0)
        {
            return new MagicEffect(kind, magnitude, Vector3.forward, 1.5f, Vector3.zero, null, 0, EffectTargets.All, nextEvent++, depth);
        }

        [Test]
        public void TheReactionQueueAllocatesNothingOnceWarm()
        {
            RequireAllocationCounter();
            var scheduler = new ReactionScheduler(new ReactionBudgets());
            var targets = new Sink[12];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = new Sink();
            }

            MagicEffectKind[] kinds = { MagicEffectKind.Heat, MagicEffectKind.ArcaneCharge, MagicEffectKind.Force, MagicEffectKind.Stabilize };
            ReactionPriority[] priorities = { ReactionPriority.Critical, ReactionPriority.Normal, ReactionPriority.Low };

            void Frame(int frame)
            {
                // Twelve offered and eight carried out per frame: the queue fills and drains, and refuses when full.
                for (int i = 0; i < targets.Length; i++)
                {
                    scheduler.Enqueue(targets[i], Effect(kinds[(frame + i) % kinds.Length], 1f, 1 + i % 3), priorities[i % priorities.Length]);
                }

                scheduler.Process();
                if (frame % 50 == 49)
                {
                    scheduler.Process(1000);
                }
            }

            for (int frame = 0; frame < 200; frame++)
            {
                Frame(frame);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 2000; frame++)
            {
                Frame(frame);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated, "Offering, refusing and carrying out reactions must not allocate.");
            Assert.Greater(targets[0].Received, 0);
            Assert.Greater(scheduler.Statistics.Delivered, 10000L);
        }

        [Test]
        public void TheExampleObjectsAllocateNothingWhileTheyReactAndTime()
        {
            RequireAllocationCounter();
            var sets = new List<MagicalPropertySet>();
            foreach (ExampleObject example in Enum.GetValues(typeof(ExampleObject)))
            {
                sets.Add(new MagicalPropertySet(WorldMagicDefaults.Settings(example)) { KineticAxis = Vector3.forward });
            }

            MagicEffectKind[] kinds = { MagicEffectKind.Heat, MagicEffectKind.ArcaneCharge, MagicEffectKind.Force, MagicEffectKind.Stabilize };

            void Frame(int frame)
            {
                for (int i = 0; i < sets.Count; i++)
                {
                    MagicalPropertySet set = sets[i];
                    if (frame % 5 == 0)
                    {
                        set.Apply(Effect(kinds[(frame / 5 + i) % kinds.Length], 30f));
                    }

                    set.Tick(Frame60);
                    set.ClearPendingEmissions();
                    if (frame % 600 == 599)
                    {
                        set.Restore();
                    }
                }
            }

            for (int frame = 0; frame < 1200; frame++)
            {
                Frame(frame);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 3000; frame++)
            {
                Frame(frame);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated,
                "Applying effects to the six example objects and advancing them in time must not allocate.");
        }

        [Test]
        public void AdvancingTheBudgetOfObjectsAllocatesNothing()
        {
            RequireAllocationCounter();
            var budgets = new ReactionBudgets();
            var simulation = new SimulationSet(budgets);
            var objects = new Restless[budgets.maxActiveObjects + 8];
            for (int i = 0; i < objects.Length; i++)
            {
                objects[i] = new Restless { Critical = i % 10 == 0 };
                simulation.Wake(objects[i]);
            }

            simulation.Tick(Frame60);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 3000; frame++)
            {
                simulation.Tick(Frame60);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated, "The simulation step must not allocate.");
            // As documented: the budget's worth are advanced, the rest wait with their state standing still.
            // Objects that never come to rest keep the waiting ones out (see Docs/MobilePerformance.md).
            int advanced = 0;
            foreach (Restless restless in objects)
            {
                advanced += restless.Time > 0f ? 1 : 0;
            }

            Assert.AreEqual(budgets.maxActiveObjects, advanced);
            Assert.AreEqual(objects.Length - budgets.maxActiveObjects, simulation.WaitingCount);
        }

        [Test]
        public void ACreatureWithArmourAndWardAllocatesNothingWhileItTakesEffects()
        {
            RequireAllocationCounter();
            var creature = new CreatureEffectModel(new HeatSettings(), 0.5f, true, 3f,
                new ArmourSettings { enabled = true }, new WardSettings { enabled = true });
            MagicEffectKind[] kinds = { MagicEffectKind.Heat, MagicEffectKind.ArcaneCharge, MagicEffectKind.Force };
            float pushed = 0f;

            void Frame(int frame)
            {
                if (frame % 4 == 0)
                {
                    creature.Apply(Effect(kinds[frame / 4 % kinds.Length], 25f), out float push);
                    pushed += push;
                }

                creature.Tick(Frame60);
                pushed += creature.DamageMultiplier(true) * 0f;
            }

            for (int frame = 0; frame < 400; frame++)
            {
                Frame(frame);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 3000; frame++)
            {
                Frame(frame);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated, "A creature's effect model must not allocate.");
            Assert.GreaterOrEqual(pushed, 0f);
        }
    }
}
