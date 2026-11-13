using System.Collections.Generic;
using System.IO;
using System.Text;
using Threefold.Cameras;
using Threefold.Combat;
using Threefold.Enemies;
using Threefold.Objectives;
using Threefold.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;
using Random = System.Random;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the combat arena: a walled ruin of three courts joined by arched gates, each holding
    /// a different combination of enemies, with the Mage at the south end. One menu command
    /// creates the scene, the environment, the enemies and their encounters, the director, the
    /// objective, the portal back to the Fantasy Playground, and the navigation mesh.
    ///
    ///   Outer Court: one Ruins Guardian on patrol. Learn its telegraph and its recovery.
    ///   Colonnade:   two Arcane Wisps among columns. Learn to dodge and to use cover.
    ///   Sanctum:     two Guardians and two Wisps on raised platforms. Everything at once,
    ///                kept readable by the director's attack tokens.
    ///
    /// Running it again rebuilds the generated environment and its navigation mesh. The enemies,
    /// the director, the Mage, the HUD and anything added by hand are kept; delete the
    /// 'ArenaEnemies' object to get the default enemy layout back.
    /// </summary>
    public static class CombatArenaSetup
    {
        private const string MenuPath = "Threefold/Setup/Combat Arena";
        private const string DialogTitle = "Combat Arena";
        internal const string SceneName = "CombatArena";
        internal const string ScenePath = "Assets/Scenes/" + SceneName + ".unity";
        private const string GeneratorName = "CombatArena";
        private const string RootName = "CombatArena Environment";
        internal const string EnemiesName = "ArenaEnemies";
        private const string DirectorName = "EnemyDirector";
        internal const string NavMeshPath = "Assets/Threefold/Generated/Navigation/CombatArenaNavMesh.asset";

        private const float HalfWidth = 18f;
        private const float HalfLength = 30f;
        private const float WallHeight = 4.5f;
        private const float GateZ = 10f;
        private const float PlatformHeight = 1.6f;

        internal static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -25.5f);
        private static readonly Vector3 PortalPosition = new Vector3(0f, 0f, -29.2f);

        // Covers the floor and the platforms, but not the tops of walls and columns.
        private static readonly Vector3 NavBoundsCentre = new Vector3(0f, 1.5f, 0f);
        private static readonly Vector3 NavBoundsSize = new Vector3(HalfWidth * 2f + 4f, 5f, HalfLength * 2f + 4f);

        private sealed class Placement
        {
            public string Name;
            public bool Wisp;
            public Vector3 Position;
            public float Yaw;
            public Vector3[] Route;
        }

        private sealed class Encounter
        {
            public string Name;
            public Placement[] Enemies;
        }

        /// <summary>The enemy layout. Positions are on the floor or on a platform top.</summary>
        private static readonly Encounter[] Encounters =
        {
            new Encounter
            {
                Name = "Encounter 1 - Outer Court",
                Enemies = new[]
                {
                    new Placement
                    {
                        Name = "Ruins Guardian A", Position = new Vector3(-9f, 0f, -14f), Yaw = 90f,
                        Route = new[] { new Vector3(-9f, 0f, -14f), new Vector3(9f, 0f, -14f) },
                    },
                },
            },
            new Encounter
            {
                Name = "Encounter 2 - Colonnade",
                Enemies = new[]
                {
                    new Placement { Name = "Arcane Wisp B1", Wisp = true, Position = new Vector3(-9f, 0f, 5f), Yaw = 160f },
                    new Placement
                    {
                        Name = "Arcane Wisp B2", Wisp = true, Position = new Vector3(9f, 0f, -6f), Yaw = 0f,
                        Route = new[] { new Vector3(9f, 0f, -6f), new Vector3(9f, 0f, 6f) },
                    },
                },
            },
            new Encounter
            {
                Name = "Encounter 3 - Sanctum",
                Enemies = new[]
                {
                    new Placement { Name = "Ruins Guardian C1", Position = new Vector3(0f, 0f, 27.5f), Yaw = 180f },
                    new Placement
                    {
                        Name = "Ruins Guardian C2", Position = new Vector3(-5.5f, 0f, 16f), Yaw = 0f,
                        Route = new[]
                        {
                            new Vector3(-5.5f, 0f, 16f), new Vector3(-5.5f, 0f, 26f),
                            new Vector3(5.5f, 0f, 26f), new Vector3(5.5f, 0f, 16f),
                        },
                    },
                    new Placement { Name = "Arcane Wisp C3", Wisp = true, Position = new Vector3(-14f, PlatformHeight, 21f), Yaw = 90f },
                    new Placement { Name = "Arcane Wisp C4", Wisp = true, Position = new Vector3(14f, PlatformHeight, 21f), Yaw = -90f },
                },
            },
        };

        [MenuItem(MenuPath, false, 5)]
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
                    "This scene already contains the generated arena.\n\n" +
                    "Rebuild it? Only the generated environment and its navigation mesh are replaced. The enemies, " +
                    "the director, the Mage, the HUD and anything you added yourself are kept.", "Rebuild", "Cancel"))
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
                "Press Play to fight.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// Builds the complete arena without asking anything, for automated builds and tests. It
        /// opens the scene without saving whatever was open before, so it must only be called
        /// where nobody has unsaved work.
        /// </summary>
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
            EnvironmentAssets assets = EnvironmentAssets.Ensure(false, report);
            EnemyAssets enemyAssets = EnemyAssets.Ensure(report);

            GameObject oldRoot = FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName);
            if (oldRoot != null)
            {
                Undo.DestroyObjectImmediate(oldRoot);
                report.AppendLine("Removed the previous generated arena.");
            }

            var root = new GameObject(RootName);
            root.AddComponent<GeneratedEnvironment>().Generator = GeneratorName;
            Undo.RegisterCreatedObjectUndo(root, DialogTitle);

            Transform architecture = FantasyPlaygroundSetup.CreateGroup(root.transform, "Architecture");
            Transform nature = FantasyPlaygroundSetup.CreateGroup(root.transform, "Nature");
            Transform background = FantasyPlaygroundSetup.CreateGroup(root.transform, "Background");
            Transform lighting = FantasyPlaygroundSetup.CreateGroup(root.transform, "Lighting");
            Transform gameplay = FantasyPlaygroundSetup.CreateGroup(root.transform, "Gameplay");

            BuildShell(assets, architecture);
            BuildOuterCourt(assets, architecture);
            BuildColonnade(assets, architecture);
            BuildSanctum(assets, architecture);
            BuildSurroundings(assets, nature);
            FantasyPlaygroundSetup.BuildBackground(assets, background);
            FantasyPlaygroundSetup.BuildLighting(assets, lighting, report);
            FantasyPlaygroundSetup.Place(assets.Arch, architecture, PortalPosition, 0f);
            ScenePortal portal = FantasyPlaygroundSetup.BuildPortal(gameplay, "PlaygroundPortal", PortalPosition, 180f);
            report.AppendLine("Built the arena: outer court, colonnade, sanctum, walls, gates and lighting.");

            BakeNavMesh(architecture, report);

            List<EnemyController> enemies = EnsureEnemies(scene, enemyAssets, report);
            var targets = new Health[enemies.Count];
            for (int i = 0; i < enemies.Count; i++)
            {
                targets[i] = enemies[i].Health;
            }

            var objective = new ObjectiveSetup.ObjectiveOptions
            {
                Title = "Clear the arena",
                CompletionHeading = "Arena cleared",
                Targets = targets,
            };

            GameObject mage = MagePrototypeSetup.EnsureCharacterRig(scene, SpawnPosition, Quaternion.identity,
                new CombatSetup.DummyPlacement[0], report, objective);

            EnemyDirector director = EnsureDirector(scene, mage, enemyAssets, report);
            foreach (EnemyController enemy in enemies)
            {
                Undo.RecordObject(enemy, DialogTitle);
                enemy.BindDirectorIfEmpty(director);
                MarkChanged(enemy);
            }

            string playground = Path.GetFileNameWithoutExtension(FantasyPlaygroundSetup.ScenePath);
            portal.Bind(playground, mage.transform, portal.transform.GetChild(0).gameObject);

            FantasyPlaygroundSetup.EnablePostProcessing(scene, report);
            FantasyPlaygroundSetup.AddToBuildSettings(ScenePath, false, report);
            ReportNavigation(enemies, report);
            MagePrototypeSetup.AppendValidation(scene, report);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return mage;
        }

        // ------------------------------------------------------------- layout

        /// <summary>
        /// The floor, a perimeter wall 4.5 m high (too high to climb: the Mage catches ledges up to
        /// about 3.75 m), and two dividing walls with an arched gate in the middle of each.
        /// </summary>
        private static void BuildShell(EnvironmentAssets assets, Transform parent)
        {
            Material stone = assets.StoneMaterial;
            float width = HalfWidth * 2f;
            float length = HalfLength * 2f;

            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "ArenaFloor", new Vector3(width, 0.6f, length),
                new Vector3(0f, -0.6f, 0f), assets.PavingMaterial);

            var wallLong = new Vector3(1f, WallHeight, length);
            var wallShort = new Vector3(width + 2f, WallHeight, 1f);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "WallNorth", wallShort, new Vector3(0f, 0f, HalfLength + 0.5f), stone);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "WallSouth", wallShort, new Vector3(0f, 0f, -HalfLength - 0.5f), stone);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "WallEast", wallLong, new Vector3(HalfWidth + 0.5f, 0f, 0f), stone);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "WallWest", wallLong, new Vector3(-HalfWidth - 0.5f, 0f, 0f), stone);

            // Each divider leaves a 6 m gap that the arch fills, with its 4 m opening.
            var divider = new Vector3(HalfWidth - 3f, WallHeight, 1f);
            float dividerX = 3f + divider.x * 0.5f;
            foreach (float z in new[] { -GateZ, GateZ })
            {
                FantasyPlaygroundSetup.PlaceBlock(assets, parent, "DividerWest", divider, new Vector3(-dividerX, 0f, z), stone);
                FantasyPlaygroundSetup.PlaceBlock(assets, parent, "DividerEast", divider, new Vector3(dividerX, 0f, z), stone);
                FantasyPlaygroundSetup.Place(assets.Arch, parent, new Vector3(0f, 0f, z), 0f);
            }

            // Buttress columns give the long walls a rhythm.
            foreach (float z in new[] { -20f, 0f })
            {
                FantasyPlaygroundSetup.Place(assets.Column, parent, new Vector3(-HalfWidth + 0.9f, 0f, z), 0f);
                FantasyPlaygroundSetup.Place(assets.Column, parent, new Vector3(HalfWidth - 0.9f, 0f, z), 0f);
            }
        }

        /// <summary>Open ground with a little cover, so the first Guardian is met in plain view.</summary>
        private static void BuildOuterCourt(EnvironmentAssets assets, Transform parent)
        {
            FantasyPlaygroundSetup.Place(assets.ColumnBroken, parent, new Vector3(-6f, 0f, -19f), 20f);
            FantasyPlaygroundSetup.Place(assets.ColumnBroken, parent, new Vector3(6.5f, 0f, -17.5f), 140f);
            FantasyPlaygroundSetup.Place(assets.WallLow, parent, new Vector3(3f, 0f, -21f), 0f);
            FantasyPlaygroundSetup.Place(assets.WallLow, parent, new Vector3(-11f, 0f, -14f), 90f);
            FantasyPlaygroundSetup.Place(assets.Rubble, parent, new Vector3(8f, 0f, -24f), 30f);
            FantasyPlaygroundSetup.Place(assets.Rubble, parent, new Vector3(-9f, 0f, -26f), 200f);
        }

        /// <summary>Two rows of columns that break line of sight, and low walls to shoot past.</summary>
        private static void BuildColonnade(EnvironmentAssets assets, Transform parent)
        {
            foreach (float z in new[] { -5f, 0f, 5f })
            {
                FantasyPlaygroundSetup.Place(assets.Column, parent, new Vector3(-5.5f, 0f, z), 0f);
                FantasyPlaygroundSetup.Place(assets.Column, parent, new Vector3(5.5f, 0f, z), 0f);
            }

            FantasyPlaygroundSetup.Place(assets.WallLow, parent, new Vector3(-12f, 0f, -4f), 90f);
            FantasyPlaygroundSetup.Place(assets.WallLow, parent, new Vector3(12f, 0f, 4f), 90f);
            FantasyPlaygroundSetup.Place(assets.ColumnBroken, parent, new Vector3(-12f, 0f, 4f), 60f);
            FantasyPlaygroundSetup.Place(assets.ColumnBroken, parent, new Vector3(12f, 0f, -4f), 250f);

            // A fallen column across half of the approach to the next gate: enemies must path round it.
            FantasyPlaygroundSetup.Place(assets.ColumnFallen, parent, new Vector3(-2f, 0f, 7f), 0f);
        }

        /// <summary>
        /// A dais in the middle, columns at the corners, and a raised platform on each side reached
        /// by a stair, where the Wisps hold the high ground.
        /// </summary>
        private static void BuildSanctum(EnvironmentAssets assets, Transform parent)
        {
            FantasyPlaygroundSetup.Place(assets.Dais, parent, new Vector3(0f, 0f, 21f), 0f);

            foreach (float x in new[] { -6.5f, 6.5f })
            {
                FantasyPlaygroundSetup.Place(assets.Column, parent, new Vector3(x, 0f, 14.5f), 0f);
                FantasyPlaygroundSetup.Place(assets.Column, parent, new Vector3(x, 0f, 27.5f), 0f);
            }

            // The stair is 3.6 m long and rises 1.6 m; it ends flush with the platform edge at |x| = 11.
            var platform = new Vector3(6f, PlatformHeight, 8f);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "PlatformWest", platform, new Vector3(-14f, 0f, 21f), assets.DarkStoneMaterial);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "PlatformEast", platform, new Vector3(14f, 0f, 21f), assets.DarkStoneMaterial);
            FantasyPlaygroundSetup.Place(assets.Stairs, parent, new Vector3(-7.4f, 0f, 21f), -90f);
            FantasyPlaygroundSetup.Place(assets.Stairs, parent, new Vector3(7.4f, 0f, 21f), 90f);

            FantasyPlaygroundSetup.Place(assets.Rubble, parent, new Vector3(-3.5f, 0f, 28.5f), 90f);
            FantasyPlaygroundSetup.Place(assets.Rubble, parent, new Vector3(9.5f, 0f, 12f), 300f);
        }

        /// <summary>Ground below the walls and a ring of trees whose crowns show above them.</summary>
        private static void BuildSurroundings(EnvironmentAssets assets, Transform parent)
        {
            Mesh ground = assets.EnsureBlockMesh(new Vector3(300f, 0.5f, 300f));
            FantasyPlaygroundSetup.CreateMeshObject(parent, "OuterGround", ground, assets.GroundMaterial,
                new Vector3(0f, -1.1f, 0f), false);

            var random = new Random(2468);
            int planted = 0;
            for (int attempt = 0; attempt < 300 && planted < 60; attempt++)
            {
                float x = EnvironmentAssets.Range(random, -60f, 60f);
                float z = EnvironmentAssets.Range(random, -75f, 75f);
                bool outside = Mathf.Abs(x) > HalfWidth + 4f || Mathf.Abs(z) > HalfLength + 4f;
                if (!outside)
                {
                    continue;
                }

                GameObject tree = assets.Trees[random.Next(assets.Trees.Length)];
                FantasyPlaygroundSetup.Place(tree, parent, new Vector3(x, -0.6f, z), EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 1f, 1.7f));
                planted++;
            }
        }

        // ---------------------------------------------------------- navigation

        /// <summary>
        /// Bakes the walkable area from the architecture's colliders and stores it as an asset, which
        /// the NavMeshSurface loads when the scene opens. The existing asset is updated in place, so
        /// the scene's reference to it stays valid. The Mage, the enemies and the trees are not part
        /// of the architecture, so they never cut holes in it.
        /// </summary>
        private static void BakeNavMesh(Transform architecture, StringBuilder report)
        {
            NavMeshSurface surface = architecture.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.center = NavBoundsCentre;
            surface.size = NavBoundsSize;

            var sources = new List<NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(architecture, surface.layerMask, NavMeshCollectGeometry.PhysicsColliders,
                surface.defaultArea, new List<NavMeshBuildMarkup>(), sources);

            NavMeshBuildSettings settings = surface.GetBuildSettings();
            var bounds = new Bounds(NavBoundsCentre, NavBoundsSize);

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
                ? $"Baked the navigation mesh from {sources.Count} collider(s) into '{NavMeshPath}'."
                : "WARNING: the navigation mesh could not be built. Enemies will not move.");
        }

        private static void ReportNavigation(List<EnemyController> enemies, StringBuilder report)
        {
            int placed = 0;
            foreach (EnemyController enemy in enemies)
            {
                if (NavMesh.SamplePosition(enemy.transform.position, out NavMeshHit _, 1f, NavMesh.AllAreas))
                {
                    placed++;
                }
            }

            bool spawnOnMesh = NavMesh.SamplePosition(SpawnPosition, out NavMeshHit _, 1f, NavMesh.AllAreas);
            report.AppendLine($"Navigation: {placed} of {enemies.Count} enemies stand on the navigation mesh; " +
                              (spawnOnMesh ? "the start point is reachable." : "WARNING: the start point is not on it."));
        }

        // ------------------------------------------------------------- enemies

        private static List<EnemyController> EnsureEnemies(Scene scene, EnemyAssets enemyAssets, StringBuilder report)
        {
            var result = new List<EnemyController>();

            GameObject existing = FindByName(scene, EnemiesName);
            if (existing != null)
            {
                result.AddRange(existing.GetComponentsInChildren<EnemyController>(true));
                report.AppendLine($"Kept existing '{EnemiesName}' ({result.Count} enemies).");
                return result;
            }

            var root = new GameObject(EnemiesName);
            Undo.RegisterCreatedObjectUndo(root, DialogTitle);

            foreach (Encounter layout in Encounters)
            {
                var group = new GameObject(layout.Name);
                group.transform.SetParent(root.transform, false);
                EnemyEncounter encounter = group.AddComponent<EnemyEncounter>();
                var members = new List<EnemyController>();

                foreach (Placement placement in layout.Enemies)
                {
                    GameObject prefab = placement.Wisp ? enemyAssets.WispPrefab : enemyAssets.GuardianPrefab;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    instance.name = placement.Name;
                    instance.transform.SetPositionAndRotation(placement.Position, Quaternion.Euler(0f, placement.Yaw, 0f));

                    EnemyController controller = instance.GetComponent<EnemyController>();
                    PatrolRoute route = placement.Route != null && placement.Route.Length >= 2
                        ? CreateRoute(group.transform, placement.Name + " Route", placement.Route)
                        : null;

                    controller.BindScene(null, route);
                    MarkChanged(controller);
                    members.Add(controller);
                }

                encounter.Configure(layout.Name, members);
                result.AddRange(members);
            }

            report.AppendLine($"Placed {result.Count} enemies in {Encounters.Length} encounters under '{EnemiesName}'.");
            return result;
        }

        private static PatrolRoute CreateRoute(Transform parent, string name, Vector3[] points)
        {
            var routeObject = new GameObject(name);
            routeObject.transform.SetParent(parent, false);
            PatrolRoute route = routeObject.AddComponent<PatrolRoute>();

            var transforms = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var point = new GameObject("Point " + (i + 1));
                point.transform.SetParent(routeObject.transform, false);
                point.transform.position = points[i];
                transforms[i] = point.transform;
            }

            route.SetPoints(transforms);
            return route;
        }

        private static EnemyDirector EnsureDirector(Scene scene, GameObject mage, EnemyAssets enemyAssets, StringBuilder report)
        {
            EnemyDirector director = FindInScene<EnemyDirector>(scene);
            if (director == null)
            {
                var host = new GameObject(DirectorName);
                Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                director = host.AddComponent<EnemyDirector>();
                report.AppendLine("Added the enemy director (Normal difficulty).");
            }

            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
            Camera viewCamera = rig != null ? rig.GetComponent<Camera>() : null;

            Undo.RecordObject(director, DialogTitle);
            director.BindMissing(mage.GetComponent<Health>(), viewCamera, enemyAssets.Normal, FindInScene<PrototypeSession>(scene));
            MarkChanged(director);
            return director;
        }
    }
}
