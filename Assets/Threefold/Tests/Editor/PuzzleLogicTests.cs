using NUnit.Framework;
using Threefold.Combat;
using Threefold.Puzzles;

namespace Threefold.Tests
{
    /// <summary>
    /// The puzzle rules as plain C#: sequence progress, timers, debouncing, door movement, gates,
    /// spell and switch decisions, and the session memory. None of these needs the Unity engine.
    /// </summary>
    public sealed class PuzzleLogicTests
    {
        private const int Lookout = 0;
        private const int Crystal = 1;
        private const int Plate = 2;

        /// <summary>The Sealed Gateway's rules: lookout and crystal held until the next step, plate latched.</summary>
        private static SequenceLogic GatewaySequence()
        {
            return new SequenceLogic(new[] { StepRule.HoldUntilNextStep, StepRule.HoldUntilNextStep, StepRule.Latch });
        }

        [TearDown]
        public void TearDown()
        {
            PuzzleSessionState.Clear();
        }

        // ------------------------------------------------------------ sequence

        [Test]
        public void StepsInOrderSolveTheSequence()
        {
            SequenceLogic logic = GatewaySequence();

            Assert.AreEqual(SequenceChange.Progressed, logic.SetStepActive(Lookout, true));
            Assert.AreEqual(SequenceChange.Progressed, logic.SetStepActive(Crystal, true));
            Assert.AreEqual(1 + 1, logic.CompletedSteps);

            // Leaving the lookout after the crystal is struck is allowed: the crystal confirmed it.
            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(Lookout, false));
            Assert.AreEqual(2, logic.CompletedSteps);

            Assert.AreEqual(SequenceChange.Solved, logic.SetStepActive(Plate, true));
            Assert.IsTrue(logic.IsSolved);
            Assert.AreEqual(3, logic.CompletedSteps);
        }

        [Test]
        public void StepOutOfOrderDoesNotCount()
        {
            SequenceLogic logic = GatewaySequence();

            Assert.AreEqual(SequenceChange.WrongOrder, logic.SetStepActive(Plate, true));
            Assert.AreEqual(0, logic.CompletedSteps);
            Assert.IsFalse(logic.IsSolved);
        }

