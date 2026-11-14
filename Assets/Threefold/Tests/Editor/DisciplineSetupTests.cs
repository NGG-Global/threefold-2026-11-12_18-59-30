using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.EditorTools;
using Threefold.Magic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// The setup command generates the three disciplines and connects them to the Mage. These tests
    /// need the Unity Editor and replace the open scene, so like the other scene assembly tests they
    /// run only in batch mode.
    /// </summary>
    public sealed class DisciplineSetupTests
    {
        [Test]
        public void TheDisciplineRangeAssemblesWithAllThreeDisciplines()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = DisciplineRangeSetup.BuildForAutomation();

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());

            GameObject mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene).gameObject;
            PlayerDisciplines disciplines = mage.GetComponent<PlayerDisciplines>();
            Assert.IsNotNull(mage.GetComponent<DisciplineCombat>());
            Assert.AreEqual(3, disciplines.Count);

            for (int slot = 0; slot < 3; slot++)
            {
                DisciplineDefinition definition = disciplines.GetDefinition(slot);
                Assert.IsNotNull(definition, $"Slot {slot}");
                Assert.AreEqual((MagicDiscipline)slot, definition.Discipline);
                Assert.Greater(definition.Melee.StepCount, 0);
                Assert.IsNotNull(definition.ChargedAbility);
                Assert.Greater(definition.ChargedAbility.ManaCost, 0f);
                Assert.IsNotNull(definition.Presentation.sweepEffect);
                Assert.IsNotNull(definition.Presentation.meleeImpactEffect);
                Assert.IsNotNull(definition.Presentation.meleeSwingEffect);
                Assert.IsNotNull(definition.Presentation.chargeLoopEffect);
            }

            Assert.IsInstanceOf<ChargedProjectileDefinition>(disciplines.GetDefinition(0).ChargedAbility);
            Assert.IsInstanceOf<ChargedAreaDefinition>(disciplines.GetDefinition(1).ChargedAbility);
            Assert.IsInstanceOf<ChargedAreaDefinition>(disciplines.GetDefinition(2).ChargedAbility);
            Assert.IsNotNull(((ChargedProjectileDefinition)disciplines.GetDefinition(0).ChargedAbility).ProjectilePrefab);

            Assert.IsNotNull(SceneSetupUtility.FindInScene<ChargeAimPreview>(scene));
            Assert.IsNotNull(SceneSetupUtility.FindInScene<DisciplineFeedback>(scene));
            Assert.IsNotNull(SceneSetupUtility.FindInScene<GuardVisual>(scene));
            Assert.IsNotNull(mage.GetComponent<PlayerGuard>().Health, "The guard protects the Mage's health.");
        }

        [Test]
        public void RunningTheDisciplineSetupAgainAddsNothing()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            GameObject mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene).gameObject;
            int before = CountObjects(scene);

            DisciplineSetup.Ensure(scene, mage, new StringBuilder());

            Assert.AreEqual(before, CountObjects(scene));
            Assert.AreEqual(1, mage.GetComponents<DisciplineCombat>().Length);
        }

        [Test]
        public void GeneratedDisciplinesMatchTheDefaults()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it generates assets.");
            }

            DisciplineDefinition[] definitions = DisciplineSetup.EnsureAssets(new StringBuilder(), out _, out _);
            Assert.AreEqual(3, definitions.Length);

            // Only meaningful for freshly generated assets, which is what a build server has.
            ChargedAttackDefaults wave = DisciplineDefaults.Charged(MagicDiscipline.Flame);
            var flameWave = (ChargedAreaDefinition)definitions[1].ChargedAbility;
            Assert.AreEqual(wave.ManaCost, flameWave.ManaCost, 0.001f);
            Assert.AreEqual(wave.Area.maxTargets, flameWave.Area.maxTargets);
            Assert.AreEqual(SpellElement.Fire, definitions[1].Element);
            Assert.AreEqual(SpellElement.Force, definitions[2].Element);
        }

        private static int CountObjects(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<Transform>(true).Length;
            }

            return count;
        }
    }
}
