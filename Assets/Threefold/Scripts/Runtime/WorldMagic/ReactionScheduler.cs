using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// The limits chain reactions run within. All three are provisional starting values chosen for
    /// the prototype. None has been measured on a device.
    /// </summary>
    [Serializable]
    public sealed class ReactionBudgets
    {
        [Tooltip("How many steps a chain may be from what the player did. A reaction deeper than this is not sent.")]
        [Range(0, 8)]
        public int maxDepth = 3;

        [Tooltip("How many reactions are carried out per frame. The rest wait for the next frame.")]
        [Range(1, 64)]
        public int reactionsPerFrame = 8;

        [Tooltip("How many reactions of normal priority, and as many of low priority, may wait at once. Critical ones are never refused.")]
        [Range(4, 512)]
        public int queueCapacity = 64;

        [Tooltip("How many objects are simulated at once (cooling, burning, gliding). Others wait their turn; critical objects never wait.")]
        [Range(1, 256)]
        public int maxActiveObjects = 32;
    }

    /// <summary>What became of a reaction that was offered to the scheduler.</summary>
    public enum EnqueueResult
    {
        Queued,

        /// <summary>It would be deeper than the chain may go.</summary>
        DepthLimit,

        /// <summary>The same effect of the same event is already waiting for this target.</summary>
        Duplicate,

        /// <summary>The queue for its priority is full. Never the answer for a critical reaction.</summary>
        QueueFull,

        /// <summary>There is no target, or the target is gone.</summary>
        InvalidTarget,
    }

    /// <summary>One reaction waiting to be carried out.</summary>
    public struct QueuedReaction
    {
        public IMagicEffectReceiver Target;
        public MagicEffect Effect;
        public ReactionPriority Priority;

        /// <summary>The order in which it was offered. Reactions of equal priority are carried out in this order.</summary>
        public long Sequence;
    }

    /// <summary>Counts of what the scheduler did, for tests, validation and tuning the budgets.</summary>
    public struct ReactionStatistics
    {
        public long Queued;
        public long Delivered;
        public long DepthLimited;
        public long Duplicates;

        /// <summary>Refused because a queue was full. Critical reactions are never counted here.</summary>
        public long Dropped;

        /// <summary>Not delivered because the target was destroyed, unloaded or switched off in the meantime.</summary>
        public long Invalidated;

        /// <summary>Critical reactions taken beyond the queue capacity.</summary>
        public long CriticalOverflow;

        /// <summary>Critical reactions delivered that their target turned away, because it had taken the same effect of the same event by another path.</summary>
        public long TurnedAway;

        /// <summary>The most that waited at one time.</summary>
        public int PeakWaiting;
    }

    /// <summary>
    /// The bounded queue for secondary reactions. An object that reacts does not act on its
    /// neighbours at once: it offers each effect here, and a fixed number are carried out per frame.
    /// That keeps one action from doing unbounded work in one frame, and makes the order in which
    /// things happen the same every time.
    ///
    /// - Depth: an effect deeper than the limit is refused, so a chain ends. The depth travels with
    ///   the effect and with the state it causes, so a slow chain (a fire spreading) ends as well.
    /// - Duplicates: the same kind of effect from the same event is not queued twice for a target. If
    ///   the second is more urgent than the one that waits (a critical one behind a low one), the
    ///   waiting one gives way, so a puzzle never loses its effect to a weaker copy.
    /// - Order: critical first, then normal, then low; within a priority, first offered, first out.
    /// - Overflow: a full queue refuses normal and low reactions, counts them and reports them
    ///   through <see cref="Refused"/>. A critical reaction is always taken; the queue grows for it.
    /// - Validity: a target is checked again at the moment of delivery, and everything waiting for
    ///   a target can be cancelled when it is destroyed or unloaded.
    /// - Every reaction that is refused, given up or turned away by its target as a duplicate is
    ///   reported through <see cref="Refused"/>, so the host can make a lost critical one visible.
    ///
    /// Plain C#. Time, physics and Unity objects are the host's business.
    /// </summary>
    public sealed class ReactionScheduler
    {
        private readonly Queue<QueuedReaction> critical = new Queue<QueuedReaction>(16);
        private readonly Queue<QueuedReaction> normal;
        private readonly Queue<QueuedReaction> low;
        private readonly Queue<QueuedReaction> scratch;
        private readonly List<QueuedReaction> givenUp = new List<QueuedReaction>(16);
        private ReactionStatistics statistics;
        private long nextSequence;
        private bool processing;

        public ReactionScheduler(ReactionBudgets budgets = null)
        {
            Budgets = budgets ?? new ReactionBudgets();
            int capacity = Mathf.Max(4, Budgets.queueCapacity);
            normal = new Queue<QueuedReaction>(capacity);
            low = new Queue<QueuedReaction>(capacity);
            scratch = new Queue<QueuedReaction>(capacity);
        }

        public ReactionBudgets Budgets { get; }

        /// <summary>
        /// Says whether a target can still receive an effect. The host supplies it (a destroyed or
        /// switched-off Unity object cannot). Without one, every target that is not null counts.
        /// </summary>
        public Func<IMagicEffectReceiver, bool> IsValidTarget { get; set; }

        /// <summary>Raised for every reaction that is refused or given up, with the reason. Nothing is dropped without it.</summary>
        public event Action<QueuedReaction, EnqueueResult> Refused;

        public int Count => critical.Count + normal.Count + low.Count;

        public int CriticalCount => critical.Count;

        public ReactionStatistics Statistics => statistics;

        /// <summary>Offers a reaction. The effect's own depth and event identity decide whether it is taken.</summary>
        public EnqueueResult Enqueue(IMagicEffectReceiver target, in MagicEffect effect, ReactionPriority priority)
        {
            var reaction = new QueuedReaction { Target = target, Effect = effect, Priority = priority, Sequence = nextSequence };

            if (target == null || !CheckValid(target))
            {
                return Refuse(reaction, EnqueueResult.InvalidTarget);
            }

            if (effect.Depth > Budgets.maxDepth)
            {
                statistics.DepthLimited++;
                return Refuse(reaction, EnqueueResult.DepthLimit);
            }

            int waiting = WaitingPriority(target, effect.Kind, effect.EventId);
            if (waiting >= (int)priority)
            {
                statistics.Duplicates++;
                return Refuse(reaction, EnqueueResult.Duplicate);
            }

            if (waiting >= 0)
            {
                // A more urgent copy of an effect that already waits: the waiting one gives way.
                RemoveWaiting(target, effect.Kind, effect.EventId, priority);
            }

            Queue<QueuedReaction> queue = QueueFor(priority);
            if (priority == ReactionPriority.Critical)
            {
                // Never refused. Beyond the capacity the queue grows, which is counted so it can be seen.
                if (critical.Count >= Budgets.queueCapacity)
                {
                    statistics.CriticalOverflow++;
                }
            }
            else if (queue.Count >= Budgets.queueCapacity)
            {
                statistics.Dropped++;
                return Refuse(reaction, EnqueueResult.QueueFull);
            }

            nextSequence++;
            queue.Enqueue(reaction);
            statistics.Queued++;
            statistics.PeakWaiting = Mathf.Max(statistics.PeakWaiting, Count);
            return EnqueueResult.Queued;
        }

        /// <summary>
        /// Carries out waiting reactions, the most important first, up to the budget for one frame.
        /// Reactions that a delivery sets off are queued like any other and may be reached in the same
        /// call if the budget allows.
        /// </summary>
        /// <param name="budget">How many to carry out, or a negative number for the configured budget.</param>
        /// <returns>How many effects were delivered.</returns>
        public int Process(int budget = -1)
        {
            if (processing)
            {
                // A delivery must not start a second pass inside the first.
                return 0;
            }

            if (budget < 0)
            {
                budget = Budgets.reactionsPerFrame;
            }

            int delivered = 0;
            processing = true;
            try
            {
                while (delivered < budget && TryDequeue(out QueuedReaction reaction))
                {
                    if (!CheckValid(reaction.Target))
                    {
                        statistics.Invalidated++;
                        Refused?.Invoke(reaction, EnqueueResult.InvalidTarget);
                        continue;
                    }

                    EffectResponse response = reaction.Target.ReceiveEffect(reaction.Effect);
                    statistics.Delivered++;
                    delivered++;

                    if (response == EffectResponse.Duplicate && reaction.Priority == ReactionPriority.Critical)
                    {
                        // The target had already taken this kind of effect from the same event by another
                        // path, so this one changed nothing. That is how loops are prevented, but a puzzle
                        // may have counted on it, so it is reported rather than passed over.
                        statistics.TurnedAway++;
                        Refused?.Invoke(reaction, EnqueueResult.Duplicate);
                    }
                }
            }
            finally
            {
                processing = false;
            }

            return delivered;
        }

        /// <summary>
        /// Gives up everything waiting for a target, because it was destroyed, unloaded or switched off.
        /// Reactions the target itself sent out earlier are kept: what it set off still happens. Each
        /// reaction given up is reported through <see cref="Refused"/>, unless <paramref name="report"/>
        /// is false: when the target's state was just set as a whole, what was on its way is meant to go.
        /// </summary>
        /// <returns>How many reactions were removed.</returns>
        public int Cancel(IMagicEffectReceiver target, bool report = true)
        {
            if (target == null)
            {
                return 0;
            }

            givenUp.Clear();
            int removed = CancelIn(critical, target) + CancelIn(normal, target) + CancelIn(low, target);
            statistics.Invalidated += removed;
            if (report)
            {
                ReportGivenUp(EnqueueResult.InvalidTarget);
            }

            givenUp.Clear();
            return removed;
        }

        /// <summary>
        /// Forgets everything that waits, for example when the world is reloaded or a level is left.
        /// Nothing is reported: what was on its way belonged to a world that is no longer there.
        /// </summary>
        /// <returns>How many critical reactions were among those forgotten.</returns>
        public int Clear()
        {
            int criticalForgotten = critical.Count;
            critical.Clear();
            normal.Clear();
            low.Clear();
            return criticalForgotten;
        }

        public void ResetStatistics()
        {
            statistics = default;
        }

        private Queue<QueuedReaction> QueueFor(ReactionPriority priority)
        {
            switch (priority)
            {
                case ReactionPriority.Critical: return critical;
                case ReactionPriority.Low: return low;
                default: return normal;
            }
        }

        private bool TryDequeue(out QueuedReaction reaction)
        {
            if (critical.Count > 0)
            {
                reaction = critical.Dequeue();
                return true;
            }

            if (normal.Count > 0)
            {
                reaction = normal.Dequeue();
                return true;
            }

            if (low.Count > 0)
            {
                reaction = low.Dequeue();
                return true;
            }

            reaction = default;
            return false;
        }

        private bool CheckValid(IMagicEffectReceiver target)
        {
            return target != null && (IsValidTarget == null || IsValidTarget(target));
        }

        /// <summary>The priority of the same effect of the same event already waiting for the target, or -1.</summary>
        private int WaitingPriority(IMagicEffectReceiver target, MagicEffectKind kind, int eventId)
        {
            // An effect without an identity is never a duplicate of anything.
            if (eventId == 0)
            {
                return -1;
            }

            if (IsWaitingIn(critical, target, kind, eventId)) return (int)ReactionPriority.Critical;
            if (IsWaitingIn(normal, target, kind, eventId)) return (int)ReactionPriority.Normal;
            if (IsWaitingIn(low, target, kind, eventId)) return (int)ReactionPriority.Low;
            return -1;
        }

        /// <summary>Takes out the waiting copies of an effect that are less urgent than <paramref name="than"/>, and reports them.</summary>
        private void RemoveWaiting(IMagicEffectReceiver target, MagicEffectKind kind, int eventId, ReactionPriority than)
        {
            givenUp.Clear();
            if (than > ReactionPriority.Normal)
            {
                RemoveFrom(normal, target, kind, eventId);
            }

            if (than > ReactionPriority.Low)
            {
                RemoveFrom(low, target, kind, eventId);
            }

            statistics.Duplicates += givenUp.Count;
            ReportGivenUp(EnqueueResult.Duplicate);
        }

        private void RemoveFrom(Queue<QueuedReaction> queue, IMagicEffectReceiver target, MagicEffectKind kind, int eventId)
        {
            int count = queue.Count;
            scratch.Clear();
            for (int i = 0; i < count; i++)
            {
                QueuedReaction waiting = queue.Dequeue();
                if (waiting.Effect.EventId == eventId && waiting.Effect.Kind == kind && ReferenceEquals(waiting.Target, target))
                {
                    givenUp.Add(waiting);
                }
                else
                {
                    scratch.Enqueue(waiting);
                }
            }

            while (scratch.Count > 0)
            {
                queue.Enqueue(scratch.Dequeue());
            }
        }

        /// <summary>Reports what was taken out, once the queues are whole again, so a listener may safely offer more.</summary>
        private void ReportGivenUp(EnqueueResult reason)
        {
            for (int i = 0; i < givenUp.Count; i++)
            {
                Refused?.Invoke(givenUp[i], reason);
            }

            givenUp.Clear();
        }

        private static bool IsWaitingIn(Queue<QueuedReaction> queue, IMagicEffectReceiver target, MagicEffectKind kind, int eventId)
        {
            foreach (QueuedReaction waiting in queue)
            {
                if (waiting.Effect.EventId == eventId && waiting.Effect.Kind == kind && ReferenceEquals(waiting.Target, target))
                {
                    return true;
                }
            }

            return false;
        }

        private int CancelIn(Queue<QueuedReaction> queue, IMagicEffectReceiver target)
        {
            int count = queue.Count;
            int removed = 0;
            scratch.Clear();

            for (int i = 0; i < count; i++)
            {
                QueuedReaction waiting = queue.Dequeue();
                if (ReferenceEquals(waiting.Target, target))
                {
                    removed++;
                    givenUp.Add(waiting);
                }
                else
                {
                    scratch.Enqueue(waiting);
                }
            }

            while (scratch.Count > 0)
            {
                queue.Enqueue(scratch.Dequeue());
            }

            return removed;
        }

        private EnqueueResult Refuse(QueuedReaction reaction, EnqueueResult reason)
        {
            Refused?.Invoke(reaction, reason);
            return reason;
        }
    }

    /// <summary>Something whose temporary state needs time to pass: it cools, burns, glides, drains.</summary>
    public interface ISimulated
    {
        /// <summary>Advances the object by the given time.</summary>
        void Simulate(float deltaTime);

        /// <summary>True once nothing more will change by itself, so the object can leave the simulation.</summary>
        bool IsSimulationAtRest { get; }

        /// <summary>A puzzle depends on it: it is simulated even when the budget is used up.</summary>
        bool IsSimulationCritical { get; }
    }

    /// <summary>
    /// The one place where time passes for objects with temporary state. An object joins when
    /// something starts to change on it and leaves when it has come to rest, so nothing has an update
    /// of its own and an idle world costs nothing.
    ///
    /// At most a set number are simulated at once. Others wait, in the order they asked, and their
    /// state stands still until a place is free; nothing is lost. Critical objects never wait.
    /// Objects are advanced in the order they joined, which is the same every time. Plain C#.
    /// </summary>
    public sealed class SimulationSet
    {
        private readonly List<ISimulated> active = new List<ISimulated>(32);
        private readonly List<ISimulated> waiting = new List<ISimulated>(8);
        private bool ticking;

        public SimulationSet(ReactionBudgets budgets = null)
        {
            Budgets = budgets ?? new ReactionBudgets();
        }

        public ReactionBudgets Budgets { get; }

        public int ActiveCount { get; private set; }

        public int WaitingCount => waiting.Count;

        /// <summary>The most that were simulated at one time.</summary>
        public int PeakActive { get; private set; }

        /// <summary>How often an object had to wait because the budget was used up.</summary>
        public long DeferredCount { get; private set; }

        public bool IsIdle => ActiveCount == 0 && waiting.Count == 0;

        /// <summary>Asks for the object to be simulated. Returns false if it has to wait for a free place.</summary>
        public bool Wake(ISimulated item)
        {
            if (item == null)
            {
                return false;
            }

            if (active.Contains(item))
            {
                return true;
            }

            if (ActiveCount >= Budgets.maxActiveObjects && !item.IsSimulationCritical)
            {
                if (!waiting.Contains(item))
                {
                    waiting.Add(item);
                    DeferredCount++;
                }

                return false;
            }

            waiting.Remove(item);
            active.Add(item);
            ActiveCount++;
            PeakActive = Mathf.Max(PeakActive, ActiveCount);
            return true;
        }

        public bool IsActive(ISimulated item)
        {
            return item != null && active.Contains(item);
        }

        /// <summary>Takes an object out, for example because it was destroyed or unloaded. Safe during a tick.</summary>
        public void Remove(ISimulated item)
        {
            if (item == null)
            {
                return;
            }

            waiting.Remove(item);
            int index = active.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            ActiveCount--;
            if (ticking)
            {
                // The list is being walked: leave a gap and close it afterwards.
                active[index] = null;
            }
            else
            {
                active.RemoveAt(index);
            }
        }

        /// <summary>Advances every simulated object, lets those that have come to rest go, and admits waiting ones.</summary>
        public void Tick(float deltaTime)
        {
            if (ticking)
            {
                return;
            }

            ticking = true;
            try
            {
                // By index and only up to the count at the start: objects that join during the tick are
                // advanced from the next tick on.
                int count = active.Count;
                for (int i = 0; i < count; i++)
                {
                    ISimulated item = active[i];
                    if (item == null)
                    {
                        continue;
                    }

                    item.Simulate(deltaTime);

                    // It may have removed itself, or been removed, during its own step.
                    if (active[i] == item && item.IsSimulationAtRest)
                    {
                        active[i] = null;
                        ActiveCount--;
                    }
                }
            }
            finally
            {
                ticking = false;
            }

            active.RemoveAll(IsGap);

            // Free places go to those that have waited longest.
            while (waiting.Count > 0 && ActiveCount < Budgets.maxActiveObjects)
            {
                ISimulated next = waiting[0];
                waiting.RemoveAt(0);
                if (next != null && !next.IsSimulationAtRest)
                {
                    active.Add(next);
                    ActiveCount++;
                    PeakActive = Mathf.Max(PeakActive, ActiveCount);
                }
            }
        }

        public void Clear()
        {
            active.Clear();
            waiting.Clear();
            ActiveCount = 0;
        }

        private static readonly Predicate<ISimulated> IsGap = item => item == null;
    }
}
