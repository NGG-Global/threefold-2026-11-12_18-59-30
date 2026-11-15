using System.IO;
using System.Text;
using NUnit.Framework;
using Threefold.Cameras;
using Threefold.DevTools;
using Threefold.EditorTools;
using Threefold.VisualStyle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using L = Threefold.EditorTools.ReferenceCourtyardLayout;

namespace Threefold.Tests
{
    /// <summary>
    /// The Reference Courtyard as Unity assembles it, and the scenes of the development build. These prove what
    /// the scene contains and what it costs in triangles, materials and lights. They do not prove how it looks.
    /// They build scenes, so they run only inside Unity, in batch mode.
    /// </summary>
    public sealed class ReferenceCourtyardAssemblyTests
    {
        private static void BatchModeOnly()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }
        }

        private static GameObject EnvironmentRoot(Scene scene)
        {
            GameObject root = FantasyPlaygroundSetup.FindEnvironmentRoot(scene, ReferenceCourtyardSetup.GeneratorName);
            Assert.IsNotNull(root, "The scene holds the generated courtyard.");
            return root;
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

        [Test]
        public void TheCourtyardAssemblesValidatesAndAssemblesAgainTheSame()
        {
            BatchModeOnly();

            Scene scene = ReferenceCourtyardSetup.BuildForAutomation();
            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());
            Assert.IsTrue(VisualStyleSetup.IsApplied(scene), "The courtyard carries the visual style.");

            int before = CountObjects(scene);
            scene = ReferenceCourtyardSetup.BuildForAutomation();
            Assert.AreEqual(before, CountObjects(scene), "Building the courtyard a second time changed the number of objects.");
        }

        [Test]
        public void TheCourtyardHoldsWhatTheBriefAsksFor()
        {
            BatchModeOnly();

            Scene scene = ReferenceCourtyardSetup.BuildForAutomation();
            Transform root = EnvironmentRoot(scene).transform;

            Transform arch = root.Find("Architecture/GreatArch");
            Assert.IsNotNull(arch, "The great arch.");
            Assert.Less(Vector3.Distance(L.Arch, arch.position), 0.001f);
            Bounds archBounds = arch.GetComponent<MeshRenderer>().bounds;
            Assert.AreEqual(L.ArchHalfExtent * 2f, archBounds.size.x, 0.6f, "The arch and its wings are as wide as the layout says.");
            Assert.Greater(archBounds.max.y, L.ArchTop - 0.1f);

            Assert.IsNotNull(root.Find("Architecture/CourtFloor"), "The paving.");
            Assert.IsNotNull(root.Find("Architecture/Flagstones"), "The worn slabs and the kerb.");
            Assert.IsNotNull(root.Find("Background/Castle"), "The castle.");
            Assert.IsNotNull(root.Find("Background/ValleyFloor"), "The valley.");

            int vines = 0;
            foreach (Transform child in root.Find("Nature"))
            {
                if (child.name.StartsWith("Vine"))
                {
                    vines++;
                }
            }

            Assert.AreEqual(L.Vines.Length, vines);
            Assert.AreEqual(L.Modules.Length + 5, CountDirect(root.Find("Architecture"), "Wall", "Column", "Rubble", "GreatArch", "ArchStep", "CourtFloor", "Flagstones") - 0,
                "Every wall, column and rubble heap of the layout is placed, with the arch, its two steps, the floor and the slabs.");
        }

        private static int CountDirect(Transform parent, params string[] prefixes)
        {
            int count = 0;
            foreach (Transform child in parent)
            {
                foreach (string prefix in prefixes)
                {
                    if (child.name.StartsWith(prefix))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        [Test]
        public void TheEnvironmentStaysWithinItsBudgetAndUsesNothingExpensive()
        {
            BatchModeOnly();

            Scene scene = ReferenceCourtyardSetup.BuildForAutomation();
            GameObject root = EnvironmentRoot(scene);

            long triangles = 0;
            var transparent = new StringBuilder();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Assert.IsNotNull(filter.sharedMesh, $"'{filter.name}' has no mesh.");
                triangles += filter.sharedMesh.triangles.Length / 3;

                foreach (Material material in filter.GetComponent<MeshRenderer>().sharedMaterials)
                {
                    Assert.IsNotNull(material, $"'{filter.name}' has an empty material slot.");
                    if (material.renderQueue >= (int)RenderQueue.AlphaTest)
                    {
                        transparent.AppendLine($"'{filter.name}' uses '{material.name}' (queue {material.renderQueue})");
                    }
                }
            }

            Assert.LessOrEqual(triangles, L.MaxEnvironmentTriangles, $"The environment has {triangles} triangles.");
            Assert.AreEqual(string.Empty, transparent.ToString(), "Nothing in the environment is transparent or alpha-tested.");

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            Assert.AreEqual(1, lights.Length, "One light: the sun.");
            Assert.AreEqual(LightType.Directional, lights[0].type);
            Assert.AreEqual(0, root.GetComponentsInChildren<ParticleSystem>(true).Length, "No particle effects.");
            Assert.AreEqual(0, root.GetComponentsInChildren<ReflectionProbe>(true).Length, "No reflection probes.");

            foreach (MeshRenderer distant in root.transform.Find("Background").GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, distant.shadowCastingMode, $"'{distant.name}' is distant scenery and casts no shadow.");
                Assert.IsNull(distant.GetComponent<Collider>(), $"'{distant.name}' is distant scenery and has no collider.");
            }

            TestContext.WriteLine($"Reference Courtyard environment: {triangles} triangles.");
        }

        [Test]
        public void TheMageWearsTheWayfarerLookInTheCourtyard()
        {
            BatchModeOnly();

            Scene scene = ReferenceCourtyardSetup.BuildForAutomation();
            GameObject mage = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Mage")
                {
                    mage = root;
                }
            }

            Assert.IsNotNull(mage, "The courtyard has the Mage.");
            Assert.Less(Vector3.Distance(L.Spawn, mage.transform.position), 0.01f, "He stands where the composition expects him.");

            SkinnedMeshRenderer[] renderers = mage.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.Greater(renderers.Length, 0);
            bool painted = false;
            bool outlined = false;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Assert.AreEqual(CharacterModelPostprocessor.MageWayfarerModelPath, AssetDatabase.GetAssetPath(renderer.sharedMesh),
                    $"The mesh of '{renderer.name}' is not the Wayfarer's.");
                foreach (Material material in renderer.sharedMaterials)
                {
                    Texture texture = material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    painted |= texture != null && AssetDatabase.GetAssetPath(texture) == CharacterModelPostprocessor.MagePaintTexturePath;
                    outlined |= material != null && material.shader != null && material.shader.name == "Threefold/Illustrated Lit";
                }
            }

            Assert.IsTrue(painted, "A material of the Mage shows the painted atlas.");
            Assert.IsTrue(outlined, "The Mage's materials are the style's, which carry the contour line.");
        }

        [Test]
        public void TheFirstViewIsTheOneTheLayoutWasComposedFor()
        {
            BatchModeOnly();

            Scene scene = ReferenceCourtyardSetup.BuildForAutomation();
            ThirdPersonCamera rig = SceneSetupUtility.FindInScene<ThirdPersonCamera>(scene);
            Assert.IsNotNull(rig);

            var serialized = new SerializedObject(rig);
            Assert.AreEqual(L.CameraDistance, serialized.FindProperty("distance").floatValue, 0.001f);
            Assert.AreEqual(L.CameraStartPitch, serialized.FindProperty("startPitch").floatValue, 0.001f);
            Assert.AreEqual(L.CameraTargetHeight, serialized.FindProperty("targetOffset").vector3Value.y, 0.001f);
            Assert.AreEqual(L.CameraShoulder, serialized.FindProperty("shoulderOffset").vector2Value.x, 0.001f);
            Assert.AreEqual(L.CameraVerticalFov, rig.GetComponent<Camera>().fieldOfView, 0.01f);
            Assert.Greater(rig.GetComponent<Camera>().farClipPlane, Vector3.Distance(L.StartCameraPosition, L.Castle) + 80f,
                "The castle is inside the camera's far plane.");
            Assert.AreEqual(L.HazeEnd, LightingPreset.GoldenAfternoon().fogEnd, 0.01f, "The haze the layout counts on is the preset's.");
        }

        [Test]
        public void EverySceneOfTheDevelopmentBuildIsAssembledValidSavedAndLoadable()
        {
            BatchModeOnly();

            foreach (PrototypeBuild.DevelopmentScene entry in PrototypeBuild.AssemblyOrder())
            {
                Scene scene = entry.Assemble();
                Assert.AreEqual(entry.Path, scene.path);

                var problems = new StringBuilder();
                Assert.AreEqual(0, entry.Validate(scene, problems), $"{entry.Path}:\n{problems}");
                Assert.IsTrue(File.Exists(entry.Path), $"{entry.Path} was saved.");
            }

            foreach (string path in PrototypeBuild.ScenePaths())
            {
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), $"{path} is a scene asset.");
                Scene loaded = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Assert.IsTrue(loaded.IsValid() && loaded.isLoaded, $"{path} loads.");
                Assert.Greater(loaded.rootCount, 0, $"{path} is not empty.");
                Assert.IsNotNull(SceneSetupUtility.FindInScene<Camera>(loaded), $"{path} has a camera.");

                // The way out of a scene is the diagnostics' scene list. The main menu has its own way: its entries.
                if (path != MainMenuSetup.ScenePath)
                {
                    Assert.IsNotNull(SceneSetupUtility.FindInScene<DevDiagnosticsOverlay>(loaded),
                        $"{path} has the development diagnostics, and with them the scene list.");
                }
            }
        }
    }
}
