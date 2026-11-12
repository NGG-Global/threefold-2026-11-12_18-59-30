using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;

namespace Threefold.Tests
{
    /// <summary>
    /// Edit Mode checks of project-level configuration. They run in CI on every pull request, and
    /// because Unity must compile every script before it can run them, a passing run also proves
    /// that the whole project compiles.
    /// </summary>
    public sealed class ProjectSanityTests
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [Test]
        public void ActiveRenderPipelineIsUniversal()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;

            Assert.IsNotNull(pipeline, "No render pipeline asset is active. The project must use URP.");
            StringAssert.Contains("UniversalRenderPipelineAsset", pipeline.GetType().Name,
                "The active render pipeline is not the Universal Render Pipeline.");
        }

        [Test]
        public void EnabledBuildScenesExist()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int enabledCount = 0;

            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (!scene.enabled)
                {
                    continue;
                }

                enabledCount++;
                Assert.IsTrue(File.Exists(scene.path),
                    $"Build Settings lists the scene '{scene.path}', but the file does not exist.");
            }

            Assert.Greater(enabledCount, 0, "Build Settings has no enabled scene, so a build would be empty.");
        }

        [Test]
        public void InputActionsAssetIsPresent()
        {
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(InputActionsPath)),
                $"'{InputActionsPath}' is missing. Player input depends on it.");
        }
    }
}
