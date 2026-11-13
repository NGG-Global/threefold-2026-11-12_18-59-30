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
    /// tap continues the combo. One swing is in progress at a time; a tap made slightly early is
    /// remembered. The profile is passed in on every tick, so the discipline can change between
    /// swings, while a swing that has started always finishes with the values it started with.
    /// Plain C# with the time passed in, tested outside Unity.
    /// </summary>
    public sealed class MeleeComboMachine
    {
        private MeleeStepProfile current;
        private float phaseStart;
        private float comboDeadline = float.NegativeInfinity;
        private float requestTime = float.NegativeInfinity;
        private int nextIndex;
        private bool comboOpen;
        private bool chainBroken;

        public MeleePhase Phase { get; private set; } = MeleePhase.Idle;

        /// <summary>Index of the swing in progress, or of the last one performed.</summary>
        public int StepIndex { get; private set; }

        /// <summary>The swing in progress, or null when idle.</summary>
        public MeleeStepProfile CurrentStep => Phase == MeleePhase.Idle ? null : current;

        public bool IsSwinging => Phase != MeleePhase.Idle;

        /// <summary>True while a tap is waiting for the current swing to finish.</summary>
        public bool HasPendingRequest => !float.IsNegativeInfinity(requestTime);

        /// <summary>Movement speed the swing in progress allows, relative to normal.</summary>
        public float MoveSpeedMultiplier => Phase == MeleePhase.Idle || current == null ? 1f : current.moveSpeedMultiplier;

        /// <summary>The player tapped attack.</summary>
        public void Request(float time)
        {
            requestTime = time;
        }

        /// <summary>
        /// Abandons a swing that has not landed, forgets a waiting tap and ends the combo. A swing in
        /// recovery has already hit; its recovery is cut short.
        /// </summary>
        public void Cancel(List<MeleeEvent> events)
        {
            if (Phase == MeleePhase.Startup && events != null)
            {
                events.Add(new MeleeEvent(MeleeEventKind.Interrupted, StepIndex, current));
            }

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
                requestTime = float.NegativeInfinity;
                comboOpen = false;

                if (current == null)
                {
                    return;
                }

                Phase = MeleePhase.Startup;
                phaseStart = start;
                events.Add(new MeleeEvent(MeleeEventKind.StepStarted, index, current));
            }
        }

        private bool HasUsableRequest(float time, MeleeComboProfile profile)
        {
            if (float.IsNegativeInfinity(requestTime))
            {
                return false;
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
