using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.CombatFeedback;
using Threefold.EditorTools;
using Threefold.Magic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    public sealed class MagicVfxSetupTests
    {
        [Test]
        public void MagicVfxAssetsWireSwingAndChargeEffects()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it generates assets.");
            }

            DisciplineDefinition[] definitions = DisciplineSetup.EnsureAssets(new StringBuilder(), out _, out _);
            MagicVfxSetup.EnsureAssets(new StringBuilder());

            Assert.AreEqual(3, definitions.Length);
            for (int i = 0; i < definitions.Length; i++)
            {
                DisciplinePresentation look = definitions[i].Presentation;
                Assert.IsNotNull(look.meleeSwingEffect, definitions[i].DisplayName);
                Assert.IsNotNull(look.meleeImpactEffect, definitions[i].DisplayName);
                Assert.IsNotNull(look.chargeLoopEffect, definitions[i].DisplayName);
                Assert.IsNotNull(look.releaseEffect, definitions[i].DisplayName);
                Assert.GreaterOrEqual(look.effectHeight, 1f);
            }
        }

        [Test]
        public void MagicVfxAssetsFillTheCueTimedEffectsOfEachDiscipline()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it generates assets.");
            }

            DisciplineDefinition[] definitions = DisciplineSetup.EnsureAssets(new StringBuilder(), out _, out _);
            MagicVfxSetup.EnsureAssets(new StringBuilder());

            foreach (DisciplineDefinition definition in definitions)
            {
                DisciplinePresentation look = definition.Presentation;
                Assert.IsTrue(look.bodyTiming.configured, definition.DisplayName);
                Assert.IsNotNull(look.limbTrailEffect, definition.DisplayName);
                Assert.IsNotNull(look.contactEffect, definition.DisplayName);
                Assert.IsNotNull(look.chargeLoopEffect.GetComponent<PooledEffect>(), definition.DisplayName);
            }

            DisciplinePresentation arcane = definitions[(int)MagicDiscipline.Arcane].Presentation;
            DisciplinePresentation flame = definitions[(int)MagicDiscipline.Flame].Presentation;
            DisciplinePresentation aegis = definitions[(int)MagicDiscipline.Aegis].Presentation;
            Assert.IsNotNull(arcane.projectileTrailEffect);
            Assert.IsNotNull(arcane.meleeSwingEffect.GetComponent<ArcEmitterEffect>(), "the Arcane sweep is a ribbon");
            Assert.IsNotNull(flame.lingerEffect);
            Assert.IsNotNull(flame.areaWaveEffect.GetComponent<ArcEmitterEffect>());
            Assert.IsNotNull(aegis.gatherEffect);
            Assert.IsNotNull(aegis.heavyContactEffect);
            Assert.IsNotNull(aegis.areaWaveEffect.GetComponent<ArcEmitterEffect>());
            Assert.IsNotNull(aegis.blockBarrierEffect);
        }

        [Test]
        public void EveryMagicEffectIsWithinTheAndroidBudget()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it generates assets.");
            }

            DisciplineSetup.EnsureAssets(new StringBuilder(), out _, out _);
            MagicVfxSetup.EnsureAssets(new StringBuilder());

            var problems = new System.Collections.Generic.List<string>();
            int count = MagicVfxValidation.Validate(problems, out _);
            Assert.Greater(count, 20);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void DisciplineRangeGetsBodyAndWorldEffects()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            DisciplineBodyEffects body = SceneSetupUtility.FindInScene<DisciplineBodyEffects>(scene);
            Assert.IsNotNull(body);
            Assert.IsNotNull(body.Combat);
            Assert.IsNotNull(SceneSetupUtility.FindInScene<WorldMagicFeedback>(scene));
        }

        [Test]
        public void DisciplineRangeGetsVfxSockets()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            GameObject mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene).gameObject;
            Assert.IsNotNull(mage.GetComponent<DisciplineVfxSockets>());

            DisciplineFeedback feedback = SceneSetupUtility.FindInScene<DisciplineFeedback>(scene);
            Assert.IsNotNull(feedback);
            Assert.IsNotNull(feedback.Combat.Disciplines.GetDefinition(0).Presentation.meleeSwingEffect);
        }
    }
}
