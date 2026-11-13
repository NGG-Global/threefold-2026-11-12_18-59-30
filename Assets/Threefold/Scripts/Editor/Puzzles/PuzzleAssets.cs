using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates the materials and meshes of the puzzle props: the gateway frame and its seal, rune
    /// lamps, the pressure plate and its timer stones, the lookout's rune ring and stele, the
    /// crystal and the reliquary. Everything is built by code and saved under
    /// Assets/Threefold/Generated/Puzzles. Stone materials and plain blocks come from the
    /// environment assets, so the puzzle matches the level.
    ///
    /// Materials are created once and kept, so tuned colours survive. Meshes are kept too unless a
    /// rebuild is requested, in which case they are updated in place.
    /// </summary>
    internal sealed class PuzzleAssets
    {
        public const string Folder = "Assets/Threefold/Generated/Puzzles";
        private const string MeshFolder = Folder + "/Meshes";
        private const string MaterialFolder = Folder + "/Materials";

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";
        private const string GlowTexturePath = "Assets/Threefold/Generated/Combat/SoftGlow.png";

        /// <summary>Sub-mesh index of plain stone.</summary>
        public const int StoneSlot = 0;

        /// <summary>Sub-mesh index of glowing runes, or of darker trim on pieces without runes.</summary>
        public const int AccentSlot = 1;

        private readonly bool rebuild;
        private int createdCount;
        private int updatedCount;

        private PuzzleAssets(EnvironmentAssets environment, bool rebuild)
        {
            Environment = environment;
            this.rebuild = rebuild;
        }

        public EnvironmentAssets Environment { get; }

        /// <summary>Glowing rune stone. Its glow is driven per object at runtime.</summary>
        public Material RuneMaterial { get; private set; }

        /// <summary>Additive light for the beam.</summary>
        public Material BeamMaterial { get; private set; }

        public Material StoneMaterial => Environment.StoneMaterial;
        public Material DarkStoneMaterial => Environment.DarkStoneMaterial;

        public Mesh GatewayFrame { get; private set; }
        public Mesh SealSlab { get; private set; }
        public Mesh Lamp { get; private set; }
        public Mesh PlateBase { get; private set; }
        public Mesh PlateTop { get; private set; }
        public Mesh TimerStone { get; private set; }
        public Mesh LookoutRing { get; private set; }
        public Mesh Stele { get; private set; }
        public Mesh Crystal { get; private set; }
        public Mesh ReliquaryBody { get; private set; }
        public Mesh ReliquaryLid { get; private set; }
        public Mesh ReliquaryGlow { get; private set; }

        public static PuzzleAssets Ensure(EnvironmentAssets environment, bool rebuild, StringBuilder report)
        {
            var assets = new PuzzleAssets(environment, rebuild);

            EnvironmentAssets.EnsureFolder(MeshFolder);
            EnvironmentAssets.EnsureFolder(MaterialFolder);

            assets.CreateMaterials();
            assets.CreateMeshes();
            AssetDatabase.SaveAssets();

            report.AppendLine($"Puzzle assets: {assets.createdCount} created, {assets.updatedCount} updated, the rest kept. " +
                              $"Folder: '{Folder}'.");
            return assets;
        }

        /// <summary>A plain block from the environment assets, with its origin at the centre of its base.</summary>
        public Mesh Block(Vector3 size)
        {
            return Environment.EnsureBlockMesh(size);
        }

        // ------------------------------------------------------------ materials

        private void CreateMaterials()
        {
            RuneMaterial = EnsureMaterial("ArcaneRune", () =>
            {
                var material = new Material(FindShader(LitShader));
                material.color = new Color(0.17f, 0.19f, 0.28f);
                SetFloatIfPresent(material, "_Smoothness", 0.55f);

                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", new Color(0.12f, 0.17f, 0.5f));
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }

                return material;
            });

            BeamMaterial = EnsureMaterial("ArcaneBeam", () =>
            {
                var material = new Material(FindShader(ParticleShader));
                var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath);
                if (glow != null)
                {
                    material.mainTexture = glow;
                }

                material.color = Color.white;
                SetFloatIfPresent(material, "_Surface", 1f);
                SetFloatIfPresent(material, "_Blend", 2f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.One);
                SetFloatIfPresent(material, "_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                return material;
            });
        }

        private static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Threefold] Shader '{shaderName}' was not found. Falling back to a built-in shader; " +
                                 "the puzzle materials will look different from the intended ones.");
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

        private Material EnsureMaterial(string name, Func<Material> create)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = create();
            AssetDatabase.CreateAsset(material, path);
            createdCount++;
            return material;
        }

        // --------------------------------------------------------------- meshes

        private void CreateMeshes()
        {
            GatewayFrame = EnsureMesh("GatewayFrame", BuildGatewayFrame);
            SealSlab = EnsureMesh("SealSlab", BuildSealSlab);
            Lamp = EnsureMesh("RuneLamp", BuildLamp);
            PlateBase = EnsureMesh("PlateBase", BuildPlateBase);
            PlateTop = EnsureMesh("PlateTop", BuildPlateTop);
            TimerStone = EnsureMesh("TimerStone", BuildTimerStone);
            LookoutRing = EnsureMesh("LookoutRing", BuildLookoutRing);
            Stele = EnsureMesh("RuneStele", BuildStele);
            Crystal = EnsureMesh("GatewayCrystal", BuildCrystal);
            ReliquaryBody = EnsureMesh("ReliquaryBody", BuildReliquaryBody);
            ReliquaryLid = EnsureMesh("ReliquaryLid", BuildReliquaryLid);
            ReliquaryGlow = EnsureMesh("ReliquaryGlow", BuildReliquaryGlow);
        }

        /// <summary>
        /// The carved surround of the gateway, in the gateway's space: the origin is the foot of the
        /// front face and +Z points out of the gateway. Two pilasters, a cornice above the opening,
        /// and a bracket on the cornice that holds the crystal.
        /// </summary>
        private static Mesh BuildGatewayFrame()
        {
            var b = new ProceduralMeshBuilder();
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 1.475f;
                b.AddBox(new Vector3(x, 1.95f, 0.15f), new Vector3(0.55f, 3.9f, 0.3f), StoneSlot);
                b.AddBox(new Vector3(x, 0.15f, 0.2f), new Vector3(0.75f, 0.3f, 0.4f), AccentSlot);
                b.AddBox(new Vector3(x, 3.75f, 0.2f), new Vector3(0.7f, 0.14f, 0.4f), AccentSlot);
            }

            b.AddBox(new Vector3(0f, 4.1f, 0.1f), new Vector3(3.6f, 0.4f, 0.45f), StoneSlot);
            b.AddBox(new Vector3(0f, 3.86f, 0.2f), new Vector3(3.3f, 0.08f, 0.3f), AccentSlot);

            // The bracket reaches forward, so the crystal floats clear of the face.
            b.AddBox(new Vector3(0f, 4.42f, 0.5f), new Vector3(0.7f, 0.24f, 0.6f), AccentSlot);
            b.AddPrism(new Vector3(0f, 4.54f, 0.5f), 0.22f, 0.12f, 0.1f, 6, AccentSlot);
            return b.Build("GatewayFrame");
        }

        /// <summary>The sealing slab, origin at the middle of its base. A circle of runes on its front face (+Z).</summary>
        private static Mesh BuildSealSlab()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 1.5f, 0f), new Vector3(2.36f, 3f, 0.4f), StoneSlot);

            var centre = new Vector3(0f, 1.75f, 0.205f);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector3 position = centre + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.62f;
                b.AddBox(position, new Vector3(0.16f, 0.16f, 0.03f), Quaternion.Euler(0f, 0f, 45f), AccentSlot);
            }

            b.AddBox(centre, new Vector3(0.34f, 0.34f, 0.03f), Quaternion.Euler(0f, 0f, 45f), AccentSlot);
            b.AddBox(new Vector3(0f, 0.75f, 0.205f), new Vector3(0.06f, 0.9f, 0.025f), AccentSlot);
            return b.Build("SealSlab");
        }

        /// <summary>A small diamond-shaped rune gem, set into a wall facing +Z.</summary>
        private static Mesh BuildLamp()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(Vector3.zero, new Vector3(0.2f, 0.2f, 0.08f), Quaternion.Euler(0f, 0f, 45f), 0);
            return b.Build("RuneLamp");
        }

        private static Mesh BuildPlateBase()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.04f, 0f), new Vector3(1.6f, 0.08f, 1.6f), StoneSlot);
            return b.Build("PlateBase");
        }

        /// <summary>The plate's pressing surface, with a rune pattern on top.</summary>
        private static Mesh BuildPlateTop()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.03f, 0f), new Vector3(1.25f, 0.06f, 1.25f), StoneSlot);
            b.AddBox(new Vector3(0f, 0.062f, 0f), new Vector3(0.42f, 0.012f, 0.42f), Quaternion.Euler(0f, 45f, 0f), AccentSlot);

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI / 2f;
                var position = new Vector3(Mathf.Cos(angle) * 0.42f, 0.062f, Mathf.Sin(angle) * 0.42f);
                b.AddBox(position, new Vector3(0.14f, 0.012f, 0.14f), Quaternion.Euler(0f, 45f, 0f), AccentSlot);
            }

            return b.Build("PlateTop");
        }

        private static Mesh BuildTimerStone()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(0.34f, 0.04f, 0.14f), 0);
            return b.Build("TimerStone");
        }

        /// <summary>A ring of rune marks lying on a surface. Slot 0 is left empty so the runes use slot 1, as on the stele.</summary>
        private static Mesh BuildLookoutRing()
        {
            var b = new ProceduralMeshBuilder();
            const int marks = 10;
            for (int i = 0; i < marks; i++)
            {
                float angle = i * Mathf.PI * 2f / marks;
                var position = new Vector3(Mathf.Cos(angle) * 0.75f, 0.012f, Mathf.Sin(angle) * 0.75f);
                float yaw = -angle * Mathf.Rad2Deg + 90f;
                b.AddBox(position, new Vector3(0.32f, 0.024f, 0.09f), Quaternion.Euler(0f, yaw, 0f), AccentSlot);
            }

            b.AddBox(new Vector3(0f, 0.012f, 0f), new Vector3(0.3f, 0.024f, 0.3f), Quaternion.Euler(0f, 45f, 0f), AccentSlot);
            return b.Build("LookoutRing");
        }

        /// <summary>A short standing stone with a rune strip on each face, so it is seen from below.</summary>
        private static Mesh BuildStele()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.65f, 0f), new Vector3(0.32f, 1.3f, 0.32f), StoneSlot);
            b.AddPrism(new Vector3(0f, 1.3f, 0f), 0.2f, 0f, 0.18f, 4, StoneSlot);

            for (int i = 0; i < 4; i++)
            {
                Quaternion facing = Quaternion.Euler(0f, i * 90f, 0f);
                b.AddBox(facing * new Vector3(0f, 0.85f, 0.165f), new Vector3(0.1f, 0.6f, 0.02f), facing, AccentSlot);
            }

            return b.Build("RuneStele");
        }

        /// <summary>A double-pointed crystal, origin at its widest point.</summary>
        private static Mesh BuildCrystal()
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(new Vector3(0f, -0.55f, 0f), 0f, 0.32f, 0.55f, 6, 0);
            b.AddPrism(Vector3.zero, 0.32f, 0f, 0.95f, 6, 0);
            return b.Build("GatewayCrystal");
        }

        /// <summary>A stone chest, origin at the middle of its base; its front faces +Z.</summary>
        private static Mesh BuildReliquaryBody()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.04f, 0f), new Vector3(1.2f, 0.08f, 0.8f), AccentSlot);
            b.AddBox(new Vector3(0f, 0.37f, 0f), new Vector3(1.1f, 0.58f, 0.7f), StoneSlot);
            b.AddBox(new Vector3(0f, 0.6f, 0f), new Vector3(1.14f, 0.06f, 0.74f), AccentSlot);
            return b.Build("ReliquaryBody");
        }

        /// <summary>The chest's lid, origin at the middle of its underside.</summary>
        private static Mesh BuildReliquaryLid()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.06f, 0f), new Vector3(1.16f, 0.12f, 0.76f), StoneSlot);
            b.AddBox(new Vector3(0f, 0.125f, 0f), new Vector3(0.3f, 0.012f, 0.3f), Quaternion.Euler(0f, 45f, 0f), AccentSlot);
            return b.Build("ReliquaryLid");
        }

        /// <summary>The light inside the chest, seen once the lid moves.</summary>
        private static Mesh BuildReliquaryGlow()
        {
            var b = new ProceduralMeshBuilder();
            b.AddBox(new Vector3(0f, 0.655f, 0f), new Vector3(0.94f, 0.01f, 0.54f), 0);
            return b.Build("ReliquaryGlow");
        }

        /// <summary>
        /// Returns the mesh asset with the given name. An existing mesh is kept, or, on a rebuild,
        /// overwritten in place so that everything already using it follows.
        /// </summary>
        private Mesh EnsureMesh(string name, Func<Mesh> build)
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
    }
}
