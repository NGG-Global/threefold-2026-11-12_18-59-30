using System.Collections.Generic;
using System.Text;
using Threefold.Cameras;
using Threefold.Combat;
using Threefold.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;
using Random = System.Random;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the first fantasy test level: an abandoned training courtyard at the edge of a
    /// forest. One menu command creates the scene, generates the environment modules, lays out the
    /// level, sets up lighting and atmosphere, and adds the Mage with camera, controls and combat.
    ///
    /// Running it again rebuilds only the generated environment (the object marked with
    /// <see cref="GeneratedEnvironment"/>). The Mage, the camera, the HUD, the touch controls, the
    /// training dummies and anything added by hand are left alone.
    /// </summary>
    public static class FantasyPlaygroundSetup
    {
        private const string MenuPath = "Threefold/Setup/Fantasy Playground";
        private const string RebuildMenuPath = "Threefold/Setup/Fantasy Playground (Regenerate Assets)";
        private const string DialogTitle = "Fantasy Playground";
        internal const string ScenePath = "Assets/Scenes/FantasyPlayground.unity";
        private const string RootName = "FantasyPlayground Environment";
        private const string GeneratorName = "FantasyPlayground";
        private const string PostProcessingProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        private const int IgnoreRaycastLayer = 2;

        /// <summary>Half the width of the paved courtyard, in metres.</summary>
        private const float CourtyardHalf = 18f;

        /// <summary>How far beyond the courtyard the player can walk before an invisible wall.</summary>
        private const float WalkableMargin = 8f;

        private const float TerraceHeight = 1.6f;

        private static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -12f);

        [MenuItem(MenuPath, false, 3)]
        public static void Run()
        {
            Build(false);
        }

        [MenuItem(RebuildMenuPath, false, 4)]
        public static void RunWithAssetRebuild()
        {
            if (EditorUtility.DisplayDialog(DialogTitle,
                    "Regenerate the environment meshes and prefabs from code?\n\n" +
                    "Shapes edited by hand in 'Assets/Threefold/Generated/Environment' will be overwritten. " +
                    "Materials and textures are kept.", "Regenerate", "Cancel"))
            {
                Build(true);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(RebuildMenuPath, true)]
        private static bool CanRunWithAssetRebuild()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// Height of the ground outside the courtyard. The land is level under the paving and rises
        /// into rolling hills around it, which hides the horizon and holds the forest.
        /// </summary>
        internal static float TerrainHeight(float x, float z)
        {
            float distance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - CourtyardHalf;
            if (distance <= 0f)
            {
                return -0.05f;
            }

            float beyond = Mathf.Max(0f, distance - 10f);
            float rise = distance * 0.08f + beyond * beyond * 0.0015f;
            float hills = Mathf.PerlinNoise(x * 0.06f + 31.7f, z * 0.06f + 12.3f) * Mathf.Min(distance * 0.25f, 3f);
            return -0.05f + rise + hills;
        }

        /// <summary>The interactive command: asks before discarding or replacing anything.</summary>
        private static void Build(bool rebuildAssets)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            if (!TryOpenScene(out Scene scene))
            {
                return;
            }

            if (FindEnvironmentRoot(scene) != null && !EditorUtility.DisplayDialog(DialogTitle,
                    "This scene already contains the generated environment.\n\n" +
                    "Rebuild it? Only the generated environment is replaced. The Mage, camera, HUD, touch " +
                    "controls, training targets and anything you added yourself are kept.", "Rebuild", "Cancel"))
            {
                return;
            }

            var report = new StringBuilder();
            GameObject mage = Assemble(scene, rebuildAssets, report);
            bool saved = EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = mage;

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle,
                summary + (saved ? "\n\nThe scene was saved. " : "\n\nThe scene could NOT be saved; save it with Ctrl+S. ") +
                "Press Play to try it.", "OK");
        }

        /// <summary>
        /// Builds the complete scene without asking anything, for automated builds and tests. It
        /// opens the scene without saving whatever was open before, so it must only be called
        /// where nobody has unsaved work: from the command line or a build server.
        /// </summary>
        /// <returns>The assembled and saved scene.</returns>
        internal static Scene BuildForAutomation()
        {
            Scene scene;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                EnvironmentAssets.EnsureFolder("Assets/Scenes");
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            var report = new StringBuilder();
            Assemble(scene, true, report);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Threefold] {DialogTitle} assembled for automation.\n{report.ToString().TrimEnd()}");
            return scene;
        }

        /// <summary>Generates the assets, rebuilds the environment and makes the scene playable.</summary>
        /// <returns>The Mage.</returns>
        private static GameObject Assemble(Scene scene, bool rebuildAssets, StringBuilder report)
        {
            EnvironmentAssets assets = EnvironmentAssets.Ensure(rebuildAssets, report);

            GameObject oldRoot = FindEnvironmentRoot(scene);
            if (oldRoot != null)
            {
                Object.DestroyImmediate(oldRoot);
                report.AppendLine("Removed the previous generated environment.");
            }

            var root = new GameObject(RootName);
            root.AddComponent<GeneratedEnvironment>().Generator = GeneratorName;

            Transform architecture = CreateGroup(root.transform, "Architecture");
            Transform nature = CreateGroup(root.transform, "Nature");
            Transform background = CreateGroup(root.transform, "Background");
            Transform lighting = CreateGroup(root.transform, "Lighting");
            Transform gameplay = CreateGroup(root.transform, "Gameplay");

            BuildCourtyard(assets, architecture);
            BuildTerrace(assets, architecture);
            BuildWestRuins(assets, architecture);
            BuildJumpCourse(assets, architecture);
            BuildPerimeter(assets, architecture);
            BuildNature(assets, nature);
            BuildBackground(assets, background);
            BuildBounds(gameplay);
            BuildLighting(assets, lighting, report);
            ManaFont font = BuildFocalPoint(assets, gameplay);
            report.AppendLine("Built the courtyard, terrace, ruins, jump course, forest, background and lighting.");

            GameObject mage = MagePrototypeSetup.EnsureCharacterRig(scene, SpawnPosition, Quaternion.identity,
                GetTargetPlacements(), report);

            if (mage.TryGetComponent(out Mana mana))
            {
                font.Bind(mana, 4.5f, 16f);
            }

            EnablePostProcessing(scene, report);
            AddToBuildSettings(report);
            MagePrototypeSetup.AppendValidation(scene, report);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return mage;
        }

        // ---------------------------------------------------------------- scene

        private static bool TryOpenScene(out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath)
            {
                return true;
            }

            // Gives the user the chance to save or cancel; nothing open is discarded silently.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return scene.IsValid();
            }

            EnvironmentAssets.EnsureFolder("Assets/Scenes");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                EditorUtility.DisplayDialog(DialogTitle, $"The scene could not be created at '{ScenePath}'.", "OK");
                return false;
            }

            return true;
        }

        private static GameObject FindEnvironmentRoot(Scene scene)
        {
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.TryGetComponent(out GeneratedEnvironment marker) && marker.Generator == GeneratorName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void AddToBuildSettings(StringBuilder report)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene entry in scenes)
            {
                if (entry.path == ScenePath)
                {
                    return;
                }
            }

            // First in the list, so a device build starts in this level.
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            report.AppendLine("Added the scene to Build Settings as the first scene.");
        }

        // --------------------------------------------------------- architecture

        /// <summary>The paved floor, the central dais, and a few broken columns to move around.</summary>
        private static void BuildCourtyard(EnvironmentAssets assets, Transform parent)
        {
            float width = CourtyardHalf * 2f;
            PlaceBlock(assets, parent, "CourtyardFloor", new Vector3(width, 0.6f, width), new Vector3(0f, -0.6f, 0f),
                assets.PavingMaterial);

            Place(assets.Dais, parent, Vector3.zero, 0f);

            // Cover in the open ground, kept clear of the centre so the fight has room.
            Place(assets.ColumnBroken, parent, new Vector3(-6f, 0f, -5f), 20f);
            Place(assets.ColumnBroken, parent, new Vector3(6.5f, 0f, 5.5f), 140f);
            Place(assets.Column, parent, new Vector3(-7f, 0f, 6f), 0f);
            Place(assets.ColumnFallen, parent, new Vector3(6f, 0f, -9f), 30f);
            Place(assets.Rubble, parent, new Vector3(4.2f, 0f, -7.6f), 75f);
            Place(assets.Rubble, parent, new Vector3(-8.2f, 0f, 7.4f), 10f);

            // The entrance behind the starting point.
            Place(assets.Arch, parent, new Vector3(0f, 0f, -17.2f), 0f);
        }

        /// <summary>A raised terrace across the north side, reached by a central stair and crowned by a great arch.</summary>
        private static void BuildTerrace(EnvironmentAssets assets, Transform parent)
        {
            PlaceBlock(assets, parent, "Terrace", new Vector3(22f, TerraceHeight, 7f), new Vector3(0f, 0f, 14.5f),
                assets.StoneMaterial);

            // The stair is 3.6 m long and ends flush with the terrace's front edge at z = 11.
            Place(assets.Stairs, parent, new Vector3(0f, 0f, 7.4f), 0f);
            PlaceBlock(assets, parent, "StairSideWest", new Vector3(0.6f, 2f, 3.6f), new Vector3(-2.8f, 0f, 9.2f), assets.StoneMaterial);
            PlaceBlock(assets, parent, "StairSideEast", new Vector3(0.6f, 2f, 3.6f), new Vector3(2.8f, 0f, 9.2f), assets.StoneMaterial);

            // The great arch frames the distant spire when seen from the starting point.
            Place(assets.Arch, parent, new Vector3(0f, TerraceHeight, 16.8f), 0f, 1.15f);

            Place(assets.Column, parent, new Vector3(-9.5f, TerraceHeight, 16.5f), 0f);
            Place(assets.ColumnBroken, parent, new Vector3(-7f, TerraceHeight, 16.5f), 60f);
            Place(assets.Column, parent, new Vector3(-4.6f, TerraceHeight, 16.5f), 0f);
            Place(assets.Column, parent, new Vector3(4.6f, TerraceHeight, 16.5f), 0f);
            Place(assets.Column, parent, new Vector3(7f, TerraceHeight, 16.5f), 0f);
            Place(assets.ColumnBroken, parent, new Vector3(9.5f, TerraceHeight, 16.5f), 200f);
            Place(assets.Rubble, parent, new Vector3(8.6f, TerraceHeight, 13.2f), 40f);
        }

        /// <summary>
        /// A line of ruined wall and arches along the west side. The narrow passage behind it is
        /// there to exercise the camera in a confined space.
        /// </summary>
        private static void BuildWestRuins(EnvironmentAssets assets, Transform parent)
        {
            Place(assets.Wall, parent, new Vector3(-13f, 0f, -9f), 90f);
            Place(assets.ArchRuined, parent, new Vector3(-13f, 0f, -3f), 90f);
            Place(assets.WallBroken, parent, new Vector3(-13f, 0f, 3.2f), 90f);
            Place(assets.Wall, parent, new Vector3(-13f, 0f, 7.6f), 270f);
            Place(assets.Rocks[0], parent, new Vector3(-15.6f, 0f, -12.5f), 0f, 0.9f);
            Place(assets.Rubble, parent, new Vector3(-11.4f, 0f, 1.2f), 120f);
        }

        /// <summary>
        /// Blocks of rising height along the east side, half a metre per step with gaps of about
        /// 1.4 m, ending on a high platform. A full jump reaches 1.5 m.
        /// </summary>
        private static void BuildJumpCourse(EnvironmentAssets assets, Transform parent)
        {
            Material stone = assets.StoneMaterial;
            PlaceBlock(assets, parent, "JumpBlock1", new Vector3(2f, 0.5f, 2f), new Vector3(10f, 0f, -8f), stone);
            PlaceBlock(assets, parent, "JumpBlock2", new Vector3(2f, 1f, 2f), new Vector3(12.8f, 0f, -6f), stone);
            PlaceBlock(assets, parent, "JumpBlock3", new Vector3(1.8f, 1.5f, 1.8f), new Vector3(14.6f, 0f, -3.2f), stone);
            PlaceBlock(assets, parent, "JumpBlock4", new Vector3(1.8f, 2f, 1.8f), new Vector3(14.6f, 0f, 0f), stone);
            PlaceBlock(assets, parent, "HighPlatform", new Vector3(4.5f, 2.4f, 4.5f), new Vector3(14.5f, 0f, 4.6f), stone);
            Place(assets.ColumnBroken, parent, new Vector3(16f, 2.4f, 6f), 300f);
        }

        /// <summary>Low walls around the edge, with gaps, so the courtyard reads as an enclosed place.</summary>
        private static void BuildPerimeter(EnvironmentAssets assets, Transform parent)
        {
            var random = new Random(4242);
            float edge = CourtyardHalf - 0.5f;

            for (float offset = -14f; offset <= 14f; offset += 4f)
            {
                bool gate = Mathf.Abs(offset) < 4f;

                if (!gate)
                {
                    Place(PickWall(assets, random), parent, new Vector3(offset, 0f, -edge), 0f);
                }

                // The terrace already closes the middle of the north side.
                if (Mathf.Abs(offset) > 11f)
                {
                    Place(PickWall(assets, random), parent, new Vector3(offset, 0f, edge), 180f);
                }

                Place(PickWall(assets, random), parent, new Vector3(-edge, 0f, offset), 90f);
                Place(PickWall(assets, random), parent, new Vector3(edge, 0f, offset), 270f);
            }
        }

        private static GameObject PickWall(EnvironmentAssets assets, Random random)
        {
            return random.NextDouble() < 0.3 ? assets.WallBroken : assets.WallLow;
        }

        // --------------------------------------------------------------- nature

        private static void BuildNature(EnvironmentAssets assets, Transform parent)
        {
            var random = new Random(9001);

            BuildTerrain(assets, parent);
            BuildGrassRing(assets, parent, random);

            // Grass and moss creeping in along the inside of the walls.
            for (int i = 0; i < 12; i++)
            {
                float along = EnvironmentAssets.Range(random, -14f, 14f);
                float inset = CourtyardHalf - EnvironmentAssets.Range(random, 2.2f, 3.4f);
                int side = i % 4;
                Vector3 position = side == 0 ? new Vector3(along, 0.01f, -inset)
                    : side == 1 ? new Vector3(-inset, 0.01f, along)
                    : side == 2 ? new Vector3(inset, 0.01f, along)
                    : new Vector3(along < 0f ? along - 2f : along + 2f, 0.01f, inset - 7.5f);

                // Keep the stair, the gate and the jump course clear.
                if (Mathf.Abs(position.x) < 4f && (position.z < -12f || position.z > 6f))
                {
                    continue;
                }

                GameObject patch = assets.GrassPatches[i % assets.GrassPatches.Length];
                Place(patch, parent, position, EnvironmentAssets.Range(random, 0f, 360f), 0.7f);
            }

            for (int i = 0; i < 16; i++)
            {
                var position = new Vector3(EnvironmentAssets.Range(random, -15f, 15f), 0.012f + i * 0.001f,
                    EnvironmentAssets.Range(random, -15f, 9f));

                if (position.sqrMagnitude < 5f * 5f)
                {
                    continue;
                }

                Place(assets.MossPatch, parent, position, EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 0.7f, 1.4f));
            }

            // The forest: dense on the hills, thin near the walls, and open along the view to the spire.
            int planted = 0;
            for (int attempt = 0; attempt < 400 && planted < 85; attempt++)
            {
                float x = EnvironmentAssets.Range(random, -75f, 75f);
                float z = EnvironmentAssets.Range(random, -75f, 75f);
                float distance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - CourtyardHalf;

                bool inSightLine = Mathf.Abs(x) < 9f && z > CourtyardHalf;
                bool tooClose = distance < 3f;
                bool thinned = distance < 10f && random.NextDouble() < 0.6;
                if (tooClose || inSightLine || thinned)
                {
                    continue;
                }

                GameObject tree = assets.Trees[random.Next(assets.Trees.Length)];
                Place(tree, parent, new Vector3(x, TerrainHeight(x, z), z), EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 0.85f, 1.6f));
                planted++;
            }

            for (int i = 0; i < 14; i++)
            {
                float angle = EnvironmentAssets.Range(random, 0f, Mathf.PI * 2f);
                float radius = CourtyardHalf + EnvironmentAssets.Range(random, 2f, 16f);
                float x = Mathf.Cos(angle) * radius * 1.2f;
                float z = Mathf.Sin(angle) * radius * 1.2f;

                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < CourtyardHalf + 1.5f)
                {
                    continue;
                }

                GameObject rock = assets.Rocks[random.Next(assets.Rocks.Length)];
                Place(rock, parent, new Vector3(x, TerrainHeight(x, z), z), EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 0.8f, 2.2f));
            }
        }

        /// <summary>One mesh for all the land outside the courtyard, with a collider so it can be walked on.</summary>
        private static void BuildTerrain(EnvironmentAssets assets, Transform parent)
        {
            const float extent = 90f;
            const int cells = 60;

            Mesh mesh = assets.EnsureMesh("PlaygroundTerrain", () =>
            {
                float step = extent * 2f / cells;
                int row = cells + 1;
                var vertices = new Vector3[row * row];
                var uvs = new Vector2[row * row];
                var triangles = new List<int>(cells * cells * 6);

                for (int zi = 0; zi < row; zi++)
                {
                    for (int xi = 0; xi < row; xi++)
                    {
                        float x = -extent + xi * step;
                        float z = -extent + zi * step;
                        vertices[zi * row + xi] = new Vector3(x, TerrainHeight(x, z), z);
                        uvs[zi * row + xi] = new Vector2(x, z) * ProceduralMeshBuilder.UvScale;
                    }
                }

                for (int zi = 0; zi < cells; zi++)
                {
                    for (int xi = 0; xi < cells; xi++)
                    {
                        // The paving covers the middle, so no ground is drawn underneath it.
                        float centreX = -extent + (xi + 0.5f) * step;
                        float centreZ = -extent + (zi + 0.5f) * step;
                        if (Mathf.Max(Mathf.Abs(centreX), Mathf.Abs(centreZ)) < CourtyardHalf)
                        {
                            continue;
                        }

                        int v00 = zi * row + xi;
                        int v10 = v00 + 1;
                        int v01 = v00 + row;
                        int v11 = v01 + 1;

                        // Ordered so that the surface faces upwards.
                        triangles.Add(v00);
                        triangles.Add(v01);
                        triangles.Add(v11);
                        triangles.Add(v00);
                        triangles.Add(v11);
                        triangles.Add(v10);
                    }
                }

                var terrain = new Mesh { name = "PlaygroundTerrain" };
                terrain.vertices = vertices;
                terrain.uv = uvs;
                terrain.triangles = triangles.ToArray();
                terrain.RecalculateNormals();
                terrain.RecalculateBounds();
                return terrain;
            });

            CreateMeshObject(parent, "Terrain", mesh, assets.GroundMaterial, Vector3.zero, true);
        }

        /// <summary>
        /// Grass on the slope just outside the walls. Each blade is placed at the exact height of
        /// the ground beneath it, and the blades are merged into eight meshes, one per direction.
        /// </summary>
        private static void BuildGrassRing(EnvironmentAssets assets, Transform parent, Random random)
        {
            const int sectors = 8;
            const int clumpsPerSector = 140;

            for (int sector = 0; sector < sectors; sector++)
            {
                int sectorIndex = sector;
                Mesh mesh = assets.EnsureMesh("PlaygroundGrass_" + sector, () =>
                {
                    var sectorRandom = new Random(5000 + sectorIndex);
                    var b = new ProceduralMeshBuilder();

                    for (int i = 0; i < clumpsPerSector; i++)
                    {
                        float angle = (sectorIndex + (float)sectorRandom.NextDouble()) * Mathf.PI * 2f / sectors;
                        float reach = CourtyardHalf + EnvironmentAssets.Range(sectorRandom, 0.6f, 11f);

                        // Stretch the circle towards a square so the grass follows the courtyard's outline.
                        float cos = Mathf.Cos(angle);
                        float sin = Mathf.Sin(angle);
                        float stretch = 1f / Mathf.Max(Mathf.Abs(cos), Mathf.Abs(sin));
                        float x = cos * reach * stretch;
                        float z = sin * reach * stretch;

                        EnvironmentAssets.AddGrassClump(b, sectorRandom, new Vector3(x, TerrainHeight(x, z), z), 0);
                    }

                    return b.Build("PlaygroundGrass_" + sectorIndex);
                });

                GameObject grass = CreateMeshObject(parent, "Grass " + sector, mesh, assets.GrassMaterial, Vector3.zero, false);
                grass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ----------------------------------------------------------- background

        /// <summary>Mountains in a ring far outside the level, and a ruined spire on the axis of the great arch.</summary>
        private static void BuildBackground(EnvironmentAssets assets, Transform parent)
        {
            var random = new Random(31337);
            const int count = 10;

            for (int i = 0; i < count; i++)
            {
                float angle = (i + EnvironmentAssets.Range(random, -0.25f, 0.25f)) * Mathf.PI * 2f / count;
                float radius = EnvironmentAssets.Range(random, 255f, 320f);
                var position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                GameObject mountain = assets.Mountains[i % assets.Mountains.Length];
                GameObject instance = Place(mountain, parent, position, EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 0.8f, 1.5f));
                DisableShadows(instance);
            }

            DisableShadows(Place(assets.Spire, parent, new Vector3(0f, 6f, 235f), 15f));
        }

        private static void DisableShadows(GameObject instance)
        {
            if (instance.TryGetComponent(out MeshRenderer meshRenderer))
            {
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------- gameplay

        /// <summary>
        /// Invisible walls a short way up the slope. They are on the Ignore Raycast layer, so they
        /// stop the character but not the camera, the aim or the projectiles.
        /// </summary>
        private static void BuildBounds(Transform parent)
        {
            float limit = CourtyardHalf + WalkableMargin;
            float length = limit * 2f + 2f;
            Transform bounds = CreateGroup(parent, "Bounds");

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

        /// <summary>The floating crystal over the dais. Standing near it restores mana.</summary>
        private static ManaFont BuildFocalPoint(EnvironmentAssets assets, Transform parent)
        {
            GameObject crystal = Place(assets.ManaCrystal, parent, new Vector3(0f, 2.5f, 0f), 0f);
            crystal.isStatic = false;

            var fontObject = new GameObject("ManaFont");
            fontObject.transform.SetParent(parent, false);
            return fontObject.AddComponent<ManaFont>();
        }

        /// <summary>
        /// Three constructs that must be defeated to complete the level, and four practice dummies.
        /// The constructs stand on the terrace, on the high platform at the end of the jump course
        /// and in the west passage, so completing the objective takes the player up the stair,
        /// across the jumps and through the confined space.
        /// </summary>
        private static CombatSetup.DummyPlacement[] GetTargetPlacements()
        {
            const float farX = 21f;
            const float farZ = 22f;

            return new[]
            {
                new CombatSetup.DummyPlacement(new Vector3(0f, TerraceHeight, 14.2f), 180f, true),
                new CombatSetup.DummyPlacement(new Vector3(14.5f, 2.4f, 5f), 220f, true),
                new CombatSetup.DummyPlacement(new Vector3(-15.6f, 0f, 1f), 120f, true),

                new CombatSetup.DummyPlacement(new Vector3(-5f, 0f, 3f), 160f),
                new CombatSetup.DummyPlacement(new Vector3(5.5f, 0f, 2.5f), 200f),
                new CombatSetup.DummyPlacement(new Vector3(-6f, TerraceHeight, 13.5f), 180f),
                new CombatSetup.DummyPlacement(new Vector3(farX, TerrainHeight(farX, farZ), farZ), 225f),
            };
        }

        // ------------------------------------------------------------- lighting

        /// <summary>
        /// Late-afternoon light: one warm directional light with shadows, cool three-colour ambient
        /// light, distance fog, and a generated sky. Nothing is baked.
        /// </summary>
        private static void BuildLighting(EnvironmentAssets assets, Transform parent, StringBuilder report)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(36f, -38f, 0f);

            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.87f, 0.7f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.sun = sun;

            if (assets.SkyboxMaterial != null)
            {
                RenderSettings.skybox = assets.SkyboxMaterial;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.47f, 0.56f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.45f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.21f, 0.18f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.6f, 0.68f, 0.74f);
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 390f;

            DynamicGI.UpdateEnvironment();

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProcessingProfilePath);
            if (profile == null)
            {
                report.AppendLine($"WARNING: '{PostProcessingProfilePath}' was not found, so no post-processing was added.");
                return;
            }

            var volumeObject = new GameObject("Global Volume");
            volumeObject.transform.SetParent(parent, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        /// <summary>Lets the camera show the bloom and tone mapping of the global volume.</summary>
        private static void EnablePostProcessing(Scene scene, StringBuilder report)
        {
            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
            if (rig == null || !rig.TryGetComponent(out Camera viewCamera))
            {
                return;
            }

            UniversalAdditionalCameraData cameraData = viewCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null && !cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
                EditorUtility.SetDirty(cameraData);
                report.AppendLine("Switched on post-processing for the camera.");
            }
        }

        // -------------------------------------------------------------- helpers

        private static Transform CreateGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, float yaw, float scale = 1f)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = new Vector3(scale, scale, scale);
            return instance;
        }

        /// <summary>A plain stone block. <paramref name="baseCentre"/> is the centre of its underside.</summary>
        private static void PlaceBlock(EnvironmentAssets assets, Transform parent, string name, Vector3 size,
            Vector3 baseCentre, Material material)
        {
            CreateMeshObject(parent, name, assets.EnsureBlockMesh(size), material, baseCentre, true);
        }

        private static GameObject CreateMeshObject(Transform parent, string name, Mesh mesh, Material material,
            Vector3 position, bool collide)
        {
            var instance = new GameObject(name);
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.AddComponent<MeshFilter>().sharedMesh = mesh;
            instance.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (collide)
            {
                instance.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            instance.isStatic = true;
            return instance;
        }
    }
}
