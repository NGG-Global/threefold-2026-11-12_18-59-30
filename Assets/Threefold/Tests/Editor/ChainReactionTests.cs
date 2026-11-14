using System;
using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.Persistence;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Chain reactions: the bounded queue that carries them out, the set of objects that are advanced
    /// in time, and whole chains played through with the example objects. A small stand-in world
    /// does what the component does in a scene (finds what is in reach, nearest first, and offers
    /// each effect to the queue), so the real rules, the real queue and the real budgets are tested
    /// without Unity.
    /// </summary>
    public sealed class ChainReactionTests
    {
        private int nextEvent = 5000;

        // ------------------------------------------------------- a stand-in world

        private sealed class Thing : IMagicEffectReceiver, ISimulated
        {
            public readonly string Name;
            public readonly MagicalPropertySet Set;
            public readonly List<Thing> Linked = new List<Thing>();
            public Vector3 Position;
            public bool Alive = true;
            public World World;

            public Thing(string name, MagicalPropertySettings settings, Vector3 position)
            {
                Name = name;
                Position = position;
                Set = new MagicalPropertySet(settings) { KineticAxis = Vector3.forward };
            }

            public bool IsSimulationAtRest => Set.IsAtRest;
            public bool IsSimulationCritical => Set.Settings.critical;

            public EffectResponse ReceiveEffect(in MagicEffect effect)
            {
                EffectResponse response = Set.Apply(effect);
                World.Log.Add($"{Name}<{effect.Kind}@{effect.Depth}:{response}");
                World.Send(this);
                if (!Set.IsAtRest)
                {
                    World.Simulation.Wake(this);
                }

                return response;
            }

            public void Simulate(float deltaTime)
            {
                Set.Tick(deltaTime);
                World.Send(this);
            }
        }

        private sealed class World
        {
            public readonly List<Thing> Things = new List<Thing>();
            public readonly List<string> Log = new List<string>();
            public readonly ReactionBudgets Budgets;
            public readonly ReactionScheduler Scheduler;
            public readonly SimulationSet Simulation;

            public World(ReactionBudgets budgets = null)
            {
                Budgets = budgets ?? new ReactionBudgets();
                Scheduler = new ReactionScheduler(Budgets) { IsValidTarget = target => ((Thing)target).Alive };
                Simulation = new SimulationSet(Budgets);
            }

            public Thing Add(string name, ExampleObject example, float z, float x = 0f)
            {
                return Add(name, WorldMagicDefaults.Settings(example), new Vector3(x, 0f, z));
            }

            public Thing Add(string name, MagicalPropertySettings settings, Vector3 position)
            {
                var thing = new Thing(name, settings, position) { World = this };
                Things.Add(thing);
                return thing;
            }

            /// <summary>What the component does: linked targets in their order, then what is in reach, nearest first.</summary>
            public void Send(Thing source)
            {
                for (int i = 0; i < source.Set.PendingEmissionCount; i++)
                {
                    EmissionRequest request = source.Set.GetPendingEmission(i);
                    ReactionEmission rule = source.Set.GetEmissionRule(request.Rule);
                    var targets = new List<Thing>();

                    if (rule.toLinkedTargets)
                    {
                        targets.AddRange(source.Linked);
                    }

                    if (rule.radius > 0f)
                    {
                        var near = new List<Thing>();
                        foreach (Thing other in Things)
                        {
                            if (other != source && other.Alive && !targets.Contains(other) &&
                                Vector3.Distance(other.Position, source.Position) <= rule.radius)
                            {
                                near.Add(other);
                            }
                        }

                        near.Sort((a, b) =>
                        {
                            int byDistance = Vector3.Distance(a.Position, source.Position)
                                .CompareTo(Vector3.Distance(b.Position, source.Position));
                            return byDistance != 0 ? byDistance : Things.IndexOf(a).CompareTo(Things.IndexOf(b));
                        });
                        targets.AddRange(near);
                    }

                    foreach (Thing target in targets)
                    {
                        Vector3 direction = (target.Position - source.Position).normalized;
                        var effect = new MagicEffect(rule.effect, rule.magnitude, direction, rule.duration, target.Position, null, -1,
                            rule.targets, request.EventId, request.Depth);
                        Scheduler.Enqueue(target, effect, rule.priority);
                    }
                }

                source.Set.ClearPendingEmissions();
            }

            public void Step(float deltaTime = 0.1f)
            {
                Simulation.Tick(deltaTime);
                Scheduler.Process();
            }

            public void Run(float seconds, float deltaTime = 0.1f)
            {
                for (float t = 0f; t < seconds - 0.0001f; t += deltaTime)
                {
                    Step(deltaTime);
                }
            }
        }

        /// <summary>Records what it is given and can do something when it is.</summary>
        private sealed class Recorder : IMagicEffectReceiver
        {
            public readonly string Name;
            public readonly List<string> Journal;
            public Action<MagicEffect> OnReceive;
            public bool Valid = true;
            public EffectResponse Response = EffectResponse.Applied;

            public Recorder(string name, List<string> journal)
            {
                Name = name;
                Journal = journal;
            }

            public EffectResponse ReceiveEffect(in MagicEffect effect)
            {
                Journal.Add($"{Name}:{effect.EventId}");
                OnReceive?.Invoke(effect);
                return Response;
            }
        }

        private MagicEffect PlayerEffect(MagicEffectKind kind, float magnitude, int eventId = -1)
        {
            return new MagicEffect(kind, magnitude, Vector3.forward, 1.5f, Vector3.zero, null, 0, EffectTargets.All,
                eventId >= 0 ? eventId : nextEvent++);
        }

        private static MagicEffect Queued(int eventId, int depth = 1, MagicEffectKind kind = MagicEffectKind.Heat)
        {
            return new MagicEffect(kind, 1f, Vector3.forward, 0f, Vector3.zero, null, -1, EffectTargets.All, eventId, depth);
        }

        // ------------------------------------------------------------ the queue

        [Test]
        public void ReactionsAreCarriedOutMostImportantFirstAndOtherwiseInTheOrderOffered()
        {
            var journal = new List<string>();
            var target = new Recorder("t", journal);
            var scheduler = new ReactionScheduler();

            scheduler.Enqueue(target, Queued(1), ReactionPriority.Low);
            scheduler.Enqueue(target, Queued(2), ReactionPriority.Normal);
            scheduler.Enqueue(target, Queued(3), ReactionPriority.Critical);
            scheduler.Enqueue(target, Queued(4), ReactionPriority.Normal);
            scheduler.Enqueue(target, Queued(5), ReactionPriority.Low);
            scheduler.Enqueue(target, Queued(6), ReactionPriority.Critical);

            Assert.AreEqual(6, scheduler.Process(100));
            CollectionAssert.AreEqual(new[] { "t:3", "t:6", "t:2", "t:4", "t:1", "t:5" }, journal);
            Assert.AreEqual(0, scheduler.Count);
        }

        [Test]
        public void OnlyTheBudgetIsCarriedOutPerFrameAndTheRestWaits()
        {
            var journal = new List<string>();
            var target = new Recorder("t", journal);
            var scheduler = new ReactionScheduler();
            for (int i = 1; i <= 20; i++)
            {
                Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(target, Queued(i), ReactionPriority.Normal));
            }

            Assert.AreEqual(8, scheduler.Process(), "The default budget for one frame.");
            Assert.AreEqual(12, scheduler.Count);
            Assert.AreEqual(8, scheduler.Process());
            Assert.AreEqual(4, scheduler.Process());
            Assert.AreEqual(0, scheduler.Process());

            // Nothing was lost and nothing changed places.
            Assert.AreEqual(20, journal.Count);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual($"t:{i + 1}", journal[i]);
            }

            Assert.AreEqual(0, scheduler.Statistics.Dropped);
            Assert.AreEqual(20, scheduler.Statistics.PeakWaiting);
        }

        [Test]
        public void WhenTheBudgetIsShortCriticalReactionsAreNotTheOnesThatWait()
        {
            var journal = new List<string>();
            var target = new Recorder("t", journal);
            var scheduler = new ReactionScheduler();
            for (int i = 1; i <= 10; i++)
            {
                scheduler.Enqueue(target, Queued(i), ReactionPriority.Normal);
            }

            scheduler.Enqueue(target, Queued(100), ReactionPriority.Critical);
            scheduler.Enqueue(target, Queued(101), ReactionPriority.Critical);
            scheduler.Enqueue(target, Queued(102), ReactionPriority.Critical);

            scheduler.Process(4);

            CollectionAssert.AreEqual(new[] { "t:100", "t:101", "t:102", "t:1" }, journal);
            Assert.AreEqual(9, scheduler.Count, "The rest is deferred, not dropped.");
        }

        [Test]
        public void AFullQueueRefusesNormalAndLowReactionsAndSaysSo()
        {
            var budgets = new ReactionBudgets { queueCapacity = 4 };
            var scheduler = new ReactionScheduler(budgets);
            var target = new Recorder("t", new List<string>());
            var refused = new List<string>();
            scheduler.Refused += (reaction, reason) => refused.Add($"{reaction.Effect.EventId}:{reason}");

            for (int i = 1; i <= 6; i++)
            {
                EnqueueResult result = scheduler.Enqueue(target, Queued(i), ReactionPriority.Normal);
                Assert.AreEqual(i <= 4 ? EnqueueResult.Queued : EnqueueResult.QueueFull, result, $"Reaction {i}.");
            }

            // Low priority has room of its own: flavour does not push out what matters, nor the reverse.
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(target, Queued(50), ReactionPriority.Low));

            Assert.AreEqual(2, scheduler.Statistics.Dropped);
            CollectionAssert.AreEqual(new[] { "5:QueueFull", "6:QueueFull" }, refused, "Nothing is dropped without a report.");

            // Once there is room again, reactions are taken again.
            scheduler.Process(2);
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(target, Queued(7), ReactionPriority.Normal));
        }

        [Test]
        public void ACriticalReactionIsNeverRefusedHoweverFullTheQueue()
        {
            var budgets = new ReactionBudgets { queueCapacity = 4, reactionsPerFrame = 3 };
            var scheduler = new ReactionScheduler(budgets);
            var journal = new List<string>();
            var target = new Recorder("t", journal);
            int refusals = 0;
            scheduler.Refused += (reaction, reason) => refusals++;

            for (int i = 1; i <= 25; i++)
            {
                Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(target, Queued(i), ReactionPriority.Critical), $"Reaction {i}.");
            }

            Assert.AreEqual(0, refusals);
            Assert.AreEqual(0, scheduler.Statistics.Dropped);
            Assert.AreEqual(21, scheduler.Statistics.CriticalOverflow, "The overflow is counted, so it can be seen and tuned.");

            int frames = 0;
            while (scheduler.Count > 0 && frames < 100)
            {
                scheduler.Process();
                frames++;
            }

            Assert.AreEqual(25, journal.Count, "Every one of them arrives.");
            Assert.AreEqual(9, frames, "Three a frame: they are deferred, not rushed.");
            for (int i = 0; i < 25; i++)
            {
                Assert.AreEqual($"t:{i + 1}", journal[i]);
            }
        }

        [Test]
        public void AReactionDeeperThanTheLimitIsRefused()
        {
            var scheduler = new ReactionScheduler();
            var target = new Recorder("t", new List<string>());

            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(target, Queued(1, 3), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.DepthLimit, scheduler.Enqueue(target, Queued(2, 4), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.DepthLimit, scheduler.Enqueue(target, Queued(3, 4), ReactionPriority.Critical),
                "The limit is what ends a chain; it holds for every priority.");
            Assert.AreEqual(2, scheduler.Statistics.DepthLimited);

            // The limit is a setting.
            scheduler.Budgets.maxDepth = 5;
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(target, Queued(4, 5), ReactionPriority.Normal));
        }

        [Test]
        public void TheSameEffectOfTheSameEventIsNotQueuedTwiceForATarget()
        {
            var scheduler = new ReactionScheduler();
            var first = new Recorder("a", new List<string>());
            var second = new Recorder("b", new List<string>());

            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(first, Queued(7), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.Duplicate, scheduler.Enqueue(first, Queued(7), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.Duplicate, scheduler.Enqueue(first, Queued(7, 2), ReactionPriority.Low),
                "By another path, at another depth and a lower priority: still the same effect of the same event.");

            // Another target, another kind of effect, another event: none of these is a duplicate.
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(second, Queued(7), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(first, Queued(7, 1, MagicEffectKind.Force), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(first, Queued(8), ReactionPriority.Normal));

            // An effect without an identity is never taken for a duplicate.
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(first, Queued(0), ReactionPriority.Normal));
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(first, Queued(0), ReactionPriority.Normal));

            Assert.AreEqual(2, scheduler.Statistics.Duplicates);
        }

        [Test]
        public void AMoreUrgentCopyTakesThePlaceOfALessUrgentOneThatWaits()
        {
            var journal = new List<string>();
            var door = new Recorder("door", journal);
            var refused = new List<string>();
            var scheduler = new ReactionScheduler();
            scheduler.Refused += (reaction, reason) => refused.Add($"{reaction.Priority}:{reason}");

            // A weak flavour reaction of an event is waiting when the critical one of the same event comes.
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(door, Queued(7), ReactionPriority.Low));
            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(door, Queued(7), ReactionPriority.Critical),
                "A puzzle must not lose its effect to a weaker copy that happened to be offered first.");
            CollectionAssert.AreEqual(new[] { "Low:Duplicate" }, refused, "The copy that gave way is reported.");
            Assert.AreEqual(1, scheduler.Count);
            Assert.AreEqual(1, scheduler.CriticalCount);

            // Equal or lower urgency is still a duplicate, as before.
            Assert.AreEqual(EnqueueResult.Duplicate, scheduler.Enqueue(door, Queued(7), ReactionPriority.Critical));
            Assert.AreEqual(EnqueueResult.Duplicate, scheduler.Enqueue(door, Queued(7), ReactionPriority.Normal));

            Assert.AreEqual(1, scheduler.Process(100));
            CollectionAssert.AreEqual(new[] { "door:7" }, journal, "Delivered once.");
            Assert.AreEqual(3, scheduler.Statistics.Duplicates);
        }

        [Test]
        public void AFullQueueNeverCostsBothCopiesOfAnEffect()
        {
            var journal = new List<string>();
            var door = new Recorder("door", journal);
            var filler = new Recorder("filler", journal);
            var scheduler = new ReactionScheduler(new ReactionBudgets { queueCapacity = 4 });

            Assert.AreEqual(EnqueueResult.Queued, scheduler.Enqueue(door, Queued(7), ReactionPriority.Low));
            for (int i = 0; i < 4; i++)
            {
                scheduler.Enqueue(filler, Queued(100 + i), ReactionPriority.Normal);
            }

            // A more urgent copy finds its queue full: it is refused, and the waiting copy stays.
            Assert.AreEqual(EnqueueResult.QueueFull, scheduler.Enqueue(door, Queued(7), ReactionPriority.Normal));
            scheduler.Process(100);
            CollectionAssert.Contains(journal, "door:7", "Before, both copies were lost.");
        }

        [Test]
        public void AListenerMayOfferMoreWhileReactionsAreReported()
        {
            var journal = new List<string>();
            var leaving = new Recorder("leaving", journal);
            var other = new Recorder("other", journal);
            var reported = new List<int>();
            var scheduler = new ReactionScheduler();
            bool acted = false;
            scheduler.Refused += (reaction, reason) =>
            {
                reported.Add(reaction.Effect.EventId);
                if (acted)
                {
                    return;
                }

                // In the middle of a report, a listener offers a more urgent copy (which makes the waiting
                // one give way, itself a report) and cancels elsewhere (another report).
                acted = true;
                scheduler.Enqueue(other, Queued(50), ReactionPriority.Critical);
                scheduler.Cancel(other);
            };

            scheduler.Enqueue(other, Queued(50), ReactionPriority.Low);
            scheduler.Enqueue(leaving, Queued(1), ReactionPriority.Normal);
            scheduler.Enqueue(leaving, Queued(2), ReactionPriority.Normal);
            scheduler.Enqueue(leaving, Queued(3), ReactionPriority.Critical);

            Assert.AreEqual(3, scheduler.Cancel(leaving));
            CollectionAssert.IsSubsetOf(new[] { 3, 1, 2 }, reported, "Every reaction given up is reported, whatever the listener does.");
        }

        [Test]
        public void EveryReactionThatIsGivenUpIsReported()
        {
            var journal = new List<string>();
            var leaving = new Recorder("leaving", journal);
            var refused = new List<string>();
            var scheduler = new ReactionScheduler();
            scheduler.Refused += (reaction, reason) => refused.Add($"{reaction.Priority}:{reason}");

            scheduler.Enqueue(leaving, Queued(1), ReactionPriority.Normal);
            scheduler.Enqueue(leaving, Queued(2), ReactionPriority.Critical);
            Assert.AreEqual(EnqueueResult.DepthLimit, scheduler.Enqueue(leaving, Queued(3, 4), ReactionPriority.Critical));

            // Switched off or unloaded while they wait.
            Assert.AreEqual(2, scheduler.Cancel(leaving));

            CollectionAssert.AreEqual(new[] { "Critical:DepthLimit", "Critical:InvalidTarget", "Normal:InvalidTarget" }, refused,
                "Nothing waiting for a target disappears without a word, least of all a critical reaction.");
        }

        [Test]
        public void ACriticalReactionThatItsTargetTurnsAwayIsReported()
        {
            var journal = new List<string>();
            var door = new Recorder("door", journal) { Response = EffectResponse.Duplicate };
            var refused = new List<string>();
            var scheduler = new ReactionScheduler();
            scheduler.Refused += (reaction, reason) => refused.Add($"{reaction.Priority}:{reason}");

            // The door had already taken charge from this event by another path, so these change nothing.
            scheduler.Enqueue(door, Queued(9, 1, MagicEffectKind.ArcaneCharge), ReactionPriority.Normal);
            scheduler.Enqueue(door, Queued(10, 1, MagicEffectKind.ArcaneCharge), ReactionPriority.Critical);
            Assert.AreEqual(2, scheduler.Process(100));

            CollectionAssert.AreEqual(new[] { "Critical:Duplicate" }, refused,
                "Only the critical one is worth a report; loops are still prevented the same way.");
            Assert.AreEqual(1, scheduler.Statistics.TurnedAway);
        }

        [Test]
        public void ATargetThatIsGoneReceivesNothing()
        {
            var journal = new List<string>();
            var gone = new Recorder("gone", journal);
            var here = new Recorder("here", journal);
            var scheduler = new ReactionScheduler { IsValidTarget = target => ((Recorder)target).Valid };

            scheduler.Enqueue(gone, Queued(1), ReactionPriority.Normal);
            scheduler.Enqueue(here, Queued(2), ReactionPriority.Normal);
            scheduler.Enqueue(gone, Queued(3), ReactionPriority.Critical);
            scheduler.Enqueue(here, Queued(4), ReactionPriority.Normal);

            // It is destroyed while its reactions wait.
            gone.Valid = false;

            Assert.AreEqual(2, scheduler.Process(2), "Skipping what cannot be delivered does not use up the budget.");
            CollectionAssert.AreEqual(new[] { "here:2", "here:4" }, journal);
            Assert.AreEqual(2, scheduler.Statistics.Invalidated);

            // And nothing new is taken for it.
            Assert.AreEqual(EnqueueResult.InvalidTarget, scheduler.Enqueue(gone, Queued(5), ReactionPriority.Critical));
            Assert.AreEqual(EnqueueResult.InvalidTarget, scheduler.Enqueue(null, Queued(6), ReactionPriority.Normal));
        }

        [Test]
        public void EverythingWaitingForATargetCanBeCancelledAndTheRestKeepsItsOrder()
        {
            var journal = new List<string>();
            var leaving = new Recorder("leaving", journal);
            var staying = new Recorder("staying", journal);
            var scheduler = new ReactionScheduler();

            scheduler.Enqueue(staying, Queued(1), ReactionPriority.Normal);
            scheduler.Enqueue(leaving, Queued(2), ReactionPriority.Normal);
            scheduler.Enqueue(staying, Queued(3), ReactionPriority.Normal);
            scheduler.Enqueue(leaving, Queued(4), ReactionPriority.Critical);
            scheduler.Enqueue(leaving, Queued(5), ReactionPriority.Low);
            scheduler.Enqueue(staying, Queued(6), ReactionPriority.Low);

            Assert.AreEqual(3, scheduler.Cancel(leaving));
            Assert.AreEqual(0, scheduler.Cancel(leaving));
            Assert.AreEqual(3, scheduler.Count);

            scheduler.Process(100);
            CollectionAssert.AreEqual(new[] { "staying:1", "staying:3", "staying:6" }, journal);
        }

        [Test]
        public void AReactionSetOffDuringDeliveryIsQueuedNotNested()
        {
            var journal = new List<string>();
            var scheduler = new ReactionScheduler();
            var second = new Recorder("second", journal);
            var first = new Recorder("first", journal);
            int nested = -1;

            first.OnReceive = effect =>
            {
                // What a reacting object does: offer the next effect. It must not be delivered inside this call.
                scheduler.Enqueue(second, Queued(effect.EventId, effect.Depth + 1), ReactionPriority.Normal);
                nested = scheduler.Process(100);
                journal.Add("first:done");
            };

            scheduler.Enqueue(first, Queued(1), ReactionPriority.Normal);
            scheduler.Process(100);

            Assert.AreEqual(0, nested, "A second pass inside the first does nothing.");
            CollectionAssert.AreEqual(new[] { "first:1", "first:done", "second:1" }, journal);
        }

        // ------------------------------------------------ the set that is advanced

        private sealed class Clock : ISimulated
        {
            public float Remaining;
            public float Elapsed;
            public bool Critical;
            public Action<Clock> OnSimulate;

            public Clock(float seconds, bool critical = false)
            {
                Remaining = seconds;
                Critical = critical;
            }

            public bool IsSimulationAtRest => Remaining <= 0f;
            public bool IsSimulationCritical => Critical;

            public void Simulate(float deltaTime)
            {
                Remaining -= deltaTime;
                Elapsed += deltaTime;
                OnSimulate?.Invoke(this);
            }
        }

        [Test]
        public void OnlyTheBudgetOfObjectsIsAdvancedAndTheOthersWaitTheirTurnWithoutLosingAnything()
        {
            var set = new SimulationSet(new ReactionBudgets { maxActiveObjects = 3 });
            var clocks = new List<Clock>();
            for (int i = 0; i < 5; i++)
            {
                clocks.Add(new Clock(1f));
            }

            Assert.IsTrue(set.Wake(clocks[0]));
            Assert.IsTrue(set.Wake(clocks[1]));
            Assert.IsTrue(set.Wake(clocks[2]));
            Assert.IsFalse(set.Wake(clocks[3]), "The fourth has to wait.");
            Assert.IsFalse(set.Wake(clocks[4]));
            Assert.IsFalse(set.Wake(clocks[4]), "Asking twice does not put it in the line twice.");
            Assert.AreEqual(3, set.ActiveCount);
            Assert.AreEqual(2, set.WaitingCount);
            Assert.AreEqual(2, set.DeferredCount);

            for (int i = 0; i < 5; i++)
            {
                set.Tick(0.1f);
            }

            Assert.AreEqual(0.5f, clocks[0].Elapsed, 0.001f);
            Assert.AreEqual(0f, clocks[3].Elapsed, "Time stands still for an object that waits.");
            Assert.AreEqual(1f, clocks[3].Remaining, "And it loses nothing of its state.");

            // The first three run out; the two that waited take their places, in the order they asked.
            for (int i = 0; i < 6; i++)
            {
                set.Tick(0.1f);
            }

            Assert.AreEqual(2, set.ActiveCount);
            Assert.AreEqual(0, set.WaitingCount);
            Assert.IsTrue(set.IsActive(clocks[3]) && set.IsActive(clocks[4]));

            for (int i = 0; i < 11; i++)
            {
                set.Tick(0.1f);
            }

            Assert.IsTrue(set.IsIdle);
            Assert.AreEqual(3, set.PeakActive, "Never more than the budget.");
            foreach (Clock clock in clocks)
            {
                Assert.LessOrEqual(clock.Remaining, 0f, "Every one of them ran its full time in the end.");
            }
        }

        [Test]
        public void ACriticalObjectNeverWaits()
        {
            var set = new SimulationSet(new ReactionBudgets { maxActiveObjects = 2 });
            set.Wake(new Clock(5f));
            set.Wake(new Clock(5f));
            var ordinary = new Clock(5f);
            var mechanism = new Clock(5f, true);

            Assert.IsFalse(set.Wake(ordinary));
            Assert.IsTrue(set.Wake(mechanism), "A puzzle depends on it: it is advanced beyond the budget.");
            Assert.AreEqual(3, set.ActiveCount);

            set.Tick(0.1f);
            Assert.AreEqual(0.1f, mechanism.Elapsed, 0.001f);
            Assert.AreEqual(0f, ordinary.Elapsed);
        }

        [Test]
        public void ObjectsAreAdvancedInTheOrderTheyJoinedAndCanLeaveDuringTheStep()
        {
            var set = new SimulationSet();
            var order = new List<string>();
            var a = new Clock(5f);
            var b = new Clock(5f);
            var c = new Clock(5f);
            var late = new Clock(5f);

            a.OnSimulate = clock =>
            {
                order.Add("a");

                // Destroyed in the middle of the step by what a did, and a newcomer arrives.
                set.Remove(b);
                set.Wake(late);
            };
            b.OnSimulate = clock => order.Add("b");
            c.OnSimulate = clock => order.Add("c");
            late.OnSimulate = clock => order.Add("late");

            set.Wake(a);
            set.Wake(b);
            set.Wake(c);
            set.Tick(0.1f);

            CollectionAssert.AreEqual(new[] { "a", "c" }, order, "The removed one is not called; the newcomer starts next step.");
            Assert.AreEqual(3, set.ActiveCount);

            order.Clear();
            set.Tick(0.1f);
            CollectionAssert.AreEqual(new[] { "a", "c", "late" }, order);
            Assert.IsFalse(set.IsActive(b));
        }

        // --------------------------------------------------------- whole chains

        private static World BarricadeRow(int count, ReactionBudgets budgets = null)
        {
            var world = new World(budgets);
            for (int i = 0; i < count; i++)
            {
                world.Add("b" + i, ExampleObject.WoodenBarricade, i * 2f);
            }

            return world;
        }

        [Test]
        public void AFireSpreadsAlongARowAndStopsAtTheDepthLimit()
        {
            World world = BarricadeRow(6);

            // The player sets the first alight. Each barricade heats only its neighbours.
            world.Things[0].ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 100f));
            world.Run(40f);

            Assert.IsTrue(world.Things[0].Set.IsDestroyed);
            Assert.IsTrue(world.Things[1].Set.IsDestroyed, "One step from the player.");
            Assert.IsTrue(world.Things[2].Set.IsDestroyed, "Two steps.");
            Assert.IsTrue(world.Things[3].Set.IsDestroyed, "Three steps: the limit.");
            Assert.IsFalse(world.Things[4].Set.IsDestroyed, "The chain ends here.");
            Assert.AreEqual(0f, world.Things[4].Set.Thermal.Heat, "It was never even warmed: the fourth's heat was not sent.");
            Assert.AreEqual(StructuralState.Intact, world.Things[5].Set.Structural.State);

            Assert.Greater(world.Scheduler.Statistics.DepthLimited, 0);
            Assert.AreEqual(0, world.Scheduler.Statistics.Dropped);
            Assert.IsTrue(world.Simulation.IsIdle, "When it is over nothing is left being simulated.");
            Assert.AreEqual(0, world.Scheduler.Count);
        }

        [Test]
        public void WithADeeperLimitTheSameFireGoesOneFurther()
        {
            World world = BarricadeRow(6, new ReactionBudgets { maxDepth = 4 });
            world.Things[0].ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 100f));
            world.Run(50f);

            Assert.IsTrue(world.Things[4].Set.IsDestroyed);
            Assert.IsFalse(world.Things[5].Set.IsDestroyed);
        }

        [Test]
        public void TheSameChainHappensTheSameWayEveryTime()
        {
            string Play()
            {
                World world = BarricadeRow(5);
                world.Add("stone", ExampleObject.HeatSensitiveStone, 2f, 2f);
                world.Add("pillar", ExampleObject.UnstableStructure, 4f, 2.5f);
                world.Things[0].ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 100f, 1));
                world.Run(8f);
                world.Things[5].ReceiveEffect(PlayerEffect(MagicEffectKind.Force, 5f, 2));
                world.Run(30f);
                return string.Join("|", world.Log);
            }

            string first = Play();
            Assert.AreEqual(first, Play());
            Assert.Greater(first.Length, 200, "Something did happen.");
        }

        [Test]
        public void ARowOfUnstableStructuresTopplesAsFarAsTheLimitAllows()
        {
            var world = new World();
            for (int i = 0; i < 7; i++)
            {
                world.Add("p" + i, ExampleObject.UnstableStructure, i * 3f);
            }

            world.Things[0].ReceiveEffect(PlayerEffect(MagicEffectKind.Force, 5f));
            world.Run(2f);

            for (int i = 0; i <= 3; i++)
            {
                Assert.AreEqual(StabilityState.Collapsed, world.Things[i].Set.Stability.State, $"Pillar {i}.");
            }

            for (int i = 4; i < 7; i++)
            {
                Assert.AreEqual(StabilityState.Unstable, world.Things[i].Set.Stability.State, $"Pillar {i} still stands.");
                Assert.AreEqual(0f, world.Things[i].Set.Stability.Stress, $"Pillar {i} was not touched.");
            }
        }

        [Test]
        public void AReactionCannotComeBackToWhereItStarted()
        {
            var world = new World();
            Thing first = world.Add("first", ExampleObject.UnstableStructure, 0f);
            Thing second = world.Add("second", ExampleObject.UnstableStructure, 3f);

            first.ReceiveEffect(PlayerEffect(MagicEffectKind.Force, 5f));
            float integrityAfterTheBlow = first.Set.Structural.Integrity;
            world.Run(2f);

            Assert.AreEqual(StabilityState.Collapsed, second.Set.Stability.State, "The first brought the second down.");
            CollectionAssert.Contains(world.Log, "first<Force@2:Duplicate", "The second's shove came back and was refused.");
            Assert.AreEqual(integrityAfterTheBlow, first.Set.Structural.Integrity, "So the first was not harmed a second time.");
        }

        [Test]
        public void OneAttackReachingATargetByTwoPathsActsOnItOnce()
        {
            var world = new World();
            // Two structures behind the platform, both in line with its rail, so each shoves it squarely.
            Thing left = world.Add("near", ExampleObject.UnstableStructure, -1.5f);
            Thing right = world.Add("far", ExampleObject.UnstableStructure, -2.4f);
            Thing platform = world.Add("platform", ExampleObject.MovablePlatform, 1f);

            // One pulse brings both structures down. Each of them then shoves the platform in front of them.
            int pulse = nextEvent++;
            left.ReceiveEffect(new MagicEffect(MagicEffectKind.Force, 5f, Vector3.forward, 0f, Vector3.zero, null, 2,
                EffectTargets.All, pulse));
            right.ReceiveEffect(new MagicEffect(MagicEffectKind.Force, 5f, Vector3.forward, 0f, Vector3.zero, null, 2,
                EffectTargets.All, pulse));

            Assert.AreEqual(1, world.Scheduler.Statistics.Duplicates, "The second shove for the platform was not queued.");
            world.Run(3f);

            int applied = world.Log.FindAll(entry => entry == "platform<Force@1:Applied").Count;
            Assert.AreEqual(1, applied);
            Assert.AreEqual(2.8f, platform.Set.Kinetic.Offset.z, 0.01f, "Moved by one shove, not by two.");
        }

        [Test]
        public void AnActivatedCrystalFeedsTheMechanismItIsLinkedTo()
        {
            var world = new World();
            Thing crystal = world.Add("crystal", ExampleObject.ArcaneCrystal, 0f);
            Thing door = world.Add("door", ExampleObject.ReinforcedDoor, 30f);
            Thing bystander = world.Add("bystander", ExampleObject.ArcaneCrystal, 1f);
            crystal.Linked.Add(door);

            crystal.ReceiveEffect(PlayerEffect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(1, world.Scheduler.CriticalCount, "What a mechanism sends on is critical.");
            world.Step();

            Assert.AreEqual(ArcaneState.Charged, door.Set.Arcane.State, "Far away, but linked.");
            Assert.AreEqual(75f, door.Set.Arcane.Charge, 0.001f);
            Assert.AreEqual(ArcaneState.Uncharged, bystander.Set.Arcane.State, "Close by, but not linked: it gets nothing.");

            // With that head start one full arrow opens the door instead of two.
            door.ReceiveEffect(PlayerEffect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(ArcaneState.Activated, door.Set.Arcane.State);
        }

        [Test]
        public void AFireNextToTheStoneBarrierMakesItBrittle()
        {
            var world = new World();
            Thing barricade = world.Add("barricade", ExampleObject.WoodenBarricade, 0f);
            Thing stone = world.Add("stone", ExampleObject.HeatSensitiveStone, 2f);

            barricade.ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 100f));
            world.Run(4.5f);

            Assert.AreEqual(ThermalState.Heated, stone.Set.Thermal.State, "Heat and structural vulnerability: brittle.");

            // Brittleness and force: fracture. The stone bursts, which is itself a reaction, of low priority.
            stone.ReceiveEffect(PlayerEffect(MagicEffectKind.Force, 5f));
            Assert.IsTrue(stone.Set.IsDestroyed);
        }

        [Test]
        public void AnObjectDestroyedInTheMiddleOfAChainReceivesNothingAfterwards()
        {
            var world = new World();
            Thing source = world.Add("source", ExampleObject.UnstableStructure, 0f);
            Thing victim = world.Add("victim", ExampleObject.WoodenBarricade, 2f);
            Thing other = world.Add("other", ExampleObject.MovablePlatform, 3f);

            // The victim is unloaded the moment anything changes on it.
            victim.Set.StateChanged += (kind, from, to) => victim.Alive = false;

            source.ReceiveEffect(PlayerEffect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(2, world.Scheduler.Count, "A shove waits for each neighbour.");

            // Something else reaches the victim first and it goes.
            victim.ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 100f));
            Assert.IsFalse(victim.Alive);
            int entries = world.Log.Count;

            world.Run(1f);

            Assert.IsFalse(world.Log.GetRange(entries, world.Log.Count - entries).Exists(entry => entry.StartsWith("victim<")),
                "Nothing called into the object that was gone.");
            Assert.AreEqual(1, world.Scheduler.Statistics.Invalidated);
            CollectionAssert.Contains(world.Log, "other<Force@1:Applied", "The rest of the chain went on.");
        }

        [Test]
        public void ManyObjectsReactingAtOnceStayWithinTheBudgetsAndNothingCriticalIsLost()
        {
            // Forty crystals all linked to one door, all activated in the same instant: far more
            // reactions than a frame allows, and all of them critical.
            var budgets = new ReactionBudgets { queueCapacity = 8, reactionsPerFrame = 8, maxActiveObjects = 32 };
            var world = new World(budgets);
            MagicalPropertySettings lockSettings = WorldMagicDefaults.Settings(ExampleObject.ReinforcedDoor);
            lockSettings.arcaneSettings.activationThreshold = 100000f;
            Thing door = world.Add("door", lockSettings, new Vector3(0f, 0f, 100f));

            for (int i = 0; i < 40; i++)
            {
                Thing crystal = world.Add("c" + i, ExampleObject.ArcaneCrystal, i);
                crystal.Linked.Add(door);
            }

            for (int i = 1; i <= 40; i++)
            {
                world.Things[i].ReceiveEffect(PlayerEffect(MagicEffectKind.ArcaneCharge, 100f));
            }

            Assert.AreEqual(40, world.Scheduler.Count);
            Assert.AreEqual(32, world.Scheduler.Statistics.CriticalOverflow);

            world.Step();
            Assert.AreEqual(8 * 75f, door.Set.Arcane.Charge, 0.01f, "Eight in the first frame.");
            Assert.AreEqual(32, world.Scheduler.Count, "The rest is deferred.");

            for (int i = 0; i < 4; i++)
            {
                world.Step();
            }

            Assert.AreEqual(0, world.Scheduler.Count);
            Assert.AreEqual(40, world.Log.FindAll(entry => entry == "door<ArcaneCharge@1:Applied").Count, "All forty arrived.");
            Assert.AreEqual(0, world.Scheduler.Statistics.Dropped);

            // The crystals and the door are mechanisms, so all forty-one are simulated although the budget is thirty-two.
            Assert.AreEqual(41, world.Simulation.PeakActive);
        }

        [Test]
        public void WhenThereIsTooMuchLowPriorityWorkItIsWhatGivesWay()
        {
            // Twelve stones shatter at once, each shoving the same platform: flavour, of low priority.
            var budgets = new ReactionBudgets { queueCapacity = 4, reactionsPerFrame = 2 };
            var world = new World(budgets);
            Thing platform = world.Add("platform", ExampleObject.MovablePlatform, 0f);
            Thing crystal = world.Add("crystal", ExampleObject.ArcaneCrystal, 50f);
            Thing door = world.Add("door", ExampleObject.ReinforcedDoor, 60f);
            crystal.Linked.Add(door);

            var dropped = new List<EnqueueResult>();
            world.Scheduler.Refused += (reaction, reason) =>
            {
                if (reason == EnqueueResult.QueueFull)
                {
                    dropped.Add(reason);
                    Assert.AreNotEqual(ReactionPriority.Critical, reaction.Priority);
                }
            };

            for (int i = 0; i < 12; i++)
            {
                Thing stone = world.Add("s" + i, ExampleObject.HeatSensitiveStone, 1f + i * 0.1f);
                stone.ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 80f));
                stone.ReceiveEffect(PlayerEffect(MagicEffectKind.Force, 5f));
            }

            crystal.ReceiveEffect(PlayerEffect(MagicEffectKind.ArcaneCharge, 100f));

            Assert.Greater(dropped.Count, 0, "Some of the shoves were given up, and reported.");
            Assert.AreEqual(dropped.Count, world.Scheduler.Statistics.Dropped);

            world.Step();
            Assert.AreEqual(75f, door.Set.Arcane.Charge, 0.001f, "The mechanism's charge went first and was not among them.");
        }

        // ------------------------------------------------- chains and saved state

        [Test]
        public void WhatAChainDestroysIsSavedAndWhatItOnlyHeatedIsNot()
        {
            World world = BarricadeRow(6);
            var store = new WorldStateStore();

            world.Things[0].ReceiveEffect(PlayerEffect(MagicEffectKind.Heat, 100f));
            world.Run(7f);

            // Saved in the middle of the fire: the first is gone, the second is burning, the third is warm.
            Assert.IsTrue(world.Things[0].Set.IsDestroyed);
            Assert.AreEqual(ThermalState.Burning, world.Things[1].Set.Thermal.State);
            for (int i = 0; i < 6; i++)
            {
                MagicalPropertySet set = world.Things[i].Set;
                store.SetObject("row/b" + i, set.Capture(set.Settings.persisted));
            }

            Assert.AreEqual(1, store.ObjectCount, "Only what lasts has an entry.");

            // Written, read back, and loaded into a fresh row.
            var read = new WorldStateStore();
            Assert.IsTrue(WorldStateText.Read(WorldStateText.Write(store), read).Complete);
            World reloaded = BarricadeRow(6);
            for (int i = 0; i < 6; i++)
            {
                read.TryGetObject("row/b" + i, out ObjectRecord record);
                MagicalPropertySet set = reloaded.Things[i].Set;
                set.Load(record, set.Settings.persisted);
            }

            Assert.IsTrue(reloaded.Things[0].Set.IsDestroyed);
            for (int i = 1; i < 6; i++)
            {
                Assert.AreEqual(ThermalState.Unheated, reloaded.Things[i].Set.Thermal.State, $"Barricade {i}: the fire did not survive.");
                Assert.AreEqual(StructuralState.Intact, reloaded.Things[i].Set.Structural.State);
            }

            // Nothing starts burning again by itself after the load.
            reloaded.Run(20f);
            Assert.IsFalse(reloaded.Things[1].Set.IsDestroyed);
            Assert.IsTrue(reloaded.Simulation.IsIdle);
        }
    }
}
