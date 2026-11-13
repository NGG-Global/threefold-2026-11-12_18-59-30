using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Threefold.World;

namespace Threefold.EditorTools
{
    /// <summary>
    /// A few simple props in wood, cloth and metal. The courtyard is built of stone and vegetation
    /// only, and the art direction asks for all five material families to read apart; these props
    /// put the other three in front of the player. They belong to the visual style: applying the
    /// style adds them, removing it takes them away.
    /// </summary>
    internal static class StyleProps
    {
        internal const string RootName = "VisualStyle Props";
        private const string GeneratorName = "VisualStyleProps";

        /// <summary>The props group in a scene, or null.</summary>
        internal static GameObject Find(Scene scene)
        {
            return FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName);
        }

        /// <summary>
        /// Builds the group at a place: a brazier, a banner stand with two banners, and two crates,
        /// in a row about seven metres long along the group's X axis.
        /// </summary>
        internal static GameObject Build(EnvironmentAssets environment, VisualStyleAssets style, Vector3 position, float yaw)
        {
            var root = new GameObject(RootName);
            root.AddComponent<GeneratedEnvironment>().Generator = GeneratorName;
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            style.ByName.TryGetValue(StyleMaterialPresets.Prefix + "Wood", out Material wood);
            style.ByName.TryGetValue(StyleMaterialPresets.Prefix + "Cloth", out Material cloth);
            style.ByName.TryGetValue(StyleMaterialPresets.Prefix + "ClothCool", out Material clothCool);
            style.ByName.TryGetValue(StyleMaterialPresets.Prefix + "Metal", out Material metal);

            // Crates: wood.
            Mesh crate = environment.EnsureMesh("StyleCrate", () =>
            {
                var b = new ProceduralMeshBuilder();
                b.AddBox(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f), 0);
                return b.Build("StyleCrate");
            });
            AddPart(root.transform, "Crate", crate, new[] { wood }, new Vector3(2.2f, 0f, 0f), 12f, 1f, true);
            AddPart(root.transform, "CrateSmall", crate, new[] { wood }, new Vector3(3.3f, 0f, 0.3f), -20f, 0.65f, true);

            // Banner stand: a wooden frame with two cloth banners, one warm and one cool.
            Mesh frame = environment.EnsureMesh("StyleBannerFrame", () =>
            {
                var b = new ProceduralMeshBuilder();
                b.AddBox(new Vector3(-1.1f, 1.4f, 0f), new Vector3(0.14f, 2.8f, 0.14f), 0);
                b.AddBox(new Vector3(1.1f, 1.4f, 0f), new Vector3(0.14f, 2.8f, 0.14f), 0);
                b.AddBox(new Vector3(0f, 2.7f, 0f), new Vector3(2.5f, 0.12f, 0.12f), 0);
                return b.Build("StyleBannerFrame");
            });
            Mesh banner = environment.EnsureMesh("StyleBanner", () =>
            {
                var b = new ProceduralMeshBuilder();
                b.AddBox(new Vector3(0f, 0.9f, 0f), new Vector3(0.85f, 1.8f, 0.03f), 0);
                return b.Build("StyleBanner");
            });
            AddPart(root.transform, "BannerFrame", frame, new[] { wood }, new Vector3(-0.6f, 0f, 0f), 0f, 1f, true);
            AddPart(root.transform, "BannerWarm", banner, new[] { cloth }, new Vector3(-1.1f, 0.8f, 0f), 0f, 1f, false);
            AddPart(root.transform, "BannerCool", banner, new[] { clothCool }, new Vector3(-0.1f, 0.8f, 0f), 0f, 1f, false);

            // Brazier: metal.
            Mesh brazier = environment.EnsureMesh("StyleBrazier", () =>
            {
                var b = new ProceduralMeshBuilder();
                b.AddPrism(Vector3.zero, 0.32f, 0.26f, 0.06f, 10, 0);
                b.AddPrism(new Vector3(0f, 0.06f, 0f), 0.07f, 0.07f, 0.84f, 8, 0);
                b.AddPrism(new Vector3(0f, 0.9f, 0f), 0.2f, 0.46f, 0.32f, 10, 0);
                return b.Build("StyleBrazier");
            });
            AddPart(root.transform, "Brazier", brazier, new[] { metal }, new Vector3(-2.9f, 0f, 0.2f), 0f, 1f, true);

            return root;
        }

        private static void AddPart(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 localPosition,
            float yaw, float scale, bool collide)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            part.transform.localScale = new Vector3(scale, scale, scale);

            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;

            if (collide)
            {
                part.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            part.isStatic = true;
        }
    }
}
