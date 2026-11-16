using System.Linq;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.CombatAnimation;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The first combat polish pass, on the Flame hook (Docs/CombatPolish.md): the follow-through plays nearer
    /// its own speed without the rules waiting for it, the later strikes of a combo show a second impact layer,
    /// and a struck body gives. These check timing and rules. They cannot say that the strike now looks fluid;
    /// that is judged from Unity's frames and with the game in the hand. Plain C#: these run outside Unity too.
    /// </summary>
    public sealed class CombatPolishTests
    {
        private const float Frame = 1f / 60f;
        private const int Hook = 1;

        private static CombatAnimationSlot HookSlot(CombatAnimationLibrary library)
        {
            return library.GetDiscipline(MagicDiscipline.Flame).attacks[Hook];
        }

        private static MeleeStepProfile HookRules()
        {
            return DisciplineDefaults.Melee(MagicDiscipline.Flame).steps[Hook];
        }

        [Test]
        public void TheRulesOfTheHookAreWhatTheyWere()
        {
            MeleeStepProfile rules = HookRules();
            Assert.AreEqual(0.12f, rules.startupSeconds, 1e-6f, "The hit lands when it did.");
            Assert.AreEqual(0.26f, rules.recoverySeconds, 1e-6f, "The Mage is free again when he was.");
        }

        [Test]
        public void TheHooksFollowThroughPlaysNearItsOwnSpeedAndTheWindUpIsUnchanged()
        {
            CombatAnimationSlot slot = HookSlot(CombatAnimationDefaults.CreateMage());
            MeleeStepProfile rules = HookRules();
            CombatTimeFit after = CombatTimeWarp.Fit(slot, rules.startupSeconds, rules.recoverySeconds, 0.5f, 3f);

            float asked = slot.followThroughSpeed;
            slot.followThroughSpeed = 0f;
            CombatTimeFit before = CombatTimeWarp.Fit(slot, rules.startupSeconds, rules.recoverySeconds, 0.5f, 3f);
            slot.followThroughSpeed = asked;

            Assert.Greater(before.RecoverySpeed, 2.8f, "Before: the follow-through was squeezed to nearly three times its speed.");
            Assert.AreEqual(CombatAnimationDefaults.HookFollowThroughSpeed, after.RecoverySpeed, 1e-4f);
            Assert.Less(after.RecoverySpeed, after.WindupSpeed, "The arm now slows after the blow instead of speeding up.");

            Assert.AreEqual(before.WindupSpeed, after.WindupSpeed, 1e-5f, "The wind-up is fitted as before.");
            Assert.AreEqual(before.Entry, after.Entry, 1e-6f);
            Assert.AreEqual(0f, after.ContactError, 1e-5f, "The contact frame is still shown exactly when the rules land the hit.");

            Assert.Greater(after.RecoverySeconds, rules.recoverySeconds, "The follow-through outlasts the rules' recovery, as a tail.");
            Assert.Less(after.RecoverySeconds, 0.6f, "And the tail is short.");
            Assert.AreEqual(after.RecoverySeconds - rules.recoverySeconds, after.RecoveryError, 1e-5f);
        }

        [Test]
        public void OnlyTheHookAsksForItAndEveryStrikeStillBlendsIn()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            int asking = 0;
            library.ForEachSlot((id, slot) =>
            {
                if (slot != null && slot.followThroughSpeed > 0f)
                {
                    asking++;
                    Assert.AreEqual("Flame_Hook", slot.clip);
                }
            });

            Assert.AreEqual(1, asking, "One reference strike; the others are as they were until this one has been judged.");
            Assert.GreaterOrEqual(HookSlot(library).blendInSeconds, 0.05f);
            Assert.Less(HookSlot(library).blendInSeconds, 0.06f, "A little more of the wind-up is the clip's own.");
        }

        [Test]
        public void ASlotThatDoesNotAskIsFittedExactlyAsBefore()
        {
            var slot = new CombatAnimationSlot { clip = "Any", clipSeconds = 1.2f, contact = 0.25f, recoveryEnd = 0.9f };
            CombatTimeFit plain = CombatTimeWarp.Fit(slot, 0.15f, 0.3f, 0.5f, 3f);
            Assert.AreEqual(Mathf.Clamp(0.65f * 1.2f / 0.3f, 0.5f, 3f), plain.RecoverySpeed, 1e-4f);

            // Asking for more than the limit, or for less than the slowest, stays inside the tuning's limits.
            slot.followThroughSpeed = 6f;
            Assert.AreEqual(plain.RecoverySpeed, CombatTimeWarp.Fit(slot, 0.15f, 0.3f, 0.5f, 3f).RecoverySpeed, 1e-4f);
            slot.followThroughSpeed = 0.1f;
            Assert.AreEqual(0.5f, CombatTimeWarp.Fit(slot, 0.15f, 0.3f, 0.5f, 3f).RecoverySpeed, 1e-4f);
        }

        [Test]
        public void TheNextStrikeCutsIntoTheTailAndTheHookStillEndsWithOneCue()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            MeleeComboProfile combo = DisciplineDefaults.Melee(MagicDiscipline.Flame);
            MeleeStepProfile hook = combo.steps[Hook];
            MeleeStepProfile flare = combo.steps[Hook + 1];
            var body = new CombatBodyState(true, 0f);

            var model = new CombatAnimationModel(library);
            model.SetDiscipline(MagicDiscipline.Flame);
            model.Tick(0f, body);
            model.SwingStarted(MagicDiscipline.Flame, Hook, hook.startupSeconds, hook.recoverySeconds, 0f);
            int hookAction = model.CurrentActionId;
            model.Tick(hook.startupSeconds, body);
            model.SwingLanded(hook.startupSeconds);

            // The rules free the Mage 0.26 s after the hit. The model is not asked: the rules lead.
            float free = hook.startupSeconds + hook.recoverySeconds;
            int ended = 0;
            for (float t = hook.startupSeconds + Frame; t < free; t += Frame)
            {
                model.Tick(t, body);
                ended += model.Cues.Count(c => c.ActionId == hookAction && c.IsTerminal);
                model.Cues.Clear();   // as the director does once it has passed the cues on
            }

            Assert.AreEqual(0, ended, "The follow-through has not ended when the rules' recovery has.");
            Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Attack, Hook), model.GetLayerState(CombatLayer.FullBody).Slot,
                "It is still playing, as a tail.");

            // The next strike of the combo starts when the rules allow it, takes the layer from the tail, and the
            // hook's action ends then, with its one Recovered cue.
            model.SwingStarted(MagicDiscipline.Flame, Hook + 1, flare.startupSeconds, flare.recoverySeconds, free);
            ended += model.Cues.Count(c => c.ActionId == hookAction && c.IsTerminal);
            model.Cues.Clear();
            for (float t = free + Frame; t < free + 1.5f; t += Frame)
            {
                model.Tick(t, body);
                ended += model.Cues.Count(c => c.ActionId == hookAction && c.IsTerminal);
                model.Cues.Clear();
                if (t < free + flare.startupSeconds)
                {
                    Assert.AreEqual(CombatSlotId.For(MagicDiscipline.Flame, CombatSlotKind.Attack, Hook + 1),
                        model.GetLayerState(CombatLayer.FullBody).Slot, "The next strike has the body.");
                }
            }

            Assert.AreEqual(1, ended, "The hook ended with exactly one Recovered or Cancelled cue.");
        }

        [Test]
        public void LeftAloneTheFollowThroughEndsWithItsRecoveredCue()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            MeleeStepProfile hook = HookRules();
            CombatAnimationSlot slot = HookSlot(library);
            CombatTimeFit fit = CombatTimeWarp.Fit(slot, hook.startupSeconds, hook.recoverySeconds, 0.5f, 3f);
            var body = new CombatBodyState(true, 0f);

            var model = new CombatAnimationModel(library);
            model.SetDiscipline(MagicDiscipline.Flame);
            model.Tick(0f, body);
            model.SwingStarted(MagicDiscipline.Flame, Hook, hook.startupSeconds, hook.recoverySeconds, 0f);
            int action = model.CurrentActionId;
            model.Tick(hook.startupSeconds, body);
            model.SwingLanded(hook.startupSeconds);

            float recoveredAt = -1f;
            for (float t = hook.startupSeconds + Frame; t < 2f; t += Frame)
            {
                model.Tick(t, body);
                foreach (CombatCue cue in model.Cues)
                {
                    if (cue.ActionId == action && cue.Kind == CombatCueKind.Recovered)
                    {
                        recoveredAt = cue.Time;
                    }
                }
            }

            Assert.AreEqual(hook.startupSeconds + fit.RecoverySeconds, recoveredAt, 2f * Frame,
                "The Recovered cue marks the end of the motion, so effects that follow the arm follow it through.");
        }

        [Test]
        public void LeftAloneTheTailEndsAndTheBodyReturnsToTheStance()
        {
            CombatAnimationLibrary library = CombatAnimationDefaults.CreateMage();
            MeleeStepProfile hook = HookRules();
            var body = new CombatBodyState(true, 0f);

            var model = new CombatAnimationModel(library);
            model.SetDiscipline(MagicDiscipline.Flame);
            model.Tick(0f, body);
            model.SwingStarted(MagicDiscipline.Flame, Hook, hook.startupSeconds, hook.recoverySeconds, 0f);
            model.Tick(hook.startupSeconds, body);
            model.SwingLanded(hook.startupSeconds);

            for (float t = hook.startupSeconds; t < 2f; t += Frame)
            {
                model.Tick(t, body);
            }

            Assert.IsTrue(model.GetLayerState(CombatLayer.FullBody).Slot.IsNone, "No strike is left playing.");
            Assert.IsTrue(model.GetLayerState(CombatLayer.UpperBody).Slot.IsNone);
        }

        [Test]
        public void ASetGeneratedBeforeTakesTheHooksNewValuesAndATunedOneIsKept()
        {
            CombatAnimationLibrary generated = CombatAnimationDefaults.CreateMage();
            CombatAnimationSlot hook = HookSlot(generated);
            hook.followThroughSpeed = 0f;
            hook.blendInSeconds = 0.06f;

            Assert.AreEqual(1, CombatAnimationDefaults.Upgrade(generated, 2));
            Assert.AreEqual(CombatAnimationDefaults.HookFollowThroughSpeed, hook.followThroughSpeed);
            Assert.AreEqual(CombatAnimationDefaults.HookBlendIn, hook.blendInSeconds);

            CombatAnimationLibrary tuned = CombatAnimationDefaults.CreateMage();
            CombatAnimationSlot mine = HookSlot(tuned);
            mine.followThroughSpeed = 0f;
            mine.blendInSeconds = 0.09f;
            Assert.AreEqual(0, CombatAnimationDefaults.Upgrade(tuned, 2), "A blend changed by hand marks the slot as tuned.");
            Assert.AreEqual(0.09f, mine.blendInSeconds);
            Assert.AreEqual(0f, mine.followThroughSpeed);

            Assert.AreEqual(0, CombatAnimationDefaults.Upgrade(CombatAnimationDefaults.CreateMage(), CombatAnimationDefaults.DataVersion));
        }

        // ------------------------------------------------------------ impact

        [TestCase(0, 1, false)]
        [TestCase(1, 1, true)]
        [TestCase(2, 1, true)]
        [TestCase(0, 0, true)]
        [TestCase(2, -1, false)]
        public void TheHeavyLayerComesWithTheLaterStrikesOfACombo(int step, int from, bool expected)
        {
            Assert.AreEqual(expected, ImpactLayers.ShowsHeavyLayer(step, from));
        }

        [Test]
        public void OneHeavyLayerAtMostForEachSwing()
        {
            Assert.AreEqual(1, ImpactLayers.MaxHeavyPerSwing, "Several at once would hide the enemies.");
        }

        [Test]
        public void AStruckBodyGivesAtOnceSwingsBackOnceAndComesToRest()
        {
            Assert.AreEqual(0f, ImpactLayers.Squash(0f));
            Assert.AreEqual(0f, ImpactLayers.Squash(1f));
            Assert.AreEqual(0f, ImpactLayers.Squash(-1f));
            Assert.AreEqual(0f, ImpactLayers.Squash(2f));
            Assert.AreEqual(0f, ImpactLayers.Squash(float.NaN));

            float most = 0f, least = 0f, mostAt = 0f;
            int crossings = 0;
            float previous = 0f;
            for (int i = 1; i < 200; i++)
            {
                float progress = i / 200f;
                float value = ImpactLayers.Squash(progress);
                Assert.LessOrEqual(Mathf.Abs(value), 1f);
                if (value > most)
                {
                    most = value;
                    mostAt = progress;
                }

                least = Mathf.Min(least, value);
                if (previous > 0f && value <= 0f || previous < 0f && value >= 0f)
                {
                    crossings++;
                }

                previous = value;
            }

            Assert.Greater(most, 0.5f, "It gives clearly.");
            Assert.Less(mostAt, 0.2f, "And at once: the largest squash is early.");
            Assert.Less(least, -0.05f, "It swings back past its own shape.");
            Assert.Greater(most, -least * 2f, "Less the other way than it gave.");
            Assert.LessOrEqual(crossings, 3, "A swing or two, not a wobble.");
            Assert.Less(Mathf.Abs(ImpactLayers.Squash(0.97f)), 0.02f, "Still again at the end.");
        }
    }
}
