using System.Collections.Generic;

namespace Threefold.CombatAnimation
{
    /// <summary>The moments of an attack that presentation can act on.</summary>
    public enum CombatCueKind
    {
        /// <summary>The action began: its anticipation is showing.</summary>
        Windup,

        /// <summary>The moment the attack's visual effect should appear.</summary>
        Effect,

        /// <summary>The strike's fast, dangerous part begins. Always followed by <see cref="HitWindowClosed"/>.</summary>
        HitWindowOpened,

        /// <summary>A melee strike lands. The rules resolved the hit at this instant.</summary>
        Contact,

        /// <summary>A charged attack leaves the hand. The rules fired it at this instant.</summary>
        Release,

        /// <summary>The strike's dangerous part is over; recovery is under way.</summary>
        HitWindowClosed,

        /// <summary>The action is over after its contact or release. Terminal.</summary>
        Recovered,

        /// <summary>The action ended before its contact or release; nothing landed. Terminal.</summary>
        Cancelled,
    }

    /// <summary>The kind of action a cue belongs to.</summary>
    public enum CombatActionKind
    {
        Melee,
        Charged,
    }

    /// <summary>One moment of one action.</summary>
    public readonly struct CombatCue
    {
        public readonly CombatCueKind Kind;

        /// <summary>Identifies the action; every cue of one action carries the same number, and no two actions share one.</summary>
        public readonly int ActionId;

        public readonly CombatActionKind Action;

        /// <summary>The discipline, as <see cref="Threefold.Magic.MagicDiscipline"/>.</summary>
        public readonly int Discipline;

        /// <summary>The melee combo step, or 0.</summary>
        public readonly int StepIndex;

        /// <summary>The hand or foot the action strikes or releases with.</summary>
        public readonly CombatEffector Effector;

        /// <summary>Game time of the moment.</summary>
        public readonly float Time;

        public CombatCue(CombatCueKind kind, int actionId, CombatActionKind action, int discipline, int stepIndex,
            CombatEffector effector, float time)
        {
            Kind = kind;
            ActionId = actionId;
            Action = action;
            Discipline = discipline;
            StepIndex = stepIndex;
            Effector = effector;
            Time = time;
        }

        public bool IsTerminal => Kind == CombatCueKind.Recovered || Kind == CombatCueKind.Cancelled;
    }

    /// <summary>
    /// Turns the timing of one action at a time into cues, with guarantees that hold however the action
    /// ends:
    /// - every cue of an action is sent at most once, in time order;
    /// - the anchor (<see cref="CombatCueKind.Contact"/> or <see cref="CombatCueKind.Release"/>) comes from the
    ///   combat rules, not from a timer, and is accepted once; a repeat, or one for an older action, is ignored;
    /// - cues scheduled before the anchor that have not been sent when it arrives are sent first, in order;
    /// - every action ends with exactly one terminal cue: <see cref="CombatCueKind.Cancelled"/> if it ended before
    ///   its anchor, <see cref="CombatCueKind.Recovered"/> after it; nothing of that action follows;
    /// - an opened hit window is always closed, before the terminal cue at the latest.
    ///
    /// No damage is ever dealt from a cue: the rules have already resolved the hit when Contact is sent.
    /// Plain C#, allocation-free, tested outside Unity.
    /// </summary>
    public sealed class CombatCueTracker
    {
        private const int Capacity = 8;

        private readonly CombatCueKind[] pendingKinds = new CombatCueKind[Capacity];
        private readonly float[] pendingTimes = new float[Capacity];
        private int pendingCount;

        private int actionId;
        private bool active;
        private bool anchored;
        private bool windowOpen;
        private bool effectSent;
        private CombatActionKind action;
        private int discipline;
        private int stepIndex;
        private CombatEffector effector;

        /// <summary>The action in progress, or 0.</summary>
        public int CurrentActionId => active ? actionId : 0;

        public bool IsActive => active;

        public bool IsAnchored => active && anchored;

        public CombatActionKind CurrentAction => action;

        /// <summary>
        /// Starts an action and sends its <see cref="CombatCueKind.Windup"/>. An action still in progress is ended
        /// first, with its terminal cue. Returns the new action's id.
        /// </summary>
        public int Begin(CombatActionKind kind, int disciplineIndex, int step, CombatEffector hand, float now,
            List<CombatCue> output)
        {
            End(now, output);

            actionId++;
            active = true;
            anchored = false;
            windowOpen = false;
            effectSent = false;
            pendingCount = 0;
            action = kind;
            discipline = disciplineIndex;
            stepIndex = step;
            effector = hand;

            Send(CombatCueKind.Windup, now, output);
            return actionId;
        }

