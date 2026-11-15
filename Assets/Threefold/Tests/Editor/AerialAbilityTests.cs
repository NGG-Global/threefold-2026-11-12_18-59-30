using System;
using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.InputHandling;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The discipline aerial abilities (<see cref="AerialAbilityModel"/>): when they can start, how often, how they
    /// move the Mage, how they end, and how they meet combat. Flights are run with <see cref="AerialSimulator"/>,
    /// which uses the motor's own gravity step. Plain C#, run outside Unity by Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class AerialAbilityTests
    {
        private const float Frame = 1f / 60f;
        private static readonly float[] FrameRates = { 15f, 30f, 60f, 120f };
        private readonly List<AerialEvent> events = new List<AerialEvent>();
        private AerialAbilityModel model;
        private AerialMotion motion;

        [SetUp]
        public void SetUp()
        {
            model = new AerialAbilityModel(null, null, null, null);
            events.Clear();
        }

        private static AerialFrameInput Air(AerialAbilityKind selected, bool press = false, bool held = false,
            float verticalSpeed = -1f, float dt = Frame)
        {
            return new AerialFrameInput
            {
                DeltaTime = dt,
                Grounded = false,
                VerticalSpeed = verticalSpeed,
                Gravity = 28f,
                JumpPressed = press,
                JumpHeld = held || press,
                Selected = selected,
                Facing = new Vector3(0f, 0f, 1f),
            };
        }

        private static AerialFrameInput Ground(AerialAbilityKind selected, bool press = false)
        {
            AerialFrameInput input = Air(selected, press, press, 0f);
            input.Grounded = true;
            return input;
        }

        private void Tick(AerialFrameInput input)
        {
            model.Tick(input, ref motion, events);
        }

        /// <summary>Stands, presses Jump on the ground, and is in the air the next frame, the motor having jumped.</summary>
        private void JumpFromGround(AerialAbilityKind selected)
        {
            Launch(selected);
            Assert.IsFalse(firstPressConsumed, "the first jump is the motor's own");
        }

        private bool firstPressConsumed;

        /// <summary>The same, without assertions: NUnit allocates, which the allocation test must not count.</summary>
        private void Launch(AerialAbilityKind selected)
        {
            Tick(Ground(selected));
            Tick(Ground(selected));
            Tick(Ground(selected, press: true));
            firstPressConsumed = motion.ConsumeJumpPress;
            AerialFrameInput leaving = Air(selected, held: true, verticalSpeed: 9f);
            leaving.JumpedFromGround = true;
            Tick(leaving);
        }

        private int Count(AerialEventKind kind)
        {
            int count = 0;
            foreach (AerialEvent e in events)
            {
                if (e.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static AerialRunResult Fly(AerialAbilityKind ability, float fps, float start = 0f, float landing = 0f,
            float wall = 0f, float holdAbility = 0.2f)
        {
            var simulated = new AerialAbilityModel(null, null, null, null);
            return AerialSimulator.Run(JumpFigures.Defaults, simulated, new AerialRunScript
            {
                Ability = ability,
                FirstHoldSeconds = 0.5f,
                AbilityAtApex = true,
                AbilityHoldSeconds = holdAbility,
                StartHeight = start,
                LandingHeight = landing,
                WallDistance = wall,
                FramesPerSecond = fps,
            });
        }

        // --------------------------------------------------------- shared rules

        [Test]
        public void TheFirstJumpIsTheMotorsOwnAndArmsTheAbility()
        {
            Assert.IsFalse(model.IsArmed);
            JumpFromGround(AerialAbilityKind.Dash);
            Assert.IsTrue(model.IsArmed);
            Assert.AreEqual(0, Count(AerialEventKind.Activated));
            Assert.AreEqual(1, model.UsesLeft);
        }

        [Test]
        public void HoldingTheFirstJumpStartsNothing()
        {
            JumpFromGround(AerialAbilityKind.Glide);
            for (int i = 0; i < 90; i++)
            {
                Tick(Air(AerialAbilityKind.Glide, held: true, verticalSpeed: 6f - i * 0.3f));
            }

            Assert.AreEqual(0, Count(AerialEventKind.Activated), "the hold of the ground jump is not a new press");
        }

        [Test]
        public void FallingOffAnEdgeGivesNoAbility()
        {
            Tick(Ground(AerialAbilityKind.Ascension));
            Tick(Ground(AerialAbilityKind.Ascension));
            for (int i = 0; i < 10; i++)
            {
                Tick(Air(AerialAbilityKind.Ascension));
            }

            Tick(Air(AerialAbilityKind.Ascension, press: true));
            Assert.AreEqual(0, Count(AerialEventKind.Activated));
            Assert.IsFalse(motion.ConsumeJumpPress, "the press is left to the motor (a coyote jump, or a buffered one)");
        }

        [Test]
        public void OneActivationPerAirborneSequenceHoweverOftenJumpIsPressed()
        {
            JumpFromGround(AerialAbilityKind.Ascension);
            int consumed = 0;
            for (int i = 0; i < 40; i++)
            {
                // Spam: a fresh press every other frame.
                Tick(Air(AerialAbilityKind.Ascension, press: i % 2 == 0, verticalSpeed: 2f - i * 0.4f));
                consumed += motion.ConsumeJumpPress ? 1 : 0;
            }

            Assert.AreEqual(1, Count(AerialEventKind.Activated));
            Assert.AreEqual(1, consumed, "only the press that started it is taken from the motor");
            Assert.AreEqual(0, model.UsesLeft);
        }

        [Test]
        public void SwitchingDisciplinesInTheAirNeverGivesAnotherUse()
        {
            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            for (int i = 0; i < 20; i++)
            {
                Tick(Air(AerialAbilityKind.Dash));
            }

            var kinds = new[] { AerialAbilityKind.Ascension, AerialAbilityKind.Glide, AerialAbilityKind.Dash };
            for (int i = 0; i < 60; i++)
            {
                // A different discipline every frame, pressing every third frame.
                Tick(Air(kinds[i % 3], press: i % 3 == 0));
            }

            Assert.AreEqual(1, Count(AerialEventKind.Activated));
            Assert.AreEqual(0, Count(AerialEventKind.Resumed), "the glide was never started, so nothing resumes");
        }

        [Test]
        public void AnAbilityInProgressFinishesAsTheOneItBeganAs()
        {
            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            Assert.AreEqual(AerialAbilityKind.Dash, model.Active);
            Tick(Air(AerialAbilityKind.Glide, held: true));
            Assert.AreEqual(AerialAbilityKind.Dash, model.Active, "switching to Flame mid-dash does not turn it into a glide");
        }

        [Test]
        public void AOneFrameTouchOfGroundOrAWallDoesNotGiveTheAbilityBack()
        {
            JumpFromGround(AerialAbilityKind.Ascension);
            Tick(Air(AerialAbilityKind.Ascension, press: true));
            Assert.AreEqual(0, model.UsesLeft);

            // Brushing a ledge top for one frame, and running along a wall.
            Tick(Ground(AerialAbilityKind.Ascension));
            Tick(Air(AerialAbilityKind.Ascension));
            AerialFrameInput wall = Air(AerialAbilityKind.Ascension);
            wall.HitWall = true;
            wall.WallNormal = new Vector3(-1f, 0f, 0f);
            for (int i = 0; i < 10; i++)
            {
                Tick(wall);
            }

            Assert.AreEqual(0, model.UsesLeft);
            Tick(Air(AerialAbilityKind.Ascension, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Activated));
        }

        [Test]
        public void AConfirmedLandingGivesTheAbilityBackForTheNextJump()
        {
            JumpFromGround(AerialAbilityKind.Ascension);
            Tick(Air(AerialAbilityKind.Ascension, press: true));
            Tick(Ground(AerialAbilityKind.Ascension));
            Tick(Ground(AerialAbilityKind.Ascension));
            Tick(Ground(AerialAbilityKind.Ascension));
            Assert.AreEqual(1, model.UsesLeft);
            Assert.IsFalse(model.IsArmed, "usable again only after the next jump");

            JumpFromGround(AerialAbilityKind.Ascension);
            Tick(Air(AerialAbilityKind.Ascension, press: true));
            Assert.AreEqual(2, Count(AerialEventKind.Activated));
        }

        [Test]
        public void AJumpOffALedgeDoesNotGiveASpentAbilityBack()
        {
            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            for (int i = 0; i < 20; i++)
            {
                Tick(Air(AerialAbilityKind.Dash));
            }

            AerialFrameInput leap = Air(AerialAbilityKind.Dash, verticalSpeed: 5f);
            leap.LaunchedFromLedge = true;
            Tick(leap);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Activated));
        }

        [Test]
        public void ThePressOfAJumpOffALedgeIsThatJumpsOnly()
        {
            // Caught a ledge without using the ability, then jumped off it backwards with one press.
            JumpFromGround(AerialAbilityKind.Dash);
            AerialFrameInput leap = Air(AerialAbilityKind.Dash, press: true, verticalSpeed: 5f);
            leap.LaunchedFromLedge = true;
            Tick(leap);
            Assert.AreEqual(0, Count(AerialEventKind.Activated), "the back jump keeps its arc");
            Assert.IsFalse(motion.ConsumeJumpPress);

            Tick(Air(AerialAbilityKind.Dash, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Activated), "a new press afterwards starts the dash");
        }

        [Test]
        public void APressJustBeforeLandingIsLeftToTheJumpBuffer()
        {
            JumpFromGround(AerialAbilityKind.Ascension);
            AerialFrameInput nearGround = Air(AerialAbilityKind.Ascension, press: true, verticalSpeed: -8f);
            nearGround.LandingImminent = true;
            Tick(nearGround);
            Assert.AreEqual(0, Count(AerialEventKind.Activated));
            Assert.IsFalse(motion.ConsumeJumpPress, "the motor jumps on landing, as before");
            Assert.AreEqual(1, model.UsesLeft);
        }

        [Test]
        public void ADashThatMeetsTheGroundEndsAndKeepsOnlyItsExitSpeed()
        {
            DashTuning tuning = AerialAbilityDefaults.Dash();
            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            Tick(Ground(AerialAbilityKind.Dash));
            Assert.AreEqual(AerialState.Idle, model.State);
            Assert.IsTrue(events.Exists(e => e.Kind == AerialEventKind.Ended && e.Reason == AerialEndReason.Landed));
            Assert.IsTrue(motion.HasStoredHorizontal, "clamped on the landing frame");
            Assert.AreEqual(tuning.exitSpeed, motion.StoredHorizontal.magnitude, 0.001f);
            Assert.IsFalse(motion.HasDashVelocity, "no dash along the ground");
        }

        [Test]
        public void AJumpFromTheGroundDuringADashDoesNotCarryItsSpeed()
        {
            DashTuning tuning = AerialAbilityDefaults.Dash();
            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            AerialFrameInput jumped = Air(AerialAbilityKind.Dash, held: true, verticalSpeed: 9f);
            jumped.JumpedFromGround = true;
            Tick(jumped);
            Assert.AreEqual(AerialState.Idle, model.State);
            Tick(Air(AerialAbilityKind.Dash, held: true, verticalSpeed: 8f));
            Assert.IsTrue(motion.HasStoredHorizontal);
            Assert.LessOrEqual(motion.StoredHorizontal.magnitude, tuning.exitSpeed + 0.001f);
        }

        // ----------------------------------------------------------- Ascension

        [Test]
        public void AscensionClimbsWellAboveAJumpAtEveryFrameRate()
        {
            foreach (float fps in FrameRates)
            {
                AerialRunResult jump = Fly(AerialAbilityKind.None, fps);
                AerialRunResult ascension = Fly(AerialAbilityKind.Ascension, fps);
                Assert.AreEqual(1, ascension.Activations);
                Assert.Greater(ascension.PeakHeight, jump.PeakHeight * 2.5f, $"{fps} fps: noticeably more height");

                // The ascension itself adds its configured height, whatever the frame rate.
                Assert.AreEqual(3f, ascension.PeakHeight - jump.PeakHeight, 0.12f, $"{fps} fps");
            }
        }

        [Test]
        public void AscensionGivesOneImpulseWhileJumpIsHeld()
        {
            JumpFromGround(AerialAbilityKind.Ascension);
            Tick(Air(AerialAbilityKind.Ascension, press: true, verticalSpeed: 0f));
            int impulses = 0;
            float speed = 0f;
            for (int i = 0; i < 120; i++)
            {
                Tick(Air(AerialAbilityKind.Ascension, held: true, verticalSpeed: speed));
                if (motion.SetsVerticalSpeed && motion.VerticalSpeed > 1f)
                {
                    impulses++;
                    speed = motion.VerticalSpeed;
                }
                else
                {
                    speed -= 28f * Frame;
                }
            }

            Assert.AreEqual(1, impulses);
            Assert.AreEqual(AerialState.Idle, model.State, "over once it stops rising: no flight");
        }

        [Test]
        public void AscensionKeepsSteeringAndIsMostlyVertical()
        {
            JumpFromGround(AerialAbilityKind.Ascension);
            Tick(Air(AerialAbilityKind.Ascension, press: true, verticalSpeed: 0f));
            Assert.IsTrue(motion.ScalesHorizontalVelocity);
            Assert.AreEqual(AerialAbilityDefaults.Ascension().horizontalSpeedKept, motion.HorizontalScale, 0.0001f);
            for (int i = 0; i < 30; i++)
            {
                Tick(Air(AerialAbilityKind.Ascension, held: true, verticalSpeed: 8f));
                Assert.IsFalse(motion.OverridesAirMovement || motion.HasStoredHorizontal, "normal air steering");
            }
        }

        // --------------------------------------------------------------- Glide

        [Test]
        public void TheGlideCapsTheDescentQuicklyAndNeverClimbs()
        {
            GlideTuning tuning = AerialAbilityDefaults.Glide();
            float speed = -15f;
            float seconds = 0f;
            while (speed < -tuning.maxFallSpeed - 0.001f)
            {
                speed = AerialAbilityModel.GlideVerticalStep(speed, 28f, Frame, tuning);
                seconds += Frame;
            }

            Assert.Less(seconds, 0.35f, "braked to the cap in a fraction of a second: responsive, not floaty");

            for (int i = 0; i < 120; i++)
            {
                speed = AerialAbilityModel.GlideVerticalStep(speed, 28f, Frame, tuning);
                Assert.GreaterOrEqual(speed, -tuning.maxFallSpeed - 0.0001f);
            }

            float rising = 4f;
            float next = AerialAbilityModel.GlideVerticalStep(rising, 28f, Frame, tuning);
            Assert.LessOrEqual(next, rising - 28f * Frame + 0.0001f, "rising, it slows at least as under gravity");
        }

        [Test]
        public void TheGlideCarriesMuchFartherThanAJumpAndSteers()
        {
            foreach (float fps in FrameRates)
            {
                AerialRunResult jump = Fly(AerialAbilityKind.None, fps, 4.2f, 1f);
                AerialRunResult glide = Fly(AerialAbilityKind.Glide, fps, 4.2f, 1f, holdAbility: 10f);
                Assert.Greater(glide.Distance, jump.Distance * 2f, $"{fps} fps");
            }

            JumpFromGround(AerialAbilityKind.Glide);
            Tick(Air(AerialAbilityKind.Glide, press: true));
            Assert.IsTrue(motion.OverridesAirMovement, "the joystick steers the glide");
            Assert.AreEqual(AerialAbilityDefaults.Glide().speed, motion.AirTopSpeed, 0.0001f);
        }

        [Test]
        public void ReleasingKeepsTheFuelAndAPressResumesIt()
        {
            JumpFromGround(AerialAbilityKind.Glide);
            Tick(Air(AerialAbilityKind.Glide, press: true));
            for (int i = 0; i < 30; i++)
            {
                Tick(Air(AerialAbilityKind.Glide, held: true));
            }

            float left = model.FuelSeconds;
            Tick(Air(AerialAbilityKind.Glide));
            Assert.AreEqual(1, Count(AerialEventKind.Paused));
            for (int i = 0; i < 20; i++)
            {
                Tick(Air(AerialAbilityKind.Glide));
                Assert.IsFalse(motion.SetsVerticalSpeed, "normal gravity while released");
            }

            Assert.AreEqual(left, model.FuelSeconds, 0.0001f, "a release does not restore or spend fuel");

            Tick(Air(AerialAbilityKind.Glide, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Resumed));
            Assert.AreEqual(1, Count(AerialEventKind.Activated), "resuming is not a new activation");
            Assert.IsTrue(motion.ConsumeJumpPress);
            Assert.AreEqual(AerialState.Gliding, model.State);
        }

        [Test]
        public void ResumingNeedsFlameSelected()
        {
            JumpFromGround(AerialAbilityKind.Glide);
            Tick(Air(AerialAbilityKind.Glide, press: true));
            Tick(Air(AerialAbilityKind.Glide));
            Tick(Air(AerialAbilityKind.Ascension, press: true));
            Tick(Air(AerialAbilityKind.Dash, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Activated));
            Assert.AreEqual(0, Count(AerialEventKind.Resumed));
            Tick(Air(AerialAbilityKind.Glide, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Resumed));
        }

        [Test]
        public void TheFuelRunsOutAndNormalGravityReturns()
        {
            GlideTuning tuning = AerialAbilityDefaults.Glide();
            JumpFromGround(AerialAbilityKind.Glide);
            Tick(Air(AerialAbilityKind.Glide, press: true));
            int frames = 1;
            while (model.State == AerialState.Gliding && frames < 1000)
            {
                Tick(Air(AerialAbilityKind.Glide, held: true));
                frames++;
            }

            Assert.AreEqual(tuning.fuelSeconds, frames * Frame, Frame * 1.01f, "it lasts its fuel budget");
            Assert.AreEqual(0f, model.FuelSeconds);
            Assert.IsTrue(events.Exists(e => e.Kind == AerialEventKind.Ended && e.Reason == AerialEndReason.FuelSpent));

            Tick(Air(AerialAbilityKind.Glide));
            Tick(Air(AerialAbilityKind.Glide, press: true));
            Assert.AreEqual(AerialState.Idle, model.State, "no propulsion without fuel");
            Assert.IsFalse(motion.SetsVerticalSpeed);
        }

        [Test]
        public void TheFuelBudgetIsTheSameAtAnyFrameRate()
        {
            foreach (float fps in FrameRates)
            {
                SetUp();
                float dt = 1f / fps;
                JumpFromGround(AerialAbilityKind.Glide);
                Tick(Air(AerialAbilityKind.Glide, press: true, dt: dt));
                float seconds = dt;
                while (model.State == AerialState.Gliding && seconds < 10f)
                {
                    Tick(Air(AerialAbilityKind.Glide, held: true, dt: dt));
                    seconds += dt;
                }

                Assert.AreEqual(AerialAbilityDefaults.Glide().fuelSeconds, seconds, dt * 1.01f, $"{fps} fps");
            }
        }

        // ---------------------------------------------------------------- Dash

        [Test]
        public void ADashCoversItsDistanceAtEveryFrameRate()
        {
            DashTuning tuning = AerialAbilityDefaults.Dash();
            foreach (float fps in FrameRates)
            {
                SetUp();
                float dt = 1f / fps;
                JumpFromGround(AerialAbilityKind.Dash);
                Tick(Air(AerialAbilityKind.Dash, press: true, dt: dt));
                float distance = motion.DashVelocity.z * dt;
                while (model.State == AerialState.Dashing)
                {
                    Tick(Air(AerialAbilityKind.Dash, dt: dt));
                    distance += motion.HasDashVelocity ? motion.DashVelocity.z * dt : 0f;
                }

                Assert.AreEqual(tuning.distance, distance, 0.01f, $"{fps} fps");
                Assert.LessOrEqual(Mathf.Abs(motion.VerticalSpeed), Mathf.Abs(tuning.verticalSpeed) + 0.0001f, "minimal lift");
            }

            foreach (float fps in FrameRates)
            {
                AerialRunResult jump = Fly(AerialAbilityKind.None, fps);
                AerialRunResult dash = Fly(AerialAbilityKind.Dash, fps);
                Assert.Greater(dash.Distance - jump.Distance, tuning.distance * 0.8f, $"{fps} fps: a jump and a dash");
            }
        }

        [Test]
        public void ADashGoesWhereTheJoystickPointsOrWhereTheMageFaces()
        {
            Vector3 facing = new Vector3(0f, 0f, 1f);
            Vector3 right = new Vector3(1f, 0f, 0f);
            Assert.AreEqual(right, AerialAbilityModel.DashDirection(right * 0.6f, facing, 0.3f));
            Assert.AreEqual(facing, AerialAbilityModel.DashDirection(right * 0.2f, facing, 0.3f), "a slight touch is ignored");
            Assert.AreEqual(facing, AerialAbilityModel.DashDirection(Vector3.zero, facing, 0.3f));
            Assert.AreEqual(0f, AerialAbilityModel.DashDirection(new Vector3(0.5f, 0.8f, 0.5f), facing, 0.3f).y, "flat");
        }

        [Test]
        public void AWallStopsADashHeadOnAndTurnsAGlancingOne()
        {
            Assert.IsTrue(AerialAbilityModel.WallStopsDash(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f), 50f));
            Assert.IsFalse(AerialAbilityModel.WallStopsDash(new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, -0.2f).normalized, 50f), "a glancing wall");

            foreach (float fps in FrameRates)
            {
                AerialRunResult stopped = Fly(AerialAbilityKind.Dash, fps, wall: 3.5f);
                Assert.IsTrue(stopped.StoppedByWall, $"{fps} fps");
                Assert.LessOrEqual(stopped.Distance, 3.5f + 0.0001f, "never through the wall");
            }

            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            AerialFrameInput glancing = Air(AerialAbilityKind.Dash);
            glancing.HitWall = true;
            glancing.WallNormal = new Vector3(-1f, 0f, -0.2f).normalized;
            Tick(glancing);
            Assert.AreEqual(AerialState.Dashing, model.State, "turned along the wall, still dashing");
            Assert.Greater(Mathf.Abs(motion.DashVelocity.normalized.x) + motion.DashVelocity.normalized.z, 0f);
            Assert.LessOrEqual(Vector3.Dot(motion.DashVelocity, glancing.WallNormal), 0.0001f, "no speed into the wall");
        }

        [Test]
        public void ADashNeverStacksSpeed()
        {
            DashTuning tuning = AerialAbilityDefaults.Dash();
            foreach (float fps in FrameRates)
            {
                AerialRunResult dash = Fly(AerialAbilityKind.Dash, fps);
                Assert.LessOrEqual(dash.FastestHorizontalSpeed, tuning.Speed + 0.001f, $"{fps} fps");
            }

            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            while (model.State == AerialState.Dashing)
            {
                Tick(Air(AerialAbilityKind.Dash, press: true));
            }

            Assert.IsTrue(motion.HasStoredHorizontal);
            Assert.AreEqual(tuning.exitSpeed, motion.StoredHorizontal.magnitude, 0.001f, "set outright, not added");
            Tick(Air(AerialAbilityKind.Dash, press: true));
            Assert.AreEqual(1, Count(AerialEventKind.Activated), "no second dash, however Jump is pressed");
        }

        // -------------------------------------------------------------- combat

        [Test]
        public void NothingStartsWhileChargingBlockingStaggeredOrClimbing()
        {
            foreach (int blocker in new[] { 0, 1, 2, 3, 4 })
            {
                SetUp();
                JumpFromGround(AerialAbilityKind.Dash);
                AerialFrameInput input = Air(AerialAbilityKind.Dash, press: true);
                input.Charging = blocker == 0;
                input.Blocking = blocker == 1;
                input.Staggered = blocker == 2;
                input.Defeated = blocker == 3;
                input.Traversing = blocker == 4;
                Tick(input);
                Assert.AreEqual(0, Count(AerialEventKind.Activated), $"blocker {blocker}");
                Assert.IsFalse(motion.ConsumeJumpPress);
                Assert.AreEqual(1, model.UsesLeft, "the use is not lost");
            }
        }

        [Test]
        public void AChargeOrAHitPausesTheGlideAndKeepsItsFuel()
        {
            JumpFromGround(AerialAbilityKind.Glide);
            Tick(Air(AerialAbilityKind.Glide, press: true));
            Tick(Air(AerialAbilityKind.Glide, held: true));
            AerialFrameInput charging = Air(AerialAbilityKind.Glide, held: true);
            charging.Charging = true;
            Tick(charging);
            Assert.AreEqual(1, Count(AerialEventKind.Paused));
            Assert.Greater(model.FuelSeconds, 0f);
            Assert.IsTrue(model.CanResumeGlide);
        }

        [Test]
        public void TheHandsAreBusyWhileAnAbilityUsesThem()
        {
            JumpFromGround(AerialAbilityKind.Dash);
            Assert.IsFalse(model.HandsBusy);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            Assert.IsTrue(model.HandsBusy);
            while (model.State == AerialState.Dashing)
            {
                Tick(Air(AerialAbilityKind.Dash));
            }

            Assert.IsFalse(model.HandsBusy, "attacks are possible again after the dash");
        }

        [Test]
        public void AnAerialAbilityAbandonsASwingThatHasNotLanded()
        {
            var combat = new DisciplineCombatModel(new CombatData(), new CombatData());
            var gesture = new AttackGestureRecognizer(new AttackGestureSettings());
            var commands = new List<CombatCommand>();
            var log = new List<CombatCommand>();
            float time = 0f;
            int slot = (int)MagicDiscipline.Aegis;

            void CombatFrame(Action press, bool handsBusy)
            {
                time += Frame;
                gesture.BeginFrame();
                press?.Invoke();
                gesture.Update(time);
                commands.Clear();
                combat.Tick(new CombatFrameInput
                {
                    Gesture = gesture.Snapshot(), HandsBusy = handsBusy, Alive = true, DisciplineSlot = slot,
                }, time, commands);
                log.AddRange(commands);
            }

            // A tap starts the Aegis palm, whose start-up is long; the dash begins before it lands.
            CombatFrame(() => gesture.Press(time, Vector2.zero), false);
            CombatFrame(null, false);
            CombatFrame(() => gesture.Release(time, false), false);
            Assert.IsTrue(log.Exists(c => c.Kind == CombatCommandKind.MeleeStepStarted));

            JumpFromGround(AerialAbilityKind.Dash);
            Tick(Air(AerialAbilityKind.Dash, press: true));
            for (int i = 0; i < 60; i++)
            {
                CombatFrame(null, model.HandsBusy);
                Tick(Air(AerialAbilityKind.Dash));
            }

            Assert.IsTrue(log.Exists(c => c.Kind == CombatCommandKind.MeleeInterrupted), "the swing is abandoned");
            Assert.IsFalse(log.Exists(c => c.Kind == CombatCommandKind.MeleeHit), "and deals nothing");
        }

        private sealed class CombatData : IDisciplineCombatData, IChargedAttackGate
        {
            public int Count => 3;
            public MeleeComboProfile GetMelee(int slot) => DisciplineDefaults.Melee((MagicDiscipline)slot);
            public ChargeProfile GetCharge(int slot) => DisciplineDefaults.Charged((MagicDiscipline)slot).Charge;
            public float GetGuardPulseSeconds(int slot) => 0f;
            public CastResult Check(int slot) => CastResult.Success;
        }

        // ---------------------------------------------------------- frame rate

        [Test]
        public void EveryAbilityBehavesTheSameAtLowFrameRates()
        {
            AerialRunResult ascension60 = Fly(AerialAbilityKind.Ascension, 60f);
            AerialRunResult glide60 = Fly(AerialAbilityKind.Glide, 60f, 4.2f, 1f, holdAbility: 10f);
            AerialRunResult dash60 = Fly(AerialAbilityKind.Dash, 60f);
            AerialRunResult jump60 = Fly(AerialAbilityKind.None, 60f);

            foreach (float fps in new[] { 15f, 20f, 30f })
            {
                AerialRunResult jump = Fly(AerialAbilityKind.None, fps);
                AerialRunResult ascension = Fly(AerialAbilityKind.Ascension, fps);
                AerialRunResult glide = Fly(AerialAbilityKind.Glide, fps, 4.2f, 1f, holdAbility: 10f);
                AerialRunResult dash = Fly(AerialAbilityKind.Dash, fps);

                Assert.AreEqual(1, ascension.Activations, $"{fps} fps");
                Assert.AreEqual(1, glide.Activations, $"{fps} fps");
                Assert.AreEqual(1, dash.Activations, $"{fps} fps");

                // What the ability adds stays within a few per cent; the first jump's own drift is the motor's.
                Assert.AreEqual(ascension60.PeakHeight - jump60.PeakHeight, ascension.PeakHeight - jump.PeakHeight, 0.12f,
                    $"{fps} fps ascension");
                Assert.AreEqual(glide60.Distance, glide.Distance, glide60.Distance * 0.06f, $"{fps} fps glide");
                Assert.AreEqual(dash60.Distance - jump60.Distance, dash.Distance - jump.Distance, 0.6f, $"{fps} fps dash");
            }
        }

        [Test]
        public void SteadyFramesAllocateNothing()
        {
            AllocationMeter.Require();

            var kinds = new[] { AerialAbilityKind.Ascension, AerialAbilityKind.Glide, AerialAbilityKind.Dash };
            events.Capacity = 64;
            for (int warm = 0; warm < 2; warm++)
            {
                AllocationMeter.Begin();
                for (int round = 0; round < 30; round++)
                {
                    events.Clear();
                    AerialAbilityKind kind = kinds[round % 3];
                    Launch(kind);
                    Tick(Air(kind, press: true, verticalSpeed: 0f));
                    for (int f = 0; f < 40; f++)
                    {
                        Tick(Air(kind, held: true, verticalSpeed: 2f - f * 0.3f));
                    }

                    Tick(Ground(kind));
                    Tick(Ground(kind));
                    Tick(Ground(kind));
                }
            }

            long allocated = AllocationMeter.End();
            Assert.AreEqual(0, allocated, "allocated by 30 airborne sequences");
        }
    }
}
