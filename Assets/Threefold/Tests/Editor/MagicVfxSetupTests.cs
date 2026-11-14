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
