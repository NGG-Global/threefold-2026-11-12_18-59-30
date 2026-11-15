using System;
using NUnit.Framework;
using Threefold.Characters;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The follow-through of hair and cloak bones (<see cref="SecondaryMotionModel"/>): bones are recognised by name,
    /// hang under gravity within their limit, lag behind movement and settle, stay stable at every frame rate and
    /// after a teleport, and cost no allocation. Plain C#, run outside Unity by Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class SecondaryMotionTests
    {
        private static readonly float[] FrameRates = { 20f, 30f, 60f, 120f };
        private static object probe;

        private static float Angle(Vector3 a, Vector3 b)
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static void AssertUnit(Vector3 direction, string message)
        {
            Assert.IsFalse(float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z), message);
            Assert.AreEqual(1f, direction.magnitude, 1e-3f, message);
        }

        [TestCase("sec_cloak_l_01", SecondaryKind.Cloak)]
        [TestCase("sec_cloak_r_02", SecondaryKind.Cloak)]
        [TestCase("sec_hair_01", SecondaryKind.Hair)]
        [TestCase("SEC_Hair_02", SecondaryKind.Hair)]
        [TestCase("sec_sash_01", SecondaryKind.Sash)]
        [TestCase("sec_pouch_01", SecondaryKind.Default)]
        [TestCase("sec_", SecondaryKind.Default)]
        [TestCase("sec_cloak", SecondaryKind.Cloak)]
        [TestCase("sec_cloaks_01", SecondaryKind.Default)]
        [TestCase("spine_03", SecondaryKind.Default)]
        public void TheKindComesFromTheBonesName(string name, SecondaryKind kind)
        {
            Assert.AreEqual(kind, SecondaryMotionModel.KindOf(name));
        }

        [TestCase("sec_cloak_l_01", true)]
        [TestCase("Sec_hair_01", true)]
        [TestCase("second_bone", false)]
        [TestCase("Head", false)]
        [TestCase("face_eyes", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlyBonesWithThePrefixAreSecondary(string name, bool secondary)
        {
            Assert.AreEqual(secondary, SecondaryMotionModel.IsSecondary(name));
        }

        [Test]
        public void TheKindsSwingDifferently()
        {
            var tuning = new SecondaryMotionTuning();
            Assert.Less(tuning.cloak.stiffness, tuning.hair.stiffness, "A cloak is slower than hair.");
            Assert.Greater(tuning.cloak.gravity, tuning.hair.gravity, "And hangs more.");
            Assert.GreaterOrEqual(tuning.cloak.maxAngleDegrees, 70f, "A cloak can hang down from a chest pitched far forward.");
            Assert.GreaterOrEqual(tuning.sash.maxAngleDegrees, 70f);
            Assert.That(tuning.hair.maxAngleDegrees, Is.InRange(40f, 60f));
            Assert.AreSame(tuning.sash, tuning.Get(SecondaryKind.Sash));
            Assert.AreSame(tuning.other, tuning.Get(SecondaryKind.Default));
        }

        [Test]
        public void TheModelHoldsAFixedNumberOfBones()
        {
            var model = new SecondaryMotionModel(null);
            for (int i = 0; i < SecondaryMotionModel.Capacity; i++)
            {
                Assert.AreEqual(i, model.Add(SecondaryKind.Hair, 0.1f));
            }

            Assert.AreEqual(-1, model.Add(SecondaryKind.Hair, 0.1f), "A bone too many is refused, not an error.");
            Assert.AreEqual(SecondaryMotionModel.Capacity, model.Count);
            Assert.AreEqual(Vector3.down, model.Step(99, Vector3.zero, Vector3.down, 0.016f), "An unknown bone keeps its animated direction.");
            model.Clear();
            Assert.AreEqual(0, model.Count);
        }

        [Test]
        public void ABoneHangingStraightDownStaysStill()
        {
            foreach (SecondaryKind kind in (SecondaryKind[])Enum.GetValues(typeof(SecondaryKind)))
            {
                var model = new SecondaryMotionModel(null);
                int bone = model.Add(kind, 0.3f);
                for (int i = 0; i < 300; i++)
                {
                    Vector3 direction = model.Step(bone, new Vector3(0f, 1.4f, 0f), Vector3.down, 1f / 60f);
                    Assert.Less(Angle(direction, Vector3.down), 0.01f, kind.ToString());
                }
            }
        }

        [Test]
        public void ACloakHangsTowardsTheGroundWhenTheChestPitchesForward()
        {
            // The chest pitched 80 degrees forward in a lunge: rigid to the chest, the cloak would stick out behind.
            Vector3 rest = new Vector3(0f, -Mathf.Cos(80f * Mathf.Deg2Rad), -Mathf.Sin(80f * Mathf.Deg2Rad));
            var tuning = new SecondaryMotionTuning();

            foreach (float rate in FrameRates)
            {
                var model = new SecondaryMotionModel(tuning);
                int cloak = model.Add(SecondaryKind.Cloak, 0.35f);
                int hair = model.Add(SecondaryKind.Hair, 0.12f);
                Vector3 cloakDirection = rest, hairDirection = rest;
                // Start hanging down in an upright body, then pitch over a quarter of a second and hold.
                for (int i = 0; i < Mathf.RoundToInt(3f * rate); i++)
                {
                    float t = Mathf.Clamp01(i / (0.25f * rate));
                    Vector3 animated = new Vector3(0f, -Mathf.Cos(80f * t * Mathf.Deg2Rad), -Mathf.Sin(80f * t * Mathf.Deg2Rad));
                    cloakDirection = model.Step(cloak, new Vector3(0f, 1.4f, 0f), animated, 1f / rate);
                    hairDirection = model.Step(hair, new Vector3(0f, 1.6f, 0f), animated, 1f / rate);
                    AssertUnit(cloakDirection, $"{rate} fps");
                    Assert.LessOrEqual(Angle(cloakDirection, animated), tuning.cloak.maxAngleDegrees + 0.1f, "Within the cloak's limit.");
                    Assert.LessOrEqual(Angle(hairDirection, animated), tuning.hair.maxAngleDegrees + 0.1f, "Within the hair's limit.");
                }

                Assert.Less(Angle(cloakDirection, Vector3.down), 20f, $"{rate} fps: the cloak hangs nearly straight down.");
                Assert.Greater(Angle(cloakDirection, rest), 60f, "Far from where a rigid cloak would be.");
                Assert.Greater(Angle(hairDirection, rest), 20f, "Hair gives way too, less.");
                Assert.Greater(Angle(hairDirection, Vector3.down), Angle(cloakDirection, Vector3.down), "Hair holds its shape more than cloth.");
                Assert.Less(Angle(cloakDirection, SecondaryMotionModel.HangDirection(rest, tuning.cloak.gravity, tuning.cloak.maxAngleDegrees)), 1f,
                    "Settled where it hangs.");
            }
        }

        [Test]
        public void ABoneLagsBehindAStartAndSettlesAgain()
        {
            foreach (float rate in FrameRates)
            {
                var model = new SecondaryMotionModel(null);
                int bone = model.Add(SecondaryKind.Cloak, 0.35f);
                float dt = 1f / rate;
                Vector3 head = new Vector3(0f, 1.4f, 0f);
                model.Step(bone, head, Vector3.down, dt);

                // The character starts running forwards (+z) at 6 m/s.
                float largestBack = 0f;
                Vector3 direction = Vector3.down;
                for (int i = 0; i < Mathf.RoundToInt(0.5f * rate); i++)
                {
                    head += Vector3.forward * (6f * dt);
                    direction = model.Step(bone, head, Vector3.down, dt);
                    AssertUnit(direction, $"{rate} fps");
                    largestBack = Mathf.Max(largestBack, -direction.z);
                    Assert.LessOrEqual(Angle(direction, Vector3.down), 75.1f);
                }

                Assert.Greater(largestBack, 0.15f, $"{rate} fps: the cloak swings back when the run starts.");

                // Still running after three seconds: it trails a little, steadily.
                for (int i = 0; i < Mathf.RoundToInt(3f * rate); i++)
                {
                    head += Vector3.forward * (6f * dt);
                    direction = model.Step(bone, head, Vector3.down, dt);
                }

                float trailing = Angle(direction, Vector3.down);
                Assert.Less(direction.z, 0f, "It trails behind, never ahead.");
                Assert.That(trailing, Is.InRange(1f, 40f), $"{rate} fps: trailing while running, not pinned at its limit.");

                // Standing again: back to hanging, within two seconds.
                for (int i = 0; i < Mathf.RoundToInt(2f * rate); i++)
                {
                    direction = model.Step(bone, head, Vector3.down, dt);
                }

                Assert.Less(Angle(direction, Vector3.down), 1f, $"{rate} fps: settled.");
            }
        }

        [Test]
        public void TheSwingIsMuchTheSameAtEveryFrameRate()
        {
            float Trail(float rate)
            {
                var model = new SecondaryMotionModel(null);
                int bone = model.Add(SecondaryKind.Hair, 0.12f);
                Vector3 head = Vector3.zero;
                Vector3 direction = Vector3.down;
                for (int i = 0; i < Mathf.RoundToInt(4f * rate); i++)
                {
                    head += Vector3.forward * (5f / rate);
                    direction = model.Step(bone, head, Vector3.down, 1f / rate);
                }

                return Angle(direction, Vector3.down);
            }

            float reference = Trail(120f);
            foreach (float rate in FrameRates)
            {
                Assert.AreEqual(reference, Trail(rate), Mathf.Max(1.5f, reference * 0.25f), $"{rate} fps");
            }
        }

        [Test]
        public void NothingMakesItUnstable()
        {
            var tuning = new SecondaryMotionTuning();
            // The stiffest and least damped the sliders allow.
            tuning.hair.stiffness = 400f;
            tuning.hair.damping = 0.5f;
            var model = new SecondaryMotionModel(tuning);
            var kinds = new[] { SecondaryKind.Cloak, SecondaryKind.Hair, SecondaryKind.Sash, SecondaryKind.Default };
            for (int i = 0; i < SecondaryMotionModel.Capacity; i++)
            {
                model.Add(kinds[i % kinds.Length], 0.05f + 0.03f * i);
            }

            var random = new System.Random(5);
            Vector3 head = Vector3.zero;
            Vector3 rest = Vector3.down;
            for (int frame = 0; frame < 20000; frame++)
            {
                float dt = 1f / FrameRates[random.Next(FrameRates.Length)];
                if (random.Next(150) == 0) dt = 1.5f;          // a hitch
                if (random.Next(200) == 0) dt = 0f;
                if (random.Next(400) == 0) dt = float.NaN;
                if (random.Next(300) == 0) head += new Vector3(40f, 3f, -25f); // a teleport
                head += new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.5f;
                if (random.Next(10) == 0)
                {
                    rest = new Vector3((float)random.NextDouble() - 0.5f, -(float)random.NextDouble() - 0.05f, (float)random.NextDouble() - 0.5f).normalized;
                }

                for (int i = 0; i < model.Count; i++)
                {
                    Vector3 direction = model.Step(i, head + Vector3.up * i * 0.1f, rest * (1f + i), dt);
                    AssertUnit(direction, $"frame {frame}, bone {i}");
                    float limit = tuning.Get(kinds[i % kinds.Length]).maxAngleDegrees;
                    Assert.LessOrEqual(Angle(direction, rest), limit + 0.2f, $"frame {frame}, bone {i}: within its limit");
                }
            }
        }

        [Test]
        public void ATeleportStartsTheBoneAtRestInsteadOfWhippingIt()
        {
            var model = new SecondaryMotionModel(null);
            int bone = model.Add(SecondaryKind.Cloak, 0.35f);
            Vector3 head = new Vector3(0f, 1.4f, 0f);
            for (int i = 0; i < 60; i++)
            {
                model.Step(bone, head, Vector3.down, 1f / 60f);
            }

            head += new Vector3(30f, 0f, 30f); // a checkpoint, a portal
            for (int i = 0; i < 60; i++)
            {
                Vector3 direction = model.Step(bone, head, Vector3.down, 1f / 60f);
                Assert.Less(Angle(direction, Vector3.down), 0.01f, "No swing from being moved.");
            }

            // The same after being hidden: ResetAll.
            head += Vector3.forward * 1.5f;
            model.ResetAll();
            Assert.Less(Angle(model.Step(bone, head, Vector3.down, 1f / 60f), Vector3.down), 0.01f);
        }

        [Test]
        public void ADegenerateDirectionIsHandedBack()
        {
            var model = new SecondaryMotionModel(null);
            int bone = model.Add(SecondaryKind.Hair, 0.1f);
            Assert.AreEqual(Vector3.zero, model.Step(bone, Vector3.zero, Vector3.zero, 0.016f));
            AssertUnit(model.Step(bone, Vector3.zero, Vector3.down, 0.016f), "It recovers on the next good frame.");
            AssertUnit(model.Step(bone, Vector3.zero, Vector3.up, 0.016f), "A bone the animation points straight up still gets a direction.");
        }

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

            var model = new SecondaryMotionModel(new SecondaryMotionTuning());
            for (int i = 0; i < 7; i++)
            {
                model.Add((SecondaryKind)(i % 4), 0.2f);
            }

            Vector3 head = Vector3.zero;
            for (int frame = 0; frame < 200; frame++)
            {
                head += Vector3.forward * 0.1f;
                for (int i = 0; i < model.Count; i++)
                {
                    model.Step(i, head, Vector3.down, 1f / 30f);
                }
            }

            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 3000; frame++)
            {
                head += frame % 500 == 0 ? Vector3.forward * 50f : Vector3.forward * 0.1f;
                for (int i = 0; i < model.Count; i++)
                {
                    model.Step(i, head, Vector3.down, 1f / 30f);
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.AreEqual(0L, allocated, "Seven bones are stepped every frame and must allocate nothing.");
        }
    }
}
