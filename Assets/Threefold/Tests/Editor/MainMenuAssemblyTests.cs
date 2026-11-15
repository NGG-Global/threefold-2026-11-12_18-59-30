using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.EditorTools;
using Threefold.MainMenu;
using Threefold.VisualStyle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// Builds the main menu scene from code and checks the result: it validates, the camera and the
    /// character stand where <see cref="MainMenuStage"/> says, the character has his bearing, and running
    /// the setup twice changes nothing. Runs only in batch mode, because it replaces the open scene
    /// without saving it.
    /// </summary>
    public sealed class MainMenuAssemblyTests
    {
        [Test]
        public void TheMainMenuIsCompleteAndFramedAsDesigned()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = MainMenuSetup.BuildForAutomation();

            var problems = new StringBuilder();
            Assert.AreEqual(0, MainMenuSetup.Validate(scene, problems), problems.ToString());

            GameObject cameraObject = GameObject.Find(MainMenuSetup.CameraName);
            Assert.IsNotNull(cameraObject);
            Camera viewCamera = cameraObject.GetComponent<Camera>();
            Assert.AreEqual(MainMenuStage.FieldOfView, viewCamera.fieldOfView, 0.01f);
            Assert.Less(Vector3.Distance(MainMenuStage.CameraPosition, cameraObject.transform.position), 0.001f);

            GameObject character = GameObject.Find(MainMenuSetup.CharacterName);
            Assert.IsNotNull(character);
            Assert.Less(Vector3.Distance(MainMenuStage.CharacterPosition, character.transform.position), 0.001f);
            Assert.IsNotNull(character.GetComponent<CharacterPersonality>(), "He breathes and blinks.");

            // The plain projection the layout relies on agrees with the engine's camera.
            const float aspect = 16f / 9f;
            viewCamera.aspect = aspect;
            Vector3 head = MainMenuStage.CharacterPosition + new Vector3(0f, MainMenuStage.CharacterHeight, 0f);
            Vector3 engine = viewCamera.WorldToViewportPoint(head);
            Vector2 plain = MainMenuStage.HeadOnScreen(aspect);
            Assert.AreEqual(engine.x, plain.x, 0.002f);
            Assert.AreEqual(engine.y, plain.y, 0.002f);
            viewCamera.ResetAspect();

            GraphicsMenu graphics = Object.FindAnyObjectByType<GraphicsMenu>();
            Assert.IsNotNull(graphics, "The Settings entry has the graphics panel to open.");
        }

        [Test]
        public void AssemblingTheMainMenuTwiceGivesTheSameScene()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            MainMenuSetup.BuildForAutomation();
            int firstCount = CountObjects(SceneManager.GetActiveScene());

            Scene scene = MainMenuSetup.BuildForAutomation();
            Assert.AreEqual(firstCount, CountObjects(scene), "Running the setup a second time changed the number of objects.");

            var problems = new StringBuilder();
            Assert.AreEqual(0, MainMenuSetup.Validate(scene, problems), problems.ToString());
            Assert.AreEqual(1, Object.FindObjectsByType<MainMenuScreen>(FindObjectsInactive.Include).Length);
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
