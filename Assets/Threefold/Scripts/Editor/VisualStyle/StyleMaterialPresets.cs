using System.Collections.Generic;

namespace Threefold.EditorTools
{
    /// <summary>The five material families the style must keep apart at a glance, plus terrain.</summary>
    public enum MaterialFamily
    {
        Stone,
        Wood,
        Cloth,
        Metal,
        Vegetation,
        Terrain,
    }

    /// <summary>
    /// One reusable material of the style: its painted texture and how it answers light. The setup
    /// tool turns each preset into a texture and a material asset; any object can then use it.
    /// </summary>
    public sealed class StyleMaterialPreset
    {
        /// <summary>Name of the material asset, for example "Illustrated_Stone".</summary>
        public string Name;

        public MaterialFamily Family;

        /// <summary>Name of the grey-box environment material it stands in for, or null if it is new.</summary>
        public string Replaces;

        /// <summary>The painted base colour map, or null for a plain colour.</summary>
        public PaintedTextureRecipe Texture;

        /// <summary>Multiplies the texture. White leaves the painted colours as they are.</summary>
        public Rgb Tint = new Rgb(1f, 1f, 1f);

        /// <summary>What ambient light is multiplied by in shade.</summary>
        public Rgb Shade = StylePalette.ShadeCool;

        /// <summary>How far light wraps past the terminator, 0 to 1.</summary>
        public float Wrap = 0.35f;

        /// <summary>Strength of the highlight, 0 to 1. 0 means none: most surfaces are matte.</summary>
        public float HighlightStrength;

        /// <summary>Tightness of the highlight, 0 to 1.</summary>
        public float HighlightSize = 0.5f;

        public Rgb HighlightColour = new Rgb(1f, 0.97f, 0.9f);

        /// <summary>Strength of the slow colour drift across the world. 0 switches it off.</summary>
        public float PaintStrength = 0.12f;

        /// <summary>Repeats of the drift per metre. 0.08 repeats every 12.5 m.</summary>
        public float PaintScale = 0.08f;

        /// <summary>Drawn from both sides, for thin shapes such as grass blades.</summary>
        public bool TwoSided;

        /// <summary>
        /// Weathering on faces that look up: what their colour is multiplied by, in soft patches. Stone takes
        /// moss and damp this way without a second material. It works only with paint variation on.
        /// </summary>
        public Rgb TopTint = StylePalette.Weathering;

        /// <summary>0 to 1. 0 leaves the surface without weathering.</summary>
        public float TopTintStrength;
    }

    /// <summary>
    /// The material presets of the style. Matte, painted surfaces throughout; only metal has a
    /// highlight, and a restrained one (Docs/ArtBible.md, section 5). Values are a proposal for
    /// review.
    /// </summary>
    public static class StyleMaterialPresets
    {
        public const string Prefix = "Illustrated_";

        /// <summary>
        /// Raised when the values of the presets change, so that materials generated earlier take the new
        /// values (VisualStyleAssets). 2: material cohesion (Docs/MaterialCohesion.md).
        /// </summary>
        public const int Revision = 2;

        private static List<StyleMaterialPreset> all;

        public static IReadOnlyList<StyleMaterialPreset> All => all ?? (all = Build());

        /// <summary>The preset that stands in for a grey-box material, or null.</summary>
        public static StyleMaterialPreset ForGreyBox(string greyBoxMaterialName)
        {
            foreach (StyleMaterialPreset preset in All)
            {
                if (preset.Replaces != null && preset.Replaces == greyBoxMaterialName)
                {
                    return preset;
                }
            }

            return null;
        }

        public static StyleMaterialPreset Find(string name)
        {
            foreach (StyleMaterialPreset preset in All)
            {
                if (preset.Name == name)
                {
                    return preset;
                }
            }

            return null;
        }

