using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.CombatAnimation;
using Threefold.InputHandling;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Tests.Editor
{
    /// <summary>
    /// The three martial styles (Docs/MartialStyles.md): three strikes per discipline, a distinct stance and rhythm,
    /// contact shown when the rules land the hit, no snapping, no stuck state, movement restored after every strike,
    /// buffering and the repeat guard, the hand-over of whole-body strikes, the comparison sequence, and the upgrade of
    /// assets generated earlier. Plain C#: runs outside Unity in Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class MartialStylesTests
    {
        private const float Frame = 1f / 60f;
        private static readonly MagicDiscipline[] All = { MagicDiscipline.Arcane, MagicDiscipline.Flame, MagicDiscipline.Aegis };

        private static CombatAnimationLibrary Library() => CombatAnimationDefaults.CreateMage();

        // ------------------------------------------------------------------ three strikes, distinct

        [Test]
        public void EveryDisciplineHasThreeStrikesInTheRulesAndInTheAnimations()
        {
            CombatAnimationLibrary library = Library();
            foreach (MagicDiscipline discipline in All)
            {
                Assert.AreEqual(3, DisciplineDefaults.Melee(discipline).StepCount, $"{discipline} rules");
                DisciplineAnimationSet set = library.GetDiscipline(discipline);
                Assert.AreEqual(3, set.AttackCount, $"{discipline} animation slots");
                Assert.AreEqual(3, set.attacks.Select(a => a.clip).Distinct().Count(), $"{discipline}: three different strikes");
            }
        }

        [Test]
        public void NoTwoDisciplinesShareAStanceOrAStrike()
        {
            CombatAnimationLibrary library = Library();
            var clips = new List<string>();
            foreach (MagicDiscipline discipline in All)
            {
                DisciplineAnimationSet set = library.GetDiscipline(discipline);
                clips.Add(set.stance.clip);
                clips.AddRange(set.attacks.Select(a => a.clip));
            }

            Assert.AreEqual(clips.Count, clips.Distinct().Count(), string.Join(", ", clips));
        }

        [Test]
        public void TheStepNamesMatchTheChoreography()
        {
            CollectionAssert.AreEqual(new[] { "Rising Palm", "Circling Palm", "Arcane Lance" }, Names(MagicDiscipline.Arcane));
            CollectionAssert.AreEqual(new[] { "Ember Fist", "Ember Hook", "Flare" }, Names(MagicDiscipline.Flame));
            CollectionAssert.AreEqual(new[] { "Force Palm", "Bulwark Forearm", "Bulwark Slam" }, Names(MagicDiscipline.Aegis));
        }

        private static string[] Names(MagicDiscipline discipline)
        {
            return DisciplineDefaults.Melee(discipline).steps.Select(s => s.name).ToArray();
        }

        [Test]
        public void ArcaneAlternatesHandsAndAegisEndsTwoHanded()
        {
            CombatAnimationSlot[] arcane = Library().GetDiscipline(MagicDiscipline.Arcane).attacks;
            Assert.AreEqual(CombatEffector.LeftHand, arcane[0].effector);
            Assert.AreEqual(CombatEffector.RightHand, arcane[1].effector, "the Circling Palm is the other hand");
            Assert.IsTrue(arcane[1].mirror, "made by mirroring the same authored arc");
            Assert.AreEqual(CombatEffector.BothHands, Library().GetDiscipline(MagicDiscipline.Aegis).attacks[2].effector);
        }

        [Test]
        public void TheRhythmsDifferArcaneFastestAegisHeaviest()
        {
            float Average(MagicDiscipline d, Func<MeleeStepProfile, float> value) =>
                DisciplineDefaults.Melee(d).steps.Average(value);

            float arcane = Average(MagicDiscipline.Arcane, s => s.recoverySeconds);
            float flame = Average(MagicDiscipline.Flame, s => s.recoverySeconds);
            float aegis = Average(MagicDiscipline.Aegis, s => s.recoverySeconds);
            Assert.Less(arcane, flame, "Arcane recovers fastest");
            Assert.Less(flame, aegis, "Flame recovers in between, Aegis slowest");

            Assert.Less(Average(MagicDiscipline.Arcane, s => s.startupSeconds), Average(MagicDiscipline.Flame, s => s.startupSeconds));
            Assert.Less(Average(MagicDiscipline.Flame, s => s.startupSeconds), Average(MagicDiscipline.Aegis, s => s.startupSeconds),
                "Aegis has the strongest anticipation");

            // Aegis's slower recovery is paid back in stagger: more stagger per second of combo than either other style.
            float StaggerRate(MagicDiscipline d) => DisciplineDefaults.Melee(d).steps.Sum(s => s.stagger) /
                                                     DisciplineDefaults.Melee(d).steps.Sum(s => s.TotalSeconds);
            Assert.Greater(StaggerRate(MagicDiscipline.Aegis), StaggerRate(MagicDiscipline.Arcane) * 2f);
            Assert.Greater(StaggerRate(MagicDiscipline.Aegis), StaggerRate(MagicDiscipline.Flame) * 2f);
        }

        [Test]
        public void ArcaneKeepsItsRhythmAndDamageWithTheLongerAnticipation()
        {
            // Version 2: 0.07 + 0.2, 0.07 + 0.2, 0.1 + 0.3 seconds; damage 9, 10, 15.
            float[] totals = { 0.27f, 0.27f, 0.4f };
            float[] damage = { 9f, 10f, 15f };
            MeleeStepProfile[] steps = DisciplineDefaults.Melee(MagicDiscipline.Arcane).steps;
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(totals[i], steps[i].TotalSeconds, 1e-4f);
                Assert.AreEqual(damage[i], steps[i].damage);
                Assert.GreaterOrEqual(steps[i].startupSeconds, 0.1f, "long enough to show an anticipation");
            }
        }

        [Test]
        public void OnlyTheCommittedStylesUseTheWholeBody()
        {
            CombatAnimationLibrary library = Library();
            Assert.IsTrue(library.GetDiscipline(MagicDiscipline.Arcane).attacks.All(a => a.scope == CombatBodyScope.UpperBody),
                "Arcane flows: every strike on the upper body, at full movement");
            CombatAnimationSlot[] flame = library.GetDiscipline(MagicDiscipline.Flame).attacks;
            Assert.AreEqual(CombatBodyScope.UpperBody, flame[0].scope, "the punch");
            Assert.AreEqual(CombatBodyScope.FullBody, flame[1].scope, "the hook");
            Assert.AreEqual(CombatBodyScope.FullBody, flame[2].scope, "the sweep");
            CombatAnimationSlot[] aegis = library.GetDiscipline(MagicDiscipline.Aegis).attacks;
            Assert.AreEqual(CombatBodyScope.FullBody, aegis[0].scope, "the sinking palm");
            Assert.AreEqual(CombatBodyScope.UpperBody, aegis[1].scope, "the forearm");
            Assert.AreEqual(CombatBodyScope.FullBody, aegis[2].scope, "the slam");
        }

        // ------------------------------------------------------------------ hit timing

        [Test]
        public void EveryStrikeShowsItsContactFrameWhenTheRulesLandTheHit()
        {
            CombatAnimationLibrary library = Library();
            foreach (MagicDiscipline discipline in All)
            {
                MeleeComboProfile combo = DisciplineDefaults.Melee(discipline);
                for (int step = 0; step < combo.StepCount; step++)
                {
                    MeleeStepProfile rules = combo.steps[step];
                    var model = new CombatAnimationModel(library);
                    model.SetDiscipline(discipline);
                    model.Tick(0f, new CombatBodyState(true, 0f));
                    model.SwingStarted(discipline, step, rules.startupSeconds, rules.recoverySeconds, 0f);

                    float t = 0f;
                    while (t + Frame < rules.startupSeconds)
                    {
                        t += Frame;
                        model.Tick(t, new CombatBodyState(true, 0f));
                    }

                    t = rules.startupSeconds;
                    model.Tick(t, new CombatBodyState(true, 0f));
                    model.SwingLanded(t);

                    CombatAnimationSlot slot = library.GetDiscipline(discipline).attacks[step];
                    CombatLayer layer = slot.scope == CombatBodyScope.FullBody ? CombatLayer.FullBody : CombatLayer.UpperBody;
                    CombatLayerState state = model.GetLayerState(layer);
                    CombatTimeFit fit = CombatTimeWarp.Fit(slot, rules.startupSeconds, rules.recoverySeconds, 0.5f, 3f);
                    float shown = state.StartNormalized + fit.WindupSpeed * rules.startupSeconds / slot.Seconds;

                    Assert.AreEqual(CombatSlotId.For(discipline, CombatSlotKind.Attack, step), state.Slot, $"{discipline} {step + 1}");
                    Assert.AreEqual(slot.contact, shown, Frame * fit.WindupSpeed / slot.Seconds + 1e-4f,
                        $"{discipline} step {step + 1} ({rules.name}) shows its contact frame within one frame of the hit");
                    Assert.IsTrue(model.Cues.Any(c => c.Kind == CombatCueKind.Contact && Mathf.Abs(c.Time - t) < 1e-5f));
                }
            }
        }

        [Test]
        public void TheHitWindowSurroundsTheContactInEveryStrike()
        {
            foreach (MagicDiscipline discipline in All)
            {
                foreach (CombatAnimationSlot slot in Library().GetDiscipline(discipline).attacks)
                {
                    Assert.LessOrEqual(slot.hitWindowOpen, slot.contact, slot.clip);
                    Assert.GreaterOrEqual(slot.hitWindowClose, slot.contact, slot.clip);
                    Assert.LessOrEqual(slot.effect, slot.hitWindowOpen, slot.clip);
                    Assert.Greater(slot.recoveryEnd, slot.hitWindowClose, slot.clip);
                }
            }
        }

        // ------------------------------------------------------------------ no snapping

        [Test]
        public void EveryStrikeAndStanceBlendsInAndOut()
        {
            foreach (MagicDiscipline discipline in All)
            {
                DisciplineAnimationSet set = Library().GetDiscipline(discipline);
                Assert.Greater(set.stance.blendInSeconds, 0.1f, $"{discipline} stance");
                Assert.Greater(set.stance.blendOutSeconds, 0.1f, $"{discipline} stance");
                foreach (CombatAnimationSlot slot in set.attacks)
                {
                    Assert.GreaterOrEqual(slot.blendInSeconds, 0.05f, slot.clip);
                    Assert.GreaterOrEqual(slot.blendOutSeconds, 0.1f, slot.clip);
                }
            }
        }

        [Test]
        public void AWholeComboCrossFadesEveryChangeAndReturnsToTheStance()
        {
            foreach (MagicDiscipline discipline in All)
            {
                var run = new StyleRun(discipline);
                run.TapEvery(0.1f, 2.5f);
                run.Idle(1.5f);

                Assert.AreEqual(3, run.Started.Distinct().Count(), $"{discipline}: all three strikes played");
                Assert.IsTrue(run.Changes.All(c => c.Blend > 0f || c.Weight < 0.001f),
                    $"{discipline}: a visible layer never cuts to a new state without a blend: " +
                    string.Join(", ", run.Changes.Where(c => c.Blend <= 0f && c.Weight >= 0.001f).Select(c => c.ToString())));
                Assert.IsTrue(run.Animation.GetLayerState(CombatLayer.UpperBody).Slot.IsNone, "the upper body is free again");
                Assert.IsTrue(run.Animation.GetLayerState(CombatLayer.FullBody).Slot.IsNone, "the whole body is free again");
                Assert.AreEqual(CombatSlotId.For(discipline, CombatSlotKind.Stance), run.Animation.GetLayerState(CombatLayer.StanceFull).Slot,
                    "back in the stance");
            }
        }

        // ------------------------------------------------------------------ stance

        [Test]
        public void TheWholeStanceShowsStandingAndOnlyItsUpperBodyWhileWalking()
        {
            var model = new CombatAnimationModel(Library());
            model.SetDiscipline(MagicDiscipline.Aegis);
            model.Tick(0f, new CombatBodyState(true, 0f));
            model.SetBlocking(true, 0f);
            model.SetBlocking(false, 0.05f);
            Run(model, 0.05f, 1f, new CombatBodyState(true, 0f));
            Assert.AreEqual(1f, model.GetLayerWeight(CombatLayer.StanceFull), 1e-3f, "standing: the low, wide legs show");
            Assert.AreEqual(1f, model.GetLayerWeight(CombatLayer.Stance), 1e-3f);

            Run(model, 1f, 2f, new CombatBodyState(true, 1f));
            Assert.AreEqual(0f, model.GetLayerWeight(CombatLayer.StanceFull), 1e-3f, "walking: the legs walk");
            Assert.Greater(model.GetLayerWeight(CombatLayer.Stance), 0.2f, "the guard still shows on the upper body");

            Run(model, 2f, 2.5f, new CombatBodyState(false, 0f));
            Assert.AreEqual(0f, model.GetLayerWeight(CombatLayer.StanceFull), 1e-3f, "in the air: no stance");
        }

        private static void Run(CombatAnimationModel model, float from, float to, CombatBodyState body)
        {
            for (float t = from; t <= to + 1e-5f; t += Frame)
            {
                model.Tick(t, body);
            }
        }

        // ------------------------------------------------------------------ hand-over

        [Test]
        public void AWholeBodyStrikeMovesToTheUpperBodyWhenTheMageMovesAndContinuesWhereItWas()
        {
            CombatAnimationLibrary library = Library();
            MeleeStepProfile slam = DisciplineDefaults.Melee(MagicDiscipline.Aegis).steps[2];
            var model = new CombatAnimationModel(library);
            model.SetDiscipline(MagicDiscipline.Aegis);
            model.Tick(0f, new CombatBodyState(true, 0f));
            model.SwingStarted(MagicDiscipline.Aegis, 2, slam.startupSeconds, slam.recoverySeconds, 0f);
            Run(model, Frame, 0.1f, new CombatBodyState(true, 0.3f));

            CombatSlotId id = CombatSlotId.For(MagicDiscipline.Aegis, CombatSlotKind.Attack, 2);
            Assert.AreEqual(id, model.GetLayerState(CombatLayer.FullBody).Slot, "standing: the whole body");
            float before = model.GetLayerState(CombatLayer.FullBody).StartNormalized;

            model.Tick(0.1f + Frame, new CombatBodyState(true, 2f));
            CombatLayerState upper = model.GetLayerState(CombatLayer.UpperBody);
            Assert.AreEqual(id, upper.Slot, "moving: the same strike on the upper body");
            Assert.IsTrue(model.GetLayerState(CombatLayer.FullBody).Slot.IsNone, "the legs are free to walk");
            Assert.Greater(upper.StartNormalized, before, "it continues from where the clip was, not from its start");
            Assert.AreEqual(library.tuning.handOverBlendSeconds, upper.BlendSeconds, 1e-5f);

            model.Tick(0.1f + 2f * Frame, new CombatBodyState(true, 0f));
            Assert.AreEqual(id, model.GetLayerState(CombatLayer.UpperBody).Slot, "it does not move back");

            model.SwingLanded(slam.startupSeconds);
            Assert.IsTrue(model.Cues.Any(c => c.Kind == CombatCueKind.Contact), "its contact still comes from the rules");
        }

        [Test]
        public void LeavingTheGroundAlsoHandsAWholeBodyStrikeToTheUpperBody()
        {
            MeleeStepProfile hook = DisciplineDefaults.Melee(MagicDiscipline.Flame).steps[1];
            var model = new CombatAnimationModel(Library());
            model.SetDiscipline(MagicDiscipline.Flame);
            model.Tick(0f, new CombatBodyState(true, 0f));
            model.SwingStarted(MagicDiscipline.Flame, 1, hook.startupSeconds, hook.recoverySeconds, 0f);
            model.Tick(Frame, new CombatBodyState(true, 0f));
            model.Tick(2f * Frame, new CombatBodyState(false, 0f));

            Assert.IsTrue(model.GetLayerState(CombatLayer.FullBody).Slot.IsNone, "no lunge in mid-air");
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Attack, 1), model.GetLayerState(CombatLayer.UpperBody).Slot);
        }

        // ------------------------------------------------------------------ movement

        [Test]
        public void CommittedStrikesRestrictMovementAroundTheirHitAndRestoreItAfter()
        {
            foreach (MagicDiscipline discipline in All)
            {
                foreach (MeleeStepProfile step in DisciplineDefaults.Melee(discipline).steps)
                {
                    Assert.AreEqual(1f, step.MoveSpeedAt(-0.01f), $"{step.name}: before");
                    Assert.AreEqual(1f, step.MoveSpeedAt(step.TotalSeconds + 0.01f), $"{step.name}: after the recovery");
                    Assert.LessOrEqual(step.MoveSpeedAt(step.startupSeconds), step.moveSpeedMultiplier, $"{step.name}: at the hit");
                    Assert.AreEqual(step.moveSpeedMultiplier, step.MoveSpeedAt(step.TotalSeconds - 0.001f), 1e-5f,
                        $"{step.name}: the end of the recovery moves at the swing's own speed again");
                }
            }

            Assert.IsTrue(DisciplineDefaults.Melee(MagicDiscipline.Arcane).steps.All(s => s.MoveSpeedAt(s.startupSeconds) >= 1f),
                "Arcane never slows the Mage");
            Assert.AreEqual(0f, DisciplineDefaults.Melee(MagicDiscipline.Aegis).steps[2].MoveSpeedAt(0.26f), "the slam roots the Mage");
            Assert.Greater(DisciplineDefaults.Melee(MagicDiscipline.Aegis).steps[2].MoveSpeedAt(0.02f), 0.5f,
                "but not during its early wind-up");
        }

        [Test]
        public void TheCommitmentWindowNeverReachesBeyondTheSwing()
        {
            var step = new MeleeStepProfile
            {
                startupSeconds = 0.1f, recoverySeconds = 0.2f, moveSpeedMultiplier = 0.8f,
                committedMoveSpeedMultiplier = 0f, commitBeforeHitSeconds = 5f, commitAfterHitSeconds = 5f,
            };
            Assert.AreEqual(0f, step.MoveSpeedAt(0f));
            Assert.AreEqual(0f, step.MoveSpeedAt(0.3f));
            Assert.AreEqual(1f, step.MoveSpeedAt(0.31f));
        }

        [Test]
        public void TheComboMachineReportsTheCommittedSpeedAtTheHitAndFullSpeedAfter()
        {
            MeleeComboProfile aegis = DisciplineDefaults.Melee(MagicDiscipline.Aegis);
            var combo = new MeleeComboMachine();
            var events = new List<MeleeEvent>();
            combo.Request(0f);
            combo.Tick(0f, aegis, events);
            combo.Tick(0.05f, aegis, events);
            Assert.AreEqual(aegis.steps[0].moveSpeedMultiplier, combo.MoveSpeedMultiplier, 1e-5f, "early in the wind-up");
            combo.Tick(0.18f, aegis, events);
            Assert.AreEqual(aegis.steps[0].committedMoveSpeedMultiplier, combo.MoveSpeedMultiplier, 1e-5f, "at the hit");
            combo.Tick(0.6f, aegis, events);
            Assert.AreEqual(1f, combo.MoveSpeedMultiplier, "recovered");
        }

        // ------------------------------------------------------------------ buffering

        [Test]
        public void ATapMadeAfterAHeavyHitIsPerformedWhenItsRecoveryEnds()
        {
            MeleeComboProfile aegis = DisciplineDefaults.Melee(MagicDiscipline.Aegis);
            MeleeStepProfile slam = aegis.steps[2];
            Assert.Greater(slam.recoverySeconds, aegis.inputBufferSeconds, "the slam's recovery is longer than the buffer");

            // Play the palm and the forearm with a tap right after each hit, up to the slam's hit.
            var combo = new MeleeComboMachine();
            var events = new List<MeleeEvent>();
            float t = 0f;
            float slamHit = -1f;
            combo.Request(t);
            while (t < 3f && slamHit < 0f)
            {
                t += Frame;
                events.Clear();
                combo.Tick(t, aegis, events);
                foreach (MeleeEvent e in events.Where(e => e.Kind == MeleeEventKind.Hit))
                {
                    if (e.StepIndex < 2) combo.Request(t);
                    else slamHit = t;
                }
            }

            Assert.Greater(slamHit, 0f, "the slam landed");
            t += Frame;
            combo.Tick(t, aegis, events);
            combo.Request(t); // right after the slam's hit, about 0.44 s before its recovery ends
            events.Clear();
            Advance(combo, aegis, events, ref t, slamHit + slam.recoverySeconds + 0.05f);

            MeleeEvent next = events.FirstOrDefault(e => e.Kind == MeleeEventKind.StepStarted);
            Assert.AreEqual(MeleeEventKind.StepStarted, next.Kind, "the tap was remembered");
            Assert.AreEqual(0, next.StepIndex, "after the third strike the combo starts again");
        }

        [Test]
        public void ADoubleTouchStartsOneStrikeAndADeliberateSecondTapChains()
        {
            // Arcane's whole swing fits in the input buffer, so without the guard a bounce would become a second strike.
            MeleeComboProfile arcane = DisciplineDefaults.Melee(MagicDiscipline.Arcane);
            Assert.Less(arcane.steps[0].TotalSeconds, arcane.inputBufferSeconds);
            var combo = new MeleeComboMachine();
            var events = new List<MeleeEvent>();
            float t = 0f;
            combo.Request(t);
            Advance(combo, arcane, events, ref t, 0.03f);
            combo.Request(t); // 30 ms later: the same finger bouncing
            Advance(combo, arcane, events, ref t, 1.2f);
            Assert.AreEqual(1, events.Count(e => e.Kind == MeleeEventKind.StepStarted), "one strike for a double touch");

            combo.Reset();
            events.Clear();
            t = 0f;
            combo.Request(t);
            Advance(combo, arcane, events, ref t, 0.15f);
            combo.Request(t); // 150 ms later: a real second tap
            Advance(combo, arcane, events, ref t, 1.2f);
            Assert.AreEqual(2, events.Count(e => e.Kind == MeleeEventKind.StepStarted), "a deliberate tap chains");
            Assert.AreEqual(1, events.Where(e => e.Kind == MeleeEventKind.StepStarted).ElementAt(1).StepIndex);
        }

        [Test]
        public void TheRepeatGuardCanBeSwitchedOff()
        {
            MeleeComboProfile arcane = DisciplineDefaults.Melee(MagicDiscipline.Arcane);
            arcane.repeatGuardSeconds = 0f;
            var combo = new MeleeComboMachine();
            var events = new List<MeleeEvent>();
            float t = 0f;
            combo.Request(t);
            Advance(combo, arcane, events, ref t, 0.03f);
            combo.Request(t);
            Advance(combo, arcane, events, ref t, 1.2f);
            Assert.AreEqual(2, events.Count(e => e.Kind == MeleeEventKind.StepStarted));
        }

        private static void Advance(MeleeComboMachine combo, MeleeComboProfile profile, List<MeleeEvent> events, ref float t, float until)
        {
            while (t < until - 1e-6f)
            {
                t = Mathf.Min(until, t + Frame);
                combo.Tick(t, profile, events);
            }
        }

        // ------------------------------------------------------------------ stuck states

        [Test]
        public void RandomPlayNeverLeavesAnAttackPlayingOrTheMageSlowed([Values(11, 23, 37)] int seed)
        {
            // Forty seconds of play, and longer if that brought few attacks: random play holds the guard or has
            // the hands busy for much of the time, so over 60 seeds the first 40 seconds start between 9 and 43
            // strikes. The run goes on until more than 20 have started, so every seed tests what it is meant to.
            var random = new System.Random(seed);
            var run = new StyleRun(MagicDiscipline.Arcane);
            for (int second = 0; second < 40 || (run.Started.Count <= 20 && second < 120); second++)
            {
                run.RandomSecond(random);
            }

            run.Idle(3f);

            Assert.IsFalse(run.Rules.Combo.IsSwinging, "no swing left in progress");
            Assert.IsFalse(run.Rules.IsCharging, "no charge left held");
            Assert.AreEqual(1f, run.Rules.MoveSpeedMultiplier, "full movement");
            Assert.IsTrue(run.Rules.SprintAllowed, "sprint allowed again");
            Assert.IsTrue(run.Animation.GetLayerState(CombatLayer.UpperBody).Slot.IsNone, "nothing on the upper body");
            Assert.IsTrue(run.Animation.GetLayerState(CombatLayer.FullBody).Slot.IsNone, "nothing on the whole body");
            Assert.AreEqual(0, run.OpenActions, "every action ended with its terminal cue");
            Assert.IsEmpty(run.Problems, string.Join("\n", run.Problems));
            Assert.Greater(run.Started.Count, 20, "the random play really attacked");
        }

        // ------------------------------------------------------------------ comparison

        [Test]
        public void TheComparisonLoopsEachWholeComboWithTheRealTiming()
        {
            foreach (MagicDiscipline discipline in All)
            {
                MeleeComboProfile profile = DisciplineDefaults.Melee(discipline);
                var sequence = new ComboShowcaseSequence { PauseSeconds = 1f };
                sequence.Restart(0f);
                var events = new List<MeleeEvent>();
                var starts = new List<(int step, float time)>();
                var hits = new List<(int step, float time)>();
                for (float t = 0f; t < 12f; t += Frame)
                {
                    events.Clear();
                    sequence.Tick(t, profile, events);
                    foreach (MeleeEvent e in events)
                    {
                        if (e.Kind == MeleeEventKind.StepStarted) starts.Add((e.StepIndex, t));
                        if (e.Kind == MeleeEventKind.Hit) hits.Add((e.StepIndex, t));
                    }
                }

                Assert.GreaterOrEqual(sequence.Round, 2, $"{discipline}: several rounds in 12 s");
                for (int i = 0; i < starts.Count; i++)
                {
                    Assert.AreEqual(i % 3, starts[i].step, $"{discipline}: strikes 1, 2, 3 in order, every round");
                }

                for (int i = 0; i < hits.Count; i++)
                {
                    float expected = starts[i].time + profile.steps[hits[i].step].startupSeconds;
                    Assert.AreEqual(expected, hits[i].time, Frame + 1e-4f, $"{discipline}: each hit lands after its start-up");
                }
            }
        }

        // ------------------------------------------------------------------ upgrades

        [Test]
        public void AnAegisComboGeneratedEarlierGetsTheForearmStrikeAndKeepsItsTuning()
        {
            var palm = new MeleeStepProfile { name = "Force Palm", damage = 17f, stagger = 30f };
            var slam = new MeleeStepProfile { name = "Bulwark Slam", damage = 26f, stagger = 50f };
            var melee = new MeleeComboProfile { steps = new[] { palm, slam } };

            Assert.IsTrue(DisciplineDefaults.FillMartialStyles(MagicDiscipline.Aegis, melee));
            Assert.AreEqual(3, melee.StepCount);
            Assert.AreSame(palm, melee.steps[0]);
            Assert.AreEqual("Bulwark Forearm", melee.steps[1].name);
            Assert.AreSame(slam, melee.steps[2]);
            Assert.AreEqual(17f, palm.damage, "tuned damage kept");
            Assert.AreEqual(26f, slam.damage);
            Assert.AreEqual(0f, slam.committedMoveSpeedMultiplier, "the slam takes its commitment");
            Assert.IsFalse(DisciplineDefaults.FillMartialStyles(MagicDiscipline.Aegis, melee), "once");
        }

        [Test]
        public void ArcaneStepsGeneratedEarlierAreRenamedAndRetimedOnlyIfUntuned()
        {
            var jab = new MeleeStepProfile { name = "Arcane Jab", startupSeconds = 0.07f, recoverySeconds = 0.2f };
            var cut = new MeleeStepProfile { name = "Arcane Cut", startupSeconds = 0.09f, recoverySeconds = 0.25f };
            var melee = new MeleeComboProfile { steps = new[] { jab, cut } };

            DisciplineDefaults.FillMartialStyles(MagicDiscipline.Arcane, melee);
            Assert.AreEqual("Rising Palm", jab.name);
            Assert.AreEqual(0.1f, jab.startupSeconds, 1e-5f);
            Assert.AreEqual(0.17f, jab.recoverySeconds, 1e-5f);
            Assert.AreEqual("Circling Palm", cut.name);
            Assert.AreEqual(0.09f, cut.startupSeconds, 1e-5f, "tuned timing kept");
            Assert.AreEqual(0.25f, cut.recoverySeconds, 1e-5f);
        }

        [Test]
        public void TheWorldEffectUpgradeMatchesAegisStepsByName()
        {
            var melee = new MeleeComboProfile
            {
                steps = new[] { new MeleeStepProfile { name = "Force Palm" }, new MeleeStepProfile { name = "Bulwark Slam" } },
            };
            DisciplineDefaults.FillWorldEffects(MagicDiscipline.Aegis, melee, null, null);
            Assert.AreEqual(0.6f, melee.steps[0].force, 1e-5f);
            Assert.AreEqual(1f, melee.steps[1].force, 1e-5f, "the slam's force, not the forearm's that is now second");
        }

        [Test]
        public void AnAnimationSetGeneratedEarlierGetsTheStylesUnlessChangedByHand()
        {
            CombatAnimationLibrary library = Library();
            SetClips(library.GetDiscipline(MagicDiscipline.Arcane), "Punch_Jab", ("Punch_Jab", false), ("Punch_Cross", false),
                ("Spell_Simple_Enter", false));
            SetClips(library.GetDiscipline(MagicDiscipline.Flame), "Sword_Idle", ("Sword_Attack", false), ("Sword_Attack", true),
                ("Sword_Regular_B", false));
            SetClips(library.GetDiscipline(MagicDiscipline.Aegis), "Sword_Block", ("Shield_OneShot", false), ("My_Own_Slam", false));
            CombatAnimationSlot block = library.shared.blockHold;

            var kept = new List<MagicDiscipline>();
            Assert.AreEqual(2, CombatAnimationDefaults.Upgrade(library, 1, kept));
            Assert.AreEqual("Arcane_Stance_Loop", library.GetDiscipline(MagicDiscipline.Arcane).stance.clip);
            Assert.AreEqual("Flame_Hook", library.GetDiscipline(MagicDiscipline.Flame).attacks[1].clip);
            CollectionAssert.AreEqual(new[] { MagicDiscipline.Aegis }, kept);
            Assert.AreEqual("My_Own_Slam", library.GetDiscipline(MagicDiscipline.Aegis).attacks[1].clip, "a hand-made change is kept");
            Assert.AreSame(block, library.shared.blockHold, "shared slots are untouched");
            Assert.AreEqual(0, CombatAnimationDefaults.Upgrade(library, CombatAnimationDefaults.DataVersion), "once");
        }

        private static void SetClips(DisciplineAnimationSet set, string stance, params (string clip, bool mirror)[] attacks)
        {
            set.stance = new CombatAnimationSlot { clip = stance };
            set.attacks = attacks.Select(a => new CombatAnimationSlot { clip = a.clip, mirror = a.mirror }).ToArray();
        }

        // ------------------------------------------------------------------ the rig used above

        /// <summary>
        /// The real combat rules and the real animation model, connected as <see cref="DisciplineCombat"/> and
        /// <see cref="CombatAnimationDirector"/> connect them, with the real attack-gesture recognizer as the input.
        /// </summary>
        private sealed class StyleRun : IDisciplineCombatData, IChargedAttackGate
        {
            public readonly DisciplineCombatModel Rules;
            public readonly CombatAnimationModel Animation;
            public readonly List<int> Started = new List<int>();
            public readonly List<LayerChange> Changes = new List<LayerChange>();
            public readonly List<string> Problems = new List<string>();

            private readonly AttackGestureRecognizer gesture = new AttackGestureRecognizer(new AttackGestureSettings());
            private readonly List<CombatCommand> commands = new List<CombatCommand>();
            private readonly MeleeComboProfile[] melee = new MeleeComboProfile[3];
            private readonly ChargedAttackDefaults[] charged = new ChargedAttackDefaults[3];
            private readonly int[] versions = new int[CombatAnimatorNames.LayerCount];
            private readonly HashSet<int> open = new HashSet<int>();
            private float time;
            private int slot;
            private bool block;
            private bool busy;
            private bool airborne;
            private float speed;
            private float damage;
            private bool pressed;
            private bool pressNow;
            private float pressedAt;

            public StyleRun(MagicDiscipline discipline)
            {
                for (int i = 0; i < 3; i++)
                {
                    melee[i] = DisciplineDefaults.Melee((MagicDiscipline)i);
                    charged[i] = DisciplineDefaults.Charged((MagicDiscipline)i);
                }

                slot = (int)discipline;
                Rules = new DisciplineCombatModel(this, this);
                Animation = new CombatAnimationModel(Library());
                Animation.SetDiscipline(discipline);
                Animation.Tick(0f, new CombatBodyState(true, 0f));
            }

            public int OpenActions => open.Count;

            public int Count => 3;
            public MeleeComboProfile GetMelee(int index) => melee[index];
            public ChargeProfile GetCharge(int index) => charged[index].Charge;
            public float GetGuardPulseSeconds(int index) => 0f;
            public CastResult Check(int index) => CastResult.Success;

            public void TapEvery(float interval, float seconds)
            {
                float end = time + seconds;
                float next = time;
                while (time < end)
                {
                    if (time >= next && !pressed)
                    {
                        Press();
                        next = time + interval;
                    }

                    Frame(pressed && time - pressedAt >= 0.05f);
                }
            }

            public void Idle(float seconds)
            {
                block = false;
                busy = false;
                airborne = false;
                speed = 0f;
                float end = time + seconds;
                while (time < end)
                {
                    Frame(pressed);
                }
            }

            /// <summary>One second of random play: taps, holds, blocks, switches, hits taken, jumps, climbing, moving.</summary>
            public void RandomSecond(System.Random random)
            {
                float end = time + 1f;
                while (time < end)
                {
                    double roll = random.NextDouble();
                    if (!pressed && roll < 0.12) Press();
                    bool release = pressed && time - pressedAt >= (random.NextDouble() < 0.8 ? 0.05f : 0.6f);
                    if (roll > 0.985) block = !block;
                    if (roll > 0.975 && roll <= 0.985) slot = random.Next(3);
                    if (roll > 0.96 && roll <= 0.975) damage += random.Next(2, 14);
                    if (roll > 0.95 && roll <= 0.96) airborne = !airborne;
                    if (roll > 0.945 && roll <= 0.95) busy = !busy;
                    if (roll > 0.9 && roll <= 0.945) speed = (float)random.NextDouble() * 5f;
                    Frame(release);
                }
            }

            /// <summary>The attack control goes down in the next frame.</summary>
            private void Press()
            {
                pressed = true;
                pressNow = true;
                pressedAt = time + MartialStylesTests.Frame;
            }

            private void Frame(bool release)
            {
                time += MartialStylesTests.Frame;
                gesture.BeginFrame();
                if (pressNow)
                {
                    gesture.Press(time, Vector2.zero);
                    pressNow = false;
                }
                else if (release && pressed)
                {
                    gesture.Release(time, false);
                    pressed = false;
                }

                gesture.Update(time);
                CombatInputRules.ApplyBlock(gesture, block, false);

                commands.Clear();
                var frame = new CombatFrameInput
                {
                    Gesture = gesture.Snapshot(), BlockHeld = block, HandsBusy = busy, Alive = true,
                    DisciplineSlot = slot, DamageTaken = damage,
                };
                Rules.Tick(frame, time, commands);

                Animation.SetHandsBusy(busy, time);
                Animation.SetBlocking(block && !busy, time);
                Animation.SetDiscipline((MagicDiscipline)slot);
                if (damage > 0f && !block)
                {
                    Animation.Damaged(damage, time);
                }

                damage = 0f;

                foreach (CombatCommand command in commands)
                {
                    switch (command.Kind)
                    {
                        case CombatCommandKind.MeleeStepStarted:
                            Started.Add(command.Slot * 10 + command.StepIndex);
                            Animation.SwingStarted((MagicDiscipline)command.Slot, command.StepIndex, command.Step.startupSeconds,
                                command.Step.recoverySeconds, time);
                            break;
                        case CombatCommandKind.MeleeHit:
                            Animation.SwingLanded(time);
                            break;
                        case CombatCommandKind.MeleeInterrupted:
                            Animation.SwingInterrupted(time);
                            break;
                        case CombatCommandKind.ChargeBegan:
                            Animation.ChargeBegan((MagicDiscipline)command.Slot, time);
                            break;
                        case CombatCommandKind.ChargedReleased:
                            Animation.ChargeEnded(true, time);
                            break;
                        case CombatCommandKind.ChargeCancelled:
                            Animation.ChargeEnded(false, time);
                            break;
                        case CombatCommandKind.InterruptInput:
                            gesture.Cancel();
                            pressed = false;
                            break;
                    }
                }

                Animation.Tick(time, new CombatBodyState(!airborne, speed));
                foreach (CombatCue cue in Animation.Cues)
                {
                    if (cue.Kind == CombatCueKind.Windup)
                    {
                        if (open.Count > 0) Problems.Add($"{time:0.000}: action {cue.ActionId} began while another was open");
                        open.Add(cue.ActionId);
                    }
                    else if (cue.IsTerminal)
                    {
                        if (!open.Remove(cue.ActionId)) Problems.Add($"{time:0.000}: action {cue.ActionId} ended twice or never began");
                    }
                }

                Animation.Cues.Clear();

                for (int i = 0; i < versions.Length; i++)
                {
                    CombatLayerState state = Animation.GetLayerState((CombatLayer)i);
                    if (state.Version != versions[i])
                    {
                        versions[i] = state.Version;
                        Changes.Add(new LayerChange((CombatLayer)i, state.Slot, state.BlendSeconds,
                            Animation.GetLayerWeight((CombatLayer)i), time));
                    }
                }
            }
        }

        private readonly struct LayerChange
        {
            public readonly CombatLayer Layer;
            public readonly CombatSlotId Slot;
            public readonly float Blend;
            public readonly float Weight;
            public readonly float Time;

            public LayerChange(CombatLayer layer, CombatSlotId slot, float blend, float weight, float time)
            {
                Layer = layer;
                Slot = slot;
                Blend = blend;
                Weight = weight;
                Time = time;
            }

            public override string ToString() => $"{Time:0.000} {Layer} -> {Slot} (blend {Blend}, weight {Weight:0.00})";
        }
    }
}
