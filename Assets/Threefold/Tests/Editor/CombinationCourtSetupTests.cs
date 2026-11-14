using System.Text;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.EditorTools;
using Threefold.Enemies;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// What Sprint LM-06 added to the setup: the combination court in the Discipline Range, the Cinder
    /// Wisp, and the look of enemy defences. These tests need the Unity Editor; those that generate
    /// assets or replace the open scene run only in batch mode. The rules themselves are covered by
    /// <see cref="CombinationTests"/>, which runs without Unity.
    /// </summary>
    public sealed class CombinationCourtSetupTests
    {
        private static void RequireBatchMode(string reason)
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it " + reason + ".");
            }
        }

        [Test]
        public void TheRangeHasTheCombinationCourtAndItValidates()
        {
            RequireBatchMode("replaces the open scene without saving it");

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            GameObject court = MagicalObjectSetup.FindCourt(scene);
            Assert.IsNotNull(court);

            for (int i = 0; i < CombinationDefaults.Count; i++)
            {
                var example = (CombinationExample)i;
                Transform instance = court.transform.Find(CombinationDefaults.Name(example));
                Assert.IsNotNull(instance, example.ToString());
                Assert.IsNotNull(instance.GetComponent<MagicalObject>().Definition, example.ToString());
            }

            MagicalObject walkway = court.transform.Find(CombinationDefaults.Name(CombinationExample.CrumblingWalkway))
                .GetComponent<MagicalObject>();
            Assert.IsNotNull(walkway.LoadZone, "The walkway counts who stands on it.");
            Assert.AreSame(walkway, walkway.LoadZone.Target);

            MagicalObject slide = court.transform.Find(CombinationDefaults.Name(CombinationExample.RuneLockedSlide))
                .GetComponent<MagicalObject>();
            Assert.IsNotEmpty(slide.PersistentId, "Where the slide was left is saved.");
            PuzzleDoor gate = court.transform.Find(MagicalObjectSetup.CourtGateName).GetComponent<PuzzleDoor>();
            Assert.AreSame(slide.GetComponent<PropertyStateTrigger>(), gate.InputElement);

            Transform wisp = court.transform.Find(MagicalObjectSetup.CourtWispName);
            Assert.IsNotNull(wisp);
            Assert.AreEqual("Cinder Wisp", wisp.GetComponent<EnemyController>().Definition.DisplayName);
            Assert.IsNotNull(Object.FindAnyObjectByType<EnemyDirector>(), "The Wisp is paced and restored by a director.");

            var problems = new StringBuilder();
            Assert.AreEqual(0, MagicalObjectValidator.Validate(scene, problems), problems.ToString());
        }

        [Test]
        public void RunningTheRangeSetupAgainAddsNothingToTheCourt()
        {
            RequireBatchMode("replaces the open scene without saving it");

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            int before = MagicalObjectSetup.FindCourt(scene).GetComponentsInChildren<Transform>(true).Length;
            scene = DisciplineRangeSetup.BuildForAutomation();
            int after = MagicalObjectSetup.FindCourt(scene).GetComponentsInChildren<Transform>(true).Length;
            Assert.AreEqual(before, after);
        }

        [Test]
        public void TheEnemyPrefabsShowTheirDefencesAndTheMageTakesHazards()
        {
            RequireBatchMode("generates assets");

            EnemyAssets assets = EnemyAssets.Ensure(new StringBuilder());
            CreatureStateVisuals guardian = assets.GuardianPrefab.GetComponent<CreatureStateVisuals>();
            Assert.IsNotNull(guardian);
            Assert.Greater(guardian.ArmourPartCount, 0, "The Guardian's plates are shown.");

            CreatureStateVisuals wisp = assets.WispPrefab.GetComponent<CreatureStateVisuals>();
            Assert.IsNotNull(wisp);
            Assert.IsNotNull(wisp.WardShell, "The Wisp's ward is shown.");

            Assert.AreEqual(EnemyArchetypes.DataVersion, assets.GuardianDefinition.DataVersion);
            Assert.IsTrue(assets.GuardianDefinition.Tuning.armour.enabled);
            Assert.IsTrue(assets.WispDefinition.Tuning.ward.enabled);
            Assert.IsNotNull(assets.CinderPrefab);

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            Health player = null;
            foreach (Health health in Object.FindObjectsByType<Health>())
            {
                if (health.Team == CombatTeam.Player && health.gameObject.scene == scene)
                {
                    player = health;
                }
            }

            Assert.IsNotNull(player);
            Assert.IsNotNull(player.GetComponent<PlayerEffectReceiver>());
        }
    }
}
