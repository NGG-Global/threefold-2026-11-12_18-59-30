using NUnit.Framework;
using Threefold.TouchControls;
using UnityEngine;

namespace Threefold.Tests
{
    public sealed class JoystickMathTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void InputInsideDeadZoneGivesNoMovement()
        {
            Vector2 result = JoystickMath.Process(new Vector2(0.05f, 0.05f), 0.2f, 1f);

            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void FullDeflectionGivesFullSpeed()
        {
            Vector2 result = JoystickMath.Process(new Vector2(0f, 1f), 0.2f, 1.5f);

            Assert.AreEqual(1f, result.magnitude, Tolerance);
            Assert.AreEqual(1f, result.y, Tolerance);
        }

        [Test]
        public void DeflectionBeyondTheEdgeIsClampedToFullSpeed()
        {
            Vector2 result = JoystickMath.Process(new Vector2(3f, 0f), 0.1f, 1f);

            Assert.AreEqual(1f, result.magnitude, Tolerance);
        }

        [Test]
        public void MovementStartsFromZeroAtTheDeadZoneEdge()
        {
            Vector2 result = JoystickMath.Process(new Vector2(0.21f, 0f), 0.2f, 1f);

            Assert.Greater(result.magnitude, 0f);
            Assert.Less(result.magnitude, 0.05f, "There must be no jump in speed when leaving the dead zone.");
        }

        [Test]
        public void LinearResponseIsRemappedAcrossTheRemainingTravel()
        {
            // Half-way between a dead zone of 0.2 and full deflection.
            Vector2 result = JoystickMath.Process(new Vector2(0.6f, 0f), 0.2f, 1f);

            Assert.AreEqual(0.5f, result.x, Tolerance);
        }

        [Test]
        public void DirectionIsPreserved()
        {
            Vector2 raw = new Vector2(-0.3f, 0.4f);

            Vector2 result = JoystickMath.Process(raw, 0.1f, 2f);

            Assert.AreEqual(raw.normalized.x, result.normalized.x, Tolerance);
            Assert.AreEqual(raw.normalized.y, result.normalized.y, Tolerance);
        }

        [Test]
        public void HigherExponentGivesFinerControlAtLowDeflection()
        {
            Vector2 raw = new Vector2(0.5f, 0f);

            float linear = JoystickMath.Process(raw, 0f, 1f).magnitude;
            float curved = JoystickMath.Process(raw, 0f, 2f).magnitude;

            Assert.Less(curved, linear);
        }

        [Test]
        public void OriginStaysPutWhileThumbIsWithinRadius()
        {
            Vector2 origin = new Vector2(100f, 100f);

            Vector2 result = JoystickMath.FollowOrigin(origin, new Vector2(130f, 100f), 50f);

            Assert.AreEqual(origin, result);
        }

        [Test]
        public void OriginFollowsThumbBeyondRadius()
        {
            Vector2 result = JoystickMath.FollowOrigin(new Vector2(100f, 100f), new Vector2(200f, 100f), 50f);

            Assert.AreEqual(150f, result.x, Tolerance);
            Assert.AreEqual(100f, result.y, Tolerance);
        }
    }
}
