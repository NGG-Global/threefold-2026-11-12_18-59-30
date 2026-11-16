using System.IO;
using System.Text;
using NUnit.Framework;
using Threefold.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// That the Mage really wears the Wayfarer look: Unity imports the painted atlas as an ordinary texture, the
    /// Wayfarer model is accepted, and the generated Mage carries that mesh with that painting. The look falls back
    /// to the mesh of Mage.fbx by design when something is wrong, and the other tests pass either way; these do not.
    /// Every failure message ends with what Unity made of the files. They read imported assets, so they run only
    /// inside Unity (EditMode).
    /// </summary>
    public sealed class MageWayfarerLookTests
    {
        private const string PaintPath = CharacterModelPostprocessor.MagePaintTexturePath;
        private const string WayfarerPath = CharacterModelPostprocessor.MageWayfarerModelPath;

        [Test]
        public void ThePaintedAtlasIsImportedAsAnOrdinaryTexture()
        {
            Assert.IsTrue(File.Exists(PaintPath), $"'{PaintPath}' is not on disk.");
            Assert.AreEqual(typeof(Texture2D), AssetDatabase.GetMainAssetTypeAtPath(PaintPath), Describe());

            var importer = AssetImporter.GetAtPath(PaintPath) as TextureImporter;
            Assert.IsNotNull(importer, Describe());
            Assert.AreEqual(TextureImporterShape.Texture2D, importer.textureShape, Describe());
            Assert.AreEqual(TextureImporterType.Default, importer.textureType, Describe());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(PaintPath), Describe());
        }

        [Test]
        public void TheWayfarerModelIsAcceptedAsTheMagesLook()
        {
            Avatar library = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(CharacterModelPostprocessor.MageModelPath))
            {
                if (asset is Avatar found)
                {
                    library = found;
                }
            }

            Assert.IsNotNull(library, "Mage.fbx has no avatar.");
            var report = new StringBuilder();
            MageCharacterSetup.AlignWayfarerMapping(library, report);
            string problem = MageCharacterSetup.WayfarerProblem(library, out GameObject model, out Avatar avatar);
            Assert.IsNull(problem, $"The Wayfarer look cannot be used: {problem}.\n{report}\n{Describe()}");
            Assert.IsTrue(avatar.isValid && avatar.isHuman, "The Wayfarer model's avatar is a valid Humanoid.");

            // Aligning a second time finds nothing to do: the mapping is settled, not imported again on every setup.
            var second = new StringBuilder();
            MageCharacterSetup.AlignWayfarerMapping(library, second);
            Assert.AreEqual(string.Empty, second.ToString());
            Assert.IsNotNull(model);
            Assert.IsNotNull(avatar);
        }

        [Test]
        public void TheGeneratedMageWearsTheWayfarerMeshWithItsPainting()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it may rebuild the generated MageVisual prefab.");
            }

            var report = new StringBuilder();
            GameObject prefab = MageCharacterSetup.EnsureVisualPrefab(report);
            Assert.IsNotNull(prefab, report.ToString());
            Assert.AreEqual(MageLook.Wayfarer, MageCharacterSetup.CurrentLook(), $"{report}\n{Describe()}");
            AssertWearsTheWayfarerLook(prefab, "The MageVisual prefab");
        }

        [Test]
        public void TheMageInAnAssembledLevelWearsTheWayfarerMeshWithItsPainting()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = FantasyPlaygroundSetup.BuildForAutomation();
            GameObject mage = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Mage")
                {
                    mage = root;
                }
            }

            Assert.IsNotNull(mage, "The assembled playground has no 'Mage'.");
            AssertWearsTheWayfarerLook(mage, "The Mage in the Fantasy Playground");
        }

        [Test]
        public void TheWayfarerModelKeepsItsExtraBonesAndStaysLight()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(WayfarerPath);
            Assert.IsNotNull(model, Describe());

            // The bones the game's secondary motion and face look for by name. A rebuilt model must keep them all.
            string[] expected =
            {
                "sec_cloak_l_01", "sec_cloak_l_02", "sec_cloak_r_01", "sec_cloak_r_02", "sec_hair_01", "sec_hair_02",
                "sec_sash_01", "face_blink", "face_brows",
            };
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            {
                names.Add(bone.name);
            }

            foreach (string bone in expected)
            {
                Assert.IsTrue(names.Contains(bone), $"The Wayfarer model has no bone '{bone}'.");
            }

            long triangles = 0;
            var materials = new System.Collections.Generic.HashSet<string>();
            int mostBones = 0;
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Assert.IsNotNull(renderer.sharedMesh);
                triangles += renderer.sharedMesh.triangles.Length / 3;
                mostBones = Mathf.Max(mostBones, renderer.bones.Length);
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        materials.Add(material.name);
                    }
                }
            }

            Assert.Greater(triangles, 0);
            Assert.LessOrEqual(triangles, 20000, $"The Wayfarer model has {triangles} triangles.");
            Assert.LessOrEqual(materials.Count, 2, "One painted material and the crystal: two draw calls for the body, no more.");
            TestContext.WriteLine($"Wayfarer model: {triangles} triangles, {materials.Count} material(s), {mostBones} bones on its largest mesh.");
        }

        /// <summary>Every skinned mesh comes from MageWayfarer.fbx, and a material on them shows the painted atlas.</summary>
        private static void AssertWearsTheWayfarerLook(GameObject root, string what)
        {
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.Greater(renderers.Length, 0, $"{what} has no skinned mesh.");

            bool painted = false;
            var found = new StringBuilder();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Assert.IsNotNull(renderer.sharedMesh, $"{what}: '{renderer.name}' has no mesh.");
                Assert.AreEqual(WayfarerPath, AssetDatabase.GetAssetPath(renderer.sharedMesh),
                    $"{what}: the mesh of '{renderer.name}' is not the Wayfarer's.\n{Describe()}");

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
                    string path = texture != null ? AssetDatabase.GetAssetPath(texture) : "no texture";
                    found.AppendLine($"  '{material.name}' ({material.shader.name}): {path}");
                    painted |= texture is Texture2D && path == PaintPath;
                }
            }

            Assert.IsTrue(painted, $"{what}: no material shows '{PaintPath}'. Materials:\n{found}{Describe()}");
        }

        /// <summary>What Unity made of the painted atlas and the Wayfarer model, for the failure messages.</summary>
        private static string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine("--- What Unity made of the Wayfarer files ---");
            DescribeAsset(text, PaintPath);
            if (AssetImporter.GetAtPath(PaintPath) is TextureImporter texture)
            {
                text.AppendLine($"  importer: type {texture.textureType}, shape {texture.textureShape}, " +
                                $"max size {texture.maxTextureSize}, non-power-of-two {texture.npotScale}, sRGB {texture.sRGBTexture}");
            }

            DescribeAsset(text, WayfarerPath);
            if (AssetImporter.GetAtPath(WayfarerPath) is ModelImporter model)
            {
                text.AppendLine($"  importer: animation {model.animationType}, avatar {model.avatarSetup}, " +
                                $"{model.humanDescription.human.Length} human bone(s) set in the import settings");
            }

            return text.ToString();
        }

        private static void DescribeAsset(StringBuilder text, string path)
        {
            text.AppendLine(path);
            if (!File.Exists(path))
            {
                text.AppendLine("  not on disk");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            string start = Encoding.ASCII.GetString(bytes, 0, Mathf.Min(bytes.Length, 7));
            text.AppendLine($"  on disk: {bytes.Length} bytes{(start == "version" ? " (a Git LFS pointer, not the file itself)" : string.Empty)}");
            text.AppendLine($"  main asset type: {AssetDatabase.GetMainAssetTypeAtPath(path)}");

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Texture || asset is Material || asset is Avatar)
                {
                    string extra = asset is Material material && material.mainTexture != null
                        ? $", texture {material.mainTexture.GetType().Name} '{AssetDatabase.GetAssetPath(material.mainTexture)}'"
                        : asset is Avatar avatar ? $", valid {avatar.isValid}, human {avatar.isHuman}" : string.Empty;
                    text.AppendLine($"  holds {asset.GetType().Name} '{asset.name}'{extra}");
                }
            }

            string meta = path + ".meta";
            if (File.Exists(meta))
            {
                string content = File.ReadAllText(meta);
                text.AppendLine($"  {meta} ({content.Length} characters):");
                text.AppendLine(content.Length > 400 ? content.Substring(0, 400) + "..." : content);
            }
        }
    }
}
