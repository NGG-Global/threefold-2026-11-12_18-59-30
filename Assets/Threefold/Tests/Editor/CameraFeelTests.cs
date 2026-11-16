using NUnit.Framework;
using Threefold.Cameras;
using Threefold.Feel;

namespace Threefold.Tests
{
    /// <summary>
    /// The camera's coming round behind the character, and the feel settings. These check the rules: when the
    /// camera may turn, which way, how fast, and that it never passes the heading. Whether the result is
    /// comfortable can only be judged with the game in the hand. Plain C#: these run outside Unity too.
    /// </summary>
    public sealed class CameraFeelTests
    {
        private const float Frame = 1f / 60f;
        private const float Running = 4f;

        private static CameraRecenterSettings On()
        {
            CameraRecenterSettings settings = CameraRecenterSettings.Default;
            settings.Enabled = true;
            return settings;
        }

        /// <summary>Lets the camera follow for a while and returns the heading it reaches.</summary>
        private static float Follow(CameraRecenter recenter, CameraRecenterSettings settings, float cameraYaw, float heading,
            float seconds, float frame = Frame, float speed = Running)
        {
            for (float time = 0f; time < seconds; time += frame)
            {
                cameraYaw += recenter.Step(settings, cameraYaw, heading, speed, false, frame);
            }

            return cameraYaw;
        }

        [Test]
        public void ByDefaultTheCameraTurnsOnlyWhenThePlayerTurnsIt()
        {
            Assert.IsFalse(CameraRecenterSettings.Default.Enabled);
            Assert.IsFalse(FeelSettings.Default.AutoRecenter);

            var recenter = new CameraRecenter();
            Assert.AreEqual(0f, Follow(recenter, CameraRecenterSettings.Default, 0f, 90f, 10f));
        }

        [Test]
        public void ItWaitsUntilThePlayerHasLeftTheCameraAlone()
        {
            var recenter = new CameraRecenter();
            CameraRecenterSettings settings = On();

            Assert.AreEqual(0f, Follow(recenter, settings, 0f, 90f, settings.DelaySeconds - 0.1f), "Nothing before the delay.");
            Assert.Greater(Follow(recenter, settings, 0f, 90f, 1f), 0f, "Then it starts.");
        }

        [Test]
        public void LookInputStopsItAtOnceAndTheWaitStartsAgain()
        {
            var recenter = new CameraRecenter();
            CameraRecenterSettings settings = On();
            float yaw = Follow(recenter, settings, 0f, 90f, settings.DelaySeconds + 1f);
            Assert.Greater(yaw, 0f);

            Assert.AreEqual(0f, recenter.Step(settings, yaw, 90f, Running, true, Frame), "A frame with look input turns nothing.");
            Assert.AreEqual(0f, recenter.QuietSeconds);
            Assert.AreEqual(yaw, Follow(recenter, settings, yaw, 90f, settings.DelaySeconds - 0.1f), "And the whole delay is waited again.");
        }

        [Test]
        public void ItNeverTurnsWhileTheCharacterStands()
        {
            var recenter = new CameraRecenter();
            Assert.AreEqual(0f, Follow(recenter, On(), 0f, 90f, 10f, Frame, 0f));
            Assert.AreEqual(0f, Follow(recenter, On(), 0f, 90f, 10f, Frame, On().MinSpeed * 0.9f));
        }

        [TestCase(0f, 60f)]
        [TestCase(0f, -60f)]
        [TestCase(350f, 40f)]
        [TestCase(10f, 300f)]
        [TestCase(-170f, 150f)]
        public void ItTurnsTheShortWayAndComesToRestBesideTheHeadingWithoutPassingIt(float cameraYaw, float heading)
        {
            var recenter = new CameraRecenter();
            CameraRecenterSettings settings = On();
            float before = CameraRecenter.DeltaAngle(cameraYaw, heading);
            float yaw = cameraYaw;
            float previous = System.Math.Abs(before);

            for (float time = 0f; time < 12f; time += Frame)
            {
                yaw += recenter.Step(settings, yaw, heading, Running, false, Frame);
                float remaining = CameraRecenter.DeltaAngle(yaw, heading);
                Assert.LessOrEqual(System.Math.Abs(remaining), previous + 1e-3f, "It only ever comes closer.");
                Assert.IsTrue(remaining == 0f || System.Math.Sign(remaining) == System.Math.Sign(before), "It never passes the heading.");
                previous = System.Math.Abs(remaining);
            }

            Assert.AreEqual(settings.DeadZoneDegrees, previous, 0.05f, "It stops at the edge of the dead zone.");
        }

        [Test]
        public void WithinTheDeadZoneItStays()
        {
            var recenter = new CameraRecenter();
            CameraRecenterSettings settings = On();
            Assert.AreEqual(0f, Follow(recenter, settings, 0f, settings.DeadZoneDegrees - 1f, 10f));
        }

        [TestCase(150f)]
        [TestCase(180f)]
        [TestCase(-135f)]
        public void RunningTowardsTheCameraLeavesItWhereItIs(float heading)
        {
            var recenter = new CameraRecenter();
            Assert.AreEqual(0f, Follow(recenter, On(), 0f, heading, 10f));
        }

