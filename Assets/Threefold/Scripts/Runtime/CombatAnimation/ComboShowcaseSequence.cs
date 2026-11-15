using System.Collections.Generic;
using Threefold.Magic;

namespace Threefold.CombatAnimation
{
    /// <summary>
    /// Plays a discipline's whole melee combo on a loop, through the real combat rules, for the style comparison in the
    /// Discipline Range: it taps the real <see cref="MeleeComboMachine"/> once to start a round and again shortly after
    /// each hit (a buffered tap, as a player chaining the combo would), so every swing starts, lands and recovers with
    /// exactly the timing the game uses. After the last strike has recovered it pauses in the stance (longer than any combo
    /// window, so the next round starts from the first strike), then starts the next round. Plain C# with the time passed
    /// in, tested outside Unity.
    /// </summary>
    public sealed class ComboShowcaseSequence
    {
        private readonly MeleeComboMachine combo = new MeleeComboMachine();
        private readonly List<MeleeEvent> buffer = new List<MeleeEvent>(4);

        private float nextRoundTime;
        private float nextTapTime = float.PositiveInfinity;
        private bool inRound;
        private bool lastStepLanded;

        /// <summary>Seconds of stance between rounds.</summary>
        public float PauseSeconds { get; set; } = 1.2f;

        /// <summary>Seconds after a hit at which the next tap is made, during that strike's recovery.</summary>
        public float TapAfterHitSeconds { get; set; } = 0.05f;

        /// <summary>Rounds completed.</summary>
        public int Round { get; private set; }

        /// <summary>True from the first tap of a round until its last strike has recovered.</summary>
        public bool InRound => inRound;

        public MeleeComboMachine Combo => combo;

        /// <summary>Starts again from the stance, the first round beginning after the pause.</summary>
        public void Restart(float now)
        {
            combo.Reset();
            inRound = false;
            lastStepLanded = false;
            nextTapTime = float.PositiveInfinity;
            nextRoundTime = now + PauseSeconds;
        }

        /// <summary>Advances to the given time and appends what the combo did, in order.</summary>
        public void Tick(float now, MeleeComboProfile profile, List<MeleeEvent> events)
        {
            int count = profile != null ? profile.StepCount : 0;
            if (count == 0)
            {
                return;
            }

            if (!inRound && now >= nextRoundTime)
            {
                inRound = true;
                lastStepLanded = false;
                combo.Request(nextRoundTime);
            }

            if (inRound && now >= nextTapTime)
            {
                combo.Request(nextTapTime);
                nextTapTime = float.PositiveInfinity;
            }

            buffer.Clear();
            combo.Tick(now, profile, buffer);
            for (int i = 0; i < buffer.Count; i++)
            {
                MeleeEvent melee = buffer[i];
                events.Add(melee);
                if (melee.Kind != MeleeEventKind.Hit)
                {
                    continue;
                }

                if (melee.StepIndex < count - 1)
                {
                    nextTapTime = now + TapAfterHitSeconds;
                }
                else
                {
                    lastStepLanded = true;
                }

                // A tap due within this same frame is made now, so a long frame does not lose it.
                if (now >= nextTapTime)
                {
                    combo.Request(nextTapTime);
                    nextTapTime = float.PositiveInfinity;
                }
            }

            if (inRound && lastStepLanded && !combo.IsSwinging)
            {
                inRound = false;
                Round++;
                nextRoundTime = now + PauseSeconds;
            }
        }
    }
}
