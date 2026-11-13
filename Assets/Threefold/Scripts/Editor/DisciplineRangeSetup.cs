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
    /// Builds the Discipline Range: a small flat scene for trying the three disciplines on practice
    /// targets that come back after they are defeated.
    ///
    ///   Close to the start, three dummies stand side by side: Arcane strikes one, Flame's wide
    ///   swings catch all three and heat them.
    ///   Further on, five dummies stand in a wedge for the Flame Wave and the Force Pulse, with a
    ///   sixth behind a wall that the wave must not reach.
    ///   Down the lane stand targets at 22, 30 and 38 metres for the Arcane Arrow; the last is the
    ///   construct that completes the scene's objective.
    ///   Along the left edge stand the six objects that respond to magic (see MagicalObjectSetup):
    ///   crystal, barricade, stone barrier, platform, unstable structure and reinforced door.
    ///
    /// Dummies cannot be pushed and have no poise, so stagger and push are tried on the real enemies
    /// in the Combat Arena, where the same disciplines are available. The scene is for testing; it
    /// is not added to the build.
    /// </summary>
    public static class DisciplineRangeSetup
    {
        private const string MenuPath = "Threefold/Setup/Discipline Range";
        private const string DialogTitle = "Discipline Range";
        internal const string ScenePath = "Assets/Scenes/DisciplineRange.unity";
        private const string GeneratorName = "DisciplineRange";
        private const string RootName = "DisciplineRange Environment";

        private static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -8f);

        /// <summary>Where the practice targets stand. The last one is the objective construct.</summary>
        internal static readonly CombatSetup.DummyPlacement[] Targets =
        {
            // Melee: three abreast, within one wide swing of each other.
            new CombatSetup.DummyPlacement(new Vector3(-1.3f, 0f, -4.6f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(0f, 0f, -4.2f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(1.3f, 0f, -4.6f), 180f),

            // Area attacks: a wedge of five.
            new CombatSetup.DummyPlacement(new Vector3(-1.6f, 0f, 1.5f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(1.6f, 0f, 1.5f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(0f, 0f, 2.8f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(-2.4f, 0f, 4.2f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(2.4f, 0f, 4.2f), 180f),

            // Behind the cover wall: an area attack from the lane must not reach it.
            new CombatSetup.DummyPlacement(new Vector3(6.2f, 0f, 3.5f), 180f),

            // The arrow lane.
            new CombatSetup.DummyPlacement(new Vector3(0f, 0f, 14f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(-3f, 0f, 22f), 180f),
            new CombatSetup.DummyPlacement(new Vector3(3f, 0f, 30f), 180f, true),
        };

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
                    "This scene already contains the Discipline Range.\n\nRebuild its floor and wall? The Mage, the targets, " +
                    "the magical objects, the HUD and anything you added yourself are kept.", "Rebuild", "Cancel"))
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
                "Press Play. Keys 1, 2 and 3 choose Arcane, Flame and Aegis.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Builds the range without asking anything, for tests. Discards unsaved work in the open scene.</summary>
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

            // A long paved floor on turf: the start, the area field and the arrow lane.
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "RangeFloor", new Vector3(26f, 0.6f, 56f),
                new Vector3(0f, -0.6f, 12f), assets.PavingMaterial);
            FantasyPlaygroundSetup.CreateMeshObject(nature, "RangeGround", assets.EnsureBlockMesh(new Vector3(120f, 0.5f, 120f)),
                assets.GroundMaterial, new Vector3(0f, -0.62f, 12f), true);

            // Cover: tall enough to hide a dummy, between the area field and the dummy behind it.
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "CoverWall", new Vector3(0.7f, 2.4f, 3.4f),
                new Vector3(4.4f, 0f, 3.5f), assets.DarkStoneMaterial);

            // Low markers at the start of each part of the range.
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "MarkerMelee", new Vector3(0.5f, 0.35f, 0.5f),
                new Vector3(-4.5f, 0f, -4.4f), assets.DarkStoneMaterial);
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "MarkerArea", new Vector3(0.5f, 0.35f, 0.5f),
                new Vector3(-4.5f, 0f, 2.8f), assets.DarkStoneMaterial);
            FantasyPlaygroundSetup.PlaceBlock(assets, architecture, "MarkerLane", new Vector3(0.5f, 0.35f, 0.5f),
                new Vector3(-4.5f, 0f, 14f), assets.DarkStoneMaterial);

            FantasyPlaygroundSetup.BuildBackground(assets, background);
            FantasyPlaygroundSetup.BuildLighting(assets, lighting, report);
            report.AppendLine("Built the Discipline Range: floor, cover wall and markers.");

            GameObject mage = MagePrototypeSetup.EnsureCharacterRig(scene, SpawnPosition, Quaternion.identity, Targets, report);

            // The objects that respond to magic stand in a lane along the left edge of the floor.
            MagicalObjectSetup.ExampleAssets examples = MagicalObjectSetup.EnsureAssets(report);
            MagicalObjectSetup.EnsureExamples(scene, examples, MagicalObjectSetup.RangeLaneOrigin, 0f, report);

            FantasyPlaygroundSetup.EnablePostProcessing(scene, report);
            MagePrototypeSetup.AppendValidation(scene, report);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return mage;
        }
    }
}