        [Test]
        public void ItIsNeverFasterThanItsLimitAndEasesInAndOut()
        {
            var recenter = new CameraRecenter();
            CameraRecenterSettings settings = On();
            float yaw = 0f;
            float fastest = 0f;
            float first = -1f;

            for (float time = 0f; time < 8f; time += Frame)
            {
                float step = recenter.Step(settings, yaw, 100f, Running, false, Frame);
                if (step > 0f && first < 0f)
                {
                    first = step;
                }

                fastest = System.Math.Max(fastest, step);
                yaw += step;
            }

            Assert.LessOrEqual(fastest, settings.DegreesPerSecond * Frame + 1e-4f);
            Assert.Less(first, fastest * 0.25f, "It starts gently.");
            Assert.Greater(fastest, settings.DegreesPerSecond * Frame * 0.8f, "It does reach about its limit in a long turn.");
        }

        [Test]
        public void TheFrameRateBarelyChangesWhereItGets()
        {
            CameraRecenterSettings settings = On();
            float at30 = Follow(new CameraRecenter(), settings, 0f, 100f, 3f, 1f / 30f);
            float at60 = Follow(new CameraRecenter(), settings, 0f, 100f, 3f, 1f / 60f);
            float at120 = Follow(new CameraRecenter(), settings, 0f, 100f, 3f, 1f / 120f);

            Assert.AreEqual(at60, at30, 2.5f);
            Assert.AreEqual(at60, at120, 1.5f);
        }

        [Test]
        public void NonsenseTimeTurnsNothing()
        {
            var recenter = new CameraRecenter();
            CameraRecenterSettings settings = On();
            Assert.AreEqual(0f, recenter.Step(settings, 0f, 90f, Running, false, 0f));
            Assert.AreEqual(0f, recenter.Step(settings, 0f, 90f, Running, false, -1f));
            Assert.AreEqual(0f, recenter.Step(settings, 0f, 90f, Running, false, float.NaN));
        }

        [TestCase(0f, 90f, 90f)]
        [TestCase(350f, 10f, 20f)]
        [TestCase(10f, 350f, -20f)]
        [TestCase(0f, 180f, 180f)]
        [TestCase(720f, 90f, 90f)]
        public void TheShortestTurnIsFound(float from, float to, float expected)
        {
            Assert.AreEqual(expected, CameraRecenter.DeltaAngle(from, to), 1e-3f);
        }

        // ------------------------------------------------------------ feel settings

        [Test]
        public void TheDefaultsAreTheGameAsItWas()
        {
            FeelSettings defaults = FeelSettings.Default;
            Assert.AreEqual(1f, defaults.LookSpeed, "Look speed unchanged.");
            Assert.AreEqual(1f, defaults.Shake, "Shake unchanged.");
            Assert.IsFalse(defaults.AutoRecenter, "No turn the player did not make.");

            FeelSettings clamped = defaults.Clamped();
            Assert.AreEqual(defaults.LookSpeed, clamped.LookSpeed);
            Assert.AreEqual(defaults.RecenterDelay, clamped.RecenterDelay);
            Assert.AreEqual(defaults.RecenterSpeed, clamped.RecenterSpeed);
        }

        [Test]
        public void EveryButtonGoesRoundItsStepsAndComesBackToTheDefault()
        {
            FeelSettings settings = FeelSettings.Default;
            bool lookBack = false, shakeBack = false, delayBack = false, speedBack = false, shakeOff = false;
            FeelSettings look = settings, shake = settings, delay = settings, speed = settings;

            for (int i = 0; i < 8; i++)
            {
                look = look.WithNextLookSpeed();
                shake = shake.WithNextShake();
                delay = delay.WithNextRecenterDelay();
                speed = speed.WithNextRecenterSpeed();

                Assert.GreaterOrEqual(look.LookSpeed, FeelSettings.MinLookSpeed);
                Assert.LessOrEqual(look.LookSpeed, FeelSettings.MaxLookSpeed);
                Assert.GreaterOrEqual(shake.Shake, 0f);
                Assert.LessOrEqual(shake.Shake, FeelSettings.MaxShake);

                lookBack |= look.LookSpeed == settings.LookSpeed;
                shakeBack |= shake.Shake == settings.Shake;
                shakeOff |= shake.Shake == 0f;
                delayBack |= delay.RecenterDelay == settings.RecenterDelay;
                speedBack |= speed.RecenterSpeed == settings.RecenterSpeed;
            }

            Assert.IsTrue(lookBack && shakeBack && delayBack && speedBack, "Each value can be brought back to its default by pressing on.");
            Assert.IsTrue(shakeOff, "Shake can be switched off altogether.");
        }

        [Test]
        public void AChangeOfOneValueLeavesTheOthers()
        {
            FeelSettings changed = FeelSettings.Default.WithAutoRecenter(true).WithNextLookSpeed();
            Assert.IsTrue(changed.AutoRecenter);
            Assert.AreNotEqual(FeelSettings.Default.LookSpeed, changed.LookSpeed);
            Assert.AreEqual(FeelSettings.Default.Shake, changed.Shake);
            Assert.AreEqual(FeelSettings.Default.RecenterDelay, changed.RecenterDelay);
        }

        [Test]
        public void SavedNonsenseBecomesUsableValues()
        {
            var broken = new FeelSettings { LookSpeed = float.NaN, Shake = -3f, RecenterDelay = 0f, RecenterSpeed = 100000f };
            FeelSettings mended = broken.Clamped();
            Assert.AreEqual(FeelSettings.Default.LookSpeed, mended.LookSpeed);
            Assert.AreEqual(0f, mended.Shake);
            Assert.Greater(mended.RecenterDelay, 0f);
            Assert.LessOrEqual(mended.RecenterSpeed, 180f);

            Assert.AreEqual(FeelSettings.MaxLookSpeed, new FeelSettings { LookSpeed = 50f, Shake = 1f, RecenterDelay = 1f, RecenterSpeed = 45f }.Clamped().LookSpeed);
        }
    }
}
