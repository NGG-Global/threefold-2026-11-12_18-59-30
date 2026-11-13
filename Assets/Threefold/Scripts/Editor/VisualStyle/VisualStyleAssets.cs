using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates the assets of the visual style in Assets/Threefold/Generated/VisualStyle: the
    /// painted textures, one material per preset, the Mage's materials with their contour line, and
    /// a sky material. Existing assets are kept, so a texture repainted by hand or a material tuned
    /// in the Inspector survives; delete an asset to have it generated again.
    ///
    /// Materials use the shader "Threefold/Illustrated Lit". If Unity reports that the shader did
    /// not compile, materials are created with URP's stock Simple Lit shader instead, with the same
    /// painted textures, so the scene never turns pink; the report says so.
    /// </summary>
    internal sealed class VisualStyleAssets
    {
        internal const string Folder = "Assets/Threefold/Generated/VisualStyle";
        internal const string TextureFolder = Folder + "/Textures";
        internal const string MaterialFolder = Folder + "/Materials";
        internal const string ShaderName = "Threefold/Illustrated Lit";
        internal const string FallbackShaderName = "Universal Render Pipeline/Simple Lit";
        internal const string OutlinePass = "SRPDefaultUnlit";
        internal const string SkyMaterialPath = MaterialFolder + "/" + StyleMaterialPresets.Prefix + "Sky.mat";
        private const string VariationPath = TextureFolder + "/PaintVariation.png";
        private const string MageMaterialFolder = "Assets/Threefold/Generated/Characters/Mage/Materials";
        private const string CharacterPrefix = StyleMaterialPresets.Prefix + "Character_";

        // The contour line: 0.22% of the screen's height is about 2.4 pixels at 1080p.
        internal const float OutlineWidth = 0.0022f;
        internal const float OutlineFadeStart = 12f;
        internal const float OutlineFadeEnd = 28f;

        private readonly StringBuilder report;
        private readonly bool regenerate;
        private int created;
        private int kept;
        private int upgraded;

        private VisualStyleAssets(StringBuilder report, bool regenerate)
        {
            this.report = report;
            this.regenerate = regenerate;
        }

        /// <summary>The shader the materials were made with: the style's own, or the stock fallback.</summary>
        public Shader Shader { get; private set; }

        /// <summary>True if the style's own shader is in use.</summary>
        public bool UsesIllustratedShader { get; private set; }

        public Material Sky { get; private set; }

        /// <summary>Grey-box material to the styled material that stands in for it.</summary>
        public Dictionary<Material, Material> Replacements { get; } = new Dictionary<Material, Material>();

        /// <summary>Every styled material by preset name, for objects that use a preset directly.</summary>
        public Dictionary<string, Material> ByName { get; } = new Dictionary<string, Material>();

        /// <param name="regenerate">Paint the textures again and overwrite them. Materials are still kept.</param>
        internal static VisualStyleAssets Ensure(StringBuilder report, bool regenerate = false)
        {
            var assets = new VisualStyleAssets(report, regenerate);
            assets.Build();
            return assets;
        }

        /// <summary>The style's shader, or null if it is missing or Unity reports compile errors for it.</summary>
        internal static Shader FindUsableShader(out string problem)
        {
            Shader shader = UnityEngine.Shader.Find(ShaderName);
            if (shader == null)
            {
                problem = $"the shader '{ShaderName}' was not found";
                return null;
            }

            if (ShaderUtil.ShaderHasError(shader))
            {
                problem = $"Unity reports compile errors in the shader '{ShaderName}' (see the Console)";
                return null;
            }

            // A build server usually runs Unity without a graphics device. What isSupported answers
            // there is not known, and it says nothing about the phone the build is for, so it is
            // only consulted where there is a device to ask.
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && !shader.isSupported)
            {
                problem = $"the shader '{ShaderName}' is not supported by this graphics device";
                return null;
            }

            problem = null;
            return shader;
        }

        private void Build()
        {
            CombatSetup.EnsureFolder(TextureFolder);
            CombatSetup.EnsureFolder(MaterialFolder);

            Shader = FindUsableShader(out string problem);
            UsesIllustratedShader = Shader != null;
            if (!UsesIllustratedShader)
            {
                Shader = UnityEngine.Shader.Find(FallbackShaderName);
                report.AppendLine($"WARNING: {problem}. New style materials use '{FallbackShaderName}' instead: " +
                                  "painted textures without the soft light response and without contour lines.");
            }

            Texture2D variation = EnsureTexture(VariationPath, 128, false,
                () => PaintedTextureGenerator.GenerateVariationMap(128, 5));

            foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
            {
                Texture2D map = null;
                if (preset.Texture != null)
                {
                    PaintedTextureRecipe recipe = preset.Texture;
                    map = EnsureTexture($"{TextureFolder}/Painted_{recipe.Name}.png", recipe.Size, true,
                        () => PaintedTextureGenerator.Generate(recipe));
                }

                Material material = EnsureMaterial($"{MaterialFolder}/{preset.Name}.mat",
                    () => CreatePresetMaterial(preset, map, variation),
                    existing => ConfigureIllustratedPreset(existing, preset, variation));
                ByName[preset.Name] = material;

                if (preset.Replaces != null)
                {
                    var greyBox = AssetDatabase.LoadAssetAtPath<Material>(
                        $"{EnvironmentAssets.MaterialFolder}/{preset.Replaces}.mat");
                    if (greyBox != null && material != null)
                    {
                        Replacements[greyBox] = material;
                    }
                }
            }

            BuildCharacterMaterials();
            BuildSky();
            AssetDatabase.SaveAssets();

            report.AppendLine($"Visual style assets: {created} created, {kept} kept. Folder: '{Folder}'.");
            if (upgraded > 0)
            {
                report.AppendLine($"{upgraded} material(s) made earlier with the stock shader now use '{ShaderName}'. " +
                                  "Their colour and texture were kept.");
            }
        }

        // ------------------------------------------------------------ textures

        private Texture2D EnsureTexture(string path, int size, bool colour, System.Func<byte[]> paint)
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null && !regenerate)
            {
                kept++;
                return existing;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(paint());
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = colour;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = size;

                // ASTC 6x6 is about 3.6 bits per pixel: a 512 texture with mipmaps takes about 155 KB.
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = size;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }

            created++;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ----------------------------------------------------------- materials

        /// <param name="upgrade">
        /// Gives an existing material the style's own shader and settings. Used only for a material
        /// that still carries the stock shader from a run in which the style's shader was unusable.
        /// </param>
        private Material EnsureMaterial(string path, System.Func<Material> create, System.Action<Material> upgrade)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (UsesIllustratedShader && existing.shader != null && existing.shader.name == FallbackShaderName)
                {
                    // Colour, texture and culling carry over: both shaders use the same property names.
                    existing.shader = Shader;
                    existing.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    upgrade(existing);
                    EditorUtility.SetDirty(existing);
                    upgraded++;
                    return existing;
                }

                kept++;
                return existing;
            }

            Material material = create();
            if (material == null)
            {
                return null;
            }

            AssetDatabase.CreateAsset(material, path);
            created++;
            return material;
        }

        private Material CreatePresetMaterial(StyleMaterialPreset preset, Texture2D map, Texture2D variation)
        {
            if (Shader == null)
            {
                return null;
            }

            var material = new Material(Shader) { enableInstancing = true };
            SetTexture(material, "_BaseMap", map);
            SetColor(material, "_BaseColor", ToColor(preset.Tint));
            SetFloat(material, "_Cull", preset.TwoSided ? 0f : 2f);

            if (!UsesIllustratedShader)
            {
                ConfigureFallback(material);
                return material;
            }

            ConfigureIllustratedPreset(material, preset, variation);
            return material;
        }

        /// <summary>The settings that only the style's own shader has, for a material preset.</summary>
        private static void ConfigureIllustratedPreset(Material material, StyleMaterialPreset preset, Texture2D variation)
        {
            SetColor(material, "_ShadeColor", ToColor(preset.Shade));
            SetFloat(material, "_Wrap", preset.Wrap);
            ConfigureHighlight(material, preset.HighlightColour, preset.HighlightStrength, preset.HighlightSize);

            bool paint = preset.PaintStrength > 0f && variation != null;
            SetFloat(material, "_PaintVariation", paint ? 1f : 0f);
            SetKeyword(material, "_PAINT_VARIATION", paint);
            SetTexture(material, "_PaintMap", variation);
            SetFloat(material, "_PaintStrength", preset.PaintStrength);
            SetFloat(material, "_PaintScale", preset.PaintScale);

            // The environment carries its lines in its textures; contour lines are for characters.
            // The pass is switched off, and the width is zero as well: should the pass be drawn
            // after all, a hull of no width is folded away by the shader and shows nothing.
            SetFloat(material, "_OutlineWidth", 0f);
            material.SetShaderPassEnabled(OutlinePass, false);
        }

        /// <summary>
        /// The Mage keeps the colours of its existing materials. It gains the soft light response
        /// and the contour line. Its model has no painted texture yet, so its surfaces are flat
        /// colour; that is asset work for later.
        /// </summary>
        private void BuildCharacterMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MageMaterialFolder))
            {
                return;
            }

            bool smoothNormals = CharacterModelPostprocessor.StoresOutlineNormals(CharacterModelPostprocessor.MageModelPath);

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MageMaterialFolder }))
            {
                var original = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (original == null)
                {
                    continue;
                }

                string key = original.name.StartsWith("Mage_") ? original.name.Substring("Mage_".Length) : original.name;
                Material styled = EnsureMaterial($"{MaterialFolder}/{CharacterPrefix}Mage_{key}.mat",
                    () => CreateCharacterMaterial(original, key, smoothNormals),
                    existing => ConfigureIllustratedCharacter(existing, original, key, smoothNormals));

                if (styled != null)
                {
                    Replacements[original] = styled;
                }
            }
        }

        private Material CreateCharacterMaterial(Material original, string key, bool smoothNormals)
        {
            if (Shader == null)
            {
                return null;
            }

            var material = new Material(Shader) { enableInstancing = true };
            Color colour = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.color;
            SetColor(material, "_BaseColor", colour);

            if (!UsesIllustratedShader)
            {
                ConfigureFallback(material);
                return material;
            }

            ConfigureIllustratedCharacter(material, original, key, smoothNormals);
            return material;
        }

        /// <summary>The settings that only the style's own shader has, for a character material.</summary>
        private static void ConfigureIllustratedCharacter(Material material, Material original, string key, bool smoothNormals)
        {
            SetColor(material, "_ShadeColor", ToColor(StylePalette.ShadeCool));

            // Skin and cloth turn more softly than stone.
            SetFloat(material, "_Wrap", 0.5f);

            if (key == "Metal")
            {
                ConfigureHighlight(material, new Rgb(1f, 0.95f, 0.85f), 0.35f, 0.6f);
            }
            else
            {
                ConfigureHighlight(material, new Rgb(1f, 1f, 1f), 0f, 0.5f);
            }

            if (original.IsKeywordEnabled("_EMISSION") && original.HasProperty("_EmissionColor"))
            {
                SetFloat(material, "_Emission", 1f);
                SetKeyword(material, "_EMISSION", true);
                SetColor(material, "_EmissionColor", original.GetColor("_EmissionColor"));
            }

            SetFloat(material, "_PaintVariation", 0f);
            SetKeyword(material, "_PAINT_VARIATION", false);

            SetColor(material, "_OutlineColor", ToColor(StylePalette.Contour));
            SetFloat(material, "_OutlineWidth", OutlineWidth);
            SetFloat(material, "_OutlineFadeStart", OutlineFadeStart);
            SetFloat(material, "_OutlineFadeEnd", OutlineFadeEnd);
            SetFloat(material, "_OutlineSmoothNormals", smoothNormals ? 1f : 0f);
            SetKeyword(material, "_OUTLINE_SMOOTH_NORMALS", smoothNormals);
            material.SetShaderPassEnabled(OutlinePass, true);
        }

        /// <summary>A copy of the scene's sky material, so tinting the sky never changes the original.</summary>
        private void BuildSky()
        {
            Sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (Sky != null)
            {
                kept++;
                return;
            }

            string source = EnvironmentAssets.MaterialFolder + "/Sky.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(source) == null || !AssetDatabase.CopyAsset(source, SkyMaterialPath))
            {
                return;
            }

            Sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            created++;
        }

        private static void ConfigureHighlight(Material material, Rgb colour, float strength, float size)
        {
            bool on = strength > 0f;
            SetFloat(material, "_Highlight", on ? 1f : 0f);
            SetKeyword(material, "_HIGHLIGHT", on);
            SetColor(material, "_HighlightColor", new Color(colour.R, colour.G, colour.B, Mathf.Clamp01(strength)));
            SetFloat(material, "_HighlightSize", size);
        }

        /// <summary>Stock Simple Lit, made matte: no highlight, so the painted colour is left alone.</summary>
        private static void ConfigureFallback(Material material)
        {
            SetFloat(material, "_Smoothness", 0f);
            SetFloat(material, "_SpecularHighlights", 0f);
            SetKeyword(material, "_SPECULARHIGHLIGHTS_OFF", true);
        }

        internal static Color ToColor(Rgb colour)
        {
            return new Color(colour.R, colour.G, colour.B, 1f);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetTexture(Material material, string property, Texture value)
        {
            if (value != null && material.HasProperty(property))
            {
                material.SetTexture(property, value);
            }
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }
    }
}
