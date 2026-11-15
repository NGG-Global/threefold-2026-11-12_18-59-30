using NUnit.Framework;
using Threefold.EditorTools;
using UnityEngine;
using L = Threefold.EditorTools.ReferenceCourtyardLayout;

namespace Threefold.Tests
{
    /// <summary>
    /// The composition of the Reference Courtyard, checked as arithmetic on its layout: what the game's camera
    /// sees when the scene starts. These say that a thing is in view and unobstructed by the layout's own walls
    /// and trees. They cannot say that the view is attractive; that is judged from renders and on a device.
    /// Plain C#: these run outside Unity too.
    /// </summary>
    public sealed class ReferenceCourtyardLayoutTests
    {
        [Test]
        public void TheMageStartsOnThePavingFacingTheArch()
        {
            Assert.Less(Mathf.Abs(L.Spawn.x), L.HalfWidth - 2f);
            Assert.Less(Mathf.Abs(L.Spawn.z), L.HalfDepth - 2f);
            Assert.Greater(L.Arch.z, L.Spawn.z + 12f, "The arch stands well ahead of the Mage.");
            Assert.Less(Mathf.Abs(L.Bearing(L.Arch)), 4f, "The arch stands on the axis of the first view.");
        }

        [Test]
        public void TheWholeArchIsInTheFirstView()
        {
            var top = new Vector3(L.Arch.x, L.ArchTop, L.Arch.z);
            Assert.Less(L.Elevation(top), L.TopOfViewElevation - 1f, "The top of the arch is below the top of the screen.");

            float half = L.ArchHalfExtent;
            Assert.Less(L.Bearing(new Vector3(L.Arch.x + half, 0f, L.Arch.z)), L.HalfHorizontalFov - 5f);
            Assert.Greater(L.Bearing(new Vector3(L.Arch.x - half, 0f, L.Arch.z)), -L.HalfHorizontalFov + 5f);
        }

        [Test]
        public void TheArchIsAGatewayTheMageCanWalkThrough()
        {
            Assert.GreaterOrEqual(L.ArchRadius * 2f, 4f, "Wide enough to read as a gateway.");
            Assert.GreaterOrEqual(L.ArchSpring + L.ArchRadius, 6f, "Tall enough to be prominent.");
            Assert.Greater(L.ArchTop, L.ArchSpring + L.ArchRadius + 0.9f, "There is masonry above the ring of stones.");
            Assert.Less(Mathf.Abs(L.Arch.z), L.HalfDepth, "It stands on the paving.");
        }

        [Test]
        public void TheDaisIsTheFocalPointBetweenTheMageAndTheArch()
        {
            Assert.Greater(L.Dais.z, L.Spawn.z + 6f);
            Assert.Less(L.Dais.z, L.Arch.z - 5f);
            Assert.Less(Mathf.Abs(L.Bearing(L.Dais)), 6f, "On the axis of the view.");
            Assert.Less(L.Elevation(L.Dais), 0f);
            Assert.Greater(L.Elevation(L.Dais), -(L.CameraVerticalFov * 0.5f + L.CameraStartPitch) + 4f, "Not below the bottom of the screen.");
        }

        [Test]
        public void TheCastleIsInTheFirstViewBesideTheArchAndAboveTheHorizon()
        {
            float left = L.Bearing(L.Castle + Vector3.left * L.CastleHalfWidth);
            float right = L.Bearing(L.Castle + Vector3.right * L.CastleHalfWidth);
            float archEdge = L.Bearing(new Vector3(L.Arch.x + L.ArchHalfExtent, 0f, L.Arch.z));

            Assert.Greater(left, archEdge + 1f, "The castle is clear of the arch and its wing.");
            Assert.Less(right, L.HalfHorizontalFov - 4f, "The castle is inside the right edge of the view.");
            Assert.Greater(L.Elevation(L.Castle), 1f, "Its foot is above the horizon.");
            Assert.Less(L.Elevation(L.Castle + Vector3.up * L.CastleHeight), L.TopOfViewElevation - 1f, "Its spire is below the top of the screen.");
        }

        [Test]
        public void NoWallOrTreeOfTheLayoutStandsBeforeTheCastle()
        {
            float left = L.Bearing(L.Castle + Vector3.left * L.CastleHalfWidth) - 2f;
            float right = L.Bearing(L.Castle + Vector3.right * L.CastleHalfWidth) + 2f;
            float foot = L.Elevation(L.Castle);

            foreach (L.Placement placement in L.Modules)
            {
                // A wall module is four metres long: take its ends as well as its middle.
                for (int end = -1; end <= 1; end++)
                {
                    // The module's own X axis after its turn about Y, without the engine's quaternions.
                    float yaw = placement.Yaw * Mathf.Deg2Rad;
                    Vector3 along = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw)) * (2f * end);
                    Vector3 top = placement.Position + along + Vector3.up * L.Height(placement.Module);
                    float bearing = L.Bearing(top);
                    if (bearing >= left && bearing <= right)
                    {
                        Assert.Less(L.Elevation(top), foot, $"{placement.Module} at {placement.Position} hides the castle.");
                    }
                }
            }

