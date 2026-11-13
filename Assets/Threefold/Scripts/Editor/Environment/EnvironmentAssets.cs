using System;
using System.Globalization;
using System.IO;
using System.Text;
using Threefold.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates the reusable building blocks of the fantasy environments: tiling textures, a
    /// small set of shared materials, low-polygon meshes, and one prefab per module (column, arch,
    /// wall, stairs, tree and so on). Everything is created by code and saved under
    /// Assets/Threefold/Generated/Environment, so no art files are needed.
    ///
    /// Materials and textures are created once and then kept, so colours tuned in the Inspector
    /// survive. Meshes and prefabs are kept too unless a rebuild is requested, in which case they
    /// are updated in place and every scene that uses them follows.
    /// </summary>
    internal sealed class EnvironmentAssets
    {
        public const string Folder = "Assets/Threefold/Generated/Environment";
        private const string MeshFolder = Folder + "/Meshes";
        internal const string MaterialFolder = Folder + "/Materials";
        private const string TextureFolder = Folder + "/Textures";
        private const string PrefabFolder = Folder + "/Prefabs";

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string SkyboxShader = "Skybox/Procedural";

        private const int Stone = 0;
        private const int Accent = 1;

        private readonly bool rebuild;
        private readonly StringBuilder report;
        private int createdCount;
        private int updatedCount;

        public Material StoneMaterial { get; private set; }
        public Material DarkStoneMaterial { get; private set; }
        public Material PavingMaterial { get; private set; }
        public Material MossMaterial { get; private set; }
        public Material GrassMaterial { get; private set; }
        public Material GroundMaterial { get; private set; }
        public Material BarkMaterial { get; private set; }
        public Material FoliageMaterial { get; private set; }
        public Material CrystalMaterial { get; private set; }
        public Material RuneMaterial { get; private set; }
        public Material MountainMaterial { get; private set; }
        public Material SkyboxMaterial { get; private set; }

        public GameObject Column { get; private set; }
        public GameObject ColumnBroken { get; private set; }
        public GameObject ColumnFallen { get; private set; }
        public GameObject Arch { get; private set; }
        public GameObject ArchRuined { get; private set; }
        public GameObject Wall { get; private set; }
        public GameObject WallBroken { get; private set; }
        public GameObject WallLow { get; private set; }
        public GameObject Stairs { get; private set; }
        public GameObject Dais { get; private set; }
        public GameObject ManaCrystal { get; private set; }
        public GameObject Rubble { get; private set; }
        public GameObject MossPatch { get; private set; }
        public GameObject Spire { get; private set; }
        public GameObject[] Rocks { get; private set; }
        public GameObject[] Trees { get; private set; }
        public GameObject[] GrassPatches { get; private set; }
        public GameObject[] Mountains { get; private set; }

        private EnvironmentAssets(bool rebuild, StringBuilder report)
        {
            this.rebuild = rebuild;
            this.report = report;
        }

        /// <summary>Creates whatever is missing. With <paramref name="rebuild"/>, meshes and prefabs are regenerated.</summary>
        public static EnvironmentAssets Ensure(bool rebuild, StringBuilder report)
        {
            var assets = new EnvironmentAssets(rebuild, report);

            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);
            EnsureFolder(PrefabFolder);

            assets.CreateMaterials();
            assets.CreateModules();
            AssetDatabase.SaveAssets();

            report.AppendLine($"Environment assets: {assets.createdCount} created, {assets.updatedCount} updated, " +
                              $"the rest kept. Folder: '{Folder}'.");
            return assets;
        }

        // ------------------------------------------------------------ materials

        private void CreateMaterials()
        {
            Texture2D stoneTexture = EnsureTexture("StoneNoise", 128, StonePixel);
            Texture2D pavingTexture = EnsureTexture("Paving", 256, PavingPixel);
            Texture2D groundTexture = EnsureTexture("GroundNoise", 128, GroundPixel);

            StoneMaterial = EnsureMaterial("Stone", () => CreateLit(new Color(0.63f, 0.6f, 0.55f), stoneTexture));
            DarkStoneMaterial = EnsureMaterial("StoneDark", () => CreateLit(new Color(0.4f, 0.4f, 0.43f), stoneTexture));
            PavingMaterial = EnsureMaterial("Paving", () => CreateLit(new Color(0.58f, 0.55f, 0.5f), pavingTexture));
            MossMaterial = EnsureMaterial("Moss", () => CreateLit(new Color(0.3f, 0.43f, 0.19f), groundTexture));
            GroundMaterial = EnsureMaterial("Ground", () => CreateLit(new Color(0.29f, 0.39f, 0.2f), groundTexture));
            BarkMaterial = EnsureMaterial("Bark", () => CreateLit(new Color(0.29f, 0.21f, 0.15f), stoneTexture));
            FoliageMaterial = EnsureMaterial("Foliage", () => CreateLit(new Color(0.12f, 0.29f, 0.2f), groundTexture));
            MountainMaterial = EnsureMaterial("Mountain", () => CreateLit(new Color(0.3f, 0.36f, 0.45f), null));

            GrassMaterial = EnsureMaterial("Grass", () =>
            {
                Material material = CreateLit(new Color(0.36f, 0.53f, 0.22f), null);
                SetFloatIfPresent(material, "_Cull", 0f);
                return material;
            });

            CrystalMaterial = EnsureMaterial("Crystal", () => CreateEmissive(new Color(0.35f, 0.6f, 0.95f), new Color(0.5f, 1.3f, 2.4f)));
            RuneMaterial = EnsureMaterial("Rune", () => CreateEmissive(new Color(0.2f, 0.4f, 0.6f), new Color(0.3f, 0.9f, 1.6f)));
            SkyboxMaterial = EnsureMaterial("Sky", CreateSkybox);
        }

        private static Material CreateLit(Color color, Texture2D texture)
        {
            var material = new Material(FindShader(LitShader));
            material.color = color;

            if (texture != null)
            {
                material.mainTexture = texture;
            }

            SetFloatIfPresent(material, "_Smoothness", 0.08f);
            material.enableInstancing = true;
            return material;
        }

        private static Material CreateEmissive(Color color, Color emission)
        {
            Material material = CreateLit(color, null);
            SetFloatIfPresent(material, "_Smoothness", 0.7f);

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            return material;
        }

        /// <summary>A late-afternoon sky. Returns null if Unity's procedural sky shader is unavailable.</summary>
        private static Material CreateSkybox()
        {
            Shader shader = Shader.Find(SkyboxShader);
            if (shader == null)
            {
                Debug.LogWarning($"[Threefold] Shader '{SkyboxShader}' was not found, so the scene keeps its default sky.");
                return null;
            }

            var material = new Material(shader);
            SetFloatIfPresent(material, "_SunSize", 0.045f);
            SetFloatIfPresent(material, "_SunSizeConvergence", 6f);
            SetFloatIfPresent(material, "_AtmosphereThickness", 1.2f);
            SetFloatIfPresent(material, "_Exposure", 1.15f);

            if (material.HasProperty("_SkyTint"))
            {
                material.SetColor("_SkyTint", new Color(0.42f, 0.5f, 0.62f));
            }

            if (material.HasProperty("_GroundColor"))
            {
                material.SetColor("_GroundColor", new Color(0.42f, 0.46f, 0.47f));
            }

            return material;
        }

        private static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Threefold] Shader '{shaderName}' was not found. Falling back to a built-in shader; " +
                                 "generated materials will look different from the intended ones.");
                shader = Shader.Find("Sprites/Default");
            }

            return shader;
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        // ------------------------------------------------------------- textures

        // Textures hold brightness only; each material tints them with its own colour.

        private static Color32 StonePixel(float u, float v)
        {
            float value = 0.76f + 0.24f * (0.6f * TileNoise(u, v, 6, 11) + 0.4f * TileNoise(u, v, 24, 23));
            return Grey(value);
        }

        private static Color32 GroundPixel(float u, float v)
        {
            float value = 0.7f + 0.3f * (0.55f * TileNoise(u, v, 4, 5) + 0.45f * TileNoise(u, v, 16, 17));
            return Grey(value);
        }

        /// <summary>Flagstones one metre across in a running bond, with darker joints.</summary>
        private static Color32 PavingPixel(float u, float v)
        {
            const int slabsPerTile = 4;
            const float joint = 0.045f;

            float row = v * slabsPerTile;
            int rowIndex = Mathf.FloorToInt(row);
            float column = u * slabsPerTile + (rowIndex % 2 == 0 ? 0f : 0.5f);
            int columnIndex = Mathf.FloorToInt(column) % slabsPerTile;

            float inRow = row - rowIndex;
            float inColumn = column - Mathf.Floor(column);
            bool isJoint = inRow < joint || inColumn < joint;

            float slabTone = 0.8f + 0.2f * Hash(columnIndex, rowIndex, 41);
            float grain = 0.12f * (TileNoise(u, v, 32, 7) - 0.5f);
            return Grey(isJoint ? 0.5f : slabTone + grain);
        }

        private static Color32 Grey(float value)
        {
            byte level = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
            return new Color32(level, level, level, 255);
        }

        /// <summary>Smooth noise that repeats exactly at the texture's edges, so the texture tiles without seams.</summary>
        private static float TileNoise(float u, float v, int cells, int seed)
        {
            float x = u * cells;
            float y = v * cells;
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = Mathf.SmoothStep(0f, 1f, x - x0);
            float fy = Mathf.SmoothStep(0f, 1f, y - y0);

            int xa = Wrap(x0, cells);
            int xb = Wrap(x0 + 1, cells);
            int ya = Wrap(y0, cells);
            int yb = Wrap(y0 + 1, cells);

            float bottom = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
            float top = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
            return Mathf.Lerp(bottom, top, fy);
        }

        private static int Wrap(int value, int size)
        {
            int result = value % size;
            return result < 0 ? result + size : result;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        private Texture2D EnsureTexture(string name, int size, Func<float, float, Color32> pixel)
        {
            string path = TextureFolder + "/" + name + ".png";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }

            createdCount++;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // -------------------------------------------------------------- modules

        private void CreateModules()
        {
            Material[] stone = { StoneMaterial };
            Material[] stoneAndMoss = { StoneMaterial, MossMaterial };

            Column = EnsureModule("Column", BuildColumn, stone, true);
            ColumnBroken = EnsureModule("ColumnBroken", BuildBrokenColumn, stoneAndMoss, true);
            ColumnFallen = EnsureModule("ColumnFallen", BuildFallenColumn, stoneAndMoss, true);
            Arch = EnsureModule("Arch", () => BuildArch(0f, 180f, 4f, 4f), stoneAndMoss, true);
            ArchRuined = EnsureModule("ArchRuined", () => BuildArch(70f, 180f, 4f, 1.7f), stoneAndMoss, true);
            Wall = EnsureModule("Wall", BuildWall, stoneAndMoss, true);
            WallBroken = EnsureModule("WallBroken", BuildBrokenWall, stoneAndMoss, true);
            WallLow = EnsureModule("WallLow", BuildLowWall, stoneAndMoss, true);
            Stairs = EnsureModule("Stairs", BuildStairs, stone, true);
            Dais = EnsureModule("Dais", BuildDais, new[] { StoneMaterial, RuneMaterial }, true);
            Rubble = EnsureModule("Rubble", BuildRubble, stone, false);
            MossPatch = EnsureModule("MossPatch", BuildMossPatch, new[] { MossMaterial }, false);
            Spire = EnsureModule("DistantSpire", BuildSpire, new[] { DarkStoneMaterial }, false);

            Rocks = new GameObject[3];
            for (int i = 0; i < Rocks.Length; i++)
            {
                int seed = 100 + i;
                Rocks[i] = EnsureModule("Rock" + (char)('A' + i), () => BuildRock(seed), new[] { DarkStoneMaterial }, true);
            }

            Material[] treeMaterials = { BarkMaterial, FoliageMaterial };
            Trees = new[]
            {
                EnsureTree("TreeA", 2.6f, 1.9f, 3),
                EnsureTree("TreeB", 3.4f, 2.3f, 4),
                EnsureTree("TreeC", 2.1f, 1.5f, 3),
            };

            GrassPatches = new GameObject[2];
            for (int i = 0; i < GrassPatches.Length; i++)
            {
                int seed = 300 + i;
                GrassPatches[i] = EnsureModule("GrassPatch" + (char)('A' + i), () => BuildGrassPatch(seed),
                    new[] { GrassMaterial }, false);
            }

            Mountains = new[]
            {
                EnsureModule("MountainA", () => BuildMountain(70f, 95f, 45f, 62f), new[] { MountainMaterial }, false),
                EnsureModule("MountainB", () => BuildMountain(85f, 70f, 55f, 88f), new[] { MountainMaterial }, false),
            };

            ManaCrystal = EnsureCrystal();

            GameObject EnsureTree(string name, float trunkHeight, float canopyRadius, int layers)
            {
                return EnsureModule(name, () => BuildTree(trunkHeight, canopyRadius, layers), treeMaterials, false, tree =>
                {
                    CapsuleCollider trunk = tree.AddComponent<CapsuleCollider>();
                    trunk.radius = 0.35f;
                    trunk.height = trunkHeight + 1f;
                    trunk.center = new Vector3(0f, trunkHeight * 0.5f, 0f);
                });
            }
        }

        // ---- architecture

        private static Mesh BuildColumn()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 1.1f), Stone);
            b.AddPrism(new Vector3(0f, 0.3f, 0f), 0.52f, 0.46f, 0.25f, 8, Stone);
            b.AddPrism(new Vector3(0f, 0.55f, 0f), 0.42f, 0.36f, 4.2f, 10, Stone);
            b.AddPrism(new Vector3(0f, 4.75f, 0f), 0.36f, 0.56f, 0.3f, 8, Stone);
            b.AddBox(new Vector3(0f, 5.175f, 0f), new Vector3(1.2f, 0.25f, 1.2f), Stone);
            return b.Build("Column");
        }

        private static Mesh BuildBrokenColumn()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 1.1f), Stone);
            b.AddPrism(new Vector3(0f, 0.3f, 0f), 0.52f, 0.46f, 0.25f, 8, Stone);
            b.AddPrism(new Vector3(0f, 0.55f, 0f), 0.42f, 0.39f, 1.7f, 10, Stone);

            // A tilted last drum reads as a clean break without any jagged geometry.
            b.AddPrism(new Vector3(0f, 2.2f, 0f), 0.39f, 0.3f, 0.35f, 7, Quaternion.Euler(13f, 0f, 9f), Stone);
            b.AddPrism(new Vector3(0.04f, 2.5f, -0.06f), 0.3f, 0.18f, 0.1f, 7, Quaternion.Euler(13f, 0f, 9f), Accent);
            return b.Build("ColumnBroken");
        }

        private static Mesh BuildFallenColumn()
        {
            var b = new ProceduralMeshBuilder();

            // Lying on its side along the X axis, slightly sunk into the ground.
            b.AddPrism(new Vector3(-2f, 0.34f, 0f), 0.42f, 0.36f, 4f, 10, Quaternion.Euler(0f, 0f, -90f), Stone);
            b.AddBox(new Vector3(-0.4f, 0.74f, 0f), new Vector3(2.4f, 0.07f, 0.34f), Accent);
            return b.Build("ColumnFallen");
        }

        /// <summary>
        /// A round arch on two pillars, standing in the XY plane with a four-metre opening. Leaving
        /// out part of the ring and shortening a pillar gives the ruined version.
        /// </summary>
        private static Mesh BuildArch(float fromDegrees, float toDegrees, float leftPillarHeight, float rightPillarHeight)
        {
            const float pillarWidth = 1f;
            const float depth = 1f;
            const float fullHeight = 4f;
            const float radius = 2.5f;
            const int stones = 9;

            var b = new ProceduralMeshBuilder();
            AddPillar(b, -radius, leftPillarHeight, pillarWidth, depth, fullHeight);
            AddPillar(b, radius, rightPillarHeight, pillarWidth, depth, fullHeight);

            float springLine = fullHeight + 0.2f;
            float stoneLength = Mathf.PI * radius / stones * 1.05f;

            for (int i = 0; i < stones; i++)
            {
                float degrees = (i + 0.5f) * 180f / stones;
                if (degrees < fromDegrees || degrees > toDegrees)
                {
                    continue;
                }

                float radians = degrees * Mathf.Deg2Rad;
                var centre = new Vector3(Mathf.Cos(radians) * radius, springLine + Mathf.Sin(radians) * radius, 0f);
                bool keystone = i == stones / 2;
                var size = new Vector3(stoneLength, pillarWidth * (keystone ? 1.15f : 0.9f), depth * (keystone ? 1.08f : 1f));

                // Rotated so the stone's thickness points away from the arch's centre.
                b.AddBox(centre, size, Quaternion.Euler(0f, 0f, degrees - 90f), Stone);
            }

            if (fromDegrees <= 90f && toDegrees >= 90f)
            {
                b.AddBox(new Vector3(0f, springLine + radius + 0.56f, 0f), new Vector3(1.7f, 0.08f, depth * 0.85f), Accent);
            }

            return b.Build("Arch");
        }

        private static void AddPillar(ProceduralMeshBuilder b, float x, float height, float width, float depth, float fullHeight)
        {
            b.AddBox(new Vector3(x, 0.15f, 0f), new Vector3(width + 0.3f, 0.3f, depth + 0.3f), Stone);
            b.AddBox(new Vector3(x, height * 0.5f, 0f), new Vector3(width, height, depth), Stone);

            if (height >= fullHeight)
            {
                b.AddBox(new Vector3(x, height + 0.1f, 0f), new Vector3(width + 0.25f, 0.2f, depth + 0.2f), Stone);
            }
            else
            {
                b.AddBox(new Vector3(x, height + 0.04f, 0f), new Vector3(width * 0.85f, 0.08f, depth * 0.85f), Accent);
            }
        }

        private static Mesh BuildWall()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 1.1f, 0f), new Vector3(4f, 2.2f, 0.7f), Stone);
            b.AddBox(new Vector3(-1.3f, 2.45f, 0f), new Vector3(1.2f, 0.5f, 0.7f), Stone);
            b.AddBox(new Vector3(0.2f, 2.35f, 0f), new Vector3(1f, 0.3f, 0.7f), Stone);
            b.AddBox(new Vector3(-1.3f, 2.73f, 0f), new Vector3(1.1f, 0.06f, 0.6f), Accent);
            b.AddBox(new Vector3(1.4f, 2.23f, 0f), new Vector3(1f, 0.06f, 0.6f), Accent);
            return b.Build("Wall");
        }

        private static Mesh BuildBrokenWall()
        {
            var b = new ProceduralMeshBuilder();
            AddWallPiece(b, -1.33f, 2.2f);
            AddWallPiece(b, 0f, 1.5f);
            AddWallPiece(b, 1.33f, 0.8f);
            return b.Build("WallBroken");
        }

        private static void AddWallPiece(ProceduralMeshBuilder b, float x, float height)
        {
            b.AddBox(new Vector3(x, height * 0.5f, 0f), new Vector3(1.34f, height, 0.7f), Stone);
            b.AddBox(new Vector3(x, height + 0.03f, 0f), new Vector3(1.2f, 0.06f, 0.58f), Accent);
        }

        private static Mesh BuildLowWall()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.45f, 0f), new Vector3(4f, 0.9f, 0.6f), Stone);
            b.AddBox(new Vector3(0.2f, 0.93f, 0f), new Vector3(3.4f, 0.06f, 0.5f), Accent);
            return b.Build("WallLow");
        }

        /// <summary>Eight steps rising 1.6 m over 3.6 m towards +Z. The origin is the bottom of the first step.</summary>
        private static Mesh BuildStairs()
        {
            const int steps = 8;
            const float rise = 0.2f;
            const float run = 0.45f;
            const float width = 5f;

            var b = new ProceduralMeshBuilder();
            for (int i = 0; i < steps; i++)
            {
                float height = (i + 1) * rise;
                b.AddBox(new Vector3(0f, height * 0.5f, i * run + run * 0.5f), new Vector3(width, height, run), Stone);
            }

            return b.Build("Stairs");
        }

        private static Mesh BuildDais()
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(Vector3.zero, 4f, 4f, 0.15f, 16, Stone);
            b.AddPrism(new Vector3(0f, 0.15f, 0f), 3.2f, 3.2f, 0.15f, 16, Stone);
            b.AddPrism(new Vector3(0f, 0.3f, 0f), 0.75f, 0.5f, 0.55f, 8, Stone);

            // Eight glowing marks set into the top step.
            for (int i = 0; i < 8; i++)
            {
                float degrees = i * 45f + 22.5f;
                float radians = degrees * Mathf.Deg2Rad;
                var centre = new Vector3(Mathf.Cos(radians) * 2.5f, 0.31f, Mathf.Sin(radians) * 2.5f);
                b.AddBox(centre, new Vector3(0.7f, 0.03f, 0.14f), Quaternion.Euler(0f, -(degrees + 90f), 0f), Accent);
            }

            return b.Build("Dais");
        }

        private static Mesh BuildRubble()
        {
            var random = new Random(77);
            var b = new ProceduralMeshBuilder();

            for (int i = 0; i < 7; i++)
            {
                float size = Range(random, 0.18f, 0.42f);
                var centre = new Vector3(Range(random, -1.1f, 1.1f), size * 0.3f, Range(random, -1.1f, 1.1f));
                var rotation = Quaternion.Euler(Range(random, -25f, 25f), Range(random, 0f, 360f), Range(random, -25f, 25f));
                b.AddBox(centre, new Vector3(size * 1.4f, size * 0.7f, size), rotation, Stone);
            }

            return b.Build("Rubble");
        }

        private static Mesh BuildSpire()
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(Vector3.zero, 7f, 5.5f, 48f, 8, Stone);
            b.AddPrism(new Vector3(0f, 48f, 0f), 5.5f, 6.4f, 3f, 8, Stone);
            b.AddPrism(new Vector3(0f, 51f, 0f), 4.6f, 0f, 15f, 8, Stone);
            b.AddPrism(new Vector3(10f, 0f, 2f), 3.6f, 3f, 27f, 6, Stone);
            b.AddPrism(new Vector3(10f, 27f, 2f), 3.4f, 0f, 8f, 6, Stone);
            b.AddPrism(new Vector3(-9f, 0f, -1f), 3f, 2.6f, 15f, 6, Quaternion.Euler(0f, 0f, 4f), Stone);
            return b.Build("DistantSpire");
        }

        // ---- nature

        private static Mesh BuildRock(int seed)
        {
            var random = new Random(seed);
            var b = new ProceduralMeshBuilder();

            float radius = Range(random, 0.8f, 1.2f);
            float height = Range(random, 0.8f, 1.3f);
            Quaternion tilt = Quaternion.Euler(Range(random, -12f, 12f), Range(random, 0f, 360f), Range(random, -12f, 12f));
            var baseCentre = new Vector3(0f, -0.25f, 0f);
            Vector3 middle = baseCentre + tilt * Vector3.up * (height * 0.5f);

            b.AddPrism(baseCentre, radius * 0.9f, radius * 1.1f, height * 0.5f, 7, tilt, Stone);
            b.AddPrism(middle, radius * 1.1f, radius * 0.5f, height * 0.65f, 7, tilt, Stone);
            return b.Build("Rock");
        }

        /// <summary>A stylised conifer: a tapered trunk and stacked cones, each turned a little.</summary>
        private static Mesh BuildTree(float trunkHeight, float canopyRadius, int layers)
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(new Vector3(0f, -0.4f, 0f), 0.3f, 0.2f, trunkHeight + 0.4f, 6, Stone);

            for (int i = 0; i < layers; i++)
            {
                float radius = canopyRadius * (1f - i * 0.22f);
                var baseCentre = new Vector3(0f, trunkHeight * 0.55f + i * 1.25f, 0f);
                b.AddPrism(baseCentre, radius, 0f, 2.3f, 7, Quaternion.Euler(0f, i * 25f, 0f), Accent);
            }

            return b.Build("Tree");
        }

        /// <summary>A flat patch of grass about five metres across, for level ground.</summary>
        private static Mesh BuildGrassPatch(int seed)
        {
            var random = new Random(seed);
            var b = new ProceduralMeshBuilder();

            for (int i = 0; i < 60; i++)
            {
                var centre = new Vector3(Range(random, -2.5f, 2.5f), 0f, Range(random, -2.5f, 2.5f));
                AddGrassClump(b, random, centre, 0);
            }

            return b.Build("GrassPatch");
        }

        /// <summary>
        /// Adds a few blades of grass. Each blade is a single triangle, and all blades share an
        /// upward normal so the patch is lit evenly instead of flickering blade by blade.
        /// </summary>
        public static void AddGrassClump(ProceduralMeshBuilder b, Random random, Vector3 centre, int subMesh)
        {
            for (int blade = 0; blade < 4; blade++)
            {
                float angle = Range(random, 0f, Mathf.PI * 2f);
                var side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var lean = new Vector3(-side.z, 0f, side.x) * Range(random, -0.22f, 0.22f);

                Vector3 root = centre + new Vector3(Range(random, -0.18f, 0.18f), 0f, Range(random, -0.18f, 0.18f));
                float height = Range(random, 0.28f, 0.62f);
                float halfWidth = Range(random, 0.035f, 0.06f);

                b.AddRawTriangle(root - side * halfWidth, root + side * halfWidth, root + Vector3.up * height + lean,
                    Vector3.up, subMesh);
            }
        }

        private static Mesh BuildMossPatch()
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(Vector3.zero, 1.3f, 1f, 0.035f, 9, Stone);
            b.AddPrism(new Vector3(0.9f, 0f, 0.5f), 0.7f, 0.5f, 0.03f, 7, Stone);
            return b.Build("MossPatch");
        }

        private static Mesh BuildMountain(float radius, float height, float secondRadius, float secondHeight)
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(new Vector3(0f, -10f, 0f), radius, 0f, height, 7, Stone);
            b.AddPrism(new Vector3(radius * 0.65f, -10f, radius * 0.15f), secondRadius, 0f, secondHeight, 6,
                Quaternion.Euler(0f, 20f, 0f), Stone);
            return b.Build("Mountain");
        }

        private static Mesh BuildCrystal()
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(new Vector3(0f, -0.9f, 0f), 0f, 0.42f, 0.9f, 6, Stone);
            b.AddPrism(Vector3.zero, 0.42f, 0f, 1.5f, 6, Stone);
            return b.Build("ManaCrystal");
        }

        /// <summary>The floating crystal: it turns and bobs, glows, and lights its surroundings.</summary>
        private GameObject EnsureCrystal()
        {
            const string name = "ManaCrystal";
            string path = PrefabFolder + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild)
            {
                return existing;
            }

            Mesh mesh = EnsureMesh(name, BuildCrystal);

            var root = new GameObject(name);

            var body = new GameObject("Crystal");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CrystalMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.AddComponent<FloatingMotion>();

            // One unshadowed point light is the only extra light in the environment.
            var glow = new GameObject("Glow");
            glow.transform.SetParent(root.transform, false);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.5f, 0.8f, 1f);
            light.intensity = 3.5f;
            light.range = 9f;
            light.shadows = LightShadows.None;

            return SavePrefab(root, path, existing != null);
        }

        // ------------------------------------------------------------- plumbing

        /// <summary>Returns the prefab for a module, creating it (and its mesh) if needed.</summary>
        private GameObject EnsureModule(string name, Func<Mesh> build, Material[] materials, bool collide,
            Action<GameObject> configure = null)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild)
            {
                return existing;
            }

            Mesh mesh = EnsureMesh(name, build);

            var root = new GameObject(name);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = materials;

            if (collide)
            {
                root.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            root.isStatic = true;
            configure?.Invoke(root);

            return SavePrefab(root, path, existing != null);
        }

        private GameObject SavePrefab(GameObject root, string path, bool existed)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            if (existed)
            {
                updatedCount++;
            }
            else
            {
                createdCount++;
            }

            return prefab;
        }

        /// <summary>
        /// Returns the mesh asset with the given name. An existing mesh is kept, or, on a rebuild,
        /// overwritten in place so that everything already using it follows.
        /// </summary>
        public Mesh EnsureMesh(string name, Func<Mesh> build)
        {
            string path = MeshFolder + "/" + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null && !rebuild)
            {
                return existing;
            }

            Mesh mesh = build();
            mesh.name = name;

            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                updatedCount++;
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            createdCount++;
            return mesh;
        }

        /// <summary>A plain stone block of the given size, with its origin at the centre of its base.</summary>
        public Mesh EnsureBlockMesh(Vector3 size)
        {
            string name = string.Format(CultureInfo.InvariantCulture, "Block_{0:0.##}x{1:0.##}x{2:0.##}", size.x, size.y, size.z)
                .Replace('.', '_');

            return EnsureMesh(name, () =>
            {
                var b = new ProceduralMeshBuilder();
                b.AddBox(new Vector3(0f, size.y * 0.5f, 0f), size, 0);
                return b.Build(name);
            });
        }

        private Material EnsureMaterial(string name, Func<Material> create)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = create();
            if (material == null)
            {
                return null;
            }

            AssetDatabase.CreateAsset(material, path);
            createdCount++;
            return material;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static float Range(Random random, float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }
    }
}
