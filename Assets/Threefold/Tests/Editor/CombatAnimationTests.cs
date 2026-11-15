using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Threefold.CombatAnimation;
using Threefold.Magic;

namespace Threefold.Tests
{
    /// <summary>
    /// The combat animation rules, outside Unity: fitting clips to the combat clock, the cue guarantees
    /// (once each, in order, nothing after cancellation, one terminal cue per action, contact only from the
    /// rules), which slot plays where, and the Mage's default slots against the disciplines' real timing.
    /// What these cannot show is how anything looks; that needs Unity and a device.
    /// </summary>
    public sealed class CombatAnimationTests
    {
        private const float Frame = 1f / 60f;
        // ------------------------------------------------------------------ time warp

        private static CombatAnimationSlot Slot(float seconds, float entry, float contact, float recoveryEnd)
        {
            return new CombatAnimationSlot
            {
                clip = "Test", clipSeconds = seconds, entry = entry, contact = contact, recoveryEnd = recoveryEnd,
                hitWindowOpen = contact, hitWindowClose = contact, effect = entry,
            };
        }

        [Test]
        public void AWindUpIsPlayedAtTheSpeedThatLandsItsContactWhenTheHitResolves()
        {
            CombatTimeFit fit = CombatTimeWarp.Fit(Slot(1f, 0f, 0.3f, 0.9f), 0.15f, 0.3f, 0.5f, 3f);

            Assert.AreEqual(0f, fit.Entry, 1e-5f);
            Assert.AreEqual(2f, fit.WindupSpeed, 1e-4f);
            Assert.AreEqual(0.15f, fit.WindupSeconds, 1e-5f);
            Assert.AreEqual(0f, fit.ContactError, 1e-5f);
            Assert.AreEqual(2f, fit.RecoverySpeed, 1e-4f, "0.6 s of recovery in 0.3 s");
            Assert.AreEqual(0.3f, fit.RecoverySeconds, 1e-5f);
        }

        [Test]
        public void AWindUpTooLongForTheFastestSpeedSkipsItsStartAndStillLandsOnTime()
        {
            // Punch_Cross for the Arcane Cut: 0.3 s to contact, 0.07 s to land.
            CombatTimeFit fit = CombatTimeWarp.Fit(Slot(1f, 0f, 0.3f, 0.933f), 0.07f, 0.2f, 0.5f, 3f);

            Assert.AreEqual(3f, fit.WindupSpeed, 1e-4f);
            Assert.AreEqual(0.09f, fit.Entry, 1e-4f, "starts 0.21 s of clip before contact");
            Assert.IsTrue(fit.EntryMoved(0f));
            Assert.AreEqual(0f, fit.ContactError, 1e-5f);
        }

        [Test]
        public void AWindUpTooShortForTheSlowestSpeedShowsContactEarlyAndSaysBy_HowMuch()
        {
            CombatTimeFit fit = CombatTimeWarp.Fit(Slot(1f, 0f, 0.1f, 1f), 0.5f, 0.5f, 0.5f, 3f);

            Assert.AreEqual(0.5f, fit.WindupSpeed, 1e-5f);
            Assert.AreEqual(0.2f, fit.WindupSeconds, 1e-5f);
            Assert.AreEqual(-0.3f, fit.ContactError, 1e-5f);
        }

        [Test]
        public void AnImmediateMomentStartsAtItsContactFrame()
        {
            CombatAnimationSlot slot = Slot(0.5f, 0f, 0.067f, 0.533f);
            CombatTimeFit fit = CombatTimeWarp.FitImmediate(slot);

            Assert.AreEqual(0.067f, fit.Entry, 1e-5f);
            Assert.AreEqual(0f, fit.WindupSeconds);
            Assert.AreEqual(1f, fit.RecoverySpeed, 1e-5f);
            Assert.AreEqual((0.533f - 0.067f) * 0.5f, fit.RecoverySeconds, 1e-4f);
        }

        [Test]
        public void MomentsAreTimedThroughTheFit()
        {
            CombatTimeFit fit = CombatTimeWarp.Fit(Slot(1f, 0.2f, 0.4f, 0.8f), 0.1f, 0.2f, 0.5f, 3f);

            Assert.AreEqual(0f, fit.SecondsToBeforeContact(0.1f), 1e-5f, "a moment before the entry happens at once");
            Assert.AreEqual(0.05f, fit.SecondsToBeforeContact(0.3f), 1e-5f, "half way through the wind-up");
            Assert.AreEqual(0.1f, fit.SecondsToBeforeContact(0.9f), 1e-5f, "a later moment happens at contact at the latest");
            Assert.AreEqual(0f, fit.SecondsAfterContact(0.3f), 1e-5f);
            Assert.AreEqual(0.1f, fit.SecondsAfterContact(0.6f), 1e-5f, "half way through the recovery");
            Assert.AreEqual(0.2f, fit.SecondsAfterContact(1f), 1e-5f, "never after the recovery end");
        }

