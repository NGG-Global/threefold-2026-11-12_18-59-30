using System.Text;
using Threefold.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;
using L = Threefold.EditorTools.ReferenceCourtyardLayout;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the Reference Courtyard (Docs/VisualReferencePrototype.md): a small courtyard for judging the
    /// visual direction against a reference picture. It is the Style Lab's kind of scene, composed: a paved
    /// court with a great arch on the axis of the first view, a standing wall on the left and ruins on the
    /// right, vines, grass and moss in a few chosen places, and a valley with a castle beyond.
    ///
    /// Everything stands where <see cref="ReferenceCourtyardLayout"/> says. The modules, materials, lighting,
    /// character and visual style are the game's own; the scene adds five kinds of mesh
    /// (<see cref="ReferenceCourtyardMeshes"/>) and no asset from outside the project. It is a test scene: it
    /// holds no progression, saves nothing, and no gameplay scene leads to it.
    /// </summary>
    public static class ReferenceCourtyardSetup
    {
        private const string MenuPath = "Threefold/Setup/Reference Courtyard";
        private const string DialogTitle = "Reference Courtyard";
        internal const string ScenePath = "Assets/Scenes/ReferenceCourtyard.unity";
        internal const string GeneratorName = "ReferenceCourtyard";
        internal const string RootName = "ReferenceCourtyard Environment";
        private const int IgnoreRaycastLayer = 2;

        [MenuItem(MenuPath, false, 7)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            if (!TryOpenOrCreateScene(ScenePath, DialogTitle, out Scene scene))
            {
                return;
            }

            if (FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName) != null && !EditorUtility.DisplayDialog(
                    DialogTitle,
                    "This scene already contains the Reference Courtyard.\n\nRebuild its environment? The Mage, the HUD, " +
                    "the style controller and anything you added yourself are kept.", "Rebuild", "Cancel"))
            {
                return;
            }

            var report = new StringBuilder();
            GameObject mage = Assemble(scene, report);
            bool saved = EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = mage;

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle,
                summary + (saved ? "\n\nThe scene was saved. " : "\n\nThe scene could NOT be saved; save it with Ctrl+S. ") +
                "Press Play: the first view is the composition. It is a proposal until it has been judged here and on a device.",
                "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Builds the courtyard without asking anything, for tests and builds. Discards unsaved work in the open scene.</summary>
        internal static Scene BuildForAutomation()
        {
            Scene scene = OpenOrCreateSceneForAutomation(ScenePath);

            var report = new StringBuilder();
            Assemble(scene, report);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Threefold] {DialogTitle} assembled for automation.\n{report.ToString().TrimEnd()}");
            return scene;
        }

        private static GameObject Assemble(Scene scene, StringBuilder report)
        {
            EnvironmentAssets assets = EnvironmentAssets.Ensure(false, report);

            GameObject oldRoot = FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName);
            if (oldRoot != null)
            {
                Undo.DestroyObjectImmediate(oldRoot);
            }

            var root = new GameObject(RootName);
            root.AddComponent<GeneratedEnvironment>().Generator = GeneratorName;
            Undo.RegisterCreatedObjectUndo(root, DialogTitle);

            Transform architecture = FantasyPlaygroundSetup.CreateGroup(root.transform, "Architecture");
            Transform nature = FantasyPlaygroundSetup.CreateGroup(root.transform, "Nature");
            Transform background = FantasyPlaygroundSetup.CreateGroup(root.transform, "Background");
            Transform lighting = FantasyPlaygroundSetup.CreateGroup(root.transform, "Lighting");

            BuildGround(assets, architecture, nature, background);
            BuildArchitecture(assets, architecture);
            BuildVegetation(assets, nature);
            BuildDistance(assets, background);
            BuildBounds(root.transform);
            FantasyPlaygroundSetup.BuildLighting(assets, lighting, report);
            report.AppendLine("Built the Reference Courtyard: paving, great arch, walls, vines, grass, valley and castle.");

            var constructs = new CombatSetup.DummyPlacement[L.Constructs.Length];
            for (int i = 0; i < constructs.Length; i++)
            {
                constructs[i] = new CombatSetup.DummyPlacement(L.Constructs[i], 180f, true);
            }

            GameObject mage = MagePrototypeSetup.EnsureCharacterRig(scene, L.Spawn, Quaternion.identity, constructs, report);

            // The same post-processing switch as every level of the game; the scene adds no effect of its own.
            FantasyPlaygroundSetup.EnablePostProcessing(scene, report);

            VisualStyleSetup.ApplyDuringSetup(scene, report,
                new VisualStyleSetup.PropPlacement { Position = L.Props, Yaw = L.PropsYaw });

            MagePrototypeSetup.AppendValidation(scene, report);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return mage;
        }

        // ------------------------------------------------------------ ground

        private static void BuildGround(EnvironmentAssets assets, Transform architecture, Transform nature, Transform background)
        {
            // The paving, on a grass terrace; its top is the level the Mage walks on.
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "CourtFloor",
                new Vector3(L.HalfWidth * 2f + 1f, 0.6f, L.HalfDepth * 2f + 1f), new Vector3(0f, -0.6f, 0f), assets.PavingMaterial);

            Mesh flagstones = assets.EnsureMesh(ReferenceCourtyardMeshes.Name("Flagstones"), ReferenceCourtyardMeshes.BuildFlagstones);
            GameObject worn = FantasyPlaygroundSetup.CreateMeshObject(architecture, "Flagstones", flagstones, assets.StoneMaterial,
                Vector3.zero, false);
            worn.GetComponent<MeshRenderer>().sharedMaterials = new[] { assets.StoneMaterial, assets.PavingMaterial };
            worn.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            FantasyPlaygroundSetup.Place(assets.Dais, architecture, L.Dais, 0f);

            FantasyPlaygroundSetup.CreateMeshObject(nature, "Terrace",
                assets.EnsureBlockMesh(new Vector3(L.TerraceHalf * 2f, 3f, L.TerraceHalf * 2f)), assets.GroundMaterial,
                new Vector3(0f, -3.12f, 0f), true);

            // The valley floor: far below, never walked on, so it has no collider and casts no shadow.
            GameObject valley = FantasyPlaygroundSetup.CreateMeshObject(background, "ValleyFloor",
                assets.EnsureBlockMesh(new Vector3(900f, 1f, 900f)), assets.GroundMaterial,
                new Vector3(0f, -L.ValleyDrop - 1f, 0f), false);
            QuietDistance(valley);
        }

        // ------------------------------------------------------------ architecture

        private static void BuildArchitecture(EnvironmentAssets assets, Transform parent)
        {
            Mesh arch = assets.EnsureMesh(ReferenceCourtyardMeshes.Name("GreatArch"), ReferenceCourtyardMeshes.BuildGreatArch);
            GameObject gate = FantasyPlaygroundSetup.CreateMeshObject(parent, "GreatArch", arch, assets.StoneMaterial, L.Arch, true);
            gate.GetComponent<MeshRenderer>().sharedMaterials = new[] { assets.StoneMaterial, assets.MossMaterial };

            // Two steps up to the threshold, as wide as the opening and its piers.
            float stepWidth = (L.ArchRadius + L.ArchPierWidth) * 2f + 1f;
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "ArchStepLower", new Vector3(stepWidth + 1.2f, 0.16f, 2.4f),
                new Vector3(L.Arch.x, 0f, L.Arch.z - 1.5f), assets.StoneMaterial);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "ArchStepUpper", new Vector3(stepWidth, 0.16f, 1.4f),
                new Vector3(L.Arch.x, 0.16f, L.Arch.z - 1.2f), assets.StoneMaterial);

            foreach (L.Placement placement in L.Modules)
            {
                FantasyPlaygroundSetup.Place(Prefab(assets, placement.Module), parent, placement.Position, placement.Yaw);
            }

            Mesh light = assets.EnsureMesh(ReferenceCourtyardMeshes.Name("Light"), ReferenceCourtyardMeshes.BuildLight);
            for (int i = 0; i < L.Lights.Length; i++)
            {
                GameObject lamp = FantasyPlaygroundSetup.CreateMeshObject(parent, "ArchLight" + (i + 1), light, assets.StoneMaterial,
                    L.Lights[i], false);
                lamp.GetComponent<MeshRenderer>().sharedMaterials = new[] { assets.StoneMaterial, assets.CrystalMaterial };
            }
        }

        private static GameObject Prefab(EnvironmentAssets assets, L.Module module)
        {
            switch (module)
            {
                case L.Module.Wall: return assets.Wall;
                case L.Module.WallBroken: return assets.WallBroken;
                case L.Module.WallLow: return assets.WallLow;
                case L.Module.Column: return assets.Column;
                case L.Module.ColumnBroken: return assets.ColumnBroken;
                case L.Module.ColumnFallen: return assets.ColumnFallen;
                default: return assets.Rubble;
            }
        }

        // ------------------------------------------------------------ vegetation

        private static void BuildVegetation(EnvironmentAssets assets, Transform parent)
        {
            const float turf = -0.12f;

            foreach (L.Tree tree in L.Trees)
            {
                FantasyPlaygroundSetup.Place(assets.Trees[tree.Kind % assets.Trees.Length], parent,
                    new Vector3(tree.Position.x, turf, tree.Position.z), tree.Yaw, tree.Scale);
            }

            for (int i = 0; i < L.GrassPatches.Length; i++)
            {
                Vector3 at = L.GrassPatches[i];
                GameObject grass = FantasyPlaygroundSetup.Place(assets.GrassPatches[i % assets.GrassPatches.Length], parent,
                    new Vector3(at.x, turf, at.z), i * 47f);
                if (grass.TryGetComponent(out MeshRenderer grassRenderer))
                {
                    grassRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            for (int i = 0; i < L.MossPatches.Length; i++)
            {
                Vector3 at = L.MossPatches[i];
                FantasyPlaygroundSetup.Place(assets.MossPatch, parent, new Vector3(at.x, 0.012f + i * 0.001f, at.z), i * 61f,
                    0.8f + (i % 3) * 0.25f);
            }

            FantasyPlaygroundSetup.Place(assets.Rocks[0], parent, new Vector3(15.5f, turf, 6.5f), 70f, 1.3f);
            FantasyPlaygroundSetup.Place(assets.Rocks[1 % assets.Rocks.Length], parent, new Vector3(-15.8f, turf, 15.5f), 200f, 1.1f);
            FantasyPlaygroundSetup.Place(assets.Rocks[2 % assets.Rocks.Length], parent, new Vector3(17f, turf, -13f), 320f, 0.9f);

            foreach (L.Vine vine in L.Vines)
            {
                Mesh mesh = assets.EnsureMesh(ReferenceCourtyardMeshes.Name("Vine" + vine.Seed),
                    () => ReferenceCourtyardMeshes.BuildVine(vine.Width, vine.Drop, vine.Seed));
                GameObject hanging = FantasyPlaygroundSetup.CreateMeshObject(parent, "Vine" + vine.Seed, mesh, assets.FoliageMaterial,
                    vine.Top, false);
                hanging.transform.rotation = Quaternion.LookRotation(vine.Facing, Vector3.up);
                MeshRenderer vineRenderer = hanging.GetComponent<MeshRenderer>();
                vineRenderer.sharedMaterials = new[] { assets.FoliageMaterial, assets.BarkMaterial };
                vineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ------------------------------------------------------------ distance

        /// <summary>
        /// The valley: a few large, simple shapes that the haze of the lighting preset turns into depth. Nothing
        /// here casts or receives a shadow, and nothing is transparent.
        /// </summary>
        private static void BuildDistance(EnvironmentAssets assets, Transform parent)
        {
            for (int i = 0; i < L.Hills.Length; i++)
            {
                L.Hill hill = L.Hills[i];
                GameObject source = assets.Mountains[i % assets.Mountains.Length];
                GameObject instance;

                if (hill.Wooded && source.TryGetComponent(out MeshFilter filter))
                {
                    instance = FantasyPlaygroundSetup.CreateMeshObject(parent, "WoodedHill" + (i + 1), filter.sharedMesh,
                        assets.FoliageMaterial, hill.Position, false);
                    instance.transform.rotation = Quaternion.Euler(0f, hill.Yaw, 0f);
                    instance.transform.localScale = Vector3.one * hill.Scale;
                }
                else
                {
                    instance = FantasyPlaygroundSetup.Place(source, parent, hill.Position, hill.Yaw, hill.Scale);
                }

                QuietDistance(instance);
            }

            foreach (L.Ridge ridge in L.Ridges)
            {
                Mesh mesh = assets.EnsureMesh(ReferenceCourtyardMeshes.Name("Ridge" + ridge.Seed),
                    () => ReferenceCourtyardMeshes.BuildRidge(ridge.Length, ridge.Height, ridge.Peaks, ridge.Seed, ridge.Wooded));
                GameObject range = FantasyPlaygroundSetup.CreateMeshObject(parent, (ridge.Wooded ? "WoodedRidge" : "Range") + ridge.Seed, mesh,
                    ridge.Wooded ? assets.FoliageMaterial : assets.MountainMaterial, ridge.Position, false);
                range.transform.rotation = Quaternion.Euler(0f, ridge.Yaw, 0f);
                QuietDistance(range);
            }

            Mesh castle = assets.EnsureMesh(ReferenceCourtyardMeshes.Name("Castle"), ReferenceCourtyardMeshes.BuildCastle);
            GameObject silhouette = FantasyPlaygroundSetup.CreateMeshObject(parent, "Castle", castle, assets.DarkStoneMaterial,
                L.Castle, false);
            silhouette.GetComponent<MeshRenderer>().sharedMaterials = new[] { assets.StoneMaterial, assets.FoliageMaterial };
            QuietDistance(silhouette);
        }

        private static void QuietDistance(GameObject instance)
        {
            if (instance.TryGetComponent(out MeshRenderer meshRenderer))
            {
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------ bounds

        /// <summary>
        /// Invisible walls on the grass round the courtyard, well inside the terrace's edge, so the Mage cannot
        /// walk off into the valley. On the Ignore Raycast layer: they stop the character, not the camera or the aim.
        /// </summary>
        private static void BuildBounds(Transform parent)
        {
            Transform bounds = FantasyPlaygroundSetup.CreateGroup(parent, "Bounds");
            float limit = L.TerraceHalf - 14f;
            float length = limit * 2f + 2f;

            CreateBound(bounds, "North", new Vector3(0f, 6f, limit), new Vector3(length, 16f, 1f));
            CreateBound(bounds, "South", new Vector3(0f, 6f, -limit), new Vector3(length, 16f, 1f));
            CreateBound(bounds, "East", new Vector3(limit, 6f, 0f), new Vector3(1f, 16f, length));
            CreateBound(bounds, "West", new Vector3(-limit, 6f, 0f), new Vector3(1f, 16f, length));
        }

        private static void CreateBound(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.layer = IgnoreRaycastLayer;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = centre;
            wall.AddComponent<BoxCollider>().size = size;
            wall.isStatic = true;
        }
    }
}
