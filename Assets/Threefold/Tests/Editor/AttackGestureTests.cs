using NUnit.Framework;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>Tap, hold, charge, aim, release and cancel, as the ATTACK button and the mouse button produce them.</summary>
    public sealed class AttackGestureTests
    {
        private AttackGestureSettings settings;
        private AttackGestureRecognizer attack;

        [SetUp]
        public void SetUp()
        {
            settings = new AttackGestureSettings
            {
                holdThreshold = 0.22f,
                tapSlop = 14f,
                dragStartsCharge = true,
                fullChargeSeconds = 1f,
                aimRadius = 64f,
                aimDeadZone = 0.2f,
                aimSensitivity = 1f,
                maxAimMagnitude = 1f,
            };
            attack = new AttackGestureRecognizer(settings);
        }

        /// <summary>One frame: clear events, apply the input, advance time.</summary>
        private void Frame(float time, System.Action input = null)
        {
            attack.BeginFrame();
            input?.Invoke();
            attack.Update(time);
        }

        [Test]
        public void ShortPressIsATapAndNotACharge()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.1f, () => attack.Release(0.1f, false));

            Assert.IsTrue(attack.Tapped);
            Assert.IsFalse(attack.ChargeStarted);
            Assert.IsFalse(attack.ChargeReleased);
            Assert.AreEqual(AttackGesturePhase.Released, attack.Phase);
            Assert.AreEqual(AttackReleaseKind.Tap, attack.LastRelease);
        }

        [Test]
        public void TapWithSmallFingerJitterIsStillATap()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.05f, () => attack.Move(0.05f, new Vector2(6f, -5f), false));
            Frame(0.12f, () => attack.Release(0.12f, false));

            Assert.IsTrue(attack.Tapped);
            Assert.IsFalse(attack.ChargeReleased);
        }

        [Test]
        public void HoldingPastTheThresholdStartsACharge()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.2f);
            Assert.AreEqual(AttackGesturePhase.Pressed, attack.Phase);

            Frame(0.23f);
            Assert.AreEqual(AttackGesturePhase.Charging, attack.Phase);
            Assert.IsTrue(attack.ChargeStarted);
            Assert.IsFalse(attack.Tapped);
        }

        [Test]
        public void ChargeStartedIsReportedForOneFrameOnly()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.25f);
            Assert.IsTrue(attack.ChargeStarted);

            Frame(0.3f);
            Assert.IsFalse(attack.ChargeStarted);
            Assert.IsTrue(attack.IsCharging);
        }

        [Test]
        public void ReleasingAChargeFiresTheChargedAttackAndNeverATap()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f);
            Frame(0.5f, () => attack.Release(0.5f, false));

            Assert.IsTrue(attack.ChargeReleased);
            Assert.IsFalse(attack.Tapped);
            Assert.IsFalse(attack.Cancelled);
            Assert.AreEqual(AttackReleaseKind.Charged, attack.LastRelease);
        }

        [Test]
        public void AReleaseAfterTheThresholdIsAChargeEvenIfNoUpdateRanInBetween()
        {
            // A long frame: the press and the release are the only events, 0.3 s apart.
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            attack.BeginFrame();
            attack.Release(0.3f, false);

            Assert.IsTrue(attack.ChargeStarted);
            Assert.IsTrue(attack.ChargeReleased);
            Assert.IsFalse(attack.Tapped);
        }

        [Test]
        public void DraggingBeyondTheTapSlopStartsTheChargeEarly()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.08f, () => attack.Move(0.08f, new Vector2(40f, 0f), false));

            Assert.IsTrue(attack.IsCharging);
            Assert.IsTrue(attack.ChargeStarted);

            Frame(0.12f, () => attack.Release(0.12f, false));
            Assert.IsTrue(attack.ChargeReleased);
            Assert.IsFalse(attack.Tapped, "A quick drag to aim must not become a melee tap.");
        }

        [Test]
        public void DragStartingTheChargeCanBeSwitchedOff()
        {
            settings.dragStartsCharge = false;
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.08f, () => attack.Move(0.08f, new Vector2(40f, 0f), false));

            Assert.AreEqual(AttackGesturePhase.Pressed, attack.Phase);
        }

        [Test]
        public void WithDragToChargeOffAQuickDragIsNeitherATapNorACharge()
        {
            settings.dragStartsCharge = false;
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.08f, () => attack.Move(0.08f, new Vector2(60f, 0f), false));
            Frame(0.15f, () => attack.Release(0.15f, false));

            Assert.IsFalse(attack.Tapped);
            Assert.IsFalse(attack.ChargeReleased);
            Assert.IsTrue(attack.Cancelled);
        }

        [Test]
        public void WithDragToChargeOffADragHeldPastTheThresholdStillCharges()
        {
            settings.dragStartsCharge = false;
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.08f, () => attack.Move(0.08f, new Vector2(60f, 0f), false));
            Frame(0.3f);
            Frame(0.4f, () => attack.Release(0.4f, false));

            Assert.IsTrue(attack.ChargeReleased);
        }

        [Test]
        public void DraggingWhileChargingAims()
        {
            Frame(0f, () => attack.Press(0f, new Vector2(100f, 100f)));
            Frame(0.3f);
            Frame(0.35f, () => attack.Move(0.35f, new Vector2(100f + 64f, 100f), false));

            Assert.AreEqual(AttackGesturePhase.Aiming, attack.Phase);
            Assert.AreEqual(1f, attack.Aim.x, 0.0001f);
            Assert.AreEqual(0f, attack.Aim.y, 0.0001f);
        }

        [Test]
        public void SmallDragsInsideTheAimDeadZoneKeepChargingWithoutAim()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f);
            Frame(0.35f, () => attack.Move(0.35f, new Vector2(10f, 0f), false));

            Assert.AreEqual(AttackGesturePhase.Charging, attack.Phase);
            Assert.AreEqual(Vector2.zero, attack.Aim);
        }

        [Test]
        public void ReturningToThePressPointGoesBackFromAimingToCharging()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f, () => attack.Move(0.3f, new Vector2(0f, 60f), false));
            Assert.AreEqual(AttackGesturePhase.Aiming, attack.Phase);

            Frame(0.4f, () => attack.Move(0.4f, new Vector2(2f, 2f), false));
            Assert.AreEqual(AttackGesturePhase.Charging, attack.Phase);
        }

        [Test]
        public void AimIsClampedToTheMaximum()
        {
            settings.maxAimMagnitude = 0.8f;
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f, () => attack.Move(0.3f, new Vector2(500f, 500f), false));

            Assert.AreEqual(0.8f, attack.Aim.magnitude, 0.0001f);
            Assert.Greater(attack.Aim.x, 0f);
            Assert.Greater(attack.Aim.y, 0f);
        }

        [Test]
        public void HigherAimSensitivityReachesFullAimWithLessTravel()
        {
            Vector2 offset = new Vector2(32f, 0f);
            settings.aimSensitivity = 1f;
            float normal = AttackGestureRecognizer.ComputeAim(offset, settings).magnitude;
            settings.aimSensitivity = 2f;
            float sensitive = AttackGestureRecognizer.ComputeAim(offset, settings).magnitude;

            Assert.Greater(sensitive, normal);
            Assert.AreEqual(1f, sensitive, 0.0001f);
        }

        [Test]
        public void AimRisesFromZeroAtTheDeadZoneEdge()
        {
            Vector2 justOutside = new Vector2(64f * 0.2f + 0.01f, 0f);
            Assert.Less(AttackGestureRecognizer.ComputeAim(justOutside, settings).magnitude, 0.01f);
        }

        [Test]
        public void ChargeProgressRisesToOneAtFullCharge()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.22f);
            Assert.AreEqual(0f, attack.ChargeProgress, 0.001f);

            Frame(0.72f);
            Assert.AreEqual(0.5f, attack.ChargeProgress, 0.001f);

            Frame(3f);
            Assert.AreEqual(1f, attack.ChargeProgress, 0.0001f);
        }

        [Test]
        public void TheReleaseFrameReportsTheAimAndChargeAtRelease()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f);
            Frame(0.4f, () => attack.Move(0.4f, new Vector2(-64f, 0f), false));
            Frame(0.72f, () => attack.Release(0.72f, false));

            AttackGestureFrame frame = attack.Snapshot();
            Assert.IsTrue(frame.ChargeReleased);
            Assert.AreEqual(-1f, frame.Aim.x, 0.0001f);
            Assert.AreEqual(0.5f, frame.ChargeProgress, 0.001f);
        }

        [Test]
        public void ReleasingOverTheCancelZoneCancelsWithoutFiring()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f, () => attack.Move(0.3f, new Vector2(0f, 150f), true));
            Assert.IsTrue(attack.CancelArmed);

            Frame(0.4f, () => attack.Release(0.4f, true));

            Assert.IsTrue(attack.Cancelled);
            Assert.IsFalse(attack.ChargeReleased);
            Assert.IsFalse(attack.Tapped);
            Assert.AreEqual(AttackGesturePhase.Cancelled, attack.Phase);
        }

        [Test]
        public void LeavingTheCancelZoneBeforeReleasingStillFires()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f, () => attack.Move(0.3f, new Vector2(0f, 150f), true));
            Frame(0.35f, () => attack.Move(0.35f, new Vector2(0f, 40f), false));
            Assert.IsFalse(attack.CancelArmed);

            Frame(0.4f, () => attack.Release(0.4f, false));
            Assert.IsTrue(attack.ChargeReleased);
        }

        [Test]
        public void ASystemCancelledTouchNeverFires()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f);
            Frame(0.4f, () => attack.Cancel());

            Assert.IsTrue(attack.Cancelled);
            Assert.IsFalse(attack.ChargeReleased);
        }

        [Test]
        public void CancellingAPressBeforeTheThresholdGivesNoTap()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.05f, () => attack.Cancel());

            Assert.IsTrue(attack.Cancelled);
            Assert.IsFalse(attack.Tapped);
        }

        [Test]
        public void ReleasedAndCancelledLastOneFrameThenReturnToIdle()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.1f, () => attack.Release(0.1f, false));
            Assert.AreEqual(AttackGesturePhase.Released, attack.Phase);

            Frame(0.2f);
            Assert.AreEqual(AttackGesturePhase.Idle, attack.Phase);
            Assert.IsFalse(attack.Tapped);
        }

        [Test]
        public void ASecondPressDuringAPressIsIgnored()
        {
            Assert.IsTrue(attack.Press(0f, Vector2.zero));
            Assert.IsFalse(attack.Press(0.05f, new Vector2(50f, 50f)));
        }

        [Test]
        public void AReleaseWithoutAPressDoesNothing()
        {
            Frame(0f, () => attack.Release(0f, false));
            Assert.IsFalse(attack.Tapped);
            Assert.IsFalse(attack.ChargeReleased);
            Assert.AreEqual(AttackGesturePhase.Idle, attack.Phase);
        }

        // ------------------------------------------------------- block rules

        [Test]
        public void PressingBlockCancelsACharge()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.3f);

            attack.BeginFrame();
            attack.Update(0.35f);
            bool cancelled = CombatInputRules.ApplyBlock(attack, true, true);

            Assert.IsTrue(cancelled);
            Assert.IsTrue(attack.Cancelled);
            Assert.IsFalse(attack.ChargeReleased);
        }

        [Test]
        public void PressingBlockDuringATapPressGivesNoTap()
        {
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            attack.BeginFrame();
            CombatInputRules.ApplyBlock(attack, true, true);
            Frame(0.1f, () => attack.Release(0.1f, false));

            Assert.IsFalse(attack.Tapped);
        }

        [Test]
        public void BlockWithoutAnAttackChangesNothing()
        {
            Assert.IsFalse(CombatInputRules.ApplyBlock(attack, true, true));
            Assert.AreEqual(AttackGesturePhase.Idle, attack.Phase);
        }

        [Test]
        public void AttackPressesAreRefusedWhileBlockIsHeld()
        {
            Assert.IsFalse(CombatInputRules.AttackPressAllowed(true));
            Assert.IsTrue(CombatInputRules.AttackPressAllowed(false));
        }

        // ------------------------------------------------- desktop and merging

        [Test]
        public void DesktopButtonClickIsATapAndHoldIsAChargeAimedByTheCamera()
        {
            // The desktop source presses and releases at the origin: the camera aims, not a drag.
            Frame(0f, () => attack.Press(0f, Vector2.zero));
            Frame(0.09f, () => attack.Release(0.09f, false));
            Assert.IsTrue(attack.Tapped);

            Frame(1f, () => attack.Press(1f, Vector2.zero));
            Frame(1.5f);
            Frame(1.6f, () => attack.Release(1.6f, false));
            Assert.IsTrue(attack.ChargeReleased);
            Assert.AreEqual(Vector2.zero, attack.Aim);
        }

        [Test]
        public void MergingTakesThePhaseOfTheFurthestSourceAndCombinesEvents()
        {
            var idle = default(AttackGestureFrame);
            var charging = new AttackGestureFrame(AttackGesturePhase.Aiming, false, false, false, false,
                new Vector2(0.5f, 0f), 0.4f, false);
            var tapped = new AttackGestureFrame(AttackGesturePhase.Released, true, false, false, false, Vector2.zero, 0f, false);

            AttackGestureFrame a = AttackGestureFrame.Merge(idle, charging);
            Assert.AreEqual(AttackGesturePhase.Aiming, a.Phase);
            Assert.AreEqual(0.5f, a.Aim.x, 0.0001f);

            AttackGestureFrame b = AttackGestureFrame.Merge(charging, tapped);
            Assert.AreEqual(AttackGesturePhase.Released, b.Phase);
            Assert.IsTrue(b.Tapped);
        }

        [Test]
        public void DefaultFrameIsIdle()
        {
            AttackGestureFrame frame = default;
            Assert.AreEqual(AttackGesturePhase.Idle, frame.Phase);
            Assert.IsFalse(frame.IsActive);
        }
    }
}
