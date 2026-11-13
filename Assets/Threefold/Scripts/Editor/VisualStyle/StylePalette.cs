using System;

namespace Threefold.EditorTools
{
    /// <summary>
    /// A colour as it is written in a texture or picked in the Inspector (sRGB, 0 to 1). Plain
    /// data without any Unity type, so the texture generator and the previews run outside Unity.
    /// </summary>
    public readonly struct Rgb
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>Perceived brightness, 0 to 1 (Rec. 709 weights on the sRGB values; adequate for comparing swatches).</summary>
        public float Luma => 0.2126f * R + 0.7152f * G + 0.0722f * B;

        public Rgb Scaled(float factor)
        {
            return new Rgb(R * factor, G * factor, B * factor);
        }

        public Rgb Clamped()
        {
            return new Rgb(Clamp01(R), Clamp01(G), Clamp01(B));
        }

        public static Rgb Lerp(Rgb a, Rgb b, float t)
        {
            t = Clamp01(t);
            return new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        public static Rgb operator *(Rgb a, Rgb b)
        {
            return new Rgb(a.R * b.R, a.G * b.G, a.B * b.B);
        }

        public static Rgb operator +(Rgb a, Rgb b)
        {
            return new Rgb(a.R + b.R, a.G + b.G, a.B + b.B);
        }

        /// <summary>Converts one sRGB channel to linear light, as the renderer does before lighting.</summary>
        public static float ToLinear(float srgb)
        {
            srgb = Clamp01(srgb);
            return srgb <= 0.04045f ? srgb / 12.92f : (float)Math.Pow((srgb + 0.055f) / 1.055f, 2.4);
        }

        /// <summary>Converts one linear channel back to sRGB for display.</summary>
        public static float ToSrgb(float linear)
        {
            linear = Clamp01(linear);
            return linear <= 0.0031308f ? linear * 12.92f : 1.055f * (float)Math.Pow(linear, 1.0 / 2.4) - 0.055f;
        }

        public Rgb Linear => new Rgb(ToLinear(R), ToLinear(G), ToLinear(B));

        public Rgb Srgb => new Rgb(ToSrgb(R), ToSrgb(G), ToSrgb(B));

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }

    /// <summary>
    /// The colours of the style, built from the palette families the art bible lists as
    /// provisional (Docs/ArtBible.md, section 7): warm parchment and ivory, weathered terracotta,
    /// moss and olive green, slate blue, desaturated teal, dark umber, and amber accents.
    ///
    /// These swatches are a proposal for review, not approved colours. Major surfaces stay in
    /// earth and mineral hues; clearer colour is kept for characters, magic and points of interest.
    /// </summary>
    public static class StylePalette
    {
        // The seven families.
        public static readonly Rgb Parchment = new Rgb(0.90f, 0.85f, 0.72f);
        public static readonly Rgb Terracotta = new Rgb(0.71f, 0.44f, 0.33f);
        public static readonly Rgb Moss = new Rgb(0.42f, 0.47f, 0.25f);
        public static readonly Rgb SlateBlue = new Rgb(0.38f, 0.45f, 0.56f);
        public static readonly Rgb Teal = new Rgb(0.33f, 0.50f, 0.50f);
        public static readonly Rgb Umber = new Rgb(0.24f, 0.18f, 0.14f);
        public static readonly Rgb Amber = new Rgb(0.90f, 0.68f, 0.28f);

        // Line colours: coloured darks, never pure black (section 6).
        public static readonly Rgb InkWarm = new Rgb(0.22f, 0.15f, 0.11f);
        public static readonly Rgb InkCool = new Rgb(0.20f, 0.23f, 0.31f);
        public static readonly Rgb InkPlum = new Rgb(0.27f, 0.18f, 0.26f);

        /// <summary>The contour line around characters: deep warm brown.</summary>
        public static readonly Rgb Contour = new Rgb(0.17f, 0.11f, 0.09f);

        // Surfaces.
        public static readonly Rgb Stone = new Rgb(0.75f, 0.70f, 0.60f);
        public static readonly Rgb StoneDark = new Rgb(0.48f, 0.50f, 0.54f);
        public static readonly Rgb Paving = new Rgb(0.66f, 0.62f, 0.54f);
        public static readonly Rgb Wood = new Rgb(0.52f, 0.38f, 0.24f);
        public static readonly Rgb Bark = new Rgb(0.37f, 0.29f, 0.22f);
        public static readonly Rgb Cloth = new Rgb(0.66f, 0.27f, 0.26f);
        public static readonly Rgb ClothCool = new Rgb(0.33f, 0.42f, 0.55f);
        public static readonly Rgb Metal = new Rgb(0.56f, 0.57f, 0.60f);
        public static readonly Rgb Foliage = new Rgb(0.30f, 0.43f, 0.25f);
        public static readonly Rgb Grass = new Rgb(0.46f, 0.54f, 0.27f);
        public static readonly Rgb Ground = new Rgb(0.41f, 0.45f, 0.26f);
        public static readonly Rgb MossSurface = new Rgb(0.40f, 0.47f, 0.24f);
        public static readonly Rgb Mountain = new Rgb(0.50f, 0.57f, 0.67f);

        /// <summary>What ambient light is multiplied by in shade: cool, to set off the warm sun.</summary>
        public static readonly Rgb ShadeCool = new Rgb(0.78f, 0.85f, 1.00f);

        /// <summary>A warmer shade for foliage, whose shadows are filled by light through the leaves.</summary>
        public static readonly Rgb ShadeGreen = new Rgb(0.80f, 0.92f, 0.88f);
    }
}
