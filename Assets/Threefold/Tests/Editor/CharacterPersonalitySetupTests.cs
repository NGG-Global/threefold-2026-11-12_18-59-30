using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.EditorTools;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The setup of the Mage's <see cref="CharacterPersonality"/>, and that the component leaves a model's bones as it
    /// found them. These create GameObjects, so they run only inside Unity (EditMode).
    /// </summary>
    public sealed class CharacterPersonalitySetupTests
    {
        private GameObject mage;
        private GameObject visual;

        [SetUp]
        public void SetUp()
        {
            mage = new GameObject("Mage");
            mage.AddComponent<CharacterController>();
            mage.AddComponent<ThirdPersonMotor>();
            mage.AddComponent<Health>();
            visual = new GameObject("MageVisual");
            visual.transform.SetParent(mage.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (mage != null)
            {
                Object.DestroyImmediate(mage);
            }
        }

        [Test]
        public void WithoutAnAnimatorNothingIsAddedAndTheReportSaysSo()
        {
            var report = new StringBuilder();
            MagePersonalitySetup.Ensure(mage, visual, report);
            Assert.IsNull(mage.GetComponent<CharacterPersonality>());
            StringAssert.Contains("WARNING", report.ToString());
        }

        [Test]
        public void RunningTheSetupTwiceAddsOneComponentAndKeepsWhatWasTuned()
        {
            var model = new GameObject("Model");
            model.transform.SetParent(visual.transform, false);
            Animator animator = model.AddComponent<Animator>();

            var report = new StringBuilder();
            MagePersonalitySetup.Ensure(mage, visual, report);
            CharacterPersonality personality = mage.GetComponent<CharacterPersonality>();
            Assert.IsNotNull(personality);
            Assert.AreSame(animator, personality.Animator);
            StringAssert.Contains("no secondary bones", report.ToString());

            personality.Tuning.flame.forwardLeanDegrees = 7.5f;
            var second = new StringBuilder();
            MagePersonalitySetup.Ensure(mage, visual, second);
            Assert.AreEqual(1, mage.GetComponents<CharacterPersonality>().Length);
            Assert.AreEqual(7.5f, personality.Tuning.flame.forwardLeanDegrees, "Tuned values are kept.");
            StringAssert.DoesNotContain("Added", second.ToString());
        }

        [Test]
        public void TheSetupCountsTheModelsSecondaryBones()
        {
            var model = new GameObject("Model");
            model.transform.SetParent(visual.transform, false);
            model.AddComponent<Animator>();
            var first = new GameObject("sec_cloak_l_01");
            first.transform.SetParent(model.transform, false);
            var second = new GameObject("sec_cloak_l_02");
            second.transform.SetParent(first.transform, false);

            var report = new StringBuilder();
            MagePersonalitySetup.Ensure(mage, visual, report);
            StringAssert.DoesNotContain("no secondary bones", report.ToString());
            StringAssert.DoesNotContain("WARNING", report.ToString());
        }
    }
}
