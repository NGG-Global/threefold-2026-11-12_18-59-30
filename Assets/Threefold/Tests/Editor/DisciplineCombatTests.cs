using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.InputHandling;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The Mage's combat sequencing with the three disciplines' default values: melee combos,
    /// charged attacks, tap versus hold, charging while moving, aim and cancellation, mana and
    /// cooldown, block, interruption and switching discipline mid-fight. The attack gesture comes
    /// from the same recognizer the touch and desktop controls use.
    /// </summary>
    public sealed class DisciplineCombatTests
    {
        private const int Arcane = (int)MagicDiscipline.Arcane;
        private const int Flame = (int)MagicDiscipline.Flame;
        private const int Aegis = (int)MagicDiscipline.Aegis;
        private const float Step = 1f / 60f;

        /// <summary>The disciplines' default data, and a mana pool with cooldowns standing in for the caster.</summary>
        private sealed class Fixture : IDisciplineCombatData, IChargedAttackGate
        {
            public readonly MeleeComboProfile[] Melee = new MeleeComboProfile[3];
            public readonly ChargedAttackDefaults[] Charged = new ChargedAttackDefaults[3];
            public readonly float[] Cooldown = new float[3];
            public float Mana = 100f;
            public bool CasterDefeated;

            public Fixture()
            {
                for (int i = 0; i < 3; i++)
                {
                    Melee[i] = DisciplineDefaults.Melee((MagicDiscipline)i);
                    Charged[i] = DisciplineDefaults.Charged((MagicDiscipline)i);
                }
            }

            public int Count => 3;
            public MeleeComboProfile GetMelee(int slot) => Melee[slot];
            public ChargeProfile GetCharge(int slot) => Charged[slot] != null ? Charged[slot].Charge : null;

            public CastResult Check(int slot)
            {
                if (CasterDefeated) return CastResult.CasterDefeated;
                if (Cooldown[slot] > 0f) return CastResult.OnCooldown;
                return Mana + 0.0001f >= Charged[slot].ManaCost ? CastResult.Success : CastResult.NotEnoughMana;
            }

            /// <summary>What the caster does on a successful release.</summary>
            public void Spend(int slot)
            {
                Mana -= Charged[slot].ManaCost;
                Cooldown[slot] = Charged[slot].CooldownSeconds;
            }
        }

        private Fixture fixture;
        private DisciplineCombatModel model;
        private AttackGestureRecognizer gesture;
        private readonly List<CombatCommand> frameCommands = new List<CombatCommand>();
        private readonly List<CombatCommand> log = new List<CombatCommand>();
        private float time;
        private int slot;
        private bool block;
        private bool handsBusy;
        private bool alive;
        private float damageTaken;

        [SetUp]
        public void SetUp()
        {
            fixture = new Fixture();
            model = new DisciplineCombatModel(fixture, fixture);
            gesture = new AttackGestureRecognizer(new AttackGestureSettings());
            log.Clear();
            time = 0f;
            slot = Arcane;
            block = false;
            handsBusy = false;
            alive = true;
            damageTaken = 0f;
        }

        /// <summary>One frame, in the order the game runs: input first, then combat.</summary>
        private void Frame(System.Action input = null)
        {
            time += Step;
            gesture.BeginFrame();
            input?.Invoke();
            gesture.Update(time);
            CombatInputRules.ApplyBlock(gesture, block, false);

            for (int i = 0; i < fixture.Cooldown.Length; i++)
            {
                fixture.Cooldown[i] = Mathf.Max(0f, fixture.Cooldown[i] - Step);
            }

            frameCommands.Clear();
            var frame = new CombatFrameInput
            {
                Gesture = gesture.Snapshot(), BlockHeld = block, HandsBusy = handsBusy, Alive = alive,
                DisciplineSlot = slot, DamageTaken = damageTaken,
            };
            model.Tick(frame, time, frameCommands);
            damageTaken = 0f;

            foreach (CombatCommand command in frameCommands)
            {
                log.Add(command);
                if (command.Kind == CombatCommandKind.InterruptInput)
                {
                    gesture.Cancel();
                }
                else if (command.Kind == CombatCommandKind.ChargedReleased && fixture.Check(command.Slot) == CastResult.Success)
                {
                    fixture.Spend(command.Slot);
                }
            }
        }

        private void Wait(float seconds)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int i = 0; i < frames; i++)
            {
                Frame();
            }
        }

        private void Tap()
        {
            Frame(() => gesture.Press(time, Vector2.zero));
            Frame();
            Frame(() => gesture.Release(time, false));
        }

        private void Hold(float seconds, Vector2 dragTo = default)
        {
            Frame(() => gesture.Press(time, Vector2.zero));
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int i = 0; i < frames; i++)
            {
                Frame(() => gesture.Move(time, dragTo, false));
            }
        }

        private void Release(bool overCancelZone = false)
        {
            Frame(() => gesture.Release(time, overCancelZone));
        }

        private int Count(CombatCommandKind kind)
        {
            int count = 0;
            foreach (CombatCommand command in log)
            {
                if (command.Kind == kind) count++;
            }

            return count;
        }

        private CombatCommand Last(CombatCommandKind kind)
        {
            for (int i = log.Count - 1; i >= 0; i--)
            {
                if (log[i].Kind == kind) return log[i];
            }

            Assert.Fail($"No {kind} command was issued.");
            return default;
        }

        private List<CombatCommand> All(CombatCommandKind kind)
        {
            return log.FindAll(command => command.Kind == kind);
        }

        // --------------------------------------------------------------- melee

        [Test]
        public void ATapPerformsOneSwingThatLandsAfterItsStartup()
        {
            Tap();
            Assert.AreEqual(1, Count(CombatCommandKind.MeleeStepStarted));
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeHit), "The hit must wait for the start-up.");

            Wait(0.1f);
            Assert.AreEqual(1, Count(CombatCommandKind.MeleeHit));
            Assert.AreEqual("Arcane Jab", Last(CombatCommandKind.MeleeHit).Step.name);
        }

        [TestCase(Arcane, 3)]
        [TestCase(Flame, 3)]
        [TestCase(Aegis, 2)]
        public void RepeatedTapsWalkThroughTheWholeCombo(int discipline, int steps)
        {
            slot = discipline;
            for (int i = 0; i < 40 && Count(CombatCommandKind.MeleeHit) < steps; i++)
            {
                Tap();
                Wait(0.12f);
            }

            List<CombatCommand> hits = All(CombatCommandKind.MeleeHit);
            Assert.AreEqual(steps, hits.Count);
            for (int i = 0; i < steps; i++)
            {
                Assert.AreEqual(i, hits[i].StepIndex);
                Assert.AreEqual(discipline, hits[i].Slot);
                Assert.AreSame(fixture.Melee[discipline].steps[i], hits[i].Step);
            }
        }

        [Test]
        public void TheComboWrapsToTheFirstSwingAfterTheLast()
        {
            slot = Aegis;
            for (int i = 0; i < 60 && Count(CombatCommandKind.MeleeHit) < 3; i++)
            {
                Tap();
                Wait(0.12f);
            }

            Assert.AreEqual(0, All(CombatCommandKind.MeleeHit)[2].StepIndex);
        }

        [Test]
        public void WaitingPastTheComboWindowRestartsTheCombo()
        {
            Tap();
            Wait(1.5f);
            Tap();
            Wait(0.3f);

            List<CombatCommand> hits = All(CombatCommandKind.MeleeHit);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(0, hits[1].StepIndex);
        }

        [Test]
        public void ATapDuringASwingIsRememberedAndChainsAtOnce()
        {
            Tap();
            Wait(0.12f); // mid-recovery of the first swing (0.07 start-up + 0.2 recovery)
            Tap();
            Wait(0.5f);

            List<CombatCommand> starts = All(CombatCommandKind.MeleeStepStarted);
            Assert.AreEqual(2, starts.Count);
            Assert.AreEqual(1, starts[1].StepIndex);
        }

        [Test]
        public void MashingNeverStartsTwoSwingsAtOnce()
        {
            for (int i = 0; i < 30; i++)
            {
                Tap();
            }

            Wait(1f);
            Assert.AreEqual(Count(CombatCommandKind.MeleeStepStarted), Count(CombatCommandKind.MeleeHit));

            // Each swing takes at least its start-up and recovery, so 30 taps in 1.5 s give few swings.
            Assert.LessOrEqual(Count(CombatCommandKind.MeleeHit), 10);
        }

        [Test]
        public void ArcaneIsTheFastestAndAegisTheSlowestToLand()
        {
            float[] landed = new float[3];
            for (int discipline = 0; discipline < 3; discipline++)
            {
                SetUp();
                slot = discipline;
                Tap();
                float start = time;
                for (int i = 0; i < 120 && Count(CombatCommandKind.MeleeHit) == 0; i++)
                {
                    Frame();
                }

                landed[discipline] = time - start;
            }

            Assert.Less(landed[Arcane], landed[Flame]);
            Assert.Less(landed[Flame], landed[Aegis]);
        }

        [Test]
        public void AHeavySwingSlowsMovementAndALightOneDoesNot()
        {
            Tap();
            Assert.AreEqual(1f, model.MoveSpeedMultiplier, 0.0001f, "Arcane strikes do not slow the Mage.");

            SetUp();
            slot = Aegis;
            Tap();
            Assert.Less(model.MoveSpeedMultiplier, 1f);
            Assert.IsFalse(model.SprintAllowed);

            Wait(1.5f);
            Assert.AreEqual(1f, model.MoveSpeedMultiplier, 0.0001f);
            Assert.IsTrue(model.SprintAllowed);
        }

        // ---------------------------------------------------- tap versus hold

        [Test]
        public void ATapNeverStartsAChargeAndAHoldNeverSwings()
        {
            Tap();
            Wait(0.5f);
            Assert.AreEqual(0, Count(CombatCommandKind.ChargeBegan));
            Assert.AreEqual(1, Count(CombatCommandKind.MeleeHit));

            log.Clear();
            Hold(0.6f);
            Release();
            Wait(0.5f);
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeStepStarted));
            Assert.AreEqual(1, Count(CombatCommandKind.ChargedReleased));
        }

        // ------------------------------------------------------------- charged

        [TestCase(Arcane)]
        [TestCase(Flame)]
        [TestCase(Aegis)]
        public void HoldingAndReleasingFiresTheChargedAttackOfTheSelectedDiscipline(int discipline)
        {
            slot = discipline;
            Hold(0.6f);
            Assert.AreEqual(1, Count(CombatCommandKind.ChargeBegan));
            Assert.IsTrue(model.IsCharging);

            Release();
            CombatCommand fired = Last(CombatCommandKind.ChargedReleased);
            Assert.AreEqual(discipline, fired.Slot);
            Assert.Greater(fired.Power, 0f);
            Assert.Less(fired.Power, 1f);
            Assert.IsFalse(model.IsCharging);
        }

        [Test]
        public void ALongerHoldGivesMorePowerUpToTheCap()
        {
            Hold(0.4f);
            Release();
            float brief = Last(CombatCommandKind.ChargedReleased).Power;

            Wait(1f);
            Hold(1f);
            Release();
            float longer = Last(CombatCommandKind.ChargedReleased).Power;

            Wait(1f);
            Hold(4f);
            Release();
            float capped = Last(CombatCommandKind.ChargedReleased).Power;

            Assert.Less(brief, longer);
            Assert.AreEqual(1f, capped, 0.0001f);
        }

        [Test]
        public void TheDragAimIsPassedWithTheRelease()
        {
            slot = Flame;
            Hold(0.6f, new Vector2(-64f, 0f));
            Release();

            Vector2 aim = Last(CombatCommandKind.ChargedReleased).Aim;
            Assert.AreEqual(-1f, aim.x, 0.0001f);
            Assert.AreEqual(0f, aim.y, 0.0001f);
        }

        [Test]
        public void ChargingAllowsMovementAtTheDisciplinesSpeed()
        {
            float[] speed = new float[3];
            for (int discipline = 0; discipline < 3; discipline++)
            {
                SetUp();
                slot = discipline;
                Hold(0.5f);
                Assert.IsTrue(model.IsCharging);
                speed[discipline] = model.MoveSpeedMultiplier;
                Assert.Greater(speed[discipline], 0.5f, "Charging must never root the Mage.");
                Assert.IsFalse(model.SprintAllowed);
            }

            Assert.Greater(speed[Arcane], speed[Flame]);
            Assert.Greater(speed[Flame], speed[Aegis]);
        }

        [Test]
        public void MovingWhileChargingDoesNotCancelTheCharge()
        {
            // Movement is not an input to combat at all: a charge held for seconds while the stick
            // moves still fires. (The routing tests show the fingers do not interfere.)
            Hold(2f);
            Assert.IsTrue(model.IsCharging);
            Release();
            Assert.AreEqual(1, Count(CombatCommandKind.ChargedReleased));
        }

        // -------------------------------------------------------- cancellation

        [Test]
        public void ReleasingOverTheCancelZoneFiresNothing()
        {
            Hold(0.6f);
            Release(overCancelZone: true);

            Assert.AreEqual(0, Count(CombatCommandKind.ChargedReleased));
            Assert.AreEqual(1, Count(CombatCommandKind.ChargeCancelled));
            Assert.IsFalse(model.IsCharging);
            Assert.AreEqual(100f, fixture.Mana, "A cancelled charge costs nothing.");
        }

        [Test]
        public void BlockingCancelsAChargeAndFiresNothing()
        {
            Hold(0.6f);
            block = true;
            Frame();
            Frame();

            Assert.AreEqual(1, Count(CombatCommandKind.ChargeCancelled));
            Assert.AreEqual(0, Count(CombatCommandKind.ChargedReleased));
            Assert.Less(model.MoveSpeedMultiplier, 1f, "Blocking slows the Mage.");
        }

        [Test]
        public void BlockingAbandonsASwingThatHasNotLanded()
        {
            slot = Aegis;
            Tap();
            block = true;
            Wait(0.6f);

            Assert.AreEqual(1, Count(CombatCommandKind.MeleeInterrupted));
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeHit));
        }

        [Test]
        public void AQuickBlockDoesNotSkipTheRecoveryOfASwingThatLanded()
        {
            slot = Aegis;
            Tap();
            Wait(0.5f); // the Force Palm lands after 0.18 s; its 0.34 s recovery ends at about 0.52 s
            Tap();      // the Bulwark Slam starts, lands 0.26 s later, then recovers for 0.46 s
            for (int i = 0; i < 60 && Count(CombatCommandKind.MeleeHit) < 2; i++)
            {
                Frame();
            }

            Assert.AreEqual(2, Count(CombatCommandKind.MeleeHit));
            float landed = time;

            block = true;
            Frame();
            block = false;
            Tap();
            Wait(0.2f);

            Assert.AreEqual(2, Count(CombatCommandKind.MeleeStepStarted),
                "Flicking block must not let the next swing start before the recovery is over.");

            Wait(0.5f);
            Tap();
            Wait(0.5f);
            List<CombatCommand> starts = All(CombatCommandKind.MeleeStepStarted);
            Assert.AreEqual(3, starts.Count);
            Assert.AreEqual(0, starts[2].StepIndex, "Blocking ends the combo; the next swing is the first.");
            Assert.Greater(time - landed, fixture.Melee[Aegis].steps[1].recoverySeconds);
        }

        [Test]
        public void TapsWhileBlockingDoNothing()
        {
            block = true;
            Frame();
            Tap();
            Wait(0.5f);
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeStepStarted));
        }

        [Test]
        public void AHeavyHitBreaksAChargeAndTellsTheInputToLetGo()
        {
            Hold(0.6f);
            damageTaken = 24f;
            Frame();

            Assert.AreEqual(1, Count(CombatCommandKind.ChargeCancelled));
            Assert.AreEqual(1, Count(CombatCommandKind.InterruptInput));

            // The finger is still down; lifting it later must not fire anything.
            Wait(0.3f);
            Release();
            Assert.AreEqual(0, Count(CombatCommandKind.ChargedReleased));
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeStepStarted));
        }

        [Test]
        public void ALightHitDoesNotBreakACharge()
        {
            Hold(0.6f);
            damageTaken = 3f;
            Frame();
            Assert.IsTrue(model.IsCharging);
        }

        [Test]
        public void StartingToClimbAbandonsTheChargeAndTheSwing()
        {
            Hold(0.6f);
            handsBusy = true;
            Frame();
            Assert.AreEqual(1, Count(CombatCommandKind.ChargeCancelled));
            Assert.IsFalse(model.IsCharging);

            handsBusy = false;
            Wait(0.2f);
            log.Clear();

            slot = Aegis;
            Tap();
            handsBusy = true;
            Wait(0.6f);
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeHit));
        }

        [Test]
        public void ADefeatedMageDoesNotAttack()
        {
            alive = false;
            Tap();
            Hold(0.6f);
            Release();
            Wait(0.5f);

            Assert.AreEqual(0, Count(CombatCommandKind.MeleeStepStarted));
            Assert.AreEqual(0, Count(CombatCommandKind.ChargedReleased));
        }

        // ----------------------------------------------------------- resources

        [TestCase(Arcane)]
        [TestCase(Flame)]
        [TestCase(Aegis)]
        public void AChargedAttackSpendsItsManaOnReleaseOnly(int discipline)
        {
            slot = discipline;
            Hold(0.6f);
            Assert.AreEqual(100f, fixture.Mana, "Nothing is spent while charging.");

            Release();
            Assert.AreEqual(100f - fixture.Charged[discipline].ManaCost, fixture.Mana, 0.0001f);
        }

        [Test]
        public void MeleeCostsNoMana()
        {
            for (int i = 0; i < 6; i++)
            {
                Tap();
                Wait(0.3f);
            }

            Assert.AreEqual(100f, fixture.Mana);
        }

        [Test]
        public void WithoutEnoughManaTheChargeIsRefusedAtOnce()
        {
            slot = Flame;
            fixture.Mana = 10f;
            Hold(0.6f);

            Assert.AreEqual(0, Count(CombatCommandKind.ChargeBegan));
            Assert.AreEqual(CastResult.NotEnoughMana, Last(CombatCommandKind.ChargeRefused).Reason);
            Assert.AreEqual(1, Count(CombatCommandKind.InterruptInput));
            Assert.IsFalse(model.IsCharging);

            Release();
            Assert.AreEqual(0, Count(CombatCommandKind.ChargedReleased));
            Assert.AreEqual(10f, fixture.Mana);
        }

        [Test]
        public void ARefusedChargeDoesNotBecomeAMeleeSwing()
        {
            fixture.Mana = 0f;
            Hold(0.6f);
            Release();
            Wait(0.5f);
            Assert.AreEqual(0, Count(CombatCommandKind.MeleeStepStarted));
        }

        [Test]
        public void ManaRunsOutAfterAFewChargedAttacks()
        {
            slot = Flame; // 26 mana each: three fit into 100, the fourth does not.
            for (int i = 0; i < 5; i++)
            {
                Hold(0.3f);
                Release();
                Wait(1.2f);
            }

            Assert.AreEqual(3, Count(CombatCommandKind.ChargedReleased));
            Assert.GreaterOrEqual(Count(CombatCommandKind.ChargeRefused), 1);
        }

        [Test]
        public void ACooldownRefusesTheNextChargeUntilItHasPassed()
        {
            slot = Aegis; // 1.5 s cooldown
            Hold(0.3f);
            Release();
            Hold(0.3f);
            Assert.AreEqual(CastResult.OnCooldown, Last(CombatCommandKind.ChargeRefused).Reason);
            Release();

            Wait(1.6f);
            Hold(0.3f);
            Release();
            Assert.AreEqual(2, Count(CombatCommandKind.ChargedReleased));
        }

        [Test]
        public void ADefeatedCasterIsRefusedACharge()
        {
            fixture.CasterDefeated = true;
            Hold(0.6f);
            Assert.AreEqual(CastResult.CasterDefeated, Last(CombatCommandKind.ChargeRefused).Reason);
            Assert.AreEqual(0, Count(CombatCommandKind.ChargeBegan));
        }

        [Test]
        public void MeleeStillWorksWhenThereIsNoMana()
        {
            fixture.Mana = 0f;
            Tap();
            Wait(0.3f);
            Assert.AreEqual(1, Count(CombatCommandKind.MeleeHit));
        }

        // ------------------------------------------------- switching discipline

        [Test]
        public void SwitchingDisciplineBetweenSwingsStartsTheNewCombo()
        {
            Tap();
            Wait(0.3f);
            slot = Aegis;
            Tap();
            Wait(0.6f);

            List<CombatCommand> hits = All(CombatCommandKind.MeleeHit);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(Arcane, hits[0].Slot);
            Assert.AreEqual(Aegis, hits[1].Slot);
            Assert.AreEqual(0, hits[1].StepIndex, "The new discipline starts from its first swing.");
            Assert.AreEqual("Force Palm", hits[1].Step.name);
        }

        [Test]
        public void ASwingInProgressLandsAsTheDisciplineItStartedWith()
        {
            slot = Aegis;
            Tap();
            slot = Flame; // switched during the start-up
            Wait(0.6f);

            CombatCommand hit = Last(CombatCommandKind.MeleeHit);
            Assert.AreEqual(Aegis, hit.Slot);
            Assert.AreEqual("Force Palm", hit.Step.name);
        }

        [Test]
        public void SwitchingDuringASwingRestartsTheComboForTheNextSwing()
        {
            slot = Arcane;
            Tap();
            Wait(0.3f);
            Tap(); // second Arcane swing, index 1
            slot = Flame; // switched while that swing is in progress
            Wait(0.12f);
            Tap();
            Wait(0.6f);

            List<CombatCommand> starts = All(CombatCommandKind.MeleeStepStarted);
            Assert.AreEqual(3, starts.Count);
            Assert.AreEqual(1, starts[1].StepIndex);
            Assert.AreEqual(Flame, starts[2].Slot);
            Assert.AreEqual(0, starts[2].StepIndex, "A new discipline never starts in the middle of its combo.");
            Assert.AreEqual("Ember Swipe", starts[2].Step.name);
        }

        [Test]
        public void AChargeIsReleasedAsTheDisciplineItBeganWith()
        {
            slot = Arcane;
            Hold(0.5f);
            slot = Flame; // switched while holding
            Frame(() => gesture.Move(time, Vector2.zero, false));
            Assert.IsTrue(model.IsCharging, "Switching does not cancel the charge.");
            Assert.AreEqual(Arcane, model.ChargeSlot);

            Release();
            Assert.AreEqual(Arcane, Last(CombatCommandKind.ChargedReleased).Slot);
            Assert.AreEqual(100f - fixture.Charged[Arcane].ManaCost, fixture.Mana, 0.0001f);

            // The switch applies to the next attack.
            Wait(0.5f);
            Hold(0.5f);
            Release();
            Assert.AreEqual(Flame, Last(CombatCommandKind.ChargedReleased).Slot);
        }

        [Test]
        public void SwitchingToADisciplineThatCannotBePaidForDoesNotLoseTheCharge()
        {
            // 20 mana pays for the Arcane Arrow (18) but not for the Flame Wave (26).
            fixture.Mana = 20f;
            slot = Arcane;
            Hold(0.5f);
            slot = Flame;
            Release();

            Assert.AreEqual(1, Count(CombatCommandKind.ChargedReleased));
            Assert.AreEqual(0, Count(CombatCommandKind.ChargeRefused));
            Assert.AreEqual(2f, fixture.Mana, 0.0001f);
        }

        [Test]
        public void EveryDisciplineCanBeUsedInOneFight()
        {
            for (int discipline = 0; discipline < 3; discipline++)
            {
                slot = discipline;
                Tap();
                Wait(0.9f);
                Hold(0.4f);
                Release();
                Wait(0.2f);
            }

            Assert.AreEqual(3, Count(CombatCommandKind.MeleeHit));
            List<CombatCommand> fired = All(CombatCommandKind.ChargedReleased);
            Assert.AreEqual(3, fired.Count);
            Assert.AreEqual(Arcane, fired[0].Slot);
            Assert.AreEqual(Flame, fired[1].Slot);
            Assert.AreEqual(Aegis, fired[2].Slot);
        }

        // -------------------------------------------------------------- desktop

        [Test]
        public void AMouseButtonGivesTheSameTapAndChargeWithTheCameraAsAim()
        {
            // The desktop source presses and releases without any drag.
            Frame(() => gesture.Press(time, Vector2.zero));
            Frame(() => gesture.Release(time, false));
            Wait(0.3f);
            Assert.AreEqual(1, Count(CombatCommandKind.MeleeHit));

            Frame(() => gesture.Press(time, Vector2.zero));
            Wait(0.7f);
            Frame(() => gesture.Release(time, false));

            CombatCommand fired = Last(CombatCommandKind.ChargedReleased);
            Assert.AreEqual(Vector2.zero, fired.Aim, "No drag: the shot follows the camera.");
        }

        // ------------------------------------------------------------- defaults

        [Test]
        public void TheThreeMeleeStylesAreDistinct()
        {
            float Time(int d) { float t = 0f; foreach (MeleeStepProfile s in fixture.Melee[d].steps) t += s.TotalSeconds; return t / fixture.Melee[d].steps.Length; }
            float Stagger(int d) { float t = 0f; foreach (MeleeStepProfile s in fixture.Melee[d].steps) t += s.stagger; return t / fixture.Melee[d].steps.Length; }
            float Arc(int d) { float t = 0f; foreach (MeleeStepProfile s in fixture.Melee[d].steps) t += s.arcDegrees; return t / fixture.Melee[d].steps.Length; }

            Assert.Less(Time(Arcane), Time(Flame), "Arcane recovers fastest.");
            Assert.Less(Time(Flame), Time(Aegis), "Aegis is the most deliberate.");
            Assert.Greater(Stagger(Aegis), Stagger(Arcane) * 2f, "Aegis staggers far more.");
            Assert.Greater(Stagger(Aegis), Stagger(Flame) * 2f);
            Assert.Greater(Arc(Flame), Arc(Arcane), "Flame swings wide.");

            foreach (MeleeStepProfile step in fixture.Melee[Arcane].steps)
            {
                Assert.AreEqual(1, step.maxTargets, "Arcane is single-target.");
                Assert.AreEqual(0f, step.heat);
            }

            foreach (MeleeStepProfile step in fixture.Melee[Flame].steps)
            {
                Assert.Greater(step.heat, 0f, "Every Flame strike heats.");
                Assert.Greater(step.maxTargets, 1);
            }

            foreach (MeleeStepProfile step in fixture.Melee[Aegis].steps)
            {
                Assert.AreEqual(0f, step.heat);
            }
        }

        [Test]
        public void TheThreeChargedAttacksAreWhatTheirDisciplinePromises()
        {
            ChargedAttackDefaults arrow = fixture.Charged[Arcane];
            ChargedAttackDefaults wave = fixture.Charged[Flame];
            ChargedAttackDefaults pulse = fixture.Charged[Aegis];

            Assert.IsNotNull(arrow.Projectile);
            Assert.IsNull(arrow.Area);
            Assert.AreEqual(ChargedTargetingMode.CameraAim, arrow.Charge.targeting);
            Assert.Greater(arrow.Charge.damageMax, arrow.Charge.damageMin, "The arrow grows with charge.");
            Assert.Greater(arrow.Projectile.range, wave.Area.reachMax, "The arrow is the long-range tool.");

            Assert.IsNotNull(wave.Area);
            Assert.AreEqual(ChargedTargetingMode.GroundDirection, wave.Charge.targeting);
            Assert.Greater(wave.Area.heatMin, 0f);
            Assert.AreEqual(0f, wave.Area.pushMax);
            Assert.Greater(wave.Area.maxTargets, 1);
            Assert.AreEqual(SpellElement.Fire, wave.Charge.element);

            Assert.IsNotNull(pulse.Area);
            Assert.Greater(pulse.Area.pushMin, 0f);
            Assert.AreEqual(0f, pulse.Area.heatMax);
            Assert.Greater(pulse.Charge.staggerMin, wave.Charge.staggerMax, "The pulse is about stagger, not damage.");
            Assert.Less(pulse.Charge.damageMax, wave.Charge.damageMax);
            Assert.AreEqual(SpellElement.Force, pulse.Charge.element);
        }

        [Test]
        public void ChargedAttacksCostManaAndFitTheManaPool()
        {
            foreach (ChargedAttackDefaults charged in fixture.Charged)
            {
                Assert.Greater(charged.ManaCost, 0f, charged.Name);
                Assert.LessOrEqual(charged.ManaCost, 50f, charged.Name);
                Assert.Greater(charged.Charge.fullChargeSeconds, 0f, charged.Name);
                Assert.GreaterOrEqual(charged.Charge.moveSpeedMultiplier, 0.5f, charged.Name);
            }
        }

        [Test]
        public void AnArrowCostsMoreThanAComboReturns()
        {
            // Melee feeds the charged attacks without making them free.
            float returned = 0f;
            foreach (MeleeStepProfile step in fixture.Melee[Arcane].steps)
            {
                returned += step.manaGainPerTarget;
            }

            Assert.Less(returned, fixture.Charged[Arcane].ManaCost);
            Assert.Greater(returned, 0f);
        }
    }
}