        [Test]
        public void EveryDefaultMeleeStepShowsItsContactWithinAFrameOfTheHit()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            CombatAnimationTuning tuning = library.tuning;

            foreach (MagicDiscipline discipline in Enum.GetValues(typeof(MagicDiscipline)).Cast<MagicDiscipline>())
            {
                MeleeComboProfile combo = DisciplineDefaults.Melee(discipline);
                for (int step = 0; step < combo.StepCount; step++)
                {
                    MeleeStepProfile profile = combo.GetStep(step);
                    ResolvedSlot resolved = CombatSlotResolver.ResolveAttack(library, discipline, step, false, 0f);
                    Assert.IsTrue(resolved.IsValid, $"{discipline} step {step + 1} has an animation");

                    CombatTimeFit fit = CombatTimeWarp.Fit(resolved.Slot, profile.startupSeconds, profile.recoverySeconds,
                        tuning.minPlaybackSpeed, tuning.maxPlaybackSpeed);
                    Assert.LessOrEqual(Math.Abs(fit.ContactError), CombatAnimationValidation.ContactTolerance,
                        $"{discipline} step {step + 1} ({profile.name}): contact off by {fit.ContactError * 1000f:0} ms");
                    Assert.LessOrEqual(fit.WindupSpeed, tuning.maxPlaybackSpeed + 1e-4f);
                    Assert.GreaterOrEqual(fit.RecoverySpeed, tuning.minPlaybackSpeed - 1e-4f);
                }
            }
        }

        // ------------------------------------------------------------------ cues

        private static List<CombatCueKind> Kinds(List<CombatCue> cues)
        {
            return cues.Select(c => c.Kind).ToList();
        }

        [Test]
        public void CuesArriveOnceEachInTimeOrder()
        {
            var tracker = new CombatCueTracker();
            var cues = new List<CombatCue>();

            int id = tracker.Begin(CombatActionKind.Melee, 0, 0, CombatEffector.LeftHand, 0f, cues);
            tracker.Schedule(id, CombatCueKind.HitWindowOpened, 0.08f);
            tracker.Schedule(id, CombatCueKind.Effect, 0.05f);
            tracker.Tick(0.06f, cues);
            tracker.Tick(0.1f, cues);
            Assert.IsTrue(tracker.Anchor(id, CombatCueKind.Contact, 0.12f, cues));
            tracker.Schedule(id, CombatCueKind.Recovered, 0.3f);
            tracker.Schedule(id, CombatCueKind.HitWindowClosed, 0.15f);
            tracker.Tick(0.2f, cues);
            tracker.Tick(0.4f, cues);
            tracker.Tick(1f, cues);

            CollectionAssert.AreEqual(new[]
            {
                CombatCueKind.Windup, CombatCueKind.Effect, CombatCueKind.HitWindowOpened, CombatCueKind.Contact,
                CombatCueKind.HitWindowClosed, CombatCueKind.Recovered,
            }, Kinds(cues));
            Assert.IsTrue(cues.All(c => c.ActionId == id && c.Effector == CombatEffector.LeftHand));
            Assert.IsFalse(tracker.IsActive);
        }

        [Test]
        public void ContactSendsWhatWasStillWaitingFirst()
        {
            var tracker = new CombatCueTracker();
            var cues = new List<CombatCue>();

            int id = tracker.Begin(CombatActionKind.Melee, 0, 0, CombatEffector.RightHand, 0f, cues);
            tracker.Schedule(id, CombatCueKind.Effect, 0.2f);
            tracker.Schedule(id, CombatCueKind.HitWindowOpened, 0.25f);
            tracker.Anchor(id, CombatCueKind.Contact, 0.1f, cues);

            CollectionAssert.AreEqual(new[]
            {
                CombatCueKind.Windup, CombatCueKind.Effect, CombatCueKind.HitWindowOpened, CombatCueKind.Contact,
            }, Kinds(cues));
        }

        [Test]
        public void ContactIsAcceptedOnceAndOnlyForTheActionInProgress()
        {
            var tracker = new CombatCueTracker();
            var cues = new List<CombatCue>();

            int first = tracker.Begin(CombatActionKind.Melee, 0, 0, CombatEffector.None, 0f, cues);
            Assert.IsTrue(tracker.Anchor(first, CombatCueKind.Contact, 0.1f, cues));
            Assert.IsFalse(tracker.Anchor(first, CombatCueKind.Contact, 0.11f, cues), "a second contact");

            int second = tracker.Begin(CombatActionKind.Melee, 0, 1, CombatEffector.None, 0.3f, cues);
            Assert.AreNotEqual(first, second);
            Assert.IsFalse(tracker.Anchor(first, CombatCueKind.Contact, 0.35f, cues), "a contact for the older swing");
            Assert.AreEqual(1, cues.Count(c => c.Kind == CombatCueKind.Contact));
        }

