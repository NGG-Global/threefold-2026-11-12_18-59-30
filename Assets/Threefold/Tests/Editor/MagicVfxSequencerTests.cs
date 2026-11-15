using System;
using System.Collections.Generic;
using NUnit.Framework;
using Threefold.CombatAnimation;
using Threefold.CombatFeedback;
using Threefold.Magic;

namespace Threefold.Tests
{
    /// <summary>
    /// The rules that time the disciplines' body effects to the moments of each attack
    /// (<see cref="BodyEffectSequencer"/>): what starts, grows, stops and bursts at each cue, and the guarantees
    /// that hold however an action ends. Plain C#, run outside Unity by Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class MagicVfxSequencerTests
    {
        private readonly List<BodyEffectCommand> output = new List<BodyEffectCommand>();
        private static object probe;

        private static BodyActionInfo Melee(MagicDiscipline discipline, int id, float secondsToContact = 0.12f, bool heavy = false,
            CombatEffector effector = CombatEffector.RightHand)
        {
            return new BodyActionInfo
            {
                Action = CombatActionKind.Melee,
                ActionId = id,
                Discipline = (int)discipline,
                Effector = effector,
                SecondsToContact = secondsToContact,
                Heavy = heavy,
                Timing = MagicVfxDefaults.For(discipline),
            };
        }

        private static BodyActionInfo Charged(MagicDiscipline discipline, int id)
        {
            return new BodyActionInfo
            {
                Action = CombatActionKind.Charged,
                ActionId = id,
                Discipline = (int)discipline,
                Effector = CombatEffector.LeftHand,
                Timing = MagicVfxDefaults.For(discipline),
            };
        }

        private int Count(BodyEffectCommandKind kind, BodyEffectLayer layer)
        {
            int count = 0;
            foreach (BodyEffectCommand command in output)
            {
                if (command.Kind == kind && command.Layer == layer)
                {
                    count++;
                }
            }

            return count;
        }

        private int Bursts(BodyEffectBurst burst)
        {
            int count = 0;
            foreach (BodyEffectCommand command in output)
            {
                if (command.Kind == BodyEffectCommandKind.Burst && command.Burst == burst)
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ melee

        [Test]
        public void ArcaneStrikeDrawsItsTrailAndRibbonFromTheEffectMomentThroughTheFollowThrough()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Arcane, 1), 0f, output);
            Assert.AreEqual(0, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Trail), "nothing before the effect moment");