        [Test]
        public void StepAlreadyActiveWhenItsTurnComesMustBeActivatedAgain()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Plate, true);
            logic.SetStepActive(Lookout, true);
            logic.SetStepActive(Crystal, true);

            Assert.IsFalse(logic.IsSolved, "Standing on the plate before the crystal was lit must not count.");

            logic.SetStepActive(Plate, false);
            Assert.AreEqual(SequenceChange.Solved, logic.SetStepActive(Plate, true));
        }

        [Test]
        public void ReportingTheSameStateTwiceChangesNothing()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Lookout, true);

            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(Lookout, true));
            Assert.AreEqual(1, logic.CompletedSteps);
        }

        [Test]
        public void HeldStepReleasedTooEarlyLosesProgress()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Lookout, true);

            Assert.AreEqual(SequenceChange.ProgressLost, logic.SetStepActive(Lookout, false));
            Assert.AreEqual(0, logic.CompletedSteps);
        }

        [Test]
        public void CrystalFadingAfterLeavingTheLookoutLosesBothSteps()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Lookout, true);
            logic.SetStepActive(Crystal, true);
            logic.SetStepActive(Lookout, false);

            // The timer runs out and puts the crystal out.
            Assert.AreEqual(SequenceChange.ProgressLost, logic.SetStepActive(Crystal, false));

            Assert.AreEqual(0, logic.CompletedSteps, "The lookout is no longer confirmed and nobody stands on it.");
        }

        [Test]
        public void CrystalFadingWhileStillOnTheLookoutKeepsTheLookoutStep()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Lookout, true);
            logic.SetStepActive(Crystal, true);

            logic.SetStepActive(Crystal, false);

            Assert.AreEqual(1, logic.CompletedSteps);
            Assert.AreEqual(SequenceChange.Progressed, logic.SetStepActive(Crystal, true), "Striking it again counts.");
        }

        [Test]
        public void LatchedStepsSurviveTheirElementSwitchingOff()
        {
            var logic = new SequenceLogic(new[] { StepRule.Latch, StepRule.Latch });
            logic.SetStepActive(0, true);

            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(0, false));
            Assert.AreEqual(1, logic.CompletedSteps);
        }

        [Test]
        public void SolvedSequenceIgnoresFurtherChanges()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Lookout, true);
            logic.SetStepActive(Crystal, true);
            logic.SetStepActive(Plate, true);

            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(Crystal, false));
            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(Plate, false));
            Assert.IsTrue(logic.IsSolved);
            Assert.AreEqual(3, logic.CompletedSteps);
        }

        [Test]
        public void ResetClearsProgressButRemembersElementStates()
        {
            SequenceLogic logic = GatewaySequence();
            logic.SetStepActive(Lookout, true);
            logic.Reset();

            Assert.AreEqual(0, logic.CompletedSteps);
            Assert.IsTrue(logic.IsStepActive(Lookout));
            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(Lookout, true), "A step must be activated again after a reset.");
        }

        [Test]
        public void ForceSolvedMarksEveryStepDone()
        {
            SequenceLogic logic = GatewaySequence();
            logic.ForceSolved();

            Assert.IsTrue(logic.IsSolved);
            Assert.AreEqual(3, logic.CompletedSteps);
        }

        [Test]
        public void SequenceWithoutStepsCannotBeSolved()
        {
            var logic = new SequenceLogic(new StepRule[0]);
            logic.ForceSolved();

            Assert.IsFalse(logic.IsSolved);
            Assert.AreEqual(SequenceChange.None, logic.SetStepActive(0, true));
        }

        /// <summary>The whole example puzzle, played through the rules with the timer, including a failed attempt.</summary>
        [Test]
        public void GatewayPuzzleWithTimerFailsWhenTooSlowAndSucceedsWhenQuick()
        {
            SequenceLogic logic = GatewaySequence();
            var timer = new CountdownTimer();

            // First attempt: climb, strike the crystal, then dawdle.
            logic.SetStepActive(Lookout, true);
            logic.SetStepActive(Crystal, true);
            timer.Start(6f);
            logic.SetStepActive(Lookout, false);

            bool expired = false;
            for (int frame = 0; frame < 7 * 30 && !expired; frame++)
            {
                expired = timer.Tick(1f / 30f);
            }

            Assert.IsTrue(expired);
            logic.SetStepActive(Crystal, false);
            logic.SetStepActive(Plate, true);
            Assert.IsFalse(logic.IsSolved, "The plate pressed after the charge faded must not open the gateway.");
            Assert.AreEqual(0, logic.CompletedSteps);

            // Second attempt, quicker.
            logic.SetStepActive(Plate, false);
            logic.SetStepActive(Lookout, true);
            logic.SetStepActive(Crystal, true);
            timer.Start(6f);
            logic.SetStepActive(Lookout, false);
            Assert.IsFalse(timer.Tick(3f));
            Assert.AreEqual(SequenceChange.Solved, logic.SetStepActive(Plate, true));
        }

        // ------------------------------------------------------------- timer

        [Test]
        public void CountdownExpiresExactlyOnce()
        {
            var timer = new CountdownTimer();
            timer.Start(1f);

            Assert.IsFalse(timer.Tick(0.6f));
            Assert.IsTrue(timer.IsRunning);
            Assert.IsTrue(timer.Tick(0.6f));
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0f, timer.Remaining);
            Assert.IsFalse(timer.Tick(0.6f));
        }

        [Test]
        public void CountdownReportsTheShareOfTimeLeft()
        {
            var timer = new CountdownTimer();
            timer.Start(4f);
            timer.Tick(1f);

            Assert.AreEqual(0.75f, timer.RemainingFraction, 0.0001f);
            Assert.AreEqual(3f, timer.Remaining, 0.0001f);
        }

        [Test]
        public void StoppedCountdownNeitherRunsNorExpires()
        {
            var timer = new CountdownTimer();
            timer.Start(1f);
            timer.Stop();

            Assert.IsFalse(timer.Tick(5f));
            Assert.AreEqual(1f, timer.Remaining, 0.0001f);
        }

        [TestCase(1f, 8)]
        [TestCase(0.99f, 8)]
        [TestCase(0.875f, 7)]
        [TestCase(0.5f, 4)]
        [TestCase(0.13f, 2)]
        [TestCase(0.01f, 1)]
        public void IndicatorStonesGoOutOneByOne(float fractionLeft, int expectedLit)
        {
            var timer = new CountdownTimer();
            timer.Start(10f);
            timer.Tick(10f * (1f - fractionLeft));

            Assert.AreEqual(expectedLit, timer.SegmentsLit(8));
        }

        [Test]
        public void NoStonesAreLitWhenTheTimerIsIdle()
        {
            var timer = new CountdownTimer();
            Assert.AreEqual(0, timer.SegmentsLit(8));
        }

        // ---------------------------------------------------------- presence

        [Test]
        public void PresenceSwitchesOnAfterTheActivationDelay()
        {
            var debounce = new PresenceDebounce();

            Assert.IsFalse(debounce.Update(true, 0.05f, 0.1f, 0.2f));
            Assert.IsTrue(debounce.Update(true, 0.06f, 0.1f, 0.2f));
        }

        [Test]
        public void PresenceWithoutDelaySwitchesOnAtOnce()
        {
            var debounce = new PresenceDebounce();
            Assert.IsTrue(debounce.Update(true, 0.016f, 0f, 0.2f));
        }

        [Test]
        public void BriefGapsDoNotReleaseAPlate()
        {
            var debounce = new PresenceDebounce();
            debounce.Update(true, 0.02f, 0f, 0.15f);

            // The character's collider flickers at the edge: alternately seen and not seen, ending seen.
            for (int i = 0; i < 21; i++)
            {
                Assert.IsTrue(debounce.Update(i % 2 == 0, 0.05f, 0f, 0.15f));
            }

            Assert.IsTrue(debounce.Update(false, 0.1f, 0f, 0.15f));
            Assert.IsFalse(debounce.Update(false, 0.1f, 0f, 0.15f));
        }

        // -------------------------------------------------------------- door

        [Test]
        public void DoorOpensInItsOpeningTime()
        {
            var motion = new DoorMotion { Target = true };

            Assert.IsFalse(motion.Tick(1f, 2f, 1f));
            Assert.AreEqual(0.5f, motion.Progress, 0.0001f);
            Assert.IsTrue(motion.Tick(1f, 2f, 1f));
            Assert.AreEqual(1f, motion.Progress);
            Assert.IsFalse(motion.IsMoving);
        }

        [Test]
        public void DoorReversesFromWhereItIs()
        {
            var motion = new DoorMotion { Target = true };
            motion.Tick(1f, 4f, 2f);
            motion.Target = false;

            Assert.IsFalse(motion.Tick(0.25f, 4f, 2f));
            Assert.AreEqual(0.125f, motion.Progress, 0.0001f);
        }

        [Test]
        public void DoorSnapsToItsTarget()
        {
            var motion = new DoorMotion { Target = true };
            motion.Snap();

            Assert.AreEqual(1f, motion.Progress);
            Assert.IsFalse(motion.IsMoving);
        }

        [Test]
        public void EasingStartsAndEndsInPlace()
        {
            Assert.AreEqual(0f, DoorMotion.Ease(0f));
            Assert.AreEqual(1f, DoorMotion.Ease(1f));
            Assert.AreEqual(0.5f, DoorMotion.Ease(0.5f), 0.0001f);
            Assert.Less(DoorMotion.Ease(0.1f), 0.1f, "Starts slowly.");
            Assert.Greater(DoorMotion.Ease(0.9f), 0.9f, "Ends slowly.");
        }

        // ------------------------------------------------------------- gates

        [TestCase(GateMode.All, 2, 2, true)]
        [TestCase(GateMode.All, 1, 2, false)]
        [TestCase(GateMode.Any, 1, 2, true)]
        [TestCase(GateMode.Any, 0, 2, false)]
        [TestCase(GateMode.All, 0, 0, false)]
        [TestCase(GateMode.Any, 0, 0, false)]
        public void LogicGateCombinesItsInputs(GateMode mode, int active, int total, bool expected)
        {
            Assert.AreEqual(expected, LogicGateRules.Evaluate(mode, active, total));
        }

        // ------------------------------------------------------------ spells

        [Test]
        public void ArcaneBoltLightsAReceptiveArcaneCrystal()
        {
            Assert.AreEqual(SpellResponse.Activated, SpellActivatorRules.Decide(SpellElement.Arcane, SpellElement.Arcane,
                true, false, false, ActivatorMode.Latch));
        }

        [Test]
        public void WrongKindOfMagicIsRejected()
        {
            Assert.AreEqual(SpellResponse.RejectedElement, SpellActivatorRules.Decide(SpellElement.Fire, SpellElement.Arcane,
                true, false, false, ActivatorMode.Latch));
        }

        [Test]
        public void DormantCrystalFizzles()
        {
            Assert.AreEqual(SpellResponse.RejectedDormant, SpellActivatorRules.Decide(SpellElement.Arcane, SpellElement.Arcane,
                false, false, false, ActivatorMode.Latch));
        }

        [Test]
        public void LitLatchingCrystalIgnoresFurtherBolts()
        {
            Assert.AreEqual(SpellResponse.Ignored, SpellActivatorRules.Decide(SpellElement.Arcane, SpellElement.Arcane,
                true, true, false, ActivatorMode.Latch));
        }

        [Test]
        public void ToggleCrystalSwitchesOffWhenStruckAgain()
        {
            Assert.AreEqual(SpellResponse.Deactivated, SpellActivatorRules.Decide(SpellElement.Arcane, SpellElement.Arcane,
                true, true, false, ActivatorMode.Toggle));
        }

        [Test]
        public void LockedCrystalIgnoresEverything()
        {
            Assert.AreEqual(SpellResponse.Ignored, SpellActivatorRules.Decide(SpellElement.Arcane, SpellElement.Fire,
                false, false, true, ActivatorMode.Toggle));
        }

        [Test]
        public void ActivatorCanAcceptSeveralKindsOfMagic()
        {
            const SpellElement accepted = SpellElement.Arcane | SpellElement.Frost;

            Assert.IsTrue(SpellElements.Matches(accepted, SpellElement.Frost));
            Assert.IsTrue(SpellElements.Matches(accepted, SpellElement.Arcane));
            Assert.IsFalse(SpellElements.Matches(accepted, SpellElement.Fire));
            Assert.IsFalse(SpellElements.Matches(accepted, SpellElement.None), "A spell without a kind matches nothing.");
        }

        // ---------------------------------------------------------- switches

        [Test]
        public void ToggleSwitchAlternates()
        {
            Assert.AreEqual(SwitchResponse.TurnedOn, SwitchRules.Use(SwitchMode.Toggle, false, false, true));
            Assert.AreEqual(SwitchResponse.TurnedOff, SwitchRules.Use(SwitchMode.Toggle, true, false, true));
        }

        [Test]
        public void OneShotSwitchWorksOnce()
        {
            Assert.AreEqual(SwitchResponse.TurnedOn, SwitchRules.Use(SwitchMode.OneShot, false, false, true));
            Assert.AreEqual(SwitchResponse.Ignored, SwitchRules.Use(SwitchMode.OneShot, true, false, true));
        }

        [Test]
        public void SwitchWithUnmetConditionRefuses()
        {
            Assert.AreEqual(SwitchResponse.Rejected, SwitchRules.Use(SwitchMode.Toggle, false, false, false));
            Assert.AreEqual(SwitchResponse.Ignored, SwitchRules.Use(SwitchMode.Toggle, false, true, false), "Locked wins.");
        }

        // ----------------------------------------------------- session memory

        [Test]
        public void SessionStateRemembersAndForgets()
        {
            PuzzleSessionState.Set("Test.Gate", new PuzzleRecord(true, true));

            Assert.IsTrue(PuzzleSessionState.TryGet("Test.Gate", out PuzzleRecord record));
            Assert.IsTrue(record.Active);
            Assert.IsTrue(record.Locked);

            Assert.IsTrue(PuzzleSessionState.Remove("Test.Gate"));
            Assert.IsFalse(PuzzleSessionState.TryGet("Test.Gate", out _));
        }

        [Test]
        public void SessionStateIgnoresEmptyKeys()
        {
            PuzzleSessionState.Set(string.Empty, new PuzzleRecord(true, false));
            PuzzleSessionState.Set(null, new PuzzleRecord(true, false));

            Assert.AreEqual(0, PuzzleSessionState.Count);
            Assert.IsFalse(PuzzleSessionState.TryGet(null, out _));
        }

        [Test]
        public void ClearForgetsEverything()
        {
            PuzzleSessionState.Set("Test.A", new PuzzleRecord(true, false));
            PuzzleSessionState.Set("Test.B", new PuzzleRecord(false, true));
            PuzzleSessionState.Clear();

            Assert.AreEqual(0, PuzzleSessionState.Count);
        }

        // ------------------------------------------------------------ sounds

        [TestCase(PuzzleTone.Chime)]
        [TestCase(PuzzleTone.Fade)]
        [TestCase(PuzzleTone.Fizzle)]
        [TestCase(PuzzleTone.Rumble)]
        [TestCase(PuzzleTone.Fanfare)]
        [TestCase(PuzzleTone.Click)]
        [TestCase(PuzzleTone.Tick)]
        public void GeneratedSoundsAreInRangeAndStartAndEndSilently(PuzzleTone tone)
        {
            float[] samples = PuzzleSounds.Synthesize(tone);

            Assert.Greater(samples.Length, PuzzleSounds.SampleRate / 50, "Too short to be heard.");
            Assert.Less(samples.Length, PuzzleSounds.SampleRate * 4, "Longer than any placeholder should be.");

            float peak = 0f;
            foreach (float sample in samples)
            {
                Assert.IsFalse(float.IsNaN(sample));
                peak = System.Math.Max(peak, System.Math.Abs(sample));
            }

            Assert.LessOrEqual(peak, 1f);
            Assert.Greater(peak, 0.5f, "Normalised to a clear level.");
            Assert.AreEqual(0f, samples[0], 0.0001f, "Starts without a click.");
            Assert.AreEqual(0f, samples[samples.Length - 1], 0.0001f, "Ends without a click.");
        }

        [Test]
        public void NoneHasNoSound()
        {
            Assert.AreEqual(0, PuzzleSounds.Synthesize(PuzzleTone.None).Length);
        }
    }
}