        [Test]
        public void NothingOfACancelledActionIsSentAfterItsCancellation()
        {
            var tracker = new CombatCueTracker();
            var cues = new List<CombatCue>();

            int id = tracker.Begin(CombatActionKind.Melee, 0, 0, CombatEffector.None, 0f, cues);
            tracker.Schedule(id, CombatCueKind.Effect, 0.05f);
            tracker.Schedule(id, CombatCueKind.HitWindowOpened, 0.06f);
            tracker.Tick(0.07f, cues);
            tracker.End(0.08f, cues);
            int count = cues.Count;

            tracker.Tick(1f, cues);
            tracker.Schedule(id, CombatCueKind.HitWindowClosed, 1.1f);
            tracker.Tick(2f, cues);
            Assert.IsFalse(tracker.Anchor(id, CombatCueKind.Contact, 2f, cues));

            Assert.AreEqual(count, cues.Count);
            CollectionAssert.AreEqual(new[]
            {
                CombatCueKind.Windup, CombatCueKind.Effect, CombatCueKind.HitWindowOpened, CombatCueKind.HitWindowClosed,
                CombatCueKind.Cancelled,
            }, Kinds(cues), "the open hit window is closed before the cancellation");
        }

        [Test]
        public void ANewActionEndsThePreviousWithExactlyOneTerminalCue()
        {
            var tracker = new CombatCueTracker();
            var cues = new List<CombatCue>();

            int first = tracker.Begin(CombatActionKind.Melee, 0, 0, CombatEffector.None, 0f, cues);
            tracker.Anchor(first, CombatCueKind.Contact, 0.1f, cues);
            tracker.Schedule(first, CombatCueKind.Recovered, 0.9f);
            int second = tracker.Begin(CombatActionKind.Melee, 0, 1, CombatEffector.None, 0.3f, cues);
            tracker.Tick(1f, cues);
            int third = tracker.Begin(CombatActionKind.Charged, 0, 0, CombatEffector.None, 1.2f, cues);

            Assert.AreEqual(1, cues.Count(c => c.ActionId == first && c.IsTerminal));
            Assert.AreEqual(CombatCueKind.Recovered, cues.Single(c => c.ActionId == first && c.IsTerminal).Kind,
                "it had landed");
            Assert.AreEqual(CombatCueKind.Cancelled, cues.Single(c => c.ActionId == second && c.IsTerminal).Kind,
                "it had not landed");
            Assert.AreEqual(third, tracker.CurrentActionId);
        }

        [Test]
        public void EveryActionEndsWithOneTerminalCueWhateverHappens()
        {
            var random = new Random(7);
            var tracker = new CombatCueTracker();
            var cues = new List<CombatCue>();
            float time = 0f;
            int current = 0;

            for (int i = 0; i < 2000; i++)
            {
                time += 0.01f + (float)random.NextDouble() * 0.05f;
                switch (random.Next(6))
                {
                    case 0:
                        current = tracker.Begin(CombatActionKind.Melee, 0, 0, CombatEffector.None, time, cues);
                        tracker.Schedule(current, CombatCueKind.HitWindowOpened, time + 0.05f);
                        break;
                    case 1:
                        if (tracker.Anchor(current, CombatCueKind.Contact, time, cues))
                        {
                            tracker.Schedule(current, CombatCueKind.HitWindowClosed, time + 0.05f);
                            tracker.Schedule(current, CombatCueKind.Recovered, time + 0.2f);
                        }

                        break;
                    case 2:
                        tracker.End(time, cues);
                        break;
                    default:
                        tracker.Tick(time, cues);
                        break;
                }
            }

            tracker.End(time + 10f, cues);

            foreach (IGrouping<int, CombatCue> action in cues.GroupBy(c => c.ActionId))
            {
                List<CombatCue> list = action.ToList();
                Assert.AreEqual(CombatCueKind.Windup, list[0].Kind);
                Assert.AreEqual(1, list.Count(c => c.IsTerminal), $"action {action.Key}");
                Assert.IsTrue(list[list.Count - 1].IsTerminal, $"action {action.Key} sends nothing after its end");
                Assert.LessOrEqual(list.Count(c => c.Kind == CombatCueKind.Contact), 1);
                Assert.AreEqual(list.Count(c => c.Kind == CombatCueKind.HitWindowOpened),
                    list.Count(c => c.Kind == CombatCueKind.HitWindowClosed), "every opened window is closed");
                if (list.Any(c => c.Kind == CombatCueKind.Contact))
                {
                    Assert.AreEqual(CombatCueKind.Recovered, list[list.Count - 1].Kind);
                }
                else
                {
                    Assert.AreEqual(CombatCueKind.Cancelled, list[list.Count - 1].Kind);
                }
            }
        }

