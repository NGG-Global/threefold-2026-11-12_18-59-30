using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Threefold.Enemies;
using Threefold.EditorTools;
using Threefold.Objectives;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// Builds the combat arena from code and checks the result: it validates, every enemy is in
    /// place and on the navigation mesh, every enemy can be reached from the start, and running
    /// the setup twice changes nothing. Runs only in batch mode, because it replaces the open
    /// scene without saving it.
    /// </summary>
    public sealed class CombatArenaAssemblyTests
    {
        private const int ExpectedEnemies = 7;

        [Test]
        public void ArenaIsCompleteConnectedAndNavigable()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = CombatArenaSetup.BuildForAutomation();

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());

            List<EnemyController> enemies = Find<EnemyController>(scene);
            Assert.AreEqual(ExpectedEnemies, enemies.Count);
            Assert.AreEqual(3, Find<EnemyEncounter>(scene).Count, "Three encounters, each a different combination.");

            DefeatTargetsObjective objective = Find<DefeatTargetsObjective>(scene)[0];
            Assert.AreEqual(ExpectedEnemies, objective.TotalCount, "Every enemy counts towards clearing the arena.");

            NavMeshSurface surface = Find<NavMeshSurface>(scene)[0];
            Assert.IsNotNull(surface.navMeshData, "The navigation mesh was baked.");

            Assert.IsTrue(NavMesh.SamplePosition(CombatArenaSetup.SpawnPosition, out NavMeshHit start, 1f, NavMesh.AllAreas));
            var path = new NavMeshPath();

            foreach (EnemyController enemy in enemies)
            {
                Assert.IsTrue(NavMesh.SamplePosition(enemy.transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas),
                    $"'{enemy.name}' stands on the navigation mesh.");
                Assert.IsTrue(NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, path),
                    $"A path to '{enemy.name}' can be calculated.");
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, $"'{enemy.name}' can be reached from the start.");
            }
        }

        [Test]
        public void AssemblingTheArenaTwiceGivesTheSameScene()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            CombatArenaSetup.BuildForAutomation();
            int firstCount = CountObjects(SceneManager.GetActiveScene());

            Scene scene = CombatArenaSetup.BuildForAutomation();
            Assert.AreEqual(firstCount, CountObjects(scene), "Running the setup a second time changed the number of objects.");

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());
            Assert.AreEqual(ExpectedEnemies, Find<EnemyController>(scene).Count);
        }

        private static List<T> Find<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return found;
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
