using System;
using Threefold.Combat;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A countdown, as plain C# so it can be tested without Unity. Time is passed in, never read.
    /// </summary>
    public struct CountdownTimer
    {
        private float duration;
        private float remaining;
        private bool running;

        public bool IsRunning => running;
        public float Duration => duration;
        public float Remaining => remaining;

        /// <summary>Time left as a fraction of the duration: 1 at the start, 0 when it ends.</summary>
        public float RemainingFraction => duration > 0f ? remaining / duration : 0f;

        public void Start(float seconds)
        {
            duration = Math.Max(0.01f, seconds);
            remaining = duration;
            running = true;
        }

        /// <summary>Stops without expiring. The remaining time is kept.</summary>
        public void Stop()
        {
            running = false;
        }

        /// <summary>Advances the countdown.</summary>
        /// <returns>True only on the call in which the time runs out.</returns>
        public bool Tick(float deltaTime)
        {
            if (!running || deltaTime <= 0f)
            {
                return false;
            }

            remaining -= deltaTime;
            if (remaining > 0f)
            {
                return false;
            }

            remaining = 0f;
            running = false;
            return true;
        }

        /// <summary>
        /// How many of <paramref name="segments"/> indicator lights stay lit while running: all of
        /// them at the start, one fewer each time a share of the time has passed, none at the end.
        /// </summary>
        public int SegmentsLit(int segments)
        {
            if (!running || segments <= 0)
            {
                return 0;
            }

            int lit = (int)Math.Ceiling(RemainingFraction * segments - 0.0001f);
            return Math.Max(0, Math.Min(segments, lit));
        }
    }

    /// <summary>
    /// Smooths a raw "something is there" signal so a character's collider wobbling at the edge of
    /// a plate does not make it flicker: a change counts only after it has lasted a moment.
    /// </summary>
    public struct PresenceDebounce
    {
        private bool state;
        private float pending;

        public bool State => state;

        /// <param name="detected">Whether something is there this frame.</param>
        /// <param name="activationDelay">Seconds something must stay before the state switches on.</param>
        /// <param name="releaseDelay">Seconds it must stay away before the state switches off.</param>
        /// <returns>The smoothed state.</returns>
        public bool Update(bool detected, float deltaTime, float activationDelay, float releaseDelay)
        {
            if (detected == state)
            {
                pending = 0f;
                return state;
            }

            pending += Math.Max(0f, deltaTime);
            if (pending >= (detected ? activationDelay : releaseDelay))
            {
                state = detected;
                pending = 0f;
            }

            return state;
        }

        public void Reset(bool value)
        {
            state = value;
            pending = 0f;
        }
    }

    /// <summary>The movement of a door between closed (0) and open (1), as plain C#.</summary>
    public struct DoorMotion
    {
        private float progress;
        private bool target;

        /// <summary>0 when closed, 1 when open, linear in time.</summary>
        public float Progress => progress;

        /// <summary>True to move towards open.</summary>
        public bool Target
        {
            get => target;
            set => target = value;
        }

        public bool IsMoving => target ? progress < 1f : progress > 0f;

        /// <summary>Moves towards the target.</summary>
        /// <returns>True only on the call in which the target is reached.</returns>
        public bool Tick(float deltaTime, float openSeconds, float closeSeconds)
        {
            if (!IsMoving || deltaTime <= 0f)
            {
                return false;
            }

            float change = target ? deltaTime / Math.Max(0.01f, openSeconds) : -deltaTime / Math.Max(0.01f, closeSeconds);
            progress = Math.Max(0f, Math.Min(1f, progress + change));
            return !IsMoving;
        }

        /// <summary>Jumps to the target without moving.</summary>
        public void Snap()
        {
            progress = target ? 1f : 0f;
        }

        /// <summary>Starts and stops gently, like something heavy.</summary>
        public static float Ease(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }
    }

    public enum GateMode
    {
        /// <summary>Active while every input is active.</summary>
        All,

        /// <summary>Active while at least one input is active.</summary>
        Any,
    }

    public static class LogicGateRules
    {
        public static bool Evaluate(GateMode mode, int activeCount, int inputCount)
        {
            if (inputCount <= 0)
            {
                return false;
            }

            return mode == GateMode.All ? activeCount >= inputCount : activeCount > 0;
        }
    }

    public enum ActivatorMode
    {
        /// <summary>A matching spell switches it on; it stays on until something resets it.</summary>
        Latch,

        /// <summary>Each matching spell switches it on or off.</summary>
        Toggle,
    }

    /// <summary>How a magical activator answered a spell.</summary>
    public enum SpellResponse
    {
        /// <summary>Nothing happened, for example because it is already on or locked.</summary>
        Ignored,
        Activated,
        Deactivated,

        /// <summary>The wrong kind of magic.</summary>
        RejectedElement,

        /// <summary>The right magic, but the activator is not receptive yet.</summary>
        RejectedDormant,
    }

    public static class SpellActivatorRules
    {
        public static SpellResponse Decide(SpellElement accepted, SpellElement element, bool receptive, bool isActive,
            bool isLocked, ActivatorMode mode)
        {
            if (isLocked)
            {
                return SpellResponse.Ignored;
            }

            if (!SpellElements.Matches(accepted, element))
            {
                return SpellResponse.RejectedElement;
            }

            if (!receptive)
            {
                return SpellResponse.RejectedDormant;
            }

            if (!isActive)
            {
                return SpellResponse.Activated;
            }

            return mode == ActivatorMode.Toggle ? SpellResponse.Deactivated : SpellResponse.Ignored;
        }
    }

    public enum SwitchMode
    {
        /// <summary>Each use switches it on or off.</summary>
        Toggle,

        /// <summary>It can be used once, for a chest or a seal.</summary>
        OneShot,
    }

    public enum SwitchResponse
    {
        Ignored,
        TurnedOn,
        TurnedOff,

        /// <summary>Its condition is not met: it rattles but does not move.</summary>
        Rejected,
    }

    public static class SwitchRules
    {
        public static SwitchResponse Use(SwitchMode mode, bool isOn, bool isLocked, bool conditionMet)
        {
            if (isLocked)
            {
                return SwitchResponse.Ignored;
            }

            if (!conditionMet)
            {
                return SwitchResponse.Rejected;
            }

            if (mode == SwitchMode.OneShot)
            {
                return isOn ? SwitchResponse.Ignored : SwitchResponse.TurnedOn;
            }

            return isOn ? SwitchResponse.TurnedOff : SwitchResponse.TurnedOn;
        }
    }
}