            sequencer.Cue(CombatCueKind.Effect, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Trail));
            Assert.AreEqual(1, Bursts(BodyEffectBurst.Swing));

            sequencer.Cue(CombatCueKind.HitWindowOpened, 1, output);
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            Assert.AreEqual(1, Bursts(BodyEffectBurst.Contact));
            Assert.AreEqual(0, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Trail), "the trail runs on after contact");

            sequencer.Cue(CombatCueKind.HitWindowClosed, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Trail));

            sequencer.Cue(CombatCueKind.Recovered, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Trail), "stopped once");
            Assert.AreEqual(0, Bursts(BodyEffectBurst.Linger), "Arcane leaves no embers");
            Assert.IsFalse(sequencer.IsActive);
        }

        [Test]
        public void FlameTrailIntensifiesTowardsContactAndNeverPastFullStrength()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Flame, 1, secondsToContact: 0.12f), 0f, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Trail), "fire from the start of the wind-up");
            Assert.Less(output[output.Count - 1].Intensity, 0.5f);

            float last = 0f;
            for (float t = 0.02f; t <= 0.2f; t += 0.02f)
            {
                output.Clear();
                sequencer.Tick(t, 0f, output);
                foreach (BodyEffectCommand command in output)
                {
                    Assert.GreaterOrEqual(command.Intensity, last, "it only grows");
                    Assert.LessOrEqual(command.Intensity, 1f);
                    last = command.Intensity;
                }
            }

            Assert.AreEqual(1f, last, 0.0001f, "full strength at contact time");
        }

        [Test]
        public void FlameLeavesEmbersOnlyAfterAStrikeThatLanded()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Flame, 1), 0f, output);
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            sequencer.Cue(CombatCueKind.HitWindowClosed, 1, output);
            sequencer.Cue(CombatCueKind.Recovered, 1, output);
            Assert.AreEqual(1, Bursts(BodyEffectBurst.Linger));

            output.Clear();
            sequencer.Begin(Melee(MagicDiscipline.Flame, 2), 1f, output);
            sequencer.Cue(CombatCueKind.Cancelled, 2, output);
            Assert.AreEqual(0, Bursts(BodyEffectBurst.Linger));
            Assert.AreEqual(0, Bursts(BodyEffectBurst.Contact));
        }

        [Test]
        public void AegisGathersForceUntilContactAndShowsTheHeavyBurstOnTheSlam()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Aegis, 1, heavy: true, effector: CombatEffector.BothHands), 0f, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Gather));
            Assert.AreEqual(0, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Trail), "no trail in the wind-up");

            sequencer.Cue(CombatCueKind.Effect, 1, output);
            Assert.AreEqual(0, Bursts(BodyEffectBurst.Swing), "Aegis has no slash");
            sequencer.Cue(CombatCueKind.HitWindowOpened, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Trail), "a streak in the fast part only");

            sequencer.Cue(CombatCueKind.Contact, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Gather), "the gathered force is spent");
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Trail), "the streak ends at contact");
            Assert.AreEqual(1, Bursts(BodyEffectBurst.HeavyContact));
            Assert.AreEqual(0, Bursts(BodyEffectBurst.Contact));
            Assert.AreEqual(CombatEffector.BothHands, output[output.Count - 1].Effector);
        }

        [Test]
        public void ACancelledStrikeStopsEverythingQuicklyAndShowsNoContact()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Aegis, 1), 0f, output);
            sequencer.Cue(CombatCueKind.HitWindowOpened, 1, output);
            output.Clear();

            sequencer.Cue(CombatCueKind.Cancelled, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Gather));
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Trail));
            foreach (BodyEffectCommand command in output)
            {
                Assert.AreNotEqual(BodyEffectCommandKind.Burst, command.Kind);
                Assert.AreEqual(MagicVfxDefaults.For(MagicDiscipline.Aegis).cancelDrainSeconds, command.DrainSeconds, 0.0001f);
            }

            output.Clear();
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            sequencer.Cue(CombatCueKind.Recovered, 1, output);
            Assert.AreEqual(0, output.Count, "nothing of an ended action");
        }

        [Test]
        public void ContactIsShownOnceEvenIfTheCueRepeats()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Arcane, 1), 0f, output);
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            Assert.AreEqual(1, Bursts(BodyEffectBurst.Contact));
        }

        [Test]
        public void CuesOfAnOlderActionAreIgnoredAndANewActionEndsTheOldOne()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Arcane, 1), 0f, output);
            sequencer.Cue(CombatCueKind.Effect, 1, output);
            output.Clear();

            sequencer.Begin(Melee(MagicDiscipline.Arcane, 2), 0.1f, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Trail), "the old trail stops");
            Assert.AreEqual(1, output[0].ActionId);

            output.Clear();
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            sequencer.Cue(CombatCueKind.HitWindowClosed, 1, output);
            Assert.AreEqual(0, output.Count);
            Assert.AreEqual(2, sequencer.CurrentActionId);
        }

        [Test]
        public void AMissingEffectCueStillStartsTheTrailWhenTheHitWindowOpens()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Melee(MagicDiscipline.Arcane, 1), 0f, output);
            sequencer.Cue(CombatCueKind.HitWindowOpened, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Trail));
            Assert.AreEqual(1, Bursts(BodyEffectBurst.Swing));
        }

        // ---------------------------------------------------------------- charged

        [Test]
        public void AChargeGrowsAndConcentratesWithItsPower()
        {
            var sequencer = new BodyEffectSequencer();
            BodyEffectTiming timing = MagicVfxDefaults.For(MagicDiscipline.Aegis);
            sequencer.Begin(Charged(MagicDiscipline.Aegis, 1), 0f, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Start, BodyEffectLayer.Charge));
            Assert.AreEqual(timing.chargeStartIntensity, output[0].Intensity, 0.0001f);
            Assert.AreEqual(timing.chargeRadiusAtStart, output[0].Radius, 0.0001f);

            output.Clear();
            sequencer.Tick(0.5f, 0.5f, output);
            Assert.AreEqual(1, output.Count);
            Assert.Greater(output[0].Intensity, timing.chargeStartIntensity);
            Assert.Less(output[0].Radius, timing.chargeRadiusAtStart);
            Assert.Greater(output[0].Radius, timing.chargeRadiusAtFull);

            output.Clear();
            sequencer.Tick(1f, 1f, output);
            Assert.AreEqual(1f, output[0].Intensity, 0.0001f);
            Assert.AreEqual(timing.chargeRadiusAtFull, output[0].Radius, 0.0001f);

            output.Clear();
            sequencer.Tick(1.1f, 1f, output);
            Assert.AreEqual(0, output.Count, "no command when nothing changes");
        }

        [Test]
        public void AReleaseStopsTheChargeAndBurstsOnce()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Charged(MagicDiscipline.Arcane, 1), 0f, output);
            sequencer.Cue(CombatCueKind.Release, 1, output);
            sequencer.Cue(CombatCueKind.Release, 1, output);
            sequencer.Cue(CombatCueKind.Recovered, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Charge));
            Assert.AreEqual(1, Bursts(BodyEffectBurst.Release));
        }

        [Test]
        public void ACancelledChargeShowsNoRelease()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Charged(MagicDiscipline.Flame, 1), 0f, output);
            sequencer.Tick(0.3f, 0.4f, output);
            sequencer.Cue(CombatCueKind.Cancelled, 1, output);
            Assert.AreEqual(1, Count(BodyEffectCommandKind.Stop, BodyEffectLayer.Charge));
            Assert.AreEqual(0, Bursts(BodyEffectBurst.Release));
            Assert.IsFalse(sequencer.IsOn(BodyEffectLayer.Charge));
        }

        [Test]
        public void MeleeCuesDoNothingToACharge()
        {
            var sequencer = new BodyEffectSequencer();
            sequencer.Begin(Charged(MagicDiscipline.Arcane, 1), 0f, output);
            output.Clear();
            sequencer.Cue(CombatCueKind.Contact, 1, output);
            sequencer.Cue(CombatCueKind.HitWindowOpened, 1, output);
            Assert.AreEqual(0, output.Count);
        }

        // ------------------------------------------------------------ guarantees

        [Test]
        public void EveryStartedEffectIsStoppedExactlyOnceInRandomPlay()
        {
            var cues = new[]
            {
                CombatCueKind.Effect, CombatCueKind.HitWindowOpened, CombatCueKind.Contact, CombatCueKind.HitWindowClosed,
                CombatCueKind.Release, CombatCueKind.Recovered, CombatCueKind.Cancelled,
            };

            for (int seed = 1; seed <= 5; seed++)
            {
                var random = new Random(seed);
                var sequencer = new BodyEffectSequencer();
                var open = new Dictionary<(int, BodyEffectLayer), int>();
                var ended = new HashSet<int>();
                int action = 0;
                float time = 0f;

                for (int step = 0; step < 3000; step++)
                {
                    output.Clear();
                    time += 0.016f;
                    int roll = random.Next(10);
                    if (roll == 0 || action == 0)
                    {
                        if (action != 0)
                        {
                            ended.Add(action);
                        }

                        action++;
                        var discipline = (MagicDiscipline)random.Next(3);
                        BodyActionInfo info = random.Next(3) == 0
                            ? Charged(discipline, action)
                            : Melee(discipline, action, (float)random.NextDouble() * 0.3f, random.Next(2) == 0);
                        sequencer.Begin(info, time, output);
                    }
                    else if (roll < 8)
                    {
                        // Sometimes a cue for an older action arrives late.
                        int id = random.Next(6) == 0 ? Math.Max(1, action - 1) : action;
                        sequencer.Cue(cues[random.Next(cues.Length)], id, output);
                    }
                    else
                    {
                        sequencer.Tick(time, (float)random.NextDouble(), output);
                    }

                    if (!sequencer.IsActive)
                    {
                        ended.Add(action);
                    }

                    Account(open, ended);
                }

                output.Clear();
                sequencer.Reset(output);
                ended.Add(action);
                Account(open, ended);

                foreach (KeyValuePair<(int, BodyEffectLayer), int> pair in open)
                {
                    Assert.AreEqual(0, pair.Value, $"seed {seed}: action {pair.Key.Item1} {pair.Key.Item2} left running");
                }
            }
        }

        private void Account(Dictionary<(int, BodyEffectLayer), int> open, HashSet<int> ended)
        {
            foreach (BodyEffectCommand command in output)
            {
                var key = (command.ActionId, command.Layer);
                open.TryGetValue(key, out int running);
                switch (command.Kind)
                {
                    case BodyEffectCommandKind.Start:
                        Assert.AreEqual(0, running, "started twice without a stop");
                        open[key] = 1;
                        break;

                    case BodyEffectCommandKind.Stop:
                        Assert.AreEqual(1, running, "stopped without a start, or twice");
                        open[key] = 0;
                        break;

                    case BodyEffectCommandKind.SetIntensity:
                        Assert.AreEqual(1, running, "changed while not running");
                        Assert.That(command.Intensity, Is.InRange(0f, 1f));
                        break;

                    case BodyEffectCommandKind.Burst:
                        Assert.IsFalse(ended.Contains(command.ActionId) && command.Burst != BodyEffectBurst.Linger,
                            "a burst of an action that had already ended");
                        break;
                }
            }
        }

        [Test]
        public void SteadyFramesAllocateNothing()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            probe = new object[8];
            if (GC.GetAllocatedBytesForCurrentThread() == before)
            {
                Assert.Inconclusive("This runtime does not count allocations. The result comes from the .NET runner of " +
                                    "Tools/ci/compile_check.sh.");
            }

            var sequencer = new BodyEffectSequencer();
            var commands = new List<BodyEffectCommand>(64);
            BodyActionInfo melee = Melee(MagicDiscipline.Flame, 1);
            BodyActionInfo charge = Charged(MagicDiscipline.Aegis, 2);

            // Warm up once so the list has grown.
            Run(sequencer, commands, melee, charge, 0f);

            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                melee.ActionId = 10 + i * 2;
                charge.ActionId = 11 + i * 2;
                Run(sequencer, commands, melee, charge, i);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "bytes allocated by 200 strikes and charges");
        }

        private static void Run(BodyEffectSequencer sequencer, List<BodyEffectCommand> commands, BodyActionInfo melee,
            BodyActionInfo charge, float time)
        {
            commands.Clear();
            sequencer.Begin(melee, time, commands);
            for (int f = 1; f <= 8; f++)
            {
                sequencer.Tick(time + f * 0.016f, 0f, commands);
            }

            sequencer.Cue(CombatCueKind.Contact, melee.ActionId, commands);
            sequencer.Cue(CombatCueKind.HitWindowClosed, melee.ActionId, commands);
            sequencer.Cue(CombatCueKind.Recovered, melee.ActionId, commands);
            commands.Clear();
            sequencer.Begin(charge, time, commands);
            for (int f = 1; f <= 8; f++)
            {
                sequencer.Tick(time + f * 0.016f, f / 8f, commands);
            }

            sequencer.Cue(CombatCueKind.Release, charge.ActionId, commands);
            sequencer.Cue(CombatCueKind.Recovered, charge.ActionId, commands);
        }
    }
}
