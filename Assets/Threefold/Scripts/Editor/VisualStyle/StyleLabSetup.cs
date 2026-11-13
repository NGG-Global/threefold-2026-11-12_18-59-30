using System.Text;
using Threefold.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the Style Lab: the small, isolated scene the art bible asks for as the first visual
    /// prototype (Docs/ArtBible.md, section 10). It holds what that test lists and nothing else:
    ///
    ///   a humanoid test character (the Mage, playable, so movement and animation can be judged);
    ///   a small setting: paved ground, a stone structure, a foliage cluster, mountains and haze;
    ///   props in wood, cloth and metal, so all five material families stand side by side;
    ///   two lighting conditions and an outline switch, on the test panel or with F8 and F7.
    ///
    /// The camera is the game's own: orbit it by hand to check that lines and brushwork stay
    /// steady in motion. The scene is for judging the look; it is not added to the build.
    /// </summary>
    public static class StyleLabSetup
    {
        private const string MenuPath = "Threefold/Setup/Style Lab";
        private const string DialogTitle = "Style Lab";
        internal const string ScenePath = "Assets/Scenes/StyleLab.unity";
        private const string GeneratorName = "StyleLab";
        private const string RootName = "StyleLab Environment";

        private static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -7f);

        [MenuItem(MenuPath, false, 6)]
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
                    "This scene already contains the Style Lab.\n\nRebuild its environment? The Mage, the HUD, the style " +
                    "controller and anything you added yourself are kept.", "Rebuild", "Cancel"))
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
                "Press Play, walk round the props, and switch lines and lighting on the panel in the top right.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Builds the lab without asking anything, for tests. Discards unsaved work in the open scene.</summary>
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

            // Ground: a paved square on turf.
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "LabFloor", new Vector3(18f, 0.6f, 18f),
                new Vector3(0f, -0.6f, 0f), assets.PavingMaterial);
            FantasyPlaygroundSetup.CreateMeshObject(nature, "LabGround", assets.EnsureBlockMesh(new Vector3(90f, 0.5f, 90f)),
                assets.GroundMaterial, new Vector3(0f, -0.62f, 0f), true);

            // Stone structure: an arch between columns, with walls and a plinth of dark stone.
            FantasyPlaygroundSetup.Place(assets.Arch, architecture, new Vector3(0f, 0f, 6.5f), 0f);
            FantasyPlaygroundSetup.Place(assets.Column, architecture, new Vector3(-4.2f, 0f, 6.5f), 0f);
            FantasyPlaygroundSetup.Place(assets.Column, architecture, new Vector3(4.2f, 0f, 6.5f), 0f);
            FantasyPlaygroundSetup.Place(assets.ColumnBroken, architecture, new Vector3(-6.8f, 0f, 2.2f), 30f);
            FantasyPlaygroundSetup.Place(assets.Wall, architecture, new Vector3(7.4f, 0f, 1.5f), 90f);
            FantasyPlaygroundSetup.Place(assets.WallLow, architecture, new Vector3(-7.4f, 0f, -2.5f), 90f);
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "Plinth", new Vector3(2f, 1f, 2f),
                new Vector3(5.2f, 0f, -3f), assets.DarkStoneMaterial);
            FantasyPlaygroundSetup.Place(assets.Rubble, architecture, new Vector3(5.6f, 0f, 4f), 40f);

            // Foliage cluster, on the turf beside the paving.
            const float turf = -0.12f;
            FantasyPlaygroundSetup.Place(assets.Trees[0], nature, new Vector3(-11.5f, turf, 8f), 20f, 1.2f);
            FantasyPlaygroundSetup.Place(assets.Trees[1 % assets.Trees.Length], nature, new Vector3(-12.5f, turf, 2.5f), 140f, 1f);
            FantasyPlaygroundSetup.Place(assets.Trees[2 % assets.Trees.Length], nature, new Vector3(11.8f, turf, 9f), 260f, 1.35f);
            FantasyPlaygroundSetup.Place(assets.Rocks[0], nature, new Vector3(10.8f, turf, -4.5f), 70f, 1.2f);
            FantasyPlaygroundSetup.Place(assets.GrassPatches[0], nature, new Vector3(-9.8f, turf, -1f), 0f);
            FantasyPlaygroundSetup.Place(assets.GrassPatches[1 % assets.GrassPatches.Length], nature, new Vector3(9.9f, turf, 4.5f), 90f);
            FantasyPlaygroundSetup.Place(assets.GrassPatches[0], nature, new Vector3(-10.2f, turf, 6f), 200f);
            FantasyPlaygroundSetup.Place(assets.MossPatch, nature, new Vector3(6.6f, 0.012f, 0.8f), 30f);
            FantasyPlaygroundSetup.Place(assets.MossPatch, nature, new Vector3(-6.4f, 0.013f, -0.8f), 130f, 1.2f);

            FantasyPlaygroundSetup.BuildBackground(assets, background);
            FantasyPlaygroundSetup.BuildLighting(assets, lighting, report);
            report.AppendLine("Built the Style Lab: paving, arch and columns, plinth, trees, grass and background.");

            // One construct on the plinth gives the rig its usual objective and something to cast at.
            GameObject mage = MagePrototypeSetup.EnsureCharacterRig(scene, SpawnPosition, Quaternion.identity,
                new[] { new CombatSetup.DummyPlacement(new Vector3(5.2f, 1f, -3f), 200f, true) }, report);

            FantasyPlaygroundSetup.EnablePostProcessing(scene, report);

            // The style, with its props in a row in front of the arch.
            VisualStyleSetup.ApplyDuringSetup(scene, report,
                new VisualStyleSetup.PropPlacement { Position = new Vector3(-2.5f, 0f, 3.2f), Yaw = 0f });

            MagePrototypeSetup.AppendValidation(scene, report);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return mage;
        }
    }
}
