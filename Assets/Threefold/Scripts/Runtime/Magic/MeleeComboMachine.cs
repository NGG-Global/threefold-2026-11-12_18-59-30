using System.Collections.Generic;

namespace Threefold.Magic
{
    public enum MeleePhase
    {
        /// <summary>No swing in progress.</summary>
        Idle,

        /// <summary>The swing has started and has not landed yet.</summary>
        Startup,

        /// <summary>The hit has landed; the next swing cannot start yet.</summary>
        Recovery,
    }

    public enum MeleeEventKind
    {
        /// <summary>A swing began. Show it and turn towards the target.</summary>
        StepStarted,

        /// <summary>The swing lands now. Resolve the hit.</summary>
        Hit,

        /// <summary>The combo window passed without a tap. The next tap starts from the first swing.</summary>
        ComboEnded,

        /// <summary>A swing was abandoned before it landed.</summary>
        Interrupted,
    }

    public readonly struct MeleeEvent
    {
        public readonly MeleeEventKind Kind;
        public readonly int StepIndex;
        public readonly MeleeStepProfile Step;

        public MeleeEvent(MeleeEventKind kind, int stepIndex, MeleeStepProfile step)
        {
            Kind = kind;
            StepIndex = stepIndex;
            Step = step;
        }
    }

    /// <summary>
    /// The timing of a melee combo: tap, start-up, hit, recovery, and the window in which another
    /// tap continues the combo. One swing is in progress at a time. A tap made slightly early is
    /// remembered (the input buffer), and so is any tap made after the current swing has hit, so a
    /// heavy swing never swallows the next tap. A second tap right after the one that started a swing
    /// is taken as accidental and ignored (the repeat guard). The profile is passed in on every tick, so
    /// the discipline can change between swings, while a swing that has started always finishes with
    /// the values it started with. Plain C# with the time passed in, tested outside Unity.
    /// </summary>
    public sealed class MeleeComboMachine
    {
        private MeleeStepProfile current;
        private float phaseStart;
        private float swingStart;
        private float hitTime = float.NegativeInfinity;
        private float lastTime;
        private float comboDeadline = float.NegativeInfinity;
        private float requestTime = float.NegativeInfinity;
        private float startingRequest = float.NegativeInfinity;
        private float repeatGuard;
        private int nextIndex;
        private bool comboOpen;
        private bool chainBroken;
        private bool recoveredThisTick;

        public MeleePhase Phase { get; private set; } = MeleePhase.Idle;

        /// <summary>Index of the swing in progress, or of the last one performed.</summary>
        public int StepIndex { get; private set; }

        /// <summary>The swing in progress, or null when idle.</summary>
        public MeleeStepProfile CurrentStep => Phase == MeleePhase.Idle ? null : current;

        public bool IsSwinging => Phase != MeleePhase.Idle;

        /// <summary>True while a tap is waiting for the current swing to finish.</summary>
        public bool HasPendingRequest => !float.IsNegativeInfinity(requestTime);

        /// <summary>
        /// Movement speed the swing in progress allows at the time of the last tick, relative to normal: the
        /// swing's own speed, and the lower committed speed around its hit (see <see cref="MeleeStepProfile.MoveSpeedAt"/>).
        /// </summary>
        public float MoveSpeedMultiplier => Phase == MeleePhase.Idle || current == null ? 1f : current.MoveSpeedAt(lastTime - swingStart);

        /// <summary>Seconds since the swing in progress started, at the last tick, or 0 when idle.</summary>
        public float SwingSeconds => Phase == MeleePhase.Idle ? 0f : System.Math.Max(0f, lastTime - swingStart);

        /// <summary>
        /// The player tapped attack. A tap within the repeat guard of the tap that started the swing in progress
        /// is ignored as accidental.
        /// </summary>
        public void Request(float time)
        {
            if (Phase != MeleePhase.Idle && repeatGuard > 0f && time - startingRequest < repeatGuard)
            {
                return;
            }

            requestTime = time;
        }

