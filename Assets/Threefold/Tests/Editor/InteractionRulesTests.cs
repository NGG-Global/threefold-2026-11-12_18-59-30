using NUnit.Framework;
using Threefold.Interaction;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The choice of what the player can interact with, with the Interactor's default reach: 2 m,
    /// 75 degrees either side, anything within 0.9 m regardless of angle, from 0.6 m below the feet
    /// to 2.4 m above them.
    /// </summary>
    public sealed class InteractionRulesTests
    {
        private static readonly InteractionReach Reach = new InteractionReach(2f, 75f, 0.9f, -0.6f, 2.4f);
        private static readonly Vector3 Feet = Vector3.zero;
        private static readonly Vector3 Forward = Vector3.forward;

        private static bool Score(Vector3 point, out float score)
        {
            return InteractionRules.TryScore(Feet, Forward, point, Reach, out score);
        }

        [Test]
        public void ThingInFrontWithinReachIsOffered()
        {
            Assert.IsTrue(Score(new Vector3(0f, 1f, 1.5f), out _));
        }

        [Test]
        public void ThingBeyondReachIsNotOffered()
        {
            Assert.IsFalse(Score(new Vector3(0f, 1f, 2.2f), out _));
        }

        [Test]
        public void ThingBehindIsNotOfferedUnlessVeryClose()
        {
            Assert.IsFalse(Score(new Vector3(0f, 1f, -1.5f), out _));
            Assert.IsTrue(Score(new Vector3(0f, 1f, -0.6f), out _), "Something right beside the character can be used.");
        }

        [Test]
        public void ThingFarAboveOrBelowIsNotOffered()
        {
            Assert.IsFalse(Score(new Vector3(0f, 2.8f, 1f), out _));
            Assert.IsFalse(Score(new Vector3(0f, -1f, 1f), out _));
        }

        [Test]
        public void NearerThingIsPreferred()
        {
            Score(new Vector3(0f, 1f, 0.8f), out float near);
            Score(new Vector3(0f, 1f, 1.8f), out float far);

            Assert.Less(near, far);
        }

        [Test]
        public void ThingStraightAheadIsPreferredOverOneAtTheSameDistanceToTheSide()
        {
            Score(new Vector3(0f, 1f, 1.5f), out float ahead);
            Score(new Vector3(1.2f, 1f, 0.9f), out float aside);

            Assert.Less(ahead, aside);
        }

        [Test]
        public void HeightDoesNotChangeTheDistance()
        {
            Score(new Vector3(0f, 0f, 1.5f), out float low);
            Score(new Vector3(0f, 2f, 1.5f), out float high);

            Assert.AreEqual(low, high, 0.0001f);
        }
    }
}
