using NUnit.Framework;
using Threefold.TouchControls;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>The stationary movement joystick: activation area, dead zone, release and sprint.</summary>
    public sealed class FixedJoystickTests
    {
        private FixedJoystickModel stick;

        [SetUp]
        public void SetUp()
        {
            stick = new FixedJoystickModel
            {
                Centre = new Vector2(132f, 118f),
                Radius = 55f,
                ActivationRadius = 105f,
                DeadZone = 0.12f,
                ResponseExponent = 1f,
                SprintThreshold = 1.45f,
                SprintReleaseDeflection = 0.6f,
            };
        }

        [Test]
        public void ATouchOutsideTheActivationRadiusDoesNotTakeTheStick()
        {
            Assert.IsFalse(stick.Begin(stick.Centre + new Vector2(106f, 0f)));
            Assert.IsFalse(stick.IsActive);
            Assert.AreEqual(Vector2.zero, stick.Value);
        }

        [Test]
        public void ATouchInsideTheActivationRadiusTakesTheStick()
        {
            Assert.IsTrue(stick.Begin(stick.Centre + new Vector2(0f, 100f)));
            Assert.IsTrue(stick.IsActive);
        }

        [Test]
        public void TheCentreNeverMoves()
        {
            Vector2 centre = stick.Centre;
            stick.Begin(centre + new Vector2(40f, 40f));
            stick.Move(centre + new Vector2(300f, -200f));
            stick.End();
            Assert.AreEqual(centre, stick.Centre);
        }

        [Test]
        public void ATouchNearTheCentreGivesNoMovement()
        {
            stick.Begin(stick.Centre + new Vector2(5f, 0f));
            Assert.AreEqual(Vector2.zero, stick.Value);
        }

        [Test]
        public void FullTravelGivesFullSpeedAndFurtherIsClamped()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(55f, 0f));
            Assert.AreEqual(1f, stick.Value.magnitude, 0.0001f);

            stick.Move(stick.Centre + new Vector2(70f, 0f));
            Assert.AreEqual(1f, stick.Value.magnitude, 0.0001f);
            Assert.AreEqual(55f, stick.KnobOffset.magnitude, 0.0001f, "The knob stays on the ring.");
        }

        [Test]
        public void MovementIsAnalogue()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(0f, 30f));
            float half = stick.Value.magnitude;
            stick.Move(stick.Centre + new Vector2(0f, 45f));

            Assert.Greater(half, 0.3f);
            Assert.Less(half, 0.7f);
            Assert.Greater(stick.Value.magnitude, half);
        }

        [Test]
        public void TheThumbMayLeaveTheActivationAreaWhileHeld()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(0f, 300f));

            Assert.IsTrue(stick.IsActive);
            Assert.AreEqual(1f, stick.Value.y, 0.0001f);
        }

        [Test]
        public void ReleasingReturnsToRestAtOnce()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(50f, 0f));
            stick.End();

            Assert.IsFalse(stick.IsActive);
            Assert.AreEqual(Vector2.zero, stick.Value);
            Assert.AreEqual(Vector2.zero, stick.KnobOffset);
        }

        [Test]
        public void MovesWithoutAnActiveTouchAreIgnored()
        {
            stick.Move(stick.Centre + new Vector2(50f, 0f));
            Assert.AreEqual(Vector2.zero, stick.Value);
        }

        [Test]
        public void PushingPastTheRingSprints()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(0f, 55f));
            Assert.IsFalse(stick.Sprinting, "Full speed at the ring is a run, not a sprint.");

            stick.Move(stick.Centre + new Vector2(0f, 55f * 1.5f));
            Assert.IsTrue(stick.Sprinting);
        }

        [Test]
        public void SprintContinuesBackAtTheRingAndEndsWhenEasedOff()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(0f, 90f));
            stick.Move(stick.Centre + new Vector2(0f, 50f));
            Assert.IsTrue(stick.Sprinting, "The thumb need not stay stretched past the ring.");

            stick.Move(stick.Centre + new Vector2(0f, 20f));
            Assert.IsFalse(stick.Sprinting);
        }

        [Test]
        public void ReleasingEndsTheSprint()
        {
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(90f, 0f));
            stick.End();
            Assert.IsFalse(stick.Sprinting);
        }

        [Test]
        public void SprintCanBeSwitchedOff()
        {
            stick.SprintThreshold = 0f;
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(200f, 0f));
            Assert.IsFalse(stick.Sprinting);
        }

        [Test]
        public void AdjustingTheDeadZoneChangesWhereMovementStarts()
        {
            stick.DeadZone = 0.4f;
            stick.Begin(stick.Centre);
            stick.Move(stick.Centre + new Vector2(20f, 0f));
            Assert.AreEqual(Vector2.zero, stick.Value);

            stick.DeadZone = 0.1f;
            stick.Move(stick.Centre + new Vector2(20f, 0f));
            Assert.Greater(stick.Value.magnitude, 0f);
        }
    }
}
