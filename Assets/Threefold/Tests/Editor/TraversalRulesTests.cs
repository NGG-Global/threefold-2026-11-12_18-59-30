using NUnit.Framework;
using Threefold.Characters;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The decisions behind vaulting, climbing and hanging, with the default tuning of
    /// <see cref="TraversalController"/>: step 0.3 m, vault up to 1.15 m and 1 m deep at 2.5 m/s or more,
    /// climb onto up to 1.25 m from the ground, catch ledges 1.25 to 2.25 m above the feet.
    /// </summary>
    public sealed class TraversalRulesTests
    {
        private const float Step = 0.3f;
        private const float VaultMax = 1.15f;
        private const float MantleMax = 1.25f;
        private const float VaultThickness = 1.0f;
        private const float VaultSpeed = 2.5f;
        private const float GrabMin = 1.25f;
        private const float GrabMax = 2.25f;

        private static ObstacleAction Ground(float height, float thickness, float speed)
        {
            return TraversalRules.ClassifyObstacle(height, thickness, speed, Step, VaultMax, MantleMax, VaultThickness, VaultSpeed);
        }

        [Test]
        public void StepsAreLeftToTheCharacterController()
        {
            Assert.AreEqual(ObstacleAction.None, Ground(0.25f, 1.2f, 4f));
        }

        [Test]
        public void ThinRailIsVaultedAtARun()
        {
            Assert.AreEqual(ObstacleAction.Vault, Ground(0.9f, 0.42f, 4f));
        }

        [Test]
        public void ThinRailIsNotVaultedWhenWalkingSlowly()
        {
            Assert.AreEqual(ObstacleAction.None, Ground(0.9f, 0.42f, 1.5f));
        }

        [Test]
        public void DeepCrateIsClimbedOntoAtAnySpeed()
        {
            Assert.AreEqual(ObstacleAction.Mantle, Ground(1.1f, -1f, 1f));
            Assert.AreEqual(ObstacleAction.Mantle, Ground(1.1f, -1f, 6f));
        }

        [Test]
        public void WaistHighThinWallTooTallToVaultIsClimbed()
        {
            Assert.AreEqual(ObstacleAction.Mantle, Ground(1.2f, 0.4f, 4f));
        }

        [Test]
        public void HighWallCannotBeClimbedFromTheGround()
        {
            Assert.AreEqual(ObstacleAction.TooHigh, Ground(2.4f, -1f, 4f));
        }

        [TestCase(0.2f, ObstacleAction.None)]
        [TestCase(1.0f, ObstacleAction.Mantle)]
        [TestCase(1.25f, ObstacleAction.Hang)]
        [TestCase(2.0f, ObstacleAction.Hang)]
        [TestCase(2.5f, ObstacleAction.None)]
        public void LedgesInTheAirAreClimbedOrCaughtByHeight(float height, ObstacleAction expected)
        {
            Assert.AreEqual(expected, TraversalRules.ClassifyAirLedge(height, Step, GrabMin, GrabMax));
        }

        [Test]
        public void VerticalSurfaceIsAWallAndFloorIsNot()
        {
            Assert.IsTrue(TraversalRules.IsWall(Vector3.right, 25f));
            Assert.IsFalse(TraversalRules.IsWall(Vector3.up, 25f));
            Assert.IsFalse(TraversalRules.IsWall(new Vector3(1f, 1f, 0f).normalized, 25f));
        }

        [Test]
        public void FloorIsWalkableAndSteepSlopeIsNot()
        {
            Assert.IsTrue(TraversalRules.IsWalkable(Vector3.up, 35f));
            Assert.IsFalse(TraversalRules.IsWalkable(new Vector3(1f, 1f, 0f).normalized, 35f));
        }

        [Test]
        public void TowardMeasuresPushIntoAWall()
        {
            Vector3 wallFacingEast = Vector3.right;

            Assert.AreEqual(1f, TraversalRules.Toward(Vector3.left, wallFacingEast), 0.0001f);
            Assert.AreEqual(-1f, TraversalRules.Toward(Vector3.right, wallFacingEast), 0.0001f);
            Assert.AreEqual(0f, TraversalRules.Toward(Vector3.forward, wallFacingEast), 0.0001f);
            Assert.AreEqual(0f, TraversalRules.Toward(Vector3.zero, wallFacingEast), 0.0001f);
        }

        [Test]
        public void HangingBodyIsOutsideTheWallAndBelowTheLedge()
        {
            var corner = new Vector3(-9f, 2.4f, -10f);

            Vector3 feet = TraversalRules.HangPosition(corner, Vector3.right, 0.35f, 0.06f, 2.0f);

            Assert.AreEqual(-9f + 0.41f, feet.x, 0.0001f);
            Assert.AreEqual(0.4f, feet.y, 0.0001f);
            Assert.AreEqual(-10f, feet.z, 0.0001f);
        }

        [Test]
        public void StandingPositionIsOnTopBehindTheEdge()
        {
            var corner = new Vector3(-9f, 2.4f, -10f);

            Vector3 feet = TraversalRules.StandPosition(corner, Vector3.right, 0.35f, 0.12f);

            Assert.AreEqual(-9f - 0.47f, feet.x, 0.0001f);
            Assert.Greater(feet.y, corner.y);
        }

        [Test]
        public void EaseStartsAndEndsAtRest()
        {
            Assert.AreEqual(0f, TraversalRules.Ease(0f), 0.0001f);
            Assert.AreEqual(0.5f, TraversalRules.Ease(0.5f), 0.0001f);
            Assert.AreEqual(1f, TraversalRules.Ease(1f), 0.0001f);
            Assert.AreEqual(1f, TraversalRules.Ease(3f), 0.0001f);
        }

        [Test]
        public void PathIsFollowedByDistance()
        {
            var points = new[] { Vector3.zero, new Vector3(0f, 2f, 0f), new Vector3(2f, 2f, 0f), Vector3.zero };

            Assert.AreEqual(Vector3.zero, TraversalRules.AlongPath(points, 3, 0f));
            Assert.AreEqual(new Vector3(0f, 2f, 0f), TraversalRules.AlongPath(points, 3, 0.5f));
            Assert.AreEqual(new Vector3(1f, 2f, 0f), TraversalRules.AlongPath(points, 3, 0.75f));
            Assert.AreEqual(new Vector3(2f, 2f, 0f), TraversalRules.AlongPath(points, 3, 1f));
        }
    }
}