        /// <summary>
        /// Abandons a swing that has not landed, forgets a waiting tap and ends the combo. A swing that
        /// has already hit still has to recover: cancelling never shortens the wait before the next swing.
        /// </summary>
        public void Cancel(List<MeleeEvent> events)
        {
            requestTime = float.NegativeInfinity;
            EndCombo();

            if (Phase == MeleePhase.Recovery)
            {
                // The recovery runs on; the swing after it starts the combo afresh.
                chainBroken = true;
                return;
            }

            if (Phase == MeleePhase.Startup && events != null)
            {
                events.Add(new MeleeEvent(MeleeEventKind.Interrupted, StepIndex, current));
            }

            Phase = MeleePhase.Idle;
            chainBroken = false;
        }

        /// <summary>Returns to idle at once and forgets everything, for when combat is switched off.</summary>
        public void Reset()
        {
            Phase = MeleePhase.Idle;
            requestTime = float.NegativeInfinity;
            chainBroken = false;
            EndCombo();
        }

        /// <summary>The next swing starts from the first step. A swing in progress still finishes.</summary>
        public void RestartChain()
        {
            EndCombo();
            chainBroken = IsSwinging;
        }

        /// <summary>Advances to the given time and reports what happened, in order.</summary>
        public void Tick(float time, MeleeComboProfile profile, List<MeleeEvent> events)
        {
            lastTime = time;
            recoveredThisTick = false;

            // A long frame can cover a hit and the start of the next swing; the loop handles each in turn.
            for (int guard = 0; guard < 8; guard++)
            {
                if (Phase == MeleePhase.Startup)
                {
                    if (time - phaseStart < current.startupSeconds)
                    {
                        return;
                    }

                    phaseStart += current.startupSeconds;
                    hitTime = phaseStart;
                    Phase = MeleePhase.Recovery;
                    events.Add(new MeleeEvent(MeleeEventKind.Hit, StepIndex, current));
                    continue;
                }

                if (Phase == MeleePhase.Recovery)
                {
                    if (time - phaseStart < current.recoverySeconds)
                    {
                        return;
                    }

                    phaseStart += current.recoverySeconds;
                    Phase = MeleePhase.Idle;
                    recoveredThisTick = true;
                    comboOpen = true;
                    comboDeadline = phaseStart + current.comboWindowSeconds;
                    nextIndex = chainBroken ? 0 : StepIndex + 1;
                    chainBroken = false;
                    continue;
                }

                // Idle.
                if (comboOpen && time > comboDeadline && !HasUsableRequest(time, profile))
                {
                    EndCombo();
                    events.Add(new MeleeEvent(MeleeEventKind.ComboEnded, StepIndex, null));
                }

                if (!HasUsableRequest(time, profile))
                {
                    requestTime = float.NegativeInfinity;
                    return;
                }

                int count = profile != null ? profile.StepCount : 0;
                if (count == 0)
                {
                    requestTime = float.NegativeInfinity;
                    return;
                }

                // A tap that waited through the recovery continues the combo even if the window is short.
                bool continues = comboOpen && (requestTime <= comboDeadline || time <= comboDeadline);
                int index = continues ? nextIndex : 0;
                if (index >= count)
                {
                    index = 0;
                }

                // The swing starts when it becomes possible, not when the frame happens to run.
                float start = comboOpen && continues ? System.Math.Max(requestTime, phaseStart) : requestTime;
                start = System.Math.Min(start, time);

                StepIndex = index;
                current = profile.GetStep(index);
                startingRequest = requestTime;
                requestTime = float.NegativeInfinity;
                comboOpen = false;

                if (current == null)
                {
                    return;
                }

                Phase = MeleePhase.Startup;
                phaseStart = start;
                swingStart = start;
                repeatGuard = profile.repeatGuardSeconds;
                events.Add(new MeleeEvent(MeleeEventKind.StepStarted, index, current));
            }
        }

        private bool HasUsableRequest(float time, MeleeComboProfile profile)
        {
            if (float.IsNegativeInfinity(requestTime))
            {
                return false;
            }

            // A tap made after the last swing hit waited through its recovery: it is performed when the
            // recovery ends, however long that took.
            if (recoveredThisTick && requestTime >= hitTime)
            {
                return true;
            }

            float buffer = profile != null ? profile.inputBufferSeconds : 0f;
            return time - requestTime <= buffer + 0.0001f;
        }

        private void EndCombo()
        {
            comboOpen = false;
            comboDeadline = float.NegativeInfinity;
            nextIndex = 0;
        }
    }
}