        /// <summary>
        /// Schedules a cue of the action in progress at an absolute game time. Terminal cues and anchors cannot
        /// be scheduled before the anchor; <see cref="CombatCueKind.Recovered"/> can be scheduled after it.
        /// </summary>
        public void Schedule(int id, CombatCueKind kind, float time)
        {
            if (!active || id != actionId || pendingCount >= Capacity)
            {
                return;
            }

            if (kind == CombatCueKind.Windup || kind == CombatCueKind.Contact || kind == CombatCueKind.Release ||
                kind == CombatCueKind.Cancelled || (kind == CombatCueKind.Recovered && !anchored))
            {
                return;
            }

            // Insertion keeps time order; equal times keep the order they were scheduled in.
            int index = pendingCount;
            while (index > 0 && pendingTimes[index - 1] > time)
            {
                pendingKinds[index] = pendingKinds[index - 1];
                pendingTimes[index] = pendingTimes[index - 1];
                index--;
            }

            pendingKinds[index] = kind;
            pendingTimes[index] = time;
            pendingCount++;
        }

        /// <summary>
        /// The rules resolved the action's hit or fired its release. Sends what was still waiting before it, then
        /// the anchor. Returns false, and sends nothing, for a repeat or for an action that is not in progress.
        /// </summary>
        public bool Anchor(int id, CombatCueKind kind, float now, List<CombatCue> output)
        {
            if (!active || id != actionId || anchored || (kind != CombatCueKind.Contact && kind != CombatCueKind.Release))
            {
                return false;
            }

            for (int i = 0; i < pendingCount; i++)
            {
                Send(pendingKinds[i], now, output);
            }

            pendingCount = 0;
            anchored = true;
            Send(kind, now, output);
            return true;
        }

        /// <summary>Sends every scheduled cue whose time has come, in order.</summary>
        public void Tick(float now, List<CombatCue> output)
        {
            int sent = 0;
            while (active && sent < pendingCount && pendingTimes[sent] <= now)
            {
                CombatCueKind kind = pendingKinds[sent];
                sent++;
                Send(kind, now, output);
            }

            if (!active)
            {
                pendingCount = 0;
                return;
            }

            if (sent > 0)
            {
                for (int i = sent; i < pendingCount; i++)
                {
                    pendingKinds[i - sent] = pendingKinds[i];
                    pendingTimes[i - sent] = pendingTimes[i];
                }

                pendingCount -= sent;
            }
        }

        /// <summary>
        /// Ends the action in progress now: <see cref="CombatCueKind.Cancelled"/> before its anchor,
        /// <see cref="CombatCueKind.Recovered"/> after it. Nothing scheduled for it is sent afterwards.
        /// </summary>
        public void End(float now, List<CombatCue> output)
        {
            if (!active)
            {
                return;
            }

            pendingCount = 0;
            Send(anchored ? CombatCueKind.Recovered : CombatCueKind.Cancelled, now, output);
        }

        /// <summary>Forgets the action in progress without sending anything. For when presentation is switched off.</summary>
        public void Reset()
        {
            active = false;
            anchored = false;
            windowOpen = false;
            pendingCount = 0;
        }

        private void Send(CombatCueKind kind, float now, List<CombatCue> output)
        {
            switch (kind)
            {
                case CombatCueKind.HitWindowOpened:
                    if (windowOpen)
                    {
                        return;
                    }

                    windowOpen = true;
                    break;

                case CombatCueKind.HitWindowClosed:
                    if (!windowOpen)
                    {
                        return;
                    }

                    windowOpen = false;
                    break;

                case CombatCueKind.Effect:
                    if (effectSent)
                    {
                        return;
                    }

                    effectSent = true;
                    break;

                case CombatCueKind.Recovered:
                case CombatCueKind.Cancelled:
                    if (windowOpen)
                    {
                        Send(CombatCueKind.HitWindowClosed, now, output);
                    }

                    output?.Add(new CombatCue(kind, actionId, action, discipline, stepIndex, effector, now));
                    active = false;
                    pendingCount = 0;
                    return;
            }

            output?.Add(new CombatCue(kind, actionId, action, discipline, stepIndex, effector, now));
        }
    }
}
