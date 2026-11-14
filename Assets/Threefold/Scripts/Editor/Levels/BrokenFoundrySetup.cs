using System.Collections.Generic;
using System.IO;
using System.Text;
using Threefold.Cameras;
using Threefold.Combat;
using Threefold.DevTools;
using Threefold.Enemies;
using Threefold.Levels;
using Threefold.Magic;
using Threefold.Objectives;
using Threefold.Persistence;
using Threefold.Puzzles;
using Threefold.World;
using Threefold.WorldMagic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;
using Random = System.Random;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the Broken Foundry (Sprint LM-07): a compact level in which Living Magic is played rather
    /// than demonstrated. One menu command creates the scene, the architecture from
    /// <see cref="FoundryLayout"/>, the machinery, the magical objects and how they are connected, the
    /// Ruins Guardian with its navigation, the checkpoints and the slag hazard, the Mage, the objective,
    /// the lighting and visual style, and the development diagnostics.
    ///
    ///   Forecourt     a dormant crystal wakes, the foundry gate rises (Arcane)
    ///   Ore Hall      a damaged barricade burns or breaks for good; a crumbling bridge over slag holds while braced
    ///   Lift Hall     a rune-locked lift is lit and pushed under a high ledge (Arcane, then Aegis)
    ///   Casting Floor the Guardian, a furnace whose heat makes its plates brittle, scaffolds that topple onto it
    ///   Exit Court    three ways through the wall: shatter the heated slag plug, slide the counterweight, or brace
    ///                 the gantry and climb over the breach
    ///
    /// Safe to run repeatedly. The generated environment (walls, floors, decoration, lighting) is rebuilt;
    /// the gameplay objects, the Mage, the HUD and anything added by hand are kept, and only what is missing
    /// is added and only empty connections are filled. Delete 'BrokenFoundry Gameplay' to get the default
    /// layout of the gameplay objects back.
    /// </summary>
    public static class BrokenFoundrySetup
    {
        private const string MenuPath = "Threefold/Setup/Broken Foundry";
        private const string DialogTitle = "Broken Foundry";
        internal const string ScenePath = "Assets/Scenes/" + FoundryLayout.SceneName + ".unity";
        private const string GeneratorName = "BrokenFoundry";
        private const string RootName = "BrokenFoundry Environment";
        internal const string GameplayName = "BrokenFoundry Gameplay";
        internal const string NavMeshPath = "Assets/Threefold/Generated/Navigation/BrokenFoundryNavMesh.asset";
        private const int IgnoreRaycastLayer = 2;

        internal const string CrystalName = "Dormant Crystal";
        internal const string ForecourtGateName = "Foundry Gate";
        internal const string BarricadeName = "Damaged Barricade";
        internal const string BridgeName = "Slag Bridge";
        internal const string HazardName = "Slag Pit";
        internal const string LiftName = "Rune Lift";
        internal const string FurnaceName = "Furnace";
        internal const string ScaffoldName = "Loose Scaffold";
        internal const string EncounterName = "Guardian Encounter";
        internal const string GuardianName = "Ruins Guardian";
        internal const string OutcomeName = "Guardian Defeated";
        internal const string GuardianGateName = "Casting Floor Gate";
        internal const string CounterweightName = "Counterweight";
        internal const string PlugName = "Slag Plug";
        internal const string GantryName = "Crumbling Gantry";
        internal const string ExitGateName = "Exit Gate";
        internal const string CheckpointsName = "Checkpoints";
        internal const string RouteName = "Checkpoint Route";
        internal const string ProgressName = "Foundry Progress";
        internal const string DiagnosticsName = "Dev Diagnostics";

        internal const string ForecourtGateKey = FoundryLayout.KeyPrefix + "Foundry Gate";
        internal const string ExitGateKey = FoundryLayout.KeyPrefix + "Exit Gate";
        internal const string GuardianKey = FoundryLayout.KeyPrefix + "Guardian Defeated";

        /// <summary>The objects of the level that the rest of the setup connects.</summary>
        internal sealed class Gameplay
        {
            public GameObject Root;
            public MagicalObject Crystal;
            public PuzzleDoor ForecourtGate;
            public MagicalObject Barricade;
            public MagicalObject Bridge;
            public HazardVolume Hazard;
            public MagicalObject Lift;
            public MagicalObject Furnace;
            public readonly List<MagicalObject> Scaffolds = new List<MagicalObject>();
            public EnemyController Guardian;
            public PersistentDefeat Outcome;
            public PuzzleDoor GuardianGate;
            public MagicalObject Counterweight;
            public MagicalObject Plug;
            public MagicalObject Gantry;
            public PuzzleDoor ExitGate;
            public readonly List<Checkpoint> Checkpoints = new List<Checkpoint>();
            public CheckpointRoute Route;
            public FoundryProgressTracker Progress;
            public DevDiagnosticsOverlay Diagnostics;
        }

        [MenuItem(MenuPath, false, 8)]
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
                    "This scene already contains the Broken Foundry.\n\nRebuild its walls, floors, decoration, lighting and " +
                    "navigation? The magical objects, the Guardian, the checkpoints, the Mage, the HUD and anything you added " +
                    "yourself are kept.", "Rebuild", "Cancel"))
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
                "Press Play. Keys 1, 2 and 3 choose Arcane, Flame and Aegis; F3 shows the development diagnostics.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Builds the level without asking anything, for automated builds and tests. Discards unsaved work in the open scene.</summary>
        internal static Scene BuildForAutomation()
        {
            Scene scene = OpenOrCreateSceneForAutomation(ScenePath);
            var report = new StringBuilder();
            Assemble(scene, report);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Threefold] {DialogTitle} assembled for automation.\n{report.ToString().TrimEnd()}");
            return scene;
        }

        /// <returns>The Mage.</returns>
        private static GameObject Assemble(Scene scene, StringBuilder report)
        {
            EnvironmentAssets environment = EnvironmentAssets.Ensure(false, report);
            EnemyAssets enemyAssets = EnemyAssets.Ensure(report);
            MagicalObjectSetup.ExampleAssets examples = MagicalObjectSetup.EnsureAssets(report);
            MagicalObjectSetup.CombinationAssets combinations = MagicalObjectSetup.EnsureCombinationAssets(report);
            BrokenFoundryAssets foundry = BrokenFoundryAssets.Ensure(examples, report);

            GameObject oldRoot = FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName);
            if (oldRoot != null)
            {
                Undo.DestroyObjectImmediate(oldRoot);
                report.AppendLine("Removed the previous generated environment.");
            }

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<GeneratedEnvironment>().Generator = GeneratorName;
            Undo.RegisterCreatedObjectUndo(root, DialogTitle);

            Transform architecture = FantasyPlaygroundSetup.CreateGroup(root.transform, "Architecture");
            Transform decor = FantasyPlaygroundSetup.CreateGroup(root.transform, "Machinery And Decor");
            Transform background = FantasyPlaygroundSetup.CreateGroup(root.transform, "Background");
            Transform lighting = FantasyPlaygroundSetup.CreateGroup(root.transform, "Lighting");

            BuildArchitecture(environment, foundry, architecture);
            BuildDecor(environment, foundry, decor);
            BuildSurroundings(environment, background);
            FantasyPlaygroundSetup.BuildBackground(environment, background);
            FantasyPlaygroundSetup.BuildLighting(environment, lighting, report);
            BuildAtmosphere(foundry, lighting);
            report.AppendLine($"Built the foundry: {FoundryLayout.Solids.Count} blocks of architecture, machinery, slag and lighting.");

            Gameplay gameplay = EnsureGameplay(scene, foundry, examples, combinations, enemyAssets, report);
            BakeNavMesh(architecture, gameplay, report);

            var objective = new ObjectiveSetup.ObjectiveOptions
            {
                Title = "Find the way out of the foundry",
                CompletionHeading = "Out of the foundry",
                Targets = new Health[0],
                Goals = new PuzzleElement[] { gameplay.Checkpoints[gameplay.Checkpoints.Count - 1] },
            };

            LayoutPoint spawn = FoundryLayout.Spawn;
            GameObject mage = MagePrototypeSetup.EnsureCharacterRig(scene, spawn.Position, Quaternion.Euler(0f, spawn.Yaw, 0f),
                new CombatSetup.DummyPlacement[0], report, objective);

            EnemyDirector director = CombatArenaSetup.EnsureDirector(scene, mage, enemyAssets, report);
            if (gameplay.Guardian != null)
            {
                Undo.RecordObject(gameplay.Guardian, DialogTitle);
                gameplay.Guardian.BindDirectorIfEmpty(director);
                MarkChanged(gameplay.Guardian);
            }

            WireFlow(scene, mage, gameplay, report);

            FantasyPlaygroundSetup.EnablePostProcessing(scene, report);
            FantasyPlaygroundSetup.AddToBuildSettings(ScenePath, false, report);
            VisualStyleSetup.ApplyDuringSetup(scene, report, null);
            ReportNavigation(gameplay, report);
            MagePrototypeSetup.AppendValidation(scene, report);
            AppendLevelChecks(gameplay, report);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return mage;
        }

        // ------------------------------------------------------------ environment

        private static void BuildArchitecture(EnvironmentAssets environment, BrokenFoundryAssets foundry, Transform parent)
        {
            foreach (LayoutBox box in FoundryLayout.Solids)
            {
                FantasyPlaygroundSetup.PlaceBlock(environment, parent, box.Name, box.Size, box.BaseCentre, SurfaceMaterial(environment, foundry, box.Surface));
            }

            // The arena's columns: cover from the Guardian, and the rhythm of a great hall.
            for (int i = 0; i < FoundryLayout.Columns.Length; i++)
            {
                LayoutPoint column = FoundryLayout.Columns[i];
                FantasyPlaygroundSetup.Place(environment.Column, parent, column.Position, column.Yaw);
            }

            // The invisible wall beyond the way out. It stops the character only.
            LayoutBox blocker = FoundryLayout.OutwardBlocker;
            var wall = new GameObject(blocker.Name);
            wall.transform.SetParent(parent, false);
            wall.transform.position = blocker.Centre;
            wall.AddComponent<BoxCollider>().size = blocker.Size;
            wall.layer = IgnoreRaycastLayer;
            wall.isStatic = true;
        }

        private static Material SurfaceMaterial(EnvironmentAssets environment, BrokenFoundryAssets foundry, FoundrySurface surface)
        {
            switch (surface)
            {
                case FoundrySurface.Paving: return environment.PavingMaterial;
                case FoundrySurface.DarkStone: return environment.DarkStoneMaterial;
                case FoundrySurface.Metal: return foundry.Metal;
                default: return environment.StoneMaterial;
            }
        }

        /// <summary>
        /// Machinery and ruin that tell what the place was. Almost nothing here collides; what does is low
        /// and stands well away from the walls, so no piece of decoration is a step up to a place the
        /// layout keeps out of reach.
        /// </summary>
        private static void BuildDecor(EnvironmentAssets environment, BrokenFoundryAssets foundry, Transform parent)
        {
            BuildDecorPieces(environment, foundry, parent);

            // Nothing in the decoration moves, so all of it is marked static, as the architecture is, which
            // lets Unity batch it. Nothing is baked: the lighting stays real-time.
            foreach (Transform piece in parent.GetComponentsInChildren<Transform>(true))
            {
                piece.gameObject.isStatic = true;
            }
        }

        private static void BuildDecorPieces(EnvironmentAssets environment, BrokenFoundryAssets foundry, Transform parent)
        {
            // Forecourt: a ruin before the foundry's front, an ore cart left behind, and an iron conduit from
            // the dormant crystal along the ground to the gate's frame: the crystal and the gate belong together.
            // The props that stand on the floor come from the layout, which the tests check for unintended climbs.
            foreach (PropPlacement prop in FoundryLayout.Props)
            {
                GameObject prefab = PropPrefab(environment, foundry, prop.Kind);
                if (prefab != null)
                {
                    FantasyPlaygroundSetup.Place(prefab, parent, prop.Point.Position, prop.Point.Yaw);
                }
            }

            Strip(parent, "Conduit", foundry.Metal, new Vector3(-4.5f, 0.06f, -27.5f), new Vector3(-2.3f, 0.06f, -24.2f), 0.14f, 0.1f);
            Strip(parent, "Conduit", foundry.Metal, new Vector3(-2.3f, 0.06f, -24.2f), new Vector3(-2.3f, 3.9f, -24.2f), 0.14f, 0.14f);
            Strip(parent, "ConduitRune", foundry.Rune, new Vector3(-2.3f, 3.9f, -24.2f), new Vector3(2.3f, 3.9f, -24.2f), 0.1f, 0.1f);

            // The foundry's chimneys stand outside the walls: the level's silhouette from everywhere inside.
            Block(parent, "ChimneyWest", foundry.DarkStone, new Vector3(-13.5f, 0f, -18f), new Vector3(3f, 20f, 3f));
            Block(parent, "ChimneyEast", foundry.DarkStone, new Vector3(13.5f, 0f, 30f), new Vector3(3.4f, 24f, 3.4f));
            Block(parent, "ChimneyNorth", foundry.DarkStone, new Vector3(-12.5f, 0f, 52f), new Vector3(2.6f, 18f, 2.6f));

            // Ore Hall: roof beams, hanging chains, rails, carts, and gears on the walls.
            for (float z = -21f; z <= -2f; z += 4.75f)
            {
                Block(parent, "RoofBeam", foundry.Wood, new Vector3(0f, FoundryLayout.OreHallWallHeight - 0.5f, z), new Vector3(18f, 0.45f, 0.4f));
            }

            FantasyPlaygroundSetup.Place(foundry.Chain, parent, new Vector3(-4f, FoundryLayout.OreHallWallHeight - 0.5f, -19f), 0f);
            FantasyPlaygroundSetup.Place(foundry.Chain, parent, new Vector3(5f, FoundryLayout.OreHallWallHeight - 0.5f, -7f), 30f);
            FantasyPlaygroundSetup.Place(foundry.Chain, parent, new Vector3(-6f, FoundryLayout.OreHallWallHeight - 0.5f, -9f), 0f);
            Strip(parent, "Rail", foundry.Metal, new Vector3(-5.5f, 0.02f, -22.5f), new Vector3(-5.5f, 0.02f, -16.2f), 0.08f, 0.04f);
            Strip(parent, "Rail", foundry.Metal, new Vector3(-4.5f, 0.02f, -22.5f), new Vector3(-4.5f, 0.02f, -16.2f), 0.08f, 0.04f);
            FantasyPlaygroundSetup.Place(foundry.GearWheel, parent, new Vector3(-8.9f, 4f, -19f), 90f);
            FantasyPlaygroundSetup.Place(foundry.GearWheel, parent, new Vector3(8.9f, 3.6f, -3f), 90f);

            // Lift Hall: gears and a rune conduit up the face of the ledge, from the lift's lock.
            FantasyPlaygroundSetup.Place(foundry.GearWheel, parent, new Vector3(7.9f, 5.5f, 6f), 90f);
            FantasyPlaygroundSetup.Place(foundry.GearWheel, parent, new Vector3(-7.9f, 6.2f, 4f), 90f);
            Strip(parent, "LiftConduit", foundry.Rune, new Vector3(-4.7f, 0.06f, 3.35f), new Vector3(-4.7f, 0.06f, 9.94f), 0.1f, 0.08f);
            Strip(parent, "LiftConduit", foundry.Rune, new Vector3(-4.7f, 0.06f, 9.94f), new Vector3(-4.7f, 4.3f, 9.94f), 0.1f, 0.08f);

            // Casting floor: crucibles, chains, and scorched paving in front of the furnace's mouth, as far as its heat reaches.
            FantasyPlaygroundSetup.Place(foundry.Chain, parent, new Vector3(3f, FoundryLayout.CastingFloorWallHeight - 0.3f, 28f), 0f);
            FantasyPlaygroundSetup.Place(foundry.Chain, parent, new Vector3(-2f, FoundryLayout.CastingFloorWallHeight - 0.3f, 36f), 45f);
            Vector3 mouth = FurnaceMouthWorld();
            GameObject scorch = MagicalObjectSetup.Part(PrimitiveType.Cylinder, "FurnaceScorch", parent, new Vector3(mouth.x, 0.012f, mouth.z),
                new Vector3(FoundryDefaults.FurnaceHeatRadius * 2f, 0.01f, FoundryDefaults.FurnaceHeatRadius * 2f), foundry.Charred);
            scorch.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Exit court: the pulley over the gate and the chain that runs down to the counterweight's stop,
            // and the hoist beam over the gantry.
            FantasyPlaygroundSetup.Place(foundry.GearWheel, parent, new Vector3(-1.6f, 6.4f, FoundryLayout.ExitCourtZ.y - 0.15f), 0f);
            Strip(parent, "CounterweightChain", foundry.Metal, new Vector3(-1.6f, 6.4f, FoundryLayout.ExitCourtZ.y - 0.2f),
                new Vector3(-3.2f, 1.2f, FoundryLayout.ExitCourtZ.y - 0.3f), 0.08f, 0.08f);
            Block(parent, "HoistBeam", foundry.Wood, new Vector3(FoundryLayout.Gantry.Position.x, FoundryLayout.HighWallHeight - 0.4f,
                (FoundryLayout.Gantry.Position.z + FoundryLayout.ExitCourtZ.y) * 0.5f), new Vector3(0.4f, 0.4f, FoundryLayout.GantryRun + 1f));

        }

        private static GameObject PropPrefab(EnvironmentAssets environment, BrokenFoundryAssets foundry, FoundryProp kind)
        {
            switch (kind)
            {
                case FoundryProp.OreCart: return foundry.OreCart;
                case FoundryProp.Crucible: return foundry.Crucible;
                case FoundryProp.BrokenColumn: return environment.ColumnBroken;
                default: return environment.Rubble;
            }
        }

        /// <summary>Ground and trees beyond the walls, seen over them and through the way out.</summary>
        private static void BuildSurroundings(EnvironmentAssets environment, Transform parent)
        {
            Mesh ground = environment.EnsureBlockMesh(new Vector3(320f, 0.5f, 320f));
            FantasyPlaygroundSetup.CreateMeshObject(parent, "OuterGround", ground, environment.GroundMaterial, new Vector3(0f, -1.1f, 15f), false);

            var random = new Random(7071);
            int planted = 0;
            for (int attempt = 0; attempt < 400 && planted < 46; attempt++)
            {
                float x = EnvironmentAssets.Range(random, -60f, 60f);
                float z = EnvironmentAssets.Range(random, -70f, 110f);
                bool outside = Mathf.Abs(x) > 16f || z < -42f || z > 76f;
                if (!outside)
                {
                    continue;
                }

                GameObject tree = environment.Trees[random.Next(environment.Trees.Length)];
                FantasyPlaygroundSetup.Place(tree, parent, new Vector3(x, -0.6f, z), EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 1.2f, 1.9f));
                planted++;
            }
        }

        /// <summary>The slag in the pit and its glow: one unshadowed light, the level's warmest note.</summary>
        private static void BuildAtmosphere(BrokenFoundryAssets foundry, Transform parent)
        {
            float width = FoundryLayout.OreHallX.y - FoundryLayout.OreHallX.x;
            float span = FoundryLayout.PitZ.y - FoundryLayout.PitZ.x;
            float pitZ = (FoundryLayout.PitZ.x + FoundryLayout.PitZ.y) * 0.5f;
            GameObject slag = MagicalObjectSetup.Part(PrimitiveType.Cube, "MoltenSlag", parent, new Vector3(0f, -1.6f, pitZ),
                new Vector3(width, 0.05f, span), foundry.Slag);
            slag.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var glow = new GameObject("SlagGlow");
            glow.transform.SetParent(parent, false);
            glow.transform.position = new Vector3(0f, 0.4f, pitZ);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.22f);
            light.range = 12f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
        }

        private static GameObject Block(Transform parent, string name, Material material, Vector3 baseCentre, Vector3 size)
        {
            GameObject part = MagicalObjectSetup.Part(PrimitiveType.Cube, name, parent, baseCentre + Vector3.up * (size.y * 0.5f), size, material);
            part.isStatic = true;
            return part;
        }

        /// <summary>A thin straight piece from one point to another, such as a pipe, a rail or a chain. It does not collide.</summary>
        private static void Strip(Transform parent, string name, Material material, Vector3 from, Vector3 to, float width, float height)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 0.01f)
            {
                return;
            }

            GameObject part = MagicalObjectSetup.Part(PrimitiveType.Cube, name, parent, (from + to) * 0.5f, new Vector3(width, height, length), material);
            part.transform.rotation = Quaternion.LookRotation(along / length, Mathf.Abs(Vector3.Dot(along / length, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up);
            part.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            part.isStatic = true;
        }

        private static Vector3 FurnaceMouthWorld()
        {
            LayoutPoint furnace = FoundryLayout.Furnace;
            return furnace.Position + Quaternion.Euler(0f, furnace.Yaw, 0f) * FoundryLayout.FurnaceMouthLocal;
        }

        // ------------------------------------------------------------ gameplay

        private static Gameplay EnsureGameplay(Scene scene, BrokenFoundryAssets foundry, MagicalObjectSetup.ExampleAssets examples,
            MagicalObjectSetup.CombinationAssets combinations, EnemyAssets enemyAssets, StringBuilder report)
        {
            var gameplay = new Gameplay { Root = FindByName(scene, GameplayName) };
            if (gameplay.Root == null)
            {
                gameplay.Root = new GameObject(GameplayName);
                SceneManager.MoveGameObjectToScene(gameplay.Root, scene);
                Undo.RegisterCreatedObjectUndo(gameplay.Root, DialogTitle);
            }

            Transform root = gameplay.Root.transform;
            int placed = 0;
            GameObject slide = combinations.Prefabs[(int)CombinationExample.RuneLockedSlide];
            GameObject scaffold = examples.Prefabs[(int)ExampleObject.UnstableStructure];

            gameplay.Crystal = Magical(EnsureInstance(foundry.DormantCrystal, root, CrystalName, FoundryLayout.DormantCrystal, ref placed));
            gameplay.ForecourtGate = Door(EnsureInstance(foundry.Gate, root, ForecourtGateName, FoundryLayout.ForecourtGate, ref placed));
            gameplay.Barricade = Magical(EnsureInstance(foundry.DamagedBarricade, root, BarricadeName, FoundryLayout.DamagedBarricade, ref placed));
            gameplay.Bridge = Magical(EnsureInstance(foundry.SlagBridge, root, BridgeName, FoundryLayout.Bridge, ref placed));
            gameplay.Lift = Magical(EnsureInstance(slide, root, LiftName, FoundryLayout.RuneLift, ref placed));
            gameplay.Furnace = Magical(EnsureInstance(foundry.Furnace, root, FurnaceName, FoundryLayout.Furnace, ref placed));
            for (int i = 0; i < FoundryLayout.Scaffolds.Length; i++)
            {
                MagicalObject loose = Magical(EnsureInstance(scaffold, root, $"{ScaffoldName} {i + 1}", FoundryLayout.Scaffolds[i], ref placed));
                if (loose != null)
                {
                    gameplay.Scaffolds.Add(loose);
                }
            }

            gameplay.GuardianGate = Door(EnsureInstance(foundry.Gate, root, GuardianGateName, FoundryLayout.GuardianGate, ref placed));
            gameplay.Counterweight = Magical(EnsureInstance(slide, root, CounterweightName, FoundryLayout.Counterweight, ref placed));
            gameplay.Plug = Magical(EnsureInstance(foundry.SlagPlug, root, PlugName, FoundryLayout.SlagPlug, ref placed));
            gameplay.Gantry = Magical(EnsureInstance(foundry.Gantry, root, GantryName, FoundryLayout.Gantry, ref placed));
            gameplay.ExitGate = Door(EnsureInstance(foundry.Gate, root, ExitGateName, FoundryLayout.ExitGate, ref placed));

            gameplay.Hazard = EnsureHazard(root, ref placed);
            EnsureGuardian(root, enemyAssets, gameplay, ref placed);
            EnsureCheckpoints(root, gameplay, ref placed);

            gameplay.Route = EnsureChildComponent<CheckpointRoute>(root, RouteName, ref placed);
            gameplay.Progress = EnsureChildComponent<FoundryProgressTracker>(root, ProgressName, ref placed);
            gameplay.Diagnostics = EnsureChildComponent<DevDiagnosticsOverlay>(root, DiagnosticsName, ref placed);
            if (gameplay.Root.GetComponent<WorldSimulationBudgets>() == null)
            {
                Undo.AddComponent<WorldSimulationBudgets>(gameplay.Root);
                report.AppendLine($"Added the world simulation budgets to '{GameplayName}' (provisional values).");
            }

            Connect(scene, gameplay, report);
            report.AppendLine(placed > 0
                ? $"Placed {placed} gameplay object(s) in '{GameplayName}'."
                : $"Kept the gameplay objects in '{GameplayName}'.");
            return gameplay;
        }

        /// <summary>Connects what is not connected yet: the gates' inputs, the saved identities and keys. Nothing set is changed.</summary>
        private static void Connect(Scene scene, Gameplay gameplay, StringBuilder report)
        {
            ConnectGate(gameplay.ForecourtGate, Trigger(gameplay.Crystal), ForecourtGateKey, "the foundry gate rises when the dormant crystal wakes",
                report);
            ConnectGate(gameplay.ExitGate, Trigger(gameplay.Counterweight), ExitGateKey,
                "the exit gate rises when the counterweight reaches its far stop", report);

            if (gameplay.GuardianGate != null && gameplay.Outcome != null && gameplay.GuardianGate.InputElement == null)
            {
                Undo.RecordObject(gameplay.GuardianGate, DialogTitle);
                gameplay.GuardianGate.InputElement = gameplay.Outcome;
                MarkChanged(gameplay.GuardianGate);
                report.AppendLine("Connected the casting floor gate: it opens when the Ruins Guardian is defeated, for good.");
            }

            // What lasts, under identities of its own. The bridge, the gantry, the furnace and the scaffolds
            // keep nothing, so they come back as new on a restart.
            int identified = 0;
            foreach (MagicalObject magical in new[] { gameplay.Crystal, gameplay.Barricade, gameplay.Lift, gameplay.Plug, gameplay.Counterweight })
            {
                if (magical != null && string.IsNullOrEmpty(magical.PersistentId) && magical.Definition != null &&
                    magical.Definition.Properties.persisted != PersistedState.None)
                {
                    Undo.RecordObject(magical, DialogTitle);
                    magical.FillPersistentId(MagicalObjectSetup.MakePersistentId(scene, magical.name));
                    MarkChanged(magical);
                    identified++;
                }
            }

            if (identified > 0)
            {
                report.AppendLine($"Gave {identified} magical object(s) a persistent id: the crystal's waking, the barricade's " +
                                  "destruction, the lift's and the counterweight's position and the plug's destruction are saved.");
            }
        }

        private static void ConnectGate(PuzzleDoor door, PropertyStateTrigger trigger, string key, string what, StringBuilder report)
        {
            if (door == null || trigger == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(trigger.PersistenceKey))
            {
                Undo.RecordObject(trigger, DialogTitle);
                trigger.PersistenceKey = key;
                MarkChanged(trigger);
            }

            if (door.InputElement == null)
            {
                Undo.RecordObject(door, DialogTitle);
                door.InputElement = trigger;
                MarkChanged(door);
                report.AppendLine($"Connected the {door.name}: {what}.");
            }
        }

        private static HazardVolume EnsureHazard(Transform root, ref int placed)
        {
            Transform existing = root.Find(HazardName);
            if (existing != null)
            {
                return existing.GetComponent<HazardVolume>();
            }

            var host = new GameObject(HazardName);
            host.transform.SetParent(root, false);
            host.transform.position = FoundryLayout.SlagHazard.BaseCentre;
            HazardVolume hazard = host.AddComponent<HazardVolume>();
            hazard.Configure(FoundryLayout.SlagHazard.Size, FoundryLayout.SlagDamage);
            Undo.RegisterCreatedObjectUndo(host, DialogTitle);
            placed++;
            return hazard;
        }

        private static void EnsureGuardian(Transform root, EnemyAssets enemyAssets, Gameplay gameplay, ref int placed)
        {
            Transform encounterTransform = root.Find(EncounterName);
            if (encounterTransform == null)
            {
                var encounterObject = new GameObject(EncounterName);
                encounterObject.transform.SetParent(root, false);
                encounterObject.AddComponent<EnemyEncounter>();
                Undo.RegisterCreatedObjectUndo(encounterObject, DialogTitle);
                encounterTransform = encounterObject.transform;
                placed++;
            }

            EnemyEncounter encounter = encounterTransform.GetComponent<EnemyEncounter>();
            Transform guardianTransform = encounterTransform.Find(GuardianName);
            if (guardianTransform == null && enemyAssets != null && enemyAssets.GuardianPrefab != null)
            {
                var guardian = (GameObject)PrefabUtility.InstantiatePrefab(enemyAssets.GuardianPrefab, encounterTransform);
                guardian.name = GuardianName;
                guardian.transform.SetPositionAndRotation(FoundryLayout.Guardian.Position, Quaternion.Euler(0f, FoundryLayout.Guardian.Yaw, 0f));
                Undo.RegisterCreatedObjectUndo(guardian, DialogTitle);

                var routeObject = new GameObject(GuardianName + " Route");
                routeObject.transform.SetParent(encounterTransform, false);
                PatrolRoute route = routeObject.AddComponent<PatrolRoute>();
                var points = new Transform[FoundryLayout.GuardianRoute.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    var point = new GameObject("Point " + (i + 1));
                    point.transform.SetParent(routeObject.transform, false);
                    point.transform.position = FoundryLayout.GuardianRoute[i];
                    points[i] = point.transform;
                }

                route.SetPoints(points);
                EnemyController created = guardian.GetComponent<EnemyController>();
                created.BindScene(null, route);
                MarkChanged(created);
                guardianTransform = guardian.transform;
                placed++;
            }

            gameplay.Guardian = guardianTransform != null ? guardianTransform.GetComponent<EnemyController>() : null;
            if (encounter != null && gameplay.Guardian != null && encounter.Members.Count == 0)
            {
                Undo.RecordObject(encounter, DialogTitle);
                encounter.Configure(EncounterName, new[] { gameplay.Guardian });
                MarkChanged(encounter);
            }

            Transform outcomeTransform = root.Find(OutcomeName);
            if (outcomeTransform == null)
            {
                var outcomeObject = new GameObject(OutcomeName);
                outcomeObject.transform.SetParent(root, false);
                outcomeObject.AddComponent<PersistentDefeat>();
                Undo.RegisterCreatedObjectUndo(outcomeObject, DialogTitle);
                outcomeTransform = outcomeObject.transform;
                placed++;
            }

            gameplay.Outcome = outcomeTransform.GetComponent<PersistentDefeat>();
            if (gameplay.Outcome != null)
            {
                Undo.RecordObject(gameplay.Outcome, DialogTitle);
                if (gameplay.Outcome.Enemy == null && gameplay.Guardian != null)
                {
                    gameplay.Outcome.Bind(gameplay.Guardian);
                }

                if (string.IsNullOrEmpty(gameplay.Outcome.PersistenceKey))
                {
                    gameplay.Outcome.PersistenceKey = GuardianKey;
                }

                MarkChanged(gameplay.Outcome);
            }
        }

        private static void EnsureCheckpoints(Transform root, Gameplay gameplay, ref int placed)
        {
            Transform group = root.Find(CheckpointsName);
            if (group == null)
            {
                group = new GameObject(CheckpointsName).transform;
                group.SetParent(root, false);
                Undo.RegisterCreatedObjectUndo(group.gameObject, DialogTitle);
            }

            foreach (CheckpointLayout layout in FoundryLayout.Checkpoints)
            {
                string name = $"Checkpoint {layout.Order + 1} - {layout.Name}";
                Transform existing = group.Find(name);
                if (existing == null)
                {
                    var host = new GameObject(name);
                    host.transform.SetParent(group, false);
                    host.transform.position = layout.Zone.BaseCentre;

                    var respawn = new GameObject("Respawn");
                    respawn.transform.SetParent(host.transform, false);
                    respawn.transform.SetPositionAndRotation(layout.Respawn.Position, Quaternion.Euler(0f, layout.Respawn.Yaw, 0f));

                    Checkpoint created = host.AddComponent<Checkpoint>();
                    created.Configure(layout.Order, layout.Zone.Size, respawn.transform);
                    created.PersistenceKey = CheckpointProgress.KeyFor(FoundryLayout.KeyPrefix, layout.Order);
                    Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                    existing = host.transform;
                    placed++;
                }

                Checkpoint checkpoint = existing.GetComponent<Checkpoint>();
                if (checkpoint != null)
                {
                    gameplay.Checkpoints.Add(checkpoint);
                }
            }
        }

        /// <summary>Connects the parts that need the Mage: the checkpoint route, the replay of the level, the progress and the diagnostics.</summary>
        private static void WireFlow(Scene scene, GameObject mage, Gameplay gameplay, StringBuilder report)
        {
            PrototypeSession session = FindInScene<PrototypeSession>(scene);
            Health health = mage.GetComponent<Health>();

            if (gameplay.Route != null && NeedsBinding(gameplay.Route, gameplay))
            {
                Undo.RecordObject(gameplay.Route, DialogTitle);
                gameplay.Route.Bind(session, mage.transform, health, gameplay.Checkpoints.ToArray(),
                    gameplay.Hazard != null ? new[] { gameplay.Hazard } : new HazardVolume[0]);
                MarkChanged(gameplay.Route);
                report.AppendLine($"Connected the checkpoint route: {gameplay.Checkpoints.Count} checkpoints and the slag pit.");
            }

            if (session != null && string.IsNullOrEmpty(session.ReplayScope))
            {
                Undo.RecordObject(session, DialogTitle);
                session.ReplayScope = FoundryLayout.KeyPrefix;
                MarkChanged(session);
                report.AppendLine("After the way out is reached, a restart replays the foundry from its start (only its own saved state is forgotten).");
            }

            if (gameplay.Progress != null)
            {
                Undo.RecordObject(gameplay.Progress, DialogTitle);
                gameplay.Progress.BindMissing(gameplay.Crystal, gameplay.Barricade, gameplay.Lift, gameplay.Outcome, gameplay.Plug,
                    gameplay.Counterweight, gameplay.Route);
                MarkChanged(gameplay.Progress);
            }

            if (gameplay.Diagnostics != null)
            {
                ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
                Undo.RecordObject(gameplay.Diagnostics, DialogTitle);
                gameplay.Diagnostics.BindMissing(mage.GetComponent<PlayerDisciplines>(), rig != null ? rig.transform : null, session,
                    gameplay.Progress != null ? new MonoBehaviour[] { gameplay.Progress } : new MonoBehaviour[0]);
                MarkChanged(gameplay.Diagnostics);
            }
        }

        private static bool NeedsBinding(CheckpointRoute route, Gameplay gameplay)
        {
            if (route.Session == null || route.Player == null || route.CheckpointCount != gameplay.Checkpoints.Count)
            {
                return true;
            }

            for (int i = 0; i < route.CheckpointCount; i++)
            {
                if (route.GetCheckpoint(i) == null)
                {
                    return true;
                }
            }

            for (int i = 0; i < route.HazardCount; i++)
            {
                if (route.GetHazard(i) == null)
                {
                    return true;
                }
            }

            return route.HazardCount == 0 && gameplay.Hazard != null;
        }

        private static GameObject EnsureInstance(GameObject prefab, Transform parent, string name, LayoutPoint point, ref int placed)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.SetPositionAndRotation(point.Position, Quaternion.Euler(0f, point.Yaw, 0f));
            Undo.RegisterCreatedObjectUndo(instance, DialogTitle);
            placed++;
            return instance;
        }

        private static T EnsureChildComponent<T>(Transform parent, string name, ref int placed) where T : Component
        {
            Transform existing = parent.Find(name);
            if (existing == null)
            {
                var host = new GameObject(name);
                host.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                existing = host.transform;
                placed++;
            }

            T component = existing.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(existing.gameObject);
        }

        private static MagicalObject Magical(GameObject instance) => instance != null ? instance.GetComponent<MagicalObject>() : null;
        private static PuzzleDoor Door(GameObject instance) => instance != null ? instance.GetComponent<PuzzleDoor>() : null;

        private static PropertyStateTrigger Trigger(MagicalObject magical) =>
            magical != null ? magical.GetComponent<PropertyStateTrigger>() : null;

        // ------------------------------------------------------------ navigation

        /// <summary>
        /// Bakes the casting floor's walkable area for the Guardian: from the architecture (floor, walls,
        /// columns) and the furnace and scaffolds as they stand, within the casting floor only, so the
        /// Guardian never follows the player up the stair or through a gate. The asset is updated in place.
        /// </summary>
        private static void BakeNavMesh(Transform architecture, Gameplay gameplay, StringBuilder report)
        {
            NavMeshSurface surface = architecture.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            Vector2 x = FoundryLayout.CastingFloorX;
            Vector2 z = FoundryLayout.CastingFloorZ;
            var bounds = new Bounds(new Vector3((x.x + x.y) * 0.5f, 3f, (z.x + z.y) * 0.5f), new Vector3(x.y - x.x, 8f, z.y - z.x));
            surface.center = bounds.center;
            surface.size = bounds.size;

            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            var roots = new List<Transform> { architecture };
            if (gameplay.Furnace != null) roots.Add(gameplay.Furnace.transform);
            foreach (MagicalObject scaffold in gameplay.Scaffolds)
            {
                roots.Add(scaffold.transform);
            }

            foreach (Transform source in roots)
            {
                var found = new List<NavMeshBuildSource>();
                UnityEngine.AI.NavMeshBuilder.CollectSources(source, surface.layerMask, NavMeshCollectGeometry.PhysicsColliders, surface.defaultArea,
                    markups, found);
                sources.AddRange(found);
            }

            NavMeshBuildSettings settings = surface.GetBuildSettings();
            CreateFolders(Path.GetDirectoryName(NavMeshPath).Replace('\\', '/'));
            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            bool created = data == null;
            if (created)
            {
                data = new NavMeshData(settings.agentTypeID) { name = Path.GetFileNameWithoutExtension(NavMeshPath) };
            }

            bool built = UnityEngine.AI.NavMeshBuilder.UpdateNavMeshData(data, settings, sources, bounds);
            if (created)
            {
                AssetDatabase.CreateAsset(data, NavMeshPath);
            }
            else
            {
                EditorUtility.SetDirty(data);
            }

            surface.navMeshData = data;
            surface.RemoveData();
            surface.AddData();

            report.AppendLine(built
                ? $"Baked the casting floor's navigation mesh from {sources.Count} collider(s) into '{NavMeshPath}'."
                : "WARNING: the navigation mesh could not be built. The Guardian will not move.");
        }

        private static void ReportNavigation(Gameplay gameplay, StringBuilder report)
        {
            if (gameplay.Guardian == null)
            {
                report.AppendLine("WARNING: there is no Ruins Guardian on the casting floor.");
                return;
            }

            bool onMesh = NavMesh.SamplePosition(gameplay.Guardian.transform.position, out NavMeshHit _, 1f, NavMesh.AllAreas);
            report.AppendLine(onMesh
                ? "Navigation: the Ruins Guardian stands on the casting floor's navigation mesh."
                : "WARNING: the Ruins Guardian is not on the navigation mesh and will not move.");
        }

        // ------------------------------------------------------------ checks

        /// <summary>What the level itself needs, beyond the general scene validation. Added to the report.</summary>
        internal static int CheckLevel(Gameplay gameplay, System.Action<string> problem)
        {
            int count = 0;
            void Problem(string text)
            {
                count++;
                problem(text);
            }

            if (gameplay.Crystal == null) Problem($"'{CrystalName}' is missing: the foundry gate can never open.");
            if (gameplay.Barricade == null) Problem($"'{BarricadeName}' is missing.");
            if (gameplay.Bridge == null) Problem($"'{BridgeName}' is missing: the slag pit cannot be crossed.");
            if (gameplay.Lift == null) Problem($"'{LiftName}' is missing: the lift hall's ledge cannot be reached.");
            if (gameplay.Guardian == null) Problem($"'{GuardianName}' is missing.");
            if (gameplay.Counterweight == null && gameplay.Plug == null && gameplay.Gantry == null)
            {
                Problem("The exit court has no way through at all.");
            }

            if (gameplay.ForecourtGate != null && gameplay.ForecourtGate.InputElement == null) Problem("The foundry gate has no input.");
            if (gameplay.GuardianGate != null && gameplay.GuardianGate.InputElement == null) Problem("The casting floor gate has no input.");
            if (gameplay.ExitGate != null && gameplay.ExitGate.InputElement == null) Problem("The exit gate has no input.");
            if (gameplay.Checkpoints.Count != FoundryLayout.Checkpoints.Length)
            {
                Problem($"There are {gameplay.Checkpoints.Count} checkpoints; the layout has {FoundryLayout.Checkpoints.Length}.");
            }

            foreach (MagicalObject magical in new[] { gameplay.Crystal, gameplay.Barricade, gameplay.Lift, gameplay.Plug, gameplay.Counterweight })
            {
                if (magical != null && string.IsNullOrEmpty(magical.PersistentId))
                {
                    Problem($"'{magical.name}' has no persistent id, so what is done to it is forgotten on a reload.");
                }
            }

            foreach (MagicalObject magical in new[] { gameplay.Bridge, gameplay.Gantry })
            {
                if (magical != null && magical.Definition != null && magical.Definition.Properties.stabilitySettings.recoverSeconds <= 0f)
                {
                    Problem($"'{magical.name}' does not stand again after it gives way, so a failed crossing needs a restart.");
                }
            }

            if (gameplay.Route == null || gameplay.Route.Session == null) Problem("The checkpoint route is not connected to the session.");
            return count;
        }

        private static void AppendLevelChecks(Gameplay gameplay, StringBuilder report)
        {
            var problems = new StringBuilder();
            int count = CheckLevel(gameplay, text => problems.AppendLine("- " + text));
            report.AppendLine(count == 0
                ? "Level checks: no problems found."
                : $"Level checks: {count} problem(s) found:\n{problems.ToString().TrimEnd()}");
        }

        /// <summary>Finds the level's gameplay objects in an assembled scene, for tests.</summary>
        internal static Gameplay Find(Scene scene)
        {
            var gameplay = new Gameplay { Root = FindByName(scene, GameplayName) };
            if (gameplay.Root == null)
            {
                return gameplay;
            }

            Transform root = gameplay.Root.transform;
            gameplay.Crystal = Magical(FindChild(root, CrystalName));
            gameplay.ForecourtGate = Door(FindChild(root, ForecourtGateName));
            gameplay.Barricade = Magical(FindChild(root, BarricadeName));
            gameplay.Bridge = Magical(FindChild(root, BridgeName));
            gameplay.Lift = Magical(FindChild(root, LiftName));
            gameplay.Furnace = Magical(FindChild(root, FurnaceName));
            for (int i = 0; i < FoundryLayout.Scaffolds.Length; i++)
            {
                MagicalObject scaffold = Magical(FindChild(root, $"{ScaffoldName} {i + 1}"));
                if (scaffold != null)
                {
                    gameplay.Scaffolds.Add(scaffold);
                }
            }

            GameObject guardian = FindChild(root.Find(EncounterName), GuardianName);
            gameplay.Guardian = guardian != null ? guardian.GetComponent<EnemyController>() : null;
            gameplay.Outcome = Component<PersistentDefeat>(FindChild(root, OutcomeName));
            gameplay.GuardianGate = Door(FindChild(root, GuardianGateName));
            gameplay.Counterweight = Magical(FindChild(root, CounterweightName));
            gameplay.Plug = Magical(FindChild(root, PlugName));
            gameplay.Gantry = Magical(FindChild(root, GantryName));
            gameplay.ExitGate = Door(FindChild(root, ExitGateName));
            gameplay.Hazard = Component<HazardVolume>(FindChild(root, HazardName));
            gameplay.Route = Component<CheckpointRoute>(FindChild(root, RouteName));
            gameplay.Progress = Component<FoundryProgressTracker>(FindChild(root, ProgressName));
            gameplay.Diagnostics = Component<DevDiagnosticsOverlay>(FindChild(root, DiagnosticsName));
            Transform checkpoints = root.Find(CheckpointsName);
            if (checkpoints != null)
            {
                gameplay.Checkpoints.AddRange(checkpoints.GetComponentsInChildren<Checkpoint>(true));
            }

            return gameplay;
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            Transform child = parent != null ? parent.Find(name) : null;
            return child != null ? child.gameObject : null;
        }

        private static T Component<T>(GameObject host) where T : Component => host != null ? host.GetComponent<T>() : null;
    }
}
