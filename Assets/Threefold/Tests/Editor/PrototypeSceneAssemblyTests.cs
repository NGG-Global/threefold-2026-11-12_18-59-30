using System.Text;
using NUnit.Framework;
using Threefold.EditorTools;
using Threefold.Objectives;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// Builds the whole playable scene from code and checks the result. This is the end-to-end
    /// test of the setup automation: asset generation, level layout, the gameplay rig, the
    /// objective and all the references between them.
    ///
    /// Assembling the scene replaces whatever scene is open without saving it, so the test only
    /// runs in batch mode (on a build server or from the command line), never in an open Editor.
    /// </summary>
    public sealed class PrototypeSceneAssemblyTests
    {
        [Test]
        public void AssembledSceneIsCompleteAndConnected()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = FantasyPlaygroundSetup.BuildForAutomation();

            var problems = new StringBuilder();
            int count = PrototypeValidator.Validate(scene, problems);
            Assert.AreEqual(0, count, problems.ToString());

            DefeatTargetsObjective objective = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                objective = root.GetComponentInChildren<DefeatTargetsObjective>(true);
                if (objective != null)
                {
                    break;
                }
            }

            Assert.IsNotNull(objective);
            Assert.AreEqual(3, objective.TotalCount, "The objective must have exactly three constructs.");
        }

        [Test]
        public void AssemblingTwiceGivesTheSameSceneWithoutDuplicates()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            FantasyPlaygroundSetup.BuildForAutomation();
            int firstCount = CountObjects(SceneManager.GetActiveScene());

            Scene scene = FantasyPlaygroundSetup.BuildForAutomation();
            int secondCount = CountObjects(scene);

            Assert.AreEqual(firstCount, secondCount, "Running the setup a second time changed the number of objects.");

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());
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