        private static List<StyleMaterialPreset> Build()
        {
            return new List<StyleMaterialPreset>
            {
                new StyleMaterialPreset
                {
                    Name = Prefix + "Stone", Family = MaterialFamily.Stone, Replaces = "Stone",
                    Texture = PaintedTextureRecipe.Create("Stone", PaintedSurface.StoneBlocks, StylePalette.Stone,
                        StylePalette.InkCool, 11),
                    PaintStrength = 0.2f, TopTintStrength = 0.5f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "StoneDark", Family = MaterialFamily.Stone, Replaces = "StoneDark",
                    Texture = PaintedTextureRecipe.Create("StoneDark", PaintedSurface.StoneBlocks, StylePalette.StoneDark,
                        StylePalette.InkCool, 23),
                    PaintStrength = 0.18f, TopTintStrength = 0.4f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Paving", Family = MaterialFamily.Stone, Replaces = "Paving",
                    Texture = PaintedTextureRecipe.Create("Paving", PaintedSurface.Flagstones, StylePalette.Paving,
                        StylePalette.InkWarm, 37),
                    PaintStrength = 0.17f, TopTint = StylePalette.Grime, TopTintStrength = 0.35f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Wood", Family = MaterialFamily.Wood,
                    Texture = PaintedTextureRecipe.Create("Wood", PaintedSurface.Planks, StylePalette.Wood,
                        StylePalette.InkWarm, 41),
                    PaintStrength = 0.12f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Bark", Family = MaterialFamily.Wood, Replaces = "Bark",
                    Texture = PaintedTextureRecipe.Create("Bark", PaintedSurface.Bark, StylePalette.Bark,
                        StylePalette.InkWarm, 43, 256),
                    PaintStrength = 0.1f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Cloth", Family = MaterialFamily.Cloth,
                    Texture = PaintedTextureRecipe.Create("Cloth", PaintedSurface.Cloth, StylePalette.Cloth,
                        StylePalette.InkPlum, 53, 256),
                    Tint = new Rgb(0.93f, 0.90f, 0.88f), Wrap = 0.55f, PaintStrength = 0f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "ClothCool", Family = MaterialFamily.Cloth,
                    Texture = PaintedTextureRecipe.Create("ClothCool", PaintedSurface.Cloth, StylePalette.ClothCool,
                        StylePalette.InkPlum, 59, 256),
                    Tint = new Rgb(0.90f, 0.92f, 0.94f), Wrap = 0.55f, PaintStrength = 0f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Metal", Family = MaterialFamily.Metal,
                    Texture = PaintedTextureRecipe.Create("Metal", PaintedSurface.Metal, StylePalette.Metal,
                        StylePalette.InkCool, 61, 256),
                    Wrap = 0.2f, HighlightStrength = 0.3f, HighlightSize = 0.55f,
                    HighlightColour = new Rgb(1f, 0.98f, 0.94f), PaintStrength = 0f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Foliage", Family = MaterialFamily.Vegetation, Replaces = "Foliage",
                    Texture = PaintedTextureRecipe.Create("Foliage", PaintedSurface.LeafMass, StylePalette.Foliage,
                        StylePalette.InkCool, 67, 256),
                    Tint = new Rgb(0.92f, 0.96f, 0.92f), Shade = StylePalette.ShadeGreen, Wrap = 0.55f, PaintStrength = 0.14f,
                    PaintScale = 0.05f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Grass", Family = MaterialFamily.Vegetation, Replaces = "Grass",
                    Texture = PaintedTextureRecipe.Create("Grass", PaintedSurface.Grass, StylePalette.Grass,
                        StylePalette.InkCool, 71, 256),
                    Shade = StylePalette.ShadeGreen, Wrap = 0.6f, PaintStrength = 0.16f, PaintScale = 0.06f,
                    TwoSided = true,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Moss", Family = MaterialFamily.Vegetation, Replaces = "Moss",
                    Texture = PaintedTextureRecipe.Create("Moss", PaintedSurface.Earth, StylePalette.MossSurface,
                        StylePalette.InkCool, 73, 256),

                    // Duller and nearer to the stone it lies on, so that it reads as growth on the stone.
                    Tint = new Rgb(0.84f, 0.86f, 0.80f), Shade = StylePalette.ShadeGreen, Wrap = 0.55f,
                },
                new StyleMaterialPreset
                {
                    Name = Prefix + "Ground", Family = MaterialFamily.Terrain, Replaces = "Ground",
                    Texture = PaintedTextureRecipe.Create("Ground", PaintedSurface.Turf, StylePalette.Ground,
                        StylePalette.InkWarm, 79, 256),
                    Shade = StylePalette.ShadeGreen, Wrap = 0.5f, PaintStrength = 0.18f, PaintScale = 0.035f,
                },
                new StyleMaterialPreset
                {
                    // Far away and seen through haze: a plain colour is enough, and cheapest.
                    Name = Prefix + "Mountain", Family = MaterialFamily.Terrain, Replaces = "Mountain",
                    Texture = null, Tint = StylePalette.Mountain, Wrap = 0.5f, PaintStrength = 0f,
                },
            };
        }
    }
}