        // ------------------------------------------------------------------ slots

        [Test]
        public void SlotsAreNamedAfterTheirStates()
        {
            Assert.AreEqual("Arcane_Attack1", CombatSlotId.For(MagicDiscipline.Arcane, CombatSlotKind.Attack, 0).StateName);
            Assert.AreEqual("Aegis_Attack2_B", CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Attack, 1).TwinStateName);
            Assert.AreEqual("Flame_ChargeRelease", CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.ChargeRelease).StateName);
            Assert.AreEqual("Shared_BlockHold", CombatSlotId.Shared(CombatSlotKind.BlockHold).StateName);
            Assert.AreEqual(CombatAnimatorNames.EmptyState, CombatSlotId.None.StateName);
            Assert.IsTrue(CombatSlotId.Shared(CombatSlotKind.BlockHold).IsHeld);
            Assert.IsFalse(CombatSlotId.Shared(CombatSlotKind.Stagger).IsHeld);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Stance),
                CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Stance));
        }

        [Test]
        public void AStepBeyondTheAttackSlotsUsesTheLast()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            ResolvedSlot resolved = CombatSlotResolver.ResolveAttack(library, MagicDiscipline.Aegis, 3, false, 0f);

            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Attack, 2), resolved.Id);
        }

        [Test]
        public void AFullBodySlotPlaysOnTheWholeBodyOnlyWhereItCannotSlide()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();

            Assert.AreEqual(CombatLayer.FullBody,
                CombatSlotResolver.ResolveAttack(library, MagicDiscipline.Aegis, 2, false, 0f).Layer, "standing");
            Assert.AreEqual(CombatLayer.UpperBody,
                CombatSlotResolver.ResolveAttack(library, MagicDiscipline.Aegis, 2, false, 3f).Layer, "running");
            Assert.AreEqual(CombatLayer.UpperBody,
                CombatSlotResolver.ResolveAttack(library, MagicDiscipline.Arcane, 0, false, 0f).Layer, "an upper-body slot");
        }

        [Test]
        public void InTheAirTheAerialSlotPlaysThenTheGroundedSlotOnTheUpperBody()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            ResolvedSlot aerial = CombatSlotResolver.ResolveAttack(library, MagicDiscipline.Flame, 0, true, 5f);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.AerialAttack), aerial.Id);
            Assert.AreEqual(CombatLayer.FullBody, aerial.Layer);
            Assert.IsTrue(aerial.Aerial);

            library.shared.aerialAttack = new CombatAnimationSlot();
            ResolvedSlot fallback = CombatSlotResolver.ResolveAttack(library, MagicDiscipline.Aegis, 2, true, 0f);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Attack, 2), fallback.Id);
            Assert.AreEqual(CombatLayer.UpperBody, fallback.Layer, "a grounded full-body slot in the air");
            Assert.IsFalse(fallback.Aerial);
        }

        [Test]
        public void TheChargeGlowsInTheHandTheAnimationHoldsItIn()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();

            Assert.AreEqual(CombatEffector.LeftHand, CombatSlotResolver.ChargeEffector(library, MagicDiscipline.Arcane));
            Assert.AreEqual(CombatEffector.RightHand, CombatSlotResolver.ChargeEffector(library, MagicDiscipline.Flame));
            Assert.AreEqual(CombatEffector.None, CombatSlotResolver.ChargeEffector(new CombatAnimationLibrary(), MagicDiscipline.Flame));
        }

        // ------------------------------------------------------------------ defaults

        [Test]
        public void TheDefaultsGiveEveryDisciplineAndTheSharedMomentsAnAnimation()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();

            foreach (MagicDiscipline discipline in Enum.GetValues(typeof(MagicDiscipline)).Cast<MagicDiscipline>())
            {
                DisciplineAnimationSet set = library.GetDiscipline(discipline);
                Assert.IsNotNull(set, discipline.ToString());
                Assert.AreEqual(DisciplineDefaults.Melee(discipline).StepCount, set.AttackCount,
                    $"{discipline}: one attack slot per melee step");
                Assert.IsTrue(set.stance.IsAssigned && set.chargeStart.IsAssigned && set.chargeHold.IsAssigned &&
                              set.chargeRelease.IsAssigned && set.chargeCancel.IsAssigned, discipline.ToString());
            }

            SharedCombatAnimations shared = library.shared;
            Assert.IsTrue(shared.blockHold.IsAssigned && shared.blockImpact.IsAssigned && shared.stagger.IsAssigned &&
                          shared.heavyStagger.IsAssigned && shared.aerialAttack.IsAssigned && shared.aerialRelease.IsAssigned &&
                          shared.defeat.IsAssigned);
        }

        [Test]
        public void TheDefaultsUseExactlyTheClipsTheModelIsBuiltWith()
        {
            var used = new HashSet<string>();
            CombatAnimationDefaults.CreateMage().ForEachSlot((id, slot) =>
            {
                if (slot.IsAssigned)
                {
                    used.Add(slot.clip);
                }
            });

            CollectionAssert.AreEquivalent(CombatAnimationDefaults.RequiredClips, used);
        }

        /// <summary>Clips as the importer would mark them; every required clip exists and is Humanoid.</summary>
        private static CombatClipInfo FakeModel(string name)
        {
            return Array.IndexOf(CombatAnimationDefaults.RequiredClips, name) >= 0
                ? new CombatClipInfo(true, true, name.EndsWith("_Loop") || name == "Sword_Idle", ClipSeconds(name))
                : CombatClipInfo.Missing;
        }

        private static float ClipSeconds(string name)
        {
            float seconds = 0f;
            CombatAnimationDefaults.CreateMage().ForEachSlot((id, slot) =>
            {
                if (slot.clip == name)
                {
                    seconds = slot.clipSeconds;
                }
            });
            return seconds;
        }

        [Test]
        public void TheDefaultsPassTheValidationAgainstTheDefaultDisciplines()
        {
            var issues = new List<CombatAnimationIssue>();
            CombatAnimationValidation.Validate(CombatAnimationDefaults.CreateMage(), FakeModel, DisciplineDefaults.Melee, issues);

            List<CombatAnimationIssue> problems = issues.Where(i => i.Severity != CombatIssueSeverity.Info).ToList();
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheValidationReportsMissingClipsSlowClipsAndHeldClipsThatStop()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            library.GetDiscipline(MagicDiscipline.Arcane).attacks[0].clip = "Not_In_The_Model";
            library.GetDiscipline(MagicDiscipline.Flame).chargeHold.clip = "Arcane_RisingPalm";
            CombatAnimationSlot slow = library.GetDiscipline(MagicDiscipline.Aegis).attacks[0];
            slow.contact = 0.01f;
            slow.hitWindowOpen = 0.01f;

            var issues = new List<CombatAnimationIssue>();
            CombatAnimationValidation.Validate(library, FakeModel, DisciplineDefaults.Melee, issues);
            string all = string.Join("\n", issues);

            StringAssert.Contains("Missing animation: Arcane_Attack1 wants 'Not_In_The_Model'", all);
            StringAssert.Contains("Flame_ChargeHold: 'Arcane_RisingPalm' does not loop", all);
            StringAssert.Contains("Aegis step 1 (Force Palm)", all);
            StringAssert.Contains("early", all);
        }

        [Test]
        public void TheControllerSignatureChangesWithWhatTheControllerHoldsAndNothingElse()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            string before = library.ControllerSignature();
            Assert.AreEqual(before, CombatAnimationDefaults.CreateMage().ControllerSignature(), "stable");

            library.GetDiscipline(MagicDiscipline.Arcane).attacks[0].contact = 0.3f;
            library.tuning.maxPlaybackSpeed = 4f;
            Assert.AreEqual(before, library.ControllerSignature(), "moments and tuning are read at run time");

            library.GetDiscipline(MagicDiscipline.Arcane).attacks[0].mirror = true;
            Assert.AreNotEqual(before, library.ControllerSignature());

            CombatAnimationLibrary torso = CombatAnimationDefaults.CreateMage();
            torso.tuning.upperBodyIncludesTorso = false;
            Assert.AreNotEqual(before, torso.ControllerSignature());
        }

        // ------------------------------------------------------------------ model

        private sealed class Harness
        {
            public readonly CombatAnimationModel Model;
            public readonly List<CombatCue> Cues = new List<CombatCue>();
            public float Time;
            public CombatBodyState Body = new CombatBodyState(true, 0f);

            public Harness(CombatAnimationLibrary library = null)
            {
                Model = new CombatAnimationModel(library ?? CombatAnimationDefaults.CreateMage());
                Model.SetDiscipline(MagicDiscipline.Arcane);
                Run(0f);
            }

            public void Run(float seconds)
            {
                float end = Time + seconds;
                do
                {
                    Model.Tick(Time, Body);
                    Cues.AddRange(Model.Cues);
                    Model.Cues.Clear();
                    Time += Frame;
                }
                while (Time <= end + 1e-5f);

                Time = end;
            }

            /// <summary>Lets an event happen at the current time and collects its cues.</summary>
            public void Event(Action<CombatAnimationModel, float> happen)
            {
                happen(Model, Time);
                Cues.AddRange(Model.Cues);
                Model.Cues.Clear();
            }

            public CombatSlotId Upper => Model.GetLayerState(CombatLayer.UpperBody).Slot;
            public CombatSlotId Full => Model.GetLayerState(CombatLayer.FullBody).Slot;
        }

        private static MeleeStepProfile Step(MagicDiscipline discipline, int index)
        {
            return DisciplineDefaults.Melee(discipline).GetStep(index);
        }

        private static void Swing(Harness h, MagicDiscipline discipline, int step)
        {
            MeleeStepProfile profile = Step(discipline, step);
            h.Event((m, t) => m.SwingStarted(discipline, step, profile.startupSeconds, profile.recoverySeconds, t));
        }

        [Test]
        public void ASwingPlaysItsStepOnTheUpperBodyFittedToTheRules()
        {
            var h = new Harness { Body = new CombatBodyState(true, 4f) };
            Swing(h, MagicDiscipline.Arcane, 1);
            h.Run(Frame);

            MeleeStepProfile step = Step(MagicDiscipline.Arcane, 1);
            CombatAnimationSlot slot = CombatAnimationDefaults.CreateMage().GetDiscipline(MagicDiscipline.Arcane).attacks[1];
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Arcane, CombatSlotKind.Attack, 1), h.Upper);
            Assert.IsTrue(h.Full.IsNone);
            Assert.Greater(slot.contact * slot.clipSeconds / step.startupSeconds, 3f, "the Circling Palm's wind-up is longer than 3x allows");
            Assert.AreEqual(3f, h.Model.GetLayerSpeed(CombatLayer.UpperBody), 1e-4f, "so it is compressed to the limit");
            Assert.Greater(h.Model.GetLayerState(CombatLayer.UpperBody).StartNormalized, 0f, "and its start is skipped");

            h.Run(step.startupSeconds);
            h.Event((m, t) => m.SwingLanded(t));
            h.Run(Frame);
            float recovery = UnityEngine.Mathf.Clamp((slot.recoveryEnd - slot.contact) * slot.clipSeconds / step.recoverySeconds, 0.5f, 3f);
            Assert.AreEqual(recovery, h.Model.GetLayerSpeed(CombatLayer.UpperBody), 1e-4f, "its recovery fitted to the rules' recovery");

            h.Run(1f);
            Assert.IsTrue(h.Upper.IsNone, "it blends out after its recovery");
            Assert.AreEqual(0f, h.Model.GetLayerWeight(CombatLayer.UpperBody), 1e-4f);
        }

        [Test]
        public void ASwingSendsItsCuesAroundTheRulesContactAndContactOnlyOnce()
        {
            var h = new Harness();
            Swing(h, MagicDiscipline.Flame, 0);
            h.Run(0.12f);
            h.Event((m, t) => m.SwingLanded(t));
            h.Event((m, t) => m.SwingLanded(t));
            h.Run(1.5f);

            CollectionAssert.AreEqual(new[]
            {
                CombatCueKind.Windup, CombatCueKind.Effect, CombatCueKind.HitWindowOpened, CombatCueKind.Contact,
                CombatCueKind.HitWindowClosed, CombatCueKind.Recovered,
            }, Kinds(h.Cues));
            Assert.AreEqual(CombatEffector.RightHand, h.Cues[0].Effector);
            Assert.AreEqual((int)MagicDiscipline.Flame, h.Cues[0].Discipline);
        }

        [Test]
        public void AnInterruptedSwingIsCancelledAndNothingOfItFollows()
        {
            var h = new Harness();
            Swing(h, MagicDiscipline.Aegis, 2);
            h.Run(0.1f);
            h.Event((m, t) => m.SwingInterrupted(t));
            int count = h.Cues.Count;
            h.Run(Frame);

            Assert.AreEqual(CombatCueKind.Cancelled, h.Cues[count - 1].Kind);
            Assert.IsTrue(h.Full.IsNone && h.Upper.IsNone);
            Assert.AreEqual(0.1f, h.Model.GetLayerState(CombatLayer.FullBody).BlendSeconds, 1e-5f, "the interruption blend");

            h.Event((m, t) => m.SwingLanded(t));
            h.Run(2f);
            Assert.AreEqual(count, h.Cues.Count, "a late landing and the clock send nothing");
            Assert.IsFalse(h.Cues.Any(c => c.Kind == CombatCueKind.Contact));
        }

        [Test]
        public void ACommittedStrikeUsesTheWholeBodyWhenStandingAndTheUpperBodyWhenMoving()
        {
            var h = new Harness();
            Swing(h, MagicDiscipline.Aegis, 2);
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Attack, 2), h.Full);

            h.Run(2f);
            h.Body = new CombatBodyState(true, 2.2f);
            h.Run(Frame); // The body state is read each frame, as the director does.
            Swing(h, MagicDiscipline.Aegis, 2);
            h.Run(Frame);
            Assert.IsTrue(h.Full.IsNone);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Attack, 2), h.Upper);
        }

        [Test]
        public void AChargeStartsThenHoldsThenReleasesFromItsReleaseFrame()
        {
            var h = new Harness();
            h.Event((m, t) => m.ChargeBegan(MagicDiscipline.Arcane, t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Arcane, CombatSlotKind.ChargeStart), h.Upper);

            h.Run(0.6f);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Arcane, CombatSlotKind.ChargeHold), h.Upper);

            h.Event((m, t) => m.ChargeEnded(true, t));
            h.Run(Frame);
            CombatLayerState release = h.Model.GetLayerState(CombatLayer.UpperBody);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Arcane, CombatSlotKind.ChargeRelease), release.Slot);
            Assert.AreEqual(0.067f, release.StartNormalized, 1e-4f, "the bolt has already left: start at the release frame");

            h.Run(1f);
            CollectionAssert.AreEqual(new[]
            {
                CombatCueKind.Windup, CombatCueKind.Effect, CombatCueKind.Release, CombatCueKind.Recovered,
            }, Kinds(h.Cues));
            Assert.IsTrue(h.Upper.IsNone);
        }

        [Test]
        public void ACancelledChargeLowersTheHandAndSendsCancelled()
        {
            var h = new Harness();
            h.Event((m, t) => m.ChargeBegan(MagicDiscipline.Flame, t));
            h.Run(0.8f);
            h.Event((m, t) => m.ChargeEnded(false, t));
            h.Run(Frame);

            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.ChargeCancel), h.Upper);
            Assert.AreEqual(CombatCueKind.Cancelled, h.Cues.Last().Kind);
            Assert.IsFalse(h.Cues.Any(c => c.Kind == CombatCueKind.Release));
        }

        [Test]
        public void BlockingTakesTheUpperBodyAndAnAttackItPushedAsideDoesNotReturn()
        {
            var h = new Harness();
            Swing(h, MagicDiscipline.Arcane, 0);
            h.Run(0.07f);
            h.Event((m, t) => m.SwingLanded(t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotKind.Attack, h.Upper.Kind);

            h.Event((m, t) => m.SetBlocking(true, t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.BlockHold), h.Upper);

            h.Event((m, t) => m.SetBlocking(false, t));
            h.Run(Frame);
            Assert.IsTrue(h.Upper.IsNone, "the attack's recovery is not resumed half-way");
        }

        [Test]
        public void AStoppedHitPlaysTheGuardImpactThenTheHoldAndIsNotAStagger()
        {
            var h = new Harness();
            h.Event((m, t) => m.SetBlocking(true, t));
            h.Run(0.2f);
            h.Event((m, t) => m.BlockedHit(t));
            h.Event((m, t) => m.Damaged(3f, t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.BlockImpact), h.Upper);

            h.Run(1f);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.BlockHold), h.Upper);
        }

        [Test]
        public void DamagePlaysAStaggerByItsAmount()
        {
            var h = new Harness();
            h.Event((m, t) => m.Damaged(3f, t));
            h.Run(Frame);
            Assert.IsTrue(h.Upper.IsNone, "below the stagger damage");

            h.Event((m, t) => m.Damaged(5f, t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.Stagger), h.Upper);

            h.Event((m, t) => m.Damaged(12f, t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.HeavyStagger), h.Upper);
        }

        [Test]
        public void TheSameOneShotTwiceInARowAlternatesBetweenItsTwoStates()
        {
            var h = new Harness();
            h.Event((m, t) => m.Damaged(5f, t));
            h.Run(Frame);
            CombatLayerState first = h.Model.GetLayerState(CombatLayer.UpperBody);
            h.Event((m, t) => m.Damaged(5f, t));
            h.Run(Frame);
            CombatLayerState second = h.Model.GetLayerState(CombatLayer.UpperBody);

            Assert.AreEqual(first.Slot, second.Slot);
            Assert.AreNotEqual(first.Version, second.Version, "a new request");
            Assert.AreNotEqual(first.Twin, second.Twin, "on the other state, so it blends");
        }

        [Test]
        public void AStaggerDoesNotHideASwingTheRulesAreStillCarryingOut()
        {
            // Moving: the Flare plays on the upper body, and the stagger does not replace it.
            var h = new Harness { Body = new CombatBodyState(true, 3f) };
            Swing(h, MagicDiscipline.Flame, 2);
            h.Run(Frame);
            h.Event((m, t) => m.Damaged(6f, t));
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Attack, 2), h.Upper);

            // Standing: the Flare plays on the whole body, and the stagger does not cover its arms either.
            var standing = new Harness();
            Swing(standing, MagicDiscipline.Flame, 2);
            standing.Run(Frame);
            standing.Event((m, t) => m.Damaged(6f, t));
            standing.Run(Frame);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Attack, 2), standing.Full);
            Assert.IsTrue(standing.Upper.IsNone, "nothing over the whole-body strike");
        }

        [Test]
        public void ClimbingStopsEveryCombatAnimationAndEndsTheAction()
        {
            var h = new Harness();
            h.Event((m, t) => m.ChargeBegan(MagicDiscipline.Aegis, t));
            h.Run(0.3f);
            Assert.Greater(h.Model.GetLayerWeight(CombatLayer.Stance), 0.5f, "standing still while charging");
            h.Event((m, t) => m.SetHandsBusy(true, t));
            h.Run(Frame);

            Assert.IsTrue(h.Upper.IsNone && h.Full.IsNone);
            Assert.AreEqual(CombatCueKind.Cancelled, h.Cues.Last().Kind);

            h.Run(0.1f);
            Assert.AreEqual(0f, h.Model.GetLayerWeight(CombatLayer.Stance), 1e-4f, "the stance is gone within the interruption blend");
        }

        [Test]
        public void DefeatHoldsItsPoseUntilRevived()
        {
            var h = new Harness();
            h.Event((m, t) => m.SetDefeated(true, t));
            h.Run(3f);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.Defeat), h.Full);
            Assert.IsTrue(h.Upper.IsNone);

            h.Event((m, t) => m.SetDefeated(false, t));
            h.Run(Frame);
            Assert.IsTrue(h.Full.IsNone);
        }

        [Test]
        public void AnAerialAttackUsesTheWholeBodyAndEndsOnLanding()
        {
            var h = new Harness { Body = new CombatBodyState(false, 5f) };
            h.Run(Frame);
            Swing(h, MagicDiscipline.Arcane, 0);
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.Shared(CombatSlotKind.AerialAttack), h.Full);

            h.Body = new CombatBodyState(true, 5f);
            h.Run(Frame);
            Assert.IsTrue(h.Full.IsNone, "the landing shows");
        }

        [Test]
        public void TheStanceFollowsTheDisciplineFadesWithSpeedAndLingersAfterCombat()
        {
            var h = new Harness();
            Assert.AreEqual(0f, h.Model.GetLayerWeight(CombatLayer.Stance), "not in combat");

            Swing(h, MagicDiscipline.Arcane, 0);
            h.Run(1f);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Arcane, CombatSlotKind.Stance),
                h.Model.GetLayerState(CombatLayer.Stance).Slot);
            Assert.AreEqual(1f, h.Model.GetLayerWeight(CombatLayer.Stance), 1e-4f);

            h.Model.SetDiscipline(MagicDiscipline.Aegis);
            h.Run(Frame);
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Stance),
                h.Model.GetLayerState(CombatLayer.Stance).Slot);

            h.Body = new CombatBodyState(true, 4f);
            h.Run(0.5f);
            Assert.AreEqual(0f, h.Model.GetLayerWeight(CombatLayer.Stance), 1e-4f, "running");

            h.Body = new CombatBodyState(true, 0f);
            h.Run(5f);
            Assert.AreEqual(0f, h.Model.GetLayerWeight(CombatLayer.Stance), 1e-4f, "combat is over");
        }

        [Test]
        public void WithoutAnimationsTheCuesStillFollowTheRules()
        {
            var h = new Harness(new CombatAnimationLibrary());
            Swing(h, MagicDiscipline.Arcane, 0);
            h.Run(0.07f);
            h.Event((m, t) => m.SwingLanded(t));
            h.Run(1f);

            Assert.IsTrue(h.Upper.IsNone && h.Full.IsNone);
            CollectionAssert.AreEqual(new[] { CombatCueKind.Windup, CombatCueKind.Contact, CombatCueKind.Recovered }, Kinds(h.Cues));
        }

        [Test]
        public void TheModelAllocatesNothingPerFrameOnceWarm()
        {
            AllocationMeter.Require();

            var model = new CombatAnimationModel(CombatAnimationDefaults.CreateMage());
            var body = new CombatBodyState(true, 1f);
            float time = 0f;

            void Play(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    int phase = i % 240;
                    if (phase == 0) model.SwingStarted(MagicDiscipline.Flame, 0, 0.12f, 0.26f, time);
                    if (phase == 8) model.SwingLanded(time);
                    if (phase == 60) model.ChargeBegan(MagicDiscipline.Arcane, time);
                    if (phase == 120) model.ChargeEnded(true, time);
                    if (phase == 150) model.SetBlocking(true, time);
                    if (phase == 160) model.BlockedHit(time);
                    if (phase == 180) model.SetBlocking(false, time);
                    if (phase == 200) model.Damaged(12f, time);
                    model.Tick(time, body);
                    model.Cues.Clear();
                    time += Frame;
                }
            }

            Play(480);
            AllocationMeter.Begin();
            Play(2400);
            long allocated = AllocationMeter.End();

            Assert.AreEqual(0L, allocated, "allocated over 40 seconds of combat");
        }
    }
}
