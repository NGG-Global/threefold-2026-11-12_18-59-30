using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.InputHandling;
using Threefold.TouchControls;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Finger ownership with the real layout on an 800 x 400 dp screen: the fixed joystick, the camera,
    /// ATTACK, BLOCK, JUMP and the discipline selector, used with one, two and three fingers.
    /// The controls are stand-ins built on the same models as the real ones (see TouchTestDoubles).
    /// </summary>
    public sealed class MultiTouchRoutingTests
    {
        private static readonly Vector2 Screen = new Vector2(800f, 400f);

        private PointerRouter router;
        private FakeJoystick joystick;
        private FakeLookArea look;
        private FakeAttackButton attack;
        private FakeButton block;
        private FakeButton jump;
        private FakeButton interact;
        private FakeSelector selector;
        private FakeFan fan;
        private float time;

        private Vector2 JoystickCentre => joystick.Model.Centre;
        private Vector2 AttackCentre => MobileControlsLayout.Default.Attack.Resolve(Screen);

        [SetUp]
        public void SetUp()
        {
            MobileControlsLayout layout = MobileControlsLayout.Default;
            joystick = new FakeJoystick(layout.Joystick.Resolve(Screen));
            look = new FakeLookArea();
            attack = new FakeAttackButton(layout.Attack.Resolve(Screen), layout.Attack.TouchDiameter * 0.5f);
            block = new FakeButton(layout.Block.Resolve(Screen), layout.Block.TouchDiameter * 0.5f);
            jump = new FakeButton(layout.Jump.Resolve(Screen), layout.Jump.TouchDiameter * 0.5f);
            interact = new FakeButton(layout.Interact.Resolve(Screen), layout.Interact.TouchDiameter * 0.5f);
            selector = new FakeSelector(layout.Indicator.Resolve(Screen), layout.Indicator.TouchDiameter * 0.5f,
                layout.FanSlotOffsetsFromIndicator(3));
            fan = new FakeFan(selector);

            router = new PointerRouter();
            router.SetTargets(new IPointerTarget[] { fan, selector, attack, block, interact, jump, joystick, look });
            router.PointerBegan += (id, position, owner) =>
            {
                if (owner != selector && owner != fan && owner != joystick)
                {
                    selector.Model.PressOutside();
                }
            };
            time = 0f;
        }

        /// <summary>One frame: every finger currently down must be reported, as Unity's touch list does.</summary>
        private void Frame(System.Action pointers)
        {
            time += 1f / 60f;
            attack.Time = time;
            selector.Time = time;
            attack.Gesture.BeginFrame();
            selector.Model.BeginFrame();
            router.BeginFrame();
            pointers();
            router.EndFrame();
            attack.Gesture.Update(time);
            selector.Model.Update(time);
        }

        private void Wait(float seconds, System.Action pointers)
        {
            int frames = Mathf.CeilToInt(seconds * 60f);
            for (int i = 0; i < frames; i++)
            {
                Frame(pointers);
            }
        }

        // -------------------------------------------------------- fixed joystick

        [Test]
        public void ATouchAwayFromTheJoystickTurnsTheCameraAndDoesNotMove()
        {
            Vector2 leftButFar = JoystickCentre + new Vector2(150f, 150f);
            Frame(() => router.Begin(1, leftButFar));
            Frame(() => router.Move(1, leftButFar + new Vector2(30f, 0f)));

            Assert.IsFalse(joystick.IsCaptured);
            Assert.AreEqual(Vector2.zero, joystick.Value);
            Assert.IsTrue(look.IsCaptured);
            Assert.AreEqual(30f, look.Total.x, 0.001f);
        }

        [Test]
        public void ATouchOnTheJoystickMovesAndNeverTurnsTheCamera()
        {
            Frame(() => router.Begin(1, JoystickCentre));
            Frame(() => router.Move(1, JoystickCentre + new Vector2(0f, 55f)));

            Assert.IsTrue(joystick.IsCaptured);
            Assert.AreEqual(1f, joystick.Value.y, 0.0001f);
            Assert.IsFalse(look.IsCaptured);
            Assert.AreEqual(Vector2.zero, look.Total);
        }

        [Test]
        public void TheJoystickKeepsItsFingerWhenTheThumbDriftsOutsideTheActivationArea()
        {
            Frame(() => router.Begin(1, JoystickCentre));
            Frame(() => router.Move(1, JoystickCentre + new Vector2(200f, 0f)));

            Assert.IsTrue(joystick.IsCaptured);
            Assert.AreEqual(1f, joystick.Value.x, 0.0001f);
            Assert.AreEqual(JoystickCentre, joystick.Model.Centre, "The fixed joystick must never move.");
        }

        // ------------------------------------------------ movement and camera

        [Test]
        public void MovementAndCameraWorkAtTheSameTime()
        {
            Vector2 cameraStart = new Vector2(400f, 250f);
            Frame(() =>
            {
                router.Begin(1, JoystickCentre);
                router.Begin(2, cameraStart);
            });
            Frame(() =>
            {
                router.Move(1, JoystickCentre + new Vector2(55f, 0f));
                router.Move(2, cameraStart + new Vector2(-40f, 10f));
            });

            Assert.AreEqual(1f, joystick.Value.x, 0.0001f);
            Assert.AreEqual(new Vector2(-40f, 10f), look.Total);
        }

        [Test]
        public void StartingAndEndingACameraDragNeverResetsMovement()
        {
            Vector2 held = JoystickCentre + new Vector2(0f, 55f);
            Frame(() => router.Begin(1, held));
            Vector2 before = joystick.Value;

            Frame(() =>
            {
                router.Move(1, held);
                router.Begin(2, new Vector2(450f, 300f));
            });
            Assert.AreEqual(before, joystick.Value);

            Frame(() =>
            {
                router.Move(1, held);
                router.End(2, new Vector2(470f, 300f), false);
            });
            Assert.AreEqual(before, joystick.Value);
            Assert.IsTrue(joystick.IsCaptured);
        }

        [Test]
        public void ADragThatStartsOnAButtonNeverTurnsTheCamera()
        {
            Vector2 onJump = MobileControlsLayout.Default.Jump.Resolve(Screen);
            Frame(() => router.Begin(1, onJump));
            Frame(() => router.Move(1, onJump + new Vector2(-200f, 120f)));

            Assert.IsTrue(jump.Held);
            Assert.IsFalse(look.IsCaptured);
            Assert.AreEqual(Vector2.zero, look.Total);
        }

        [Test]
        public void ACameraDragThatCrossesAButtonStaysWithTheCamera()
        {
            Vector2 start = new Vector2(420f, 250f);
            Vector2 overBlock = MobileControlsLayout.Default.Block.Resolve(Screen);
            Frame(() => router.Begin(1, start));
            Frame(() => router.Move(1, overBlock));

            Assert.IsTrue(look.IsCaptured);
            Assert.IsFalse(block.Held);
        }

        [Test]
        public void AHiddenButtonPassesItsTouchToTheCamera()
        {
            interact.Available = false;
            Vector2 onInteract = MobileControlsLayout.Default.Interact.Resolve(Screen);
            Frame(() => router.Begin(1, onInteract));

            Assert.IsFalse(interact.IsCaptured);
            Assert.IsTrue(look.IsCaptured);
        }

        // ------------------------------------------------------ several fingers

        [Test]
        public void ThreeFingersMoveTurnAndChargeIndependently()
        {
            Vector2 move = JoystickCentre + new Vector2(0f, 55f);
            Vector2 cam = new Vector2(420f, 260f);
            Frame(() =>
            {
                router.Begin(1, move);
                router.Begin(2, cam);
                router.Begin(3, AttackCentre);
            });

            Wait(0.3f, () =>
            {
                router.Move(1, move);
                router.Move(2, cam);
                router.Move(3, AttackCentre);
            });

            Assert.IsTrue(attack.Gesture.IsCharging);
            Assert.AreEqual(1f, joystick.Value.y, 0.0001f);
            Assert.IsTrue(look.IsCaptured);

            // Aim while still moving and turning.
            Frame(() =>
            {
                router.Move(1, move);
                router.Move(2, cam + new Vector2(25f, 0f));
                router.Move(3, AttackCentre + new Vector2(-64f, 0f));
            });

            Assert.AreEqual(AttackGesturePhase.Aiming, attack.Gesture.Phase);
            Assert.AreEqual(1f, joystick.Value.y, 0.0001f, "Aiming must not disturb movement.");
            Assert.AreEqual(25f, look.Total.x, 0.001f);
        }

        [Test]
        public void ReleasingOneFingerNeverEndsAnotherFingersAction()
        {
            Vector2 move = JoystickCentre + new Vector2(55f, 0f);
            Vector2 cam = new Vector2(420f, 260f);
            Frame(() =>
            {
                router.Begin(1, move);
                router.Begin(2, cam);
                router.Begin(3, AttackCentre);
            });
            Wait(0.3f, () =>
            {
                router.Move(1, move);
                router.Move(2, cam);
                router.Move(3, AttackCentre);
            });

            // The camera finger lifts: the charge and the movement continue.
            Frame(() =>
            {
                router.Move(1, move);
                router.End(2, cam, false);
                router.Move(3, AttackCentre);
            });
            Assert.IsTrue(attack.Gesture.IsCharging);
            Assert.AreEqual(1f, joystick.Value.x, 0.0001f);

            // The joystick finger lifts: the charge continues and movement stops.
            Frame(() =>
            {
                router.End(1, move, false);
                router.Move(3, AttackCentre);
            });
            Assert.IsTrue(attack.Gesture.IsCharging);
            Assert.AreEqual(Vector2.zero, joystick.Value);

            // The attack finger lifts: the charged attack fires.
            Frame(() => router.End(3, AttackCentre, false));
            Assert.IsTrue(attack.Gesture.ChargeReleased);
        }

        [Test]
        public void TwoButtonsCanBeHeldTogether()
        {
            Vector2 onBlock = MobileControlsLayout.Default.Block.Resolve(Screen);
            Vector2 onJump = MobileControlsLayout.Default.Jump.Resolve(Screen);
            Frame(() =>
            {
                router.Begin(1, onBlock);
                router.Begin(2, onJump);
            });

            Assert.IsTrue(block.Held);
            Assert.IsTrue(jump.Held);
        }

        [Test]
        public void ASecondFingerOnAHeldButtonGoesToTheCameraInstead()
        {
            Vector2 onJump = MobileControlsLayout.Default.Jump.Resolve(Screen);
            Frame(() => router.Begin(1, onJump));
            Frame(() =>
            {
                router.Move(1, onJump);
                router.Begin(2, onJump + new Vector2(5f, 5f));
            });

            Assert.AreEqual(1, jump.PointerId);
            Assert.AreEqual(2, look.PointerId);
        }

        // ------------------------------------------------- tap versus hold

        [Test]
        public void AQuickTapOnAttackIsATap()
        {
            Frame(() => router.Begin(1, AttackCentre));
            Frame(() => router.Move(1, AttackCentre));
            Frame(() => router.End(1, AttackCentre, false));

            Assert.IsTrue(attack.Gesture.Tapped);
            Assert.IsFalse(attack.Gesture.ChargeReleased);
        }

        [Test]
        public void HoldingAttackChargesAndReleasingFiresWithoutATap()
        {
            Frame(() => router.Begin(1, AttackCentre));
            bool tappedDuringHold = false;
            Wait(0.4f, () =>
            {
                router.Move(1, AttackCentre);
                tappedDuringHold |= attack.Gesture.Tapped;
            });
            Frame(() => router.End(1, AttackCentre, false));

            Assert.IsFalse(tappedDuringHold);
            Assert.IsTrue(attack.Gesture.ChargeReleased);
            Assert.IsFalse(attack.Gesture.Tapped);
        }

        // ------------------------------------------------------ interruption

        [Test]
        public void ASystemCancelledTouchCancelsTheChargeInsteadOfFiring()
        {
            Frame(() => router.Begin(1, AttackCentre));
            Wait(0.3f, () => router.Move(1, AttackCentre));
            Frame(() => router.End(1, AttackCentre, true));

            Assert.IsTrue(attack.Gesture.Cancelled);
            Assert.IsFalse(attack.Gesture.ChargeReleased);
        }

        [Test]
        public void AFingerThatVanishesWithoutAnEndEventIsReleasedAsCancelled()
        {
            Frame(() => router.Begin(1, JoystickCentre + new Vector2(30f, 0f)));
            Frame(() => router.Begin(2, AttackCentre));
            Wait(0.3f, () => router.Move(2, AttackCentre));

            // Neither finger is reported this frame.
            Frame(() => { });

            Assert.IsFalse(joystick.IsCaptured);
            Assert.AreEqual(Vector2.zero, joystick.Value);
            Assert.IsTrue(joystick.LastReleaseCancelled);
            Assert.IsTrue(attack.Gesture.Cancelled);
            Assert.IsFalse(attack.Gesture.ChargeReleased);
        }

        [Test]
        public void LosingFocusReleasesEverythingWithoutFiring()
        {
            Frame(() =>
            {
                router.Begin(1, JoystickCentre + new Vector2(30f, 0f));
                router.Begin(2, AttackCentre);
            });
            Wait(0.3f, () =>
            {
                router.Move(1, JoystickCentre + new Vector2(30f, 0f));
                router.Move(2, AttackCentre);
            });

            attack.Gesture.Cancel();
            router.ReleaseAll(true);

            Assert.AreEqual(Vector2.zero, joystick.Value);
            Assert.IsTrue(attack.Gesture.Cancelled);
            Assert.IsFalse(attack.Gesture.ChargeReleased);
        }

        [Test]
        public void AReusedIdentifierReleasesTheStaleOwner()
        {
            Frame(() => router.Begin(7, JoystickCentre));
            Frame(() => router.Begin(7, new Vector2(420f, 260f)));

            Assert.IsFalse(joystick.IsCaptured);
            Assert.IsTrue(look.IsCaptured);
        }

        [Test]
        public void HoldingBlockStopsANewAttackPressFromStarting()
        {
            Vector2 onBlock = MobileControlsLayout.Default.Block.Resolve(Screen);
            Frame(() => router.Begin(1, onBlock));

            attack.PressAllowed = !block.Held;
            Frame(() =>
            {
                router.Move(1, onBlock);
                router.Begin(2, AttackCentre);
            });

            Assert.IsTrue(attack.IsCaptured, "The finger still belongs to ATTACK, so it does not turn the camera.");
            Assert.IsFalse(attack.Gesture.IsActive);
            Assert.IsFalse(look.IsCaptured);
        }

        // ------------------------------------------------ discipline selection

        [Test]
        public void ADisciplineCanBeChosenByDraggingWhileMoving()
        {
            Vector2 move = JoystickCentre + new Vector2(0f, 55f);
            Vector2[] slots = MobileControlsLayout.Default.FanSlotOffsetsFromIndicator(3);
            Vector2 flame = selector.Centre + slots[1];

            Frame(() =>
            {
                router.Begin(1, move);
                router.Begin(2, selector.Centre);
            });
            Assert.IsTrue(selector.Model.IsOpen);

            Frame(() =>
            {
                router.Move(1, move);
                router.Move(2, Vector2.Lerp(selector.Centre, flame, 0.5f));
            });
            Frame(() =>
            {
                router.Move(1, move);
                router.Move(2, flame);
            });
            Assert.AreEqual(1, selector.Model.Highlighted);

            Frame(() =>
            {
                router.Move(1, move);
                router.End(2, flame, false);
            });

            Assert.AreEqual(1, selector.Model.SelectedThisFrame);
            Assert.AreEqual(1f, joystick.Value.y, 0.0001f, "Choosing a discipline must not disturb movement.");
            Assert.AreEqual(Vector2.zero, look.Total, "Choosing a discipline must not turn the camera.");
        }

        [Test]
        public void ADisciplineCanBeChosenByTwoTapsWhileMoving()
        {
            Vector2 move = JoystickCentre + new Vector2(55f, 0f);
            Vector2 aegis = selector.Centre + MobileControlsLayout.Default.FanSlotOffsetsFromIndicator(3)[2];

            Frame(() =>
            {
                router.Begin(1, move);
                router.Begin(2, selector.Centre);
            });
            Frame(() =>
            {
                router.Move(1, move);
                router.End(2, selector.Centre, false);
            });
            Assert.AreEqual(DisciplineSelectorState.Open, selector.Model.State);

            Frame(() =>
            {
                router.Move(1, move);
                router.Begin(3, aegis);
            });
            Assert.IsTrue(fan.IsCaptured);

            Frame(() =>
            {
                router.Move(1, move);
                router.End(3, aegis, false);
            });

            Assert.AreEqual(2, selector.Model.SelectedThisFrame);
            Assert.AreEqual(1f, joystick.Value.x, 0.0001f);
        }

        [Test]
        public void TouchingTheCameraAreaClosesAnOpenFanAndStillTurnsTheCamera()
        {
            Frame(() => router.Begin(1, selector.Centre));
            Frame(() => router.End(1, selector.Centre, false));
            Assert.AreEqual(DisciplineSelectorState.Open, selector.Model.State);

            Vector2 cam = new Vector2(400f, 250f);
            Frame(() => router.Begin(2, cam));
            Frame(() => router.Move(2, cam + new Vector2(20f, 0f)));

            Assert.IsFalse(selector.Model.IsOpen);
            Assert.AreEqual(20f, look.Total.x, 0.001f);
        }

        [Test]
        public void TheOpenFanDoesNotTakeTouchesBesideItsSlots()
        {
            Frame(() => router.Begin(1, selector.Centre));
            Frame(() => router.End(1, selector.Centre, false));

            // Between the slots and the screen centre: the camera's.
            Vector2 beside = selector.Centre + new Vector2(-320f, 120f);
            Frame(() => router.Begin(2, beside));

            Assert.IsFalse(fan.IsCaptured);
            Assert.IsTrue(look.IsCaptured);
        }

        [Test]
        public void TheClosedFanTakesNoTouches()
        {
            Vector2 flame = selector.Centre + MobileControlsLayout.Default.FanSlotOffsetsFromIndicator(3)[1];
            Frame(() => router.Begin(1, flame));

            Assert.IsFalse(fan.IsCaptured);
        }

    
        // ------------------------------------------------------ aerial abilities

        [Test]
        public void AGlideHeldOnJumpSurvivesMovingAndTurningTheCameraAtTheSameTime()
        {
            // Sprint 4: the Flame glide propels while JUMP is held. With the thumb on the joystick and a second finger
            // turning the camera, the third finger on JUMP keeps its own touch; lifting it stops the glide and a new
            // tap resumes it, while the other two fingers carry on.
            Vector2 jumpCentre = MobileControlsLayout.Default.Jump.Resolve(Screen);
            Vector2 cameraStart = new Vector2(420f, 260f);
            var model = new AerialAbilityModel(null, null, null, null);
            var events = new List<AerialEvent>();
            var motion = new AerialMotion();
            bool wasHeld = false;
            int frame = 0;

            void Aerial(bool grounded = false, bool jumped = false)
            {
                bool held = jump.Held;
                var input = new AerialFrameInput
                {
                    DeltaTime = 1f / 60f, Grounded = grounded, VerticalSpeed = grounded ? 0f : -1f, Gravity = 28f,
                    JumpedFromGround = jumped, JumpPressed = held && !wasHeld, JumpHeld = held,
                    Selected = AerialAbilityKind.Glide, Move = new Vector3(joystick.Value.x, 0f, joystick.Value.y),
                    Facing = new Vector3(0f, 0f, 1f),
                };
                wasHeld = held;
                model.Tick(input, ref motion, events);
            }

            // Thumb on the joystick, a finger dragging the camera.
            Frame(() =>
            {
                router.Begin(1, JoystickCentre);
                router.Begin(2, cameraStart);
            });
            Aerial(grounded: true);
            Aerial(grounded: true);

            // Every frame reports every finger still down, as Unity's touch list does.
            Vector2 thumb = JoystickCentre + new Vector2(0f, 50f);
            Vector2 look2 = cameraStart + new Vector2(10f, 0f);

            // First jump: tap and release JUMP while moving and turning.
            Frame(() =>
            {
                router.Move(1, thumb);
                router.Move(2, look2);
                router.Begin(3, jumpCentre);
            });
            Aerial(grounded: true);
            Frame(() =>
            {
                router.Move(1, thumb);
                router.Move(2, look2);
                router.End(3, jumpCentre, false);
            });
            Aerial(jumped: true);

            // Press and hold JUMP in the air: the glide starts and keeps going while the other fingers move.
            Frame(() =>
            {
                router.Move(1, thumb);
                router.Move(2, look2);
                router.Begin(3, jumpCentre);
            });
            Aerial();
            Assert.AreEqual(AerialState.Gliding, model.State);
            for (frame = 0; frame < 30; frame++)
            {
                int i = frame;
                look2 = cameraStart + new Vector2(10f + i * 3f, 0f);
                thumb = JoystickCentre + new Vector2(30f, 40f);
                Frame(() =>
                {
                    router.Move(1, thumb);
                    router.Move(2, look2);
                    router.Move(3, jumpCentre);
                });
                Aerial();
                Assert.AreEqual(AerialState.Gliding, model.State, $"frame {i}");
            }

            Assert.IsTrue(joystick.IsCaptured);
            Assert.IsTrue(look.IsCaptured);
            Assert.IsTrue(jump.Held);
            Assert.Greater(look.Total.x, 0f, "the camera turned during the glide");
            Assert.AreNotEqual(Vector2.zero, joystick.Value, "and the Mage was steered");

            // Lift JUMP: propulsion stops at once; the other fingers stay.
            Frame(() =>
            {
                router.Move(1, thumb);
                router.Move(2, look2);
                router.End(3, jumpCentre, false);
            });
            Aerial();
            Assert.AreEqual(AerialState.Idle, model.State);
            Assert.IsTrue(joystick.IsCaptured && look.IsCaptured);

            // Tap again: the glide resumes with the fuel left.
            Frame(() =>
            {
                router.Move(1, thumb);
                router.Move(2, look2);
                router.Begin(3, jumpCentre);
            });
            Aerial();
            Assert.AreEqual(AerialState.Gliding, model.State);
            Assert.AreEqual(1, events.FindAll(e => e.Kind == AerialEventKind.Activated).Count);
            Assert.AreEqual(1, events.FindAll(e => e.Kind == AerialEventKind.Resumed).Count);
        }
}
}