            foreach (L.Tree tree in L.Trees)
            {
                // A tree is about six metres across at scale 1.
                float halfWidth = Mathf.Atan2(3f * tree.Scale, Vector3.Distance(L.StartCameraPosition, tree.Position)) * Mathf.Rad2Deg;
                float bearing = L.Bearing(tree.Position);
                bool before = bearing + halfWidth >= left && bearing - halfWidth <= right;
                Assert.IsFalse(before && L.Elevation(tree.Position + Vector3.up * tree.Height) > foot,
                    $"The tree at {tree.Position} hides the castle.");
            }
        }

        [Test]
        public void TheLeftIsTallAndTheRightIsLow()
        {
            float left = 0f;
            float right = 0f;
            foreach (L.Placement placement in L.Modules)
            {
                if (placement.Position.z < -L.HalfDepth || Mathf.Abs(placement.Position.x) < 9f)
                {
                    continue;
                }

                float height = L.Height(placement.Module);
                if (placement.Position.x < 0f)
                {
                    left = Mathf.Max(left, height);
                }
                else
                {
                    right = Mathf.Max(right, height);
                }
            }

            Assert.Greater(left, 4f, "The left side frames the view with columns.");
            Assert.Less(right, L.StartCameraPosition.y, "The right side stays below the camera's eye, so the valley is seen over it.");
        }

        [Test]
        public void TheValleyAxisBehindTheArchIsOpen()
        {
            foreach (L.Hill hill in L.Hills)
            {
                if (hill.Position.z > L.Arch.z && hill.Wooded)
                {
                    Assert.Greater(Mathf.Abs(L.Bearing(hill.Position)), 10f, $"The wooded hill at {hill.Position} closes the view through the arch.");
                }
            }
        }

        [Test]
        public void TheDistanceIsWithinTheHazeAndNothingDistantIsNear()
        {
            Assert.Less(Vector3.Distance(L.StartCameraPosition, L.Castle), L.HazeEnd * 0.75f, "The castle is not lost in the haze.");
            foreach (L.Hill hill in L.Hills)
            {
                float distance = new Vector2(hill.Position.x, hill.Position.z).magnitude;
                Assert.Greater(distance, L.TerraceHalf * 2f, $"The hill at {hill.Position} would stand on the terrace.");
            }
        }

        [Test]
        public void EverythingPlacedStandsWhereItCanStand()
        {
            foreach (Vector3 construct in L.Constructs)
            {
                Assert.Less(Mathf.Abs(construct.x), L.HalfWidth - 1f);
                Assert.Less(Mathf.Abs(construct.z), L.HalfDepth - 1f);
                Assert.Greater(Vector3.Distance(construct, L.Spawn), 8f, "Not on top of the Mage.");
            }

            foreach (Vector3 grass in L.GrassPatches)
            {
                bool onPaving = Mathf.Abs(grass.x) < L.HalfWidth && Mathf.Abs(grass.z) < L.HalfDepth;
                Assert.IsFalse(onPaving, $"The grass at {grass} would grow through the paving.");
                Assert.Less(Mathf.Max(Mathf.Abs(grass.x), Mathf.Abs(grass.z)), L.TerraceHalf - 4f);
            }

            foreach (Vector3 moss in L.MossPatches)
            {
                Assert.Less(Mathf.Abs(moss.x), L.HalfWidth);
                Assert.Less(Mathf.Abs(moss.z), L.HalfDepth);
            }

            foreach (L.Tree tree in L.Trees)
            {
                bool onPaving = Mathf.Abs(tree.Position.x) < L.HalfWidth + 2f && Mathf.Abs(tree.Position.z) < L.HalfDepth + 2f;
                Assert.IsFalse(onPaving, $"The tree at {tree.Position} stands on the paving.");
            }

            foreach (L.Placement placement in L.Modules)
            {
                Assert.Greater(Vector3.Distance(placement.Position, L.Spawn), 3f, $"{placement.Module} stands on the Mage.");
            }
        }

        [Test]
        public void VegetationIsSelective()
        {
            Assert.LessOrEqual(L.Vines.Length, 8);
            Assert.LessOrEqual(L.Trees.Length, 8);
            Assert.LessOrEqual(L.GrassPatches.Length, 12);

            float leafArea = 0f;
            foreach (L.Vine vine in L.Vines)
            {
                Assert.AreEqual(1f, vine.Facing.magnitude, 0.001f);
                Assert.AreEqual(0f, vine.Facing.y, 0.001f, "A vine hangs on an upright face.");
                leafArea += vine.Width * vine.Drop;
            }

            Assert.Less(leafArea, 60f, "Vines cover a small part of the stone, not all of it.");
        }

        [Test]
        public void TheReviewViewpointsStartWithTheGamesOwnFirstView()
        {
            Assert.GreaterOrEqual(L.Viewpoints.Count, 4);
            Assert.AreEqual(L.StartCameraPosition, L.Viewpoints[0].Position);
            foreach (L.Viewpoint viewpoint in L.Viewpoints)
            {
                Assert.Greater(Vector3.Distance(viewpoint.Position, viewpoint.Target), 1f, viewpoint.Name);
                Assert.Greater(viewpoint.Position.y, 0.5f, $"{viewpoint.Name} is above the ground.");
            }
        }
    }
}
