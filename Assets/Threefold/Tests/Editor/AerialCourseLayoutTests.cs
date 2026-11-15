using NUnit.Framework;
using Threefold.Characters;
using Threefold.Levels;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The aerial course (<see cref="AerialCourseLayout"/>): every station must defeat a normal jump and be crossed
    /// by its ability, at 15 to 120 frames per second, with the motor's gravity, the traversal limits and the
    /// abilities' default tuning. Plain C#, run outside Unity by Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class AerialCourseLayoutTests
    {
        private static readonly float[] FrameRates = { 15f, 30f, 60f, 120f };
        private static readonly TraversalReach Reach = TraversalReach.Defaults;

        private static AerialRunResult Fly(AerialAbilityKind ability, float fps, float start, float landing, float hold = 0.2f)
        {
            return AerialSimulator.Run(JumpFigures.Defaults, new AerialAbilityModel(null, null, null, null), new AerialRunScript
            {
                Ability = ability,
                FirstHoldSeconds = 0.5f,
                AbilityAtApex = true,
                AbilityHoldSeconds = hold,
                StartHeight = start,
                LandingHeight = landing,
                FramesPerSecond = fps,
            });
        }

        [Test]
        public void TheAscensionPillarIsBeyondAJumpAndALedgeCatchButNotBeyondAscension()
        {
            float pillar = AerialCourseLayout.Find("AscensionPillar").Top;
            foreach (float fps in FrameRates)
            {
                float jumpPeak = Fly(AerialAbilityKind.None, fps, 0f, 0f).PeakHeight;
                Assert.Less(jumpPeak + Reach.GrabMaxHeight, pillar - 0.1f, $"{fps} fps: a jump and a catch fall short");

                float ascensionPeak = Fly(AerialAbilityKind.Ascension, fps, 0f, 0f).PeakHeight;
                Assert.Greater(ascensionPeak, pillar + 0.2f, $"{fps} fps: Ascension lifts the feet above its top");
            }
        }

        [Test]
        public void NothingNearThePillarCanBeJumpedFrom()
        {
            CourseBlock pillar = AerialCourseLayout.Find("AscensionPillar");
            float worstReach = 0f;
            foreach (float fps in FrameRates)
            {
                worstReach = Mathf.Max(worstReach, Fly(AerialAbilityKind.None, fps, 0f, 0f).PeakHeight + Reach.GrabMaxHeight);
            }

            foreach (CourseBlock block in AerialCourseLayout.Blocks())
            {
                if (block.Name == pillar.Name || block.Top + worstReach < pillar.Top)
                {
                    continue;
                }

                if (block.Name == "AerialFloor")
                {
                    continue; // the floor is the ground the pillar stands on, covered by the test above
                }

                float dx = Mathf.Max(0f, Mathf.Max(block.MinX - pillar.MaxX, pillar.MinX - block.MaxX));
                float dz = Mathf.Max(0f, Mathf.Max(block.MinZ - pillar.MaxZ, pillar.MinZ - block.MaxZ));
                Assert.GreaterOrEqual(Mathf.Sqrt(dx * dx + dz * dz), AerialCourseLayout.PillarClearance - 0.001f, block.Name);
            }
        }

        [Test]
        public void OnlyTheGlideCrossesFromTheTowerToTheLanding()
        {
            CourseBlock tower = AerialCourseLayout.Find("GlideTower");
            CourseBlock landing = AerialCourseLayout.Find("GlideLanding");
            float gap = landing.MinZ - tower.MaxZ;
            Assert.AreEqual(AerialCourseLayout.GlideGap, gap, 0.001f);

            foreach (float fps in FrameRates)
            {
                Assert.Less(Fly(AerialAbilityKind.None, fps, tower.Top, landing.Top).Distance, gap - 1f, $"{fps} fps jump");
                Assert.Less(Fly(AerialAbilityKind.Dash, fps, tower.Top, landing.Top).Distance, gap - 0.5f, $"{fps} fps dash");
                Assert.Less(Fly(AerialAbilityKind.Ascension, fps, tower.Top, landing.Top).Distance, gap - 0.3f,
                    $"{fps} fps ascension");

                float glide = Fly(AerialAbilityKind.Glide, fps, tower.Top, landing.Top, hold: 10f).Distance;
                Assert.Greater(glide, gap + 1f, $"{fps} fps glide reaches the landing");
                Assert.Less(glide, gap + landing.Size.z, $"{fps} fps glide comes down on it, not beyond");
            }
        }

        [Test]
        public void TheDashCrossesTheDashGapAndAJumpDoesNot()
        {
            CourseBlock start = AerialCourseLayout.Find("DashStart");
            CourseBlock end = AerialCourseLayout.Find("DashLanding");
            float gap = end.MinZ - start.MaxZ;
            Assert.AreEqual(AerialCourseLayout.DashGap, gap, 0.001f);

            foreach (float fps in FrameRates)
            {
                Assert.Less(Fly(AerialAbilityKind.None, fps, start.Top, end.Top).Distance, gap - 1f, $"{fps} fps jump");
                Assert.Greater(Fly(AerialAbilityKind.Dash, fps, start.Top, end.Top).Distance, gap + 1f, $"{fps} fps dash");
            }
        }

        [Test]
        public void TheDashWallStopsADashFromItsMarker()
        {
            CourseBlock marker = AerialCourseLayout.Find("DashWallMarker");
            CourseBlock wall = AerialCourseLayout.Find("DashWall");
            float distance = wall.MinZ - marker.BaseCentre.z;
            Assert.AreEqual(AerialCourseLayout.DashWallDistance, distance, 0.001f);
            Assert.Greater(wall.Top, 2f, "taller than a jump clears");

            foreach (float fps in FrameRates)
            {
                AerialRunResult run = AerialSimulator.Run(JumpFigures.Defaults, new AerialAbilityModel(null, null, null, null),
                    new AerialRunScript
                    {
                        Ability = AerialAbilityKind.Dash, FirstHoldSeconds = 0.5f, AbilityAtApex = true,
                        AbilityHoldSeconds = 0.1f, WallDistance = distance, FramesPerSecond = fps,
                    });
                Assert.IsTrue(run.StoppedByWall, $"{fps} fps");
            }
        }

        [Test]
        public void TheStepsToTheTowerCanBeClimbedOneByOne()
        {
            float previous = 0f;
            foreach (string name in new[] { "GlideStep1", "GlideStep2", "GlideStep3", "GlideTower" })
            {
                CourseBlock block = AerialCourseLayout.Find(name);
                Assert.LessOrEqual(block.Top - previous, Reach.MantleMaxHeight, name);
                Assert.Greater(block.Top, previous, name);
                previous = block.Top;
            }
        }

        [Test]
        public void EveryBlockStandsOnTheCourseFloorWithoutOverlapping()
        {
            CourseBlock[] blocks = AerialCourseLayout.Blocks();
            CourseBlock floor = blocks[0];
            for (int i = 1; i < blocks.Length; i++)
            {
                CourseBlock a = blocks[i];
                Assert.AreEqual(0f, a.BaseCentre.y, 0.0001f, a.Name);
                Assert.GreaterOrEqual(a.MinX, floor.MinX, a.Name);
                Assert.LessOrEqual(a.MaxX, floor.MaxX, a.Name);
                Assert.GreaterOrEqual(a.MinZ, floor.MinZ, a.Name);
                Assert.LessOrEqual(a.MaxZ, floor.MaxZ, a.Name);

                for (int j = i + 1; j < blocks.Length; j++)
                {
                    CourseBlock b = blocks[j];
                    bool overlap = a.MinX < b.MaxX - 0.001f && b.MinX < a.MaxX - 0.001f &&
                                   a.MinZ < b.MaxZ - 0.001f && b.MinZ < a.MaxZ - 0.001f;
                    Assert.IsFalse(overlap, $"{a.Name} and {b.Name}");
                }
            }

            Assert.AreEqual(AerialCourseLayout.FloorStartZ, floor.MinZ, 0.001f, "the course continues the range's floor");
        }
    }
}
