using System;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The discipline bearing (<see cref="PersonalityPoseModel"/>): that the three disciplines differ the way their
    /// characters ask, that changing discipline never jumps, that nothing leaves its limits, that an action gets the
    /// arms to itself, that the reactions are restrained and die away, and that a frame allocates nothing.
    /// Plain C#, run outside Unity by Tools/ci/compile_check.sh. What it looks like on the character is not tested.
    /// </summary>
    public sealed class PersonalityTests
    {
        private const float Frame = 1f / 60f;
        private static readonly float[] FrameRates = { 20f, 30f, 60f, 120f };
        private static readonly MagicDiscipline[] Disciplines = { MagicDiscipline.Arcane, MagicDiscipline.Flame, MagicDiscipline.Aegis };
        private static object probe;

        private static PersonalityPoseModel Model(MagicDiscipline discipline, uint seed = 7u)
        {
            var model = new PersonalityPoseModel(PersonalityDefaults.Create(), seed);
            model.Reset(discipline);
            return model;
        }

        /// <summary>Runs a model at rest in one discipline and returns the average and the swing of some channels.</summary>
        private static void Observe(MagicDiscipline discipline, float seconds, out float meanLean, out float breathSwing,
            out float swaySwing, out float armSwing, out float shoulderSwing, out int breaths)
        {
            PersonalityPoseModel model = Model(discipline);
            int frames = Mathf.RoundToInt(seconds / Frame);
            float leanSum = 0f, chestMin = float.MaxValue, chestMax = float.MinValue, swayMax = 0f, armMax = 0f;
            float shoulderMin = float.MaxValue, shoulderMax = float.MinValue;
            breaths = 0;
            float previousLift = 0f;
            bool rising = false;
            for (int i = 0; i < frames; i++)
            {
                PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Idle);
                leanSum += pose.Spine.x + pose.Chest.x;
                chestMin = Mathf.Min(chestMin, pose.Chest.x);
                chestMax = Mathf.Max(chestMax, pose.Chest.x);
                swayMax = Mathf.Max(swayMax, Mathf.Abs(pose.Spine.z));
                armMax = Mathf.Max(armMax, Mathf.Max(Mathf.Abs(pose.LeftArm.x), Mathf.Abs(pose.LeftArm.y)));
                shoulderMin = Mathf.Min(shoulderMin, pose.RightShoulder.z);
                shoulderMax = Mathf.Max(shoulderMax, pose.RightShoulder.z);

                // A breath is counted each time the chest starts to rise (its pitch starts to fall).
                float lift = -pose.Chest.x;
                bool nowRising = lift > previousLift;
                if (i > 1 && nowRising && !rising)
                {
                    breaths++;
                }

                rising = nowRising;
                previousLift = lift;
            }

            meanLean = leanSum / frames;
            breathSwing = chestMax - chestMin;
            swaySwing = swayMax;
            armSwing = armMax;
            shoulderSwing = shoulderMax - shoulderMin;
        }

        // ------------------------------------------------------------ the three characters

        [Test]
        public void TheDefaultsDescribeThreeDifferentBearings()
        {
            PersonalityProfile arcane = PersonalityDefaults.Arcane();
            PersonalityProfile flame = PersonalityDefaults.Flame();
            PersonalityProfile aegis = PersonalityDefaults.Aegis();

            Assert.Greater(flame.forwardLeanDegrees, arcane.forwardLeanDegrees + 2f, "Flame leans in.");
            Assert.Greater(flame.forwardLeanDegrees, aegis.forwardLeanDegrees + 2f);
            Assert.Greater(flame.breathsPerMinute, arcane.breathsPerMinute, "Flame breathes fastest.");
            Assert.Less(aegis.breathsPerMinute, arcane.breathsPerMinute, "Aegis breathes slowest.");
            Assert.Greater(arcane.handCircleDegrees, flame.handCircleDegrees * 2f, "The circling hands are Arcane's.");
            Assert.Greater(arcane.handCircleDegrees, aegis.handCircleDegrees * 2f);
            Assert.Greater(flame.restlessDegrees, arcane.restlessDegrees * 3f, "The restless shoulders are Flame's.");
            Assert.Less(aegis.restlessDegrees, arcane.restlessDegrees, "Aegis is the stillest.");
            Assert.Less(aegis.weightShiftDegrees, arcane.weightShiftDegrees, "Aegis sways least.");
            Assert.Less(aegis.weightShiftDegrees, flame.weightShiftDegrees);
            Assert.Greater(aegis.chestOpenDegrees, arcane.chestOpenDegrees, "Aegis squares the chest most.");
            Assert.Less(flame.lookTurnSeconds, arcane.lookTurnSeconds, "Flame's looks are the sharpest.");
            Assert.Greater(aegis.lookTurnSeconds, arcane.lookTurnSeconds, "Aegis's are the most deliberate.");
            Assert.Less(flame.lookIntervalMax, arcane.lookIntervalMin + 0.01f, "Flame checks its surroundings most often.");
            Assert.Greater(aegis.lookIntervalMin, arcane.lookIntervalMin);

            foreach (PersonalityProfile profile in new[] { arcane, flame, aegis })
            {
                Assert.LessOrEqual(profile.lookIntervalMin, profile.lookIntervalMax);
                Assert.Greater(profile.blendSeconds, 0.1f);
                Assert.LessOrEqual(profile.blendSeconds, 1f, "A change of discipline must read at once.");
                Assert.LessOrEqual(profile.handCircleDegrees + profile.restlessDegrees, PersonalityPoseModel.MaxArmDegrees,
                    "The hands move by a degree or two.");
            }
        }

        [Test]
        public void TheBearingsDifferInTheMotionTheyProduce()
        {
            Observe(MagicDiscipline.Arcane, 60f, out float arcaneLean, out float arcaneBreath, out float arcaneSway,
                out float arcaneArms, out float arcaneShoulders, out int arcaneBreaths);
            Observe(MagicDiscipline.Flame, 60f, out float flameLean, out float flameBreath, out float flameSway,
                out float flameArms, out float flameShoulders, out int flameBreaths);
            Observe(MagicDiscipline.Aegis, 60f, out float aegisLean, out float aegisBreath, out float aegisSway,
                out float aegisArms, out float aegisShoulders, out int aegisBreaths);

            Assert.Greater(flameLean, arcaneLean + 2f, "Flame stands further forward than Arcane.");
            Assert.Greater(flameLean, aegisLean + 2f, "And than Aegis.");
            Assert.Greater(flameBreaths, arcaneBreaths, "Breaths in a minute: Flame most.");
            Assert.Greater(arcaneBreaths, aegisBreaths, "Aegis fewest.");
            Assert.AreEqual(PersonalityDefaults.Arcane().breathsPerMinute, arcaneBreaths, 1.01f, "Breaths are counted as set.");
            Assert.Greater(arcaneBreath, 0.5f, "Breathing shows.");
            Assert.Greater(flameBreath, 0.5f);
            Assert.Greater(aegisBreath, 0.5f);
            Assert.Less(aegisSway, arcaneSway, "Aegis sways least.");
            Assert.Less(arcaneSway, flameSway);
            Assert.Greater(arcaneArms, 1f, "Arcane's hands circle.");
            Assert.Less(flameArms, arcaneArms * 0.5f);
            Assert.Less(aegisArms, arcaneArms * 0.5f);
            Assert.Greater(flameShoulders, arcaneShoulders + 0.5f, "Flame's shoulders are restless.");
            Assert.Greater(flameShoulders, aegisShoulders + 0.5f);
        }

        [Test]
        public void ArcanesHandsTraceACircle()
        {
            PersonalityPoseModel model = Model(MagicDiscipline.Arcane);
            float radius = PersonalityDefaults.Arcane().handCircleDegrees;
            float turned = 0f;
            float previous = 0f;
            for (int i = 0; i < 60 * 20; i++)
            {
                PersonalityPose pose = model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
                var left = new Vector2(pose.LeftArm.x, pose.LeftArm.y);
                var right = new Vector2(pose.RightArm.x, pose.RightArm.y);
                Assert.AreEqual(radius, left.magnitude, 0.01f, "The left arm stays on its circle.");
                Assert.AreEqual(radius, right.magnitude, 0.01f, "And the right.");
                float angle = Mathf.Atan2(left.y, left.x);
                if (i > 0)
                {
                    turned += Mathf.DeltaAngle(previous * Mathf.Rad2Deg, angle * Mathf.Rad2Deg);
                }

                previous = angle;
            }

            // 9 circles a minute for 20 seconds: three turns.
            Assert.AreEqual(3f * 360f, Mathf.Abs(turned), 20f);
        }

        [Test]
        public void TheHeadLooksAroundAndComesBack()
        {
            foreach (MagicDiscipline discipline in Disciplines)
            {
                PersonalityPoseModel model = Model(discipline);
                PersonalityProfile profile = PersonalityDefaults.Create().Get((int)discipline);
                float largest = 0f;
                int looks = 0;
                bool away = false;
                bool left = false, right = false;
                for (int i = 0; i < 60 * 120; i++)
                {
                    PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Idle);
                    float yaw = pose.Neck.y + pose.Head.y;
                    largest = Mathf.Max(largest, Mathf.Abs(yaw));
                    bool nowAway = Mathf.Abs(yaw) > profile.lookYawDegrees * 0.25f;
                    if (nowAway && !away)
                    {
                        looks++;
                        left |= yaw < 0f;
                        right |= yaw > 0f;
                    }

                    away = nowAway;
                }

                Assert.Greater(looks, 3, $"{discipline} looks around.");
                Assert.IsTrue(left && right, $"{discipline} looks both ways.");
                Assert.LessOrEqual(largest, profile.lookYawDegrees + 0.01f, "Never further than its range.");
                Assert.Greater(largest, profile.lookYawDegrees * 0.5f);
            }

            // Flame looks more often than Aegis over the same time.
            Assert.Greater(CountLooks(MagicDiscipline.Flame), CountLooks(MagicDiscipline.Aegis) * 2);
        }

        private static int CountLooks(MagicDiscipline discipline)
        {
            PersonalityPoseModel model = Model(discipline);
            float range = PersonalityDefaults.Create().Get((int)discipline).lookYawDegrees;
            int looks = 0;
            bool away = false;
            for (int i = 0; i < 60 * 120; i++)
            {
                PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Idle);
                bool nowAway = Mathf.Abs(pose.Neck.y + pose.Head.y) > range * 0.25f;
                looks += nowAway && !away ? 1 : 0;
                away = nowAway;
            }

            return looks;
        }

        [Test]
        public void TheSameSeedGivesTheSameRun()
        {
            PersonalityPoseModel first = Model(MagicDiscipline.Flame, 99u);
            PersonalityPoseModel second = Model(MagicDiscipline.Flame, 99u);
            for (int i = 0; i < 2000; i++)
            {
                PersonalityPose a = first.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
                PersonalityPose b = second.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
                Assert.AreEqual(0f, a.LargestDifference(b));
            }
        }

        // ------------------------------------------------------------ changing discipline

        [Test]
        public void ChangingDisciplineIsSmoothAndFinishesInItsBlendTime()
        {
            foreach (float rate in FrameRates)
            {
                float dt = 1f / rate;
                foreach (MagicDiscipline from in Disciplines)
                {
                    foreach (MagicDiscipline to in Disciplines)
                    {
                        if (from == to)
                        {
                            continue;
                        }

                        PersonalityPoseModel model = Model(from);
                        PersonalityPose previous = default;
                        float settledStep = 0f;
                        for (int i = 0; i < Mathf.RoundToInt(3f * rate); i++)
                        {
                            PersonalityPose pose = model.Advance(dt, from, PersonalityActivity.Idle);
                            if (i > 0)
                            {
                                settledStep = Mathf.Max(settledStep, pose.LargestDifference(previous));
                            }

                            previous = pose;
                        }

                        // The change may move the body no faster than a look or a breath already does, plus the
                        // posture's own travel spread over the blend: at most 30 degrees a second on any channel.
                        float blend = PersonalityDefaults.Create().Get((int)to).blendSeconds;
                        float largestStep = 0f;
                        int frames = Mathf.CeilToInt(blend * rate) + 2;
                        for (int i = 0; i < frames; i++)
                        {
                            PersonalityPose pose = model.Advance(dt, to, PersonalityActivity.Idle);
                            largestStep = Mathf.Max(largestStep, pose.LargestDifference(previous));
                            previous = pose;
                        }

                        Assert.LessOrEqual(largestStep, Mathf.Max(settledStep, 0f) + 30f * dt,
                            $"{from} to {to} at {rate} fps: no jump.");
                        Assert.AreEqual(1f, model.Weight(to), 1e-4f, $"{from} to {to} at {rate} fps: finished after {blend} s.");
                        Assert.AreEqual(0f, model.Weight(from), 1e-4f);
                    }
                }
            }
        }

        [Test]
        public void TheWeightsOfTheDisciplinesAlwaysAddUpToOne()
        {
            PersonalityPoseModel model = Model(MagicDiscipline.Arcane);
            var random = new System.Random(3);
            MagicDiscipline discipline = MagicDiscipline.Arcane;
            for (int i = 0; i < 5000; i++)
            {
                if (random.Next(12) == 0)
                {
                    discipline = Disciplines[random.Next(3)];
                }

                model.Advance(Frame, discipline, PersonalityActivity.Idle);
                float sum = model.Weight(MagicDiscipline.Arcane) + model.Weight(MagicDiscipline.Flame) + model.Weight(MagicDiscipline.Aegis);
                Assert.AreEqual(1f, sum, 1e-3f);
                foreach (MagicDiscipline each in Disciplines)
                {
                    Assert.GreaterOrEqual(model.Weight(each), 0f);
                }
            }
        }

        [Test]
        public void SwitchingBackAndForthQuicklyNeverJumps()
        {
            PersonalityPoseModel model = Model(MagicDiscipline.Arcane);
            PersonalityPose previous = model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
            for (int i = 0; i < 3000; i++)
            {
                MagicDiscipline discipline = Disciplines[(i / 5) % 3]; // a new discipline every five frames
                PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Idle);
                Assert.LessOrEqual(pose.LargestDifference(previous), 1f, "At most a degree a frame at 60 fps.");
                previous = pose;
            }
        }

        // ------------------------------------------------------------ limits

        [Test]
        public void NothingEverLeavesItsLimits()
        {
            var tuning = PersonalityDefaults.Create();
            // Data far beyond the sliders: the limits must hold whatever the data says.
            tuning.flame.forwardLeanDegrees = 90f;
            tuning.flame.restlessDegrees = 50f;
            tuning.arcane.handCircleDegrees = 40f;
            tuning.aegis.lookYawDegrees = 170f;
            tuning.aegis.breathDepthDegrees = 60f;
            tuning.reactions.impactDegrees = 80f;
            tuning.reactions.castBraceDegrees = 80f;

            var model = new PersonalityPoseModel(tuning, 5u);
            var random = new System.Random(11);
            var activities = (PersonalityActivity[])Enum.GetValues(typeof(PersonalityActivity));
            MagicDiscipline discipline = MagicDiscipline.Arcane;
            PersonalityActivity activity = PersonalityActivity.Idle;
            for (int i = 0; i < 40000; i++)
            {
                if (random.Next(40) == 0) discipline = Disciplines[random.Next(3)];
                if (random.Next(25) == 0) activity = activities[random.Next(activities.Length)];
                if (random.Next(30) == 0)
                {
                    model.NotifyImpact((float)random.NextDouble() * 3f,
                        new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
                }

                if (random.Next(50) == 0) model.NotifyPowerfulCast((float)random.NextDouble() * 3f);

                float dt = 1f / FrameRates[random.Next(FrameRates.Length)];
                if (random.Next(200) == 0) dt = 2f;             // a hitch
                if (random.Next(300) == 0) dt = 0f;
                if (random.Next(500) == 0) dt = float.NaN;
                PersonalityPose pose = model.Advance(dt, discipline, activity);

                AssertWithin(pose.Spine, PersonalityPoseModel.MaxSpineDegrees, "spine");
                AssertWithin(pose.Chest, PersonalityPoseModel.MaxChestDegrees, "chest");
                AssertWithin(pose.Neck, PersonalityPoseModel.MaxNeckDegrees, "neck");
                AssertWithin(pose.Head, PersonalityPoseModel.MaxHeadDegrees, "head");
                AssertWithin(pose.LeftShoulder, PersonalityPoseModel.MaxShoulderDegrees, "left shoulder");
                AssertWithin(pose.RightShoulder, PersonalityPoseModel.MaxShoulderDegrees, "right shoulder");
                AssertWithin(pose.LeftArm, PersonalityPoseModel.MaxArmDegrees, "left arm");
                AssertWithin(pose.RightArm, PersonalityPoseModel.MaxArmDegrees, "right arm");
                AssertWithin(pose.LeftHand, PersonalityPoseModel.MaxHandDegrees, "left hand");
                AssertWithin(pose.RightHand, PersonalityPoseModel.MaxHandDegrees, "right hand");
            }
        }

        private static void AssertWithin(Vector3 value, float limit, string channel)
        {
            Assert.IsFalse(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z), channel);
            Assert.LessOrEqual(Mathf.Abs(value.x), limit + 1e-4f, channel);
            Assert.LessOrEqual(Mathf.Abs(value.y), limit + 1e-4f, channel);
            Assert.LessOrEqual(Mathf.Abs(value.z), limit + 1e-4f, channel);
        }

        [Test]
        public void TheDefaultsStayWellInsideTheLimits()
        {
            foreach (MagicDiscipline discipline in Disciplines)
            {
                PersonalityPoseModel model = Model(discipline);
                for (int i = 0; i < 60 * 60; i++)
                {
                    PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Idle);
                    Assert.Less(Mathf.Abs(pose.Spine.x) + Mathf.Abs(pose.Chest.x), 9f, $"{discipline}: a lean, not a bow.");
                    Assert.Less(Mathf.Max(Mathf.Abs(pose.LeftArm.x), Mathf.Abs(pose.LeftArm.y)), 2f, $"{discipline}: hands by a degree or two.");
                    Assert.Less(Mathf.Abs(pose.LeftHand.x), PersonalityPoseModel.MaxHandDegrees);
                    Assert.AreEqual(0f, pose.Spine.y, "The spine is never twisted: the stances own the facing.");
                    Assert.AreEqual(0f, pose.Chest.y);
                }
            }
        }

        // ------------------------------------------------------------ activities

        [Test]
        public void AnActionHasTheArmsToItselfBeforeAnyStrikeCanLand()
        {
            // The fastest strike's start-up is 0.10 s (Arcane); the arms must be free before that at any frame rate.
            Assert.Less(PersonalityPoseModel.ArmsReleaseSeconds, 0.10f);
            foreach (float rate in FrameRates)
            {
                foreach (MagicDiscipline discipline in Disciplines)
                {
                    PersonalityPoseModel model = Model(discipline);
                    float dt = 1f / rate;
                    for (int i = 0; i < Mathf.RoundToInt(5f * rate); i++)
                    {
                        model.Advance(dt, discipline, PersonalityActivity.Idle);
                    }

                    PersonalityPose pose = default;
                    int frames = Mathf.CeilToInt(0.10f * rate);
                    for (int i = 0; i < frames; i++)
                    {
                        pose = model.Advance(dt, discipline, PersonalityActivity.Action);
                    }

                    Assert.AreEqual(0f, model.ArmsShare, $"{discipline} at {rate} fps");
                    Assert.AreEqual(Vector3.zero, pose.LeftArm);
                    Assert.AreEqual(Vector3.zero, pose.RightArm);
                    Assert.AreEqual(Vector3.zero, pose.LeftHand);
                    Assert.AreEqual(Vector3.zero, pose.RightHand);

                    // And for as long as the action lasts: the shoulders keep only the posture and the breath.
                    for (int i = 0; i < Mathf.RoundToInt(2f * rate); i++)
                    {
                        pose = model.Advance(dt, discipline, PersonalityActivity.Action);
                        Assert.AreEqual(Vector3.zero, pose.LeftArm);
                        Assert.AreEqual(Vector3.zero, pose.RightHand);
                        Assert.AreEqual(0f, pose.Neck.y, 1e-3f + (i < rate ? 20f : 0f), "The head stops looking around.");
                    }

                    Assert.AreEqual(0f, pose.Head.y, 1e-3f);
                }
            }
        }

        [TestCase(PersonalityActivity.Action)]
        [TestCase(PersonalityActivity.Airborne)]
        [TestCase(PersonalityActivity.Blocking)]
        [TestCase(PersonalityActivity.Staggered)]
        public void TheArmsAreLeftAloneWheneverAuthoredMotionOwnsThem(PersonalityActivity activity)
        {
            PersonalityPoseModel model = Model(MagicDiscipline.Flame);
            for (int i = 0; i < 300; i++)
            {
                model.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
            }

            PersonalityPose pose = default;
            for (int i = 0; i < 30; i++)
            {
                pose = model.Advance(Frame, MagicDiscipline.Flame, activity);
            }

            Assert.AreEqual(Vector3.zero, pose.LeftArm);
            Assert.AreEqual(Vector3.zero, pose.RightArm);
            Assert.AreEqual(Vector3.zero, pose.LeftHand);
            Assert.AreEqual(Vector3.zero, pose.RightHand);
        }

        [Test]
        public void MovingShowsLessThanStanding()
        {
            PersonalityPoseModel standing = Model(MagicDiscipline.Flame);
            PersonalityPoseModel moving = Model(MagicDiscipline.Flame);
            float standingLean = 0f, movingLean = 0f, movingSway = 0f;
            for (int i = 0; i < 60 * 30; i++)
            {
                PersonalityPose a = standing.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
                PersonalityPose b = moving.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Moving);
                if (i > 60)
                {
                    standingLean += a.Spine.x;
                    movingLean += b.Spine.x;
                    movingSway = Mathf.Max(movingSway, Mathf.Abs(b.Spine.z));
                }
            }

            Assert.Less(movingLean, standingLean * 0.6f);
            Assert.Greater(movingLean, 0f, "Some of the discipline still shows while running.");
            Assert.AreEqual(0f, movingSway, 1e-4f, "The run has its own weight shift.");
        }

        [Test]
        public void ADefeatedCharacterGetsNothing()
        {
            foreach (MagicDiscipline discipline in Disciplines)
            {
                PersonalityPoseModel model = Model(discipline);
                for (int i = 0; i < 400; i++)
                {
                    model.Advance(Frame, discipline, PersonalityActivity.Idle);
                }

                model.NotifyImpact(1f, Vector3.back);
                PersonalityPose previous = model.Pose;
                for (int i = 0; i < 30; i++)
                {
                    PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Defeated);
                    Assert.LessOrEqual(pose.LargestDifference(previous), 2.5f, "It lets go smoothly, not in one frame.");
                    previous = pose;
                }

                for (int i = 0; i < 600; i++)
                {
                    model.NotifyImpact(1f, Vector3.right);
                    model.NotifyPowerfulCast(1f);
                    PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Defeated);
                    Assert.AreEqual(0f, pose.Largest, $"{discipline}: nothing is added to the defeat animation.");
                    Assert.AreEqual(0f, pose.EyeClosed, "No blinking either.");
                    Assert.AreEqual(0f, pose.BrowMillimetres);
                }
            }
        }

        // ------------------------------------------------------------ reactions

        [Test]
        public void AHitMovesTheBodyTheWayItTravelledAndDiesAway()
        {
            float limit = PersonalityDefaults.Create().reactions.impactDegrees;

            // From the front (travelling backwards): the chest and head give way backwards.
            PersonalityPoseModel quiet = Model(MagicDiscipline.Aegis, 21u);
            PersonalityPoseModel struck = Model(MagicDiscipline.Aegis, 21u);
            struck.NotifyImpact(1f, Vector3.back);
            float mostBack = 0f, largestAfterTwoSeconds = 0f, largestStep = 0f;
            float previousDifference = 0f;
            for (int i = 0; i < 60 * 4; i++)
            {
                PersonalityPose a = quiet.Advance(Frame, MagicDiscipline.Aegis, PersonalityActivity.Idle);
                PersonalityPose b = struck.Advance(Frame, MagicDiscipline.Aegis, PersonalityActivity.Idle);
                float chest = b.Chest.x - a.Chest.x;
                float head = b.Head.x - a.Head.x;
                mostBack = Mathf.Min(mostBack, chest);
                Assert.LessOrEqual(Mathf.Abs(chest), limit * 0.6f + 1e-3f, "Restrained: the chest by a few degrees at most.");
                Assert.LessOrEqual(Mathf.Abs(head), limit + 1e-3f);
                largestStep = Mathf.Max(largestStep, Mathf.Abs(head - previousDifference));
                previousDifference = head;
                if (i > 120)
                {
                    largestAfterTwoSeconds = Mathf.Max(largestAfterTwoSeconds, b.LargestDifference(a));
                }
            }

            Assert.Less(mostBack, -1f, "A full hit from the front shows.");
            Assert.Less(largestAfterTwoSeconds, 0.05f, "And is over within two seconds.");
            Assert.Less(largestStep, 3f, "It swings; it does not snap.");

            // From the left (travelling to the right): the body tips to its right, which is negative roll.
            PersonalityPoseModel side = Model(MagicDiscipline.Aegis, 21u);
            PersonalityPoseModel reference = Model(MagicDiscipline.Aegis, 21u);
            side.NotifyImpact(1f, Vector3.right);
            float roll = 0f;
            for (int i = 0; i < 4; i++)
            {
                roll = side.Advance(Frame, MagicDiscipline.Aegis, PersonalityActivity.Idle).Chest.z -
                       reference.Advance(Frame, MagicDiscipline.Aegis, PersonalityActivity.Idle).Chest.z;
            }

            Assert.Less(roll, -0.5f);
        }

        [Test]
        public void AWeakHitShowsLessThanAStrongOne()
        {
            float Peak(float strength)
            {
                PersonalityPoseModel quiet = Model(MagicDiscipline.Arcane, 4u);
                PersonalityPoseModel struck = Model(MagicDiscipline.Arcane, 4u);
                struck.NotifyImpact(strength, Vector3.back);
                float peak = 0f;
                for (int i = 0; i < 120; i++)
                {
                    peak = Mathf.Max(peak, struck.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle)
                        .LargestDifference(quiet.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle)));
                }

                return peak;
            }

            Assert.AreEqual(0f, Peak(0f));
            Assert.AreEqual(0f, Peak(-1f));
            Assert.Greater(Peak(0.2f), 0.1f);
            Assert.Greater(Peak(1f), Peak(0.2f) * 2f);
            Assert.AreEqual(Peak(1f), Peak(5f), 1e-4f, "Strength is capped at 1.");
        }

        [Test]
        public void ManyHitsAtOnceStayWithinTheLimit()
        {
            float limit = PersonalityDefaults.Create().reactions.impactDegrees;
            PersonalityPoseModel quiet = Model(MagicDiscipline.Flame, 8u);
            PersonalityPoseModel struck = Model(MagicDiscipline.Flame, 8u);
            for (int i = 0; i < 600; i++)
            {
                struck.NotifyImpact(1f, Vector3.back);
                struck.NotifyImpact(1f, Vector3.back);
                PersonalityPose a = quiet.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
                PersonalityPose b = struck.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
                Assert.LessOrEqual(Mathf.Abs(b.Head.x - a.Head.x), limit + 1e-3f);
                Assert.LessOrEqual(Mathf.Abs(b.Chest.x - a.Chest.x), limit * 0.6f + 1e-3f);
            }
        }

        [Test]
        public void APowerfulCastBracesTheChestBackAndSettles()
        {
            float limit = PersonalityDefaults.Create().reactions.castBraceDegrees;
            PersonalityPoseModel quiet = Model(MagicDiscipline.Arcane, 13u);
            PersonalityPoseModel casting = Model(MagicDiscipline.Arcane, 13u);
            casting.NotifyPowerfulCast(1f);
            float mostBack = 0f, late = 0f;
            for (int i = 0; i < 60 * 3; i++)
            {
                // The cast happens during an action: the brace must show there too.
                PersonalityActivity activity = i < 30 ? PersonalityActivity.Action : PersonalityActivity.Idle;
                float chest = casting.Advance(Frame, MagicDiscipline.Arcane, activity).Chest.x -
                              quiet.Advance(Frame, MagicDiscipline.Arcane, activity).Chest.x;
                mostBack = Mathf.Min(mostBack, chest);
                Assert.LessOrEqual(Mathf.Abs(chest), limit + 1e-3f);
                if (i > 120)
                {
                    late = Mathf.Max(late, Mathf.Abs(chest));
                }
            }

            Assert.Less(mostBack, -0.5f, "It shows.");
            Assert.Less(late, 0.05f, "It settles.");
        }

        [Test]
        public void ReactionsBehaveTheSameAtEveryFrameRate()
        {
            float Peak(float rate)
            {
                PersonalityPoseModel quiet = Model(MagicDiscipline.Aegis, 2u);
                PersonalityPoseModel struck = Model(MagicDiscipline.Aegis, 2u);
                struck.NotifyImpact(0.5f, Vector3.back);
                float peak = 0f;
                for (int i = 0; i < Mathf.RoundToInt(rate); i++)
                {
                    float chest = struck.Advance(1f / rate, MagicDiscipline.Aegis, PersonalityActivity.Staggered).Chest.x -
                                  quiet.Advance(1f / rate, MagicDiscipline.Aegis, PersonalityActivity.Staggered).Chest.x;
                    peak = Mathf.Min(peak, chest);
                }

                return peak;
            }

            float reference = Peak(120f);
            Assert.Less(reference, -0.3f);
            foreach (float rate in FrameRates)
            {
                Assert.AreEqual(reference, Peak(rate), Mathf.Abs(reference) * 0.35f, $"{rate} fps");
            }
        }

        // ------------------------------------------------------------ face

        private static int CountBlinks(MagicDiscipline discipline, float seconds, out float longest)
        {
            PersonalityPoseModel model = Model(discipline, 31u);
            int blinks = 0;
            bool closed = false;
            float run = 0f;
            longest = 0f;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Frame); i++)
            {
                PersonalityPose pose = model.Advance(Frame, discipline, PersonalityActivity.Idle);
                Assert.GreaterOrEqual(pose.EyeClosed, 0f);
                Assert.LessOrEqual(pose.EyeClosed, 1f);
                bool nowClosed = pose.EyeClosed > 0.5f;
                blinks += nowClosed && !closed ? 1 : 0;
                run = pose.EyeClosed > 0f ? run + Frame : 0f;
                longest = Mathf.Max(longest, run);
                closed = nowClosed;
            }

            return blinks;
        }

        [Test]
        public void EachDisciplineBlinksInItsOwnRhythm()
        {
            int arcane = CountBlinks(MagicDiscipline.Arcane, 300f, out float arcaneLongest);
            int flame = CountBlinks(MagicDiscipline.Flame, 300f, out float flameLongest);
            int aegis = CountBlinks(MagicDiscipline.Aegis, 300f, out float aegisLongest);

            // Between the two ends of each interval range (plus the blink itself), over five minutes.
            Assert.That(arcane, Is.InRange(300f / 6.3f, 300f / 3f), "Arcane: relaxed, regular.");
            Assert.That(flame, Is.InRange(300f / 9.2f, 300f / 4.5f), "Flame: fewer, a fixed stare.");
            Assert.That(aegis, Is.InRange(300f / 8.3f, 300f / 4f), "Aegis: slow.");
            Assert.Greater(arcane, flame, "Flame blinks less often than Arcane.");

            Assert.Less(flameLongest, arcaneLongest, "Flame's blinks are the quickest.");
            Assert.Greater(aegisLongest, arcaneLongest, "Aegis's the slowest.");
            Assert.Less(aegisLongest, 0.3f, "A blink, not closed eyes.");
        }

        [Test]
        public void AHeavyHitMakesTheEyesBlinkAndTheBrowsRise()
        {
            PersonalityPoseModel model = Model(MagicDiscipline.Arcane, 17u);
            model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
            Assert.AreEqual(0f, model.Pose.EyeClosed);
            Assert.AreEqual(0f, model.Pose.BrowMillimetres, 1e-4f, "Arcane's brows are neutral.");

            model.NotifyImpact(0.2f, Vector3.back);
            float closedLight = 0f, browLight = 0f;
            for (int i = 0; i < 12; i++)
            {
                PersonalityPose pose = model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Staggered);
                closedLight = Mathf.Max(closedLight, pose.EyeClosed);
                browLight = Mathf.Max(browLight, pose.BrowMillimetres);
            }

            Assert.AreEqual(0f, closedLight, "A light hit does not make the eyes blink.");
            Assert.Greater(browLight, 0.1f);

            for (int i = 0; i < 60; i++)
            {
                model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
            }

            model.NotifyImpact(1f, Vector3.back);
            float closed = 0f, brow = 0f, last = 0f, largestStep = 0f;
            for (int i = 0; i < 20; i++)
            {
                PersonalityPose pose = model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Staggered);
                closed = Mathf.Max(closed, pose.EyeClosed);
                brow = Mathf.Max(brow, pose.BrowMillimetres);
                Assert.LessOrEqual(pose.BrowMillimetres, PersonalityPoseModel.MaxBrowMillimetres);
                if (i > 0)
                {
                    largestStep = Mathf.Max(largestStep, Mathf.Abs(pose.BrowMillimetres - last));
                }

                last = pose.BrowMillimetres;
            }

            Assert.Greater(closed, 0.9f, "A heavy hit does.");
            Assert.Greater(brow, browLight * 2f);

            for (int i = 0; i < 60; i++)
            {
                model.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
            }

            Assert.AreEqual(0f, model.Pose.BrowMillimetres, 1e-3f, "The brows come back.");
        }

        [Test]
        public void TheBrowsLowerInFlameAndDuringAStrike()
        {
            PersonalityPoseModel flame = Model(MagicDiscipline.Flame);
            PersonalityPoseModel aegis = Model(MagicDiscipline.Aegis);
            PersonalityPoseModel arcane = Model(MagicDiscipline.Arcane);
            for (int i = 0; i < 120; i++)
            {
                flame.Advance(Frame, MagicDiscipline.Flame, PersonalityActivity.Idle);
                aegis.Advance(Frame, MagicDiscipline.Aegis, PersonalityActivity.Idle);
                arcane.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
                Assert.AreEqual(0f, aegis.Pose.BrowMillimetres, 1e-4f, "Aegis keeps them level and still.");
            }

            Assert.Less(flame.Pose.BrowMillimetres, -1f, "Flame's brows are drawn down.");
            Assert.AreEqual(0f, arcane.Pose.BrowMillimetres, 1e-4f);

            float previous = arcane.Pose.BrowMillimetres;
            for (int i = 0; i < 30; i++)
            {
                float brow = arcane.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Action).BrowMillimetres;
                Assert.LessOrEqual(Mathf.Abs(brow - previous), 0.25f, "They move; they do not jump.");
                previous = brow;
            }

            Assert.Less(previous, -1f, "Concentration during a strike.");

            for (int i = 0; i < 30; i++)
            {
                arcane.Advance(Frame, MagicDiscipline.Arcane, PersonalityActivity.Idle);
            }

            Assert.AreEqual(0f, arcane.Pose.BrowMillimetres, 1e-4f);
        }

        // ------------------------------------------------------------ cost

        [Test]
        public void AFrameAllocatesNothing()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            probe = new object[8];
            if (GC.GetAllocatedBytesForCurrentThread() == before)
            {
                Assert.Inconclusive("This runtime does not count allocations. The result comes from the .NET runner of " +
                                    "Tools/ci/compile_check.sh.");
            }

            PersonalityPoseModel model = Model(MagicDiscipline.Arcane);
            // Warm up every path once: discipline changes, each activity, both reactions, looks.
            for (int i = 0; i < 2000; i++)
            {
                model.Advance(Frame, Disciplines[(i / 200) % 3], (PersonalityActivity)((i / 150) % 7));
                if (i % 300 == 0)
                {
                    model.NotifyImpact(0.7f, Vector3.back);
                    model.NotifyPowerfulCast(1f);
                }
            }

            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++)
            {
                model.Advance(Frame, Disciplines[(i / 200) % 3], (PersonalityActivity)((i / 150) % 7));
                if (i % 300 == 0)
                {
                    model.NotifyImpact(0.7f, Vector3.back);
                    model.NotifyPowerfulCast(1f);
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.AreEqual(0L, allocated, "The bearing is decided every frame and must allocate nothing.");
        }
    }
}
