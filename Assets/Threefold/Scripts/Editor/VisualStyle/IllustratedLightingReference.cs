using System;

namespace Threefold.EditorTools
{
    /// <summary>
    /// The arithmetic of the shader "Threefold/Illustrated Lit", written again in C#. Shaders cannot
    /// be run outside Unity, so this copy is what the tests check and what the preview tool renders
    /// with. It mirrors IllustratedLighting.hlsl and IllustratedInput.hlsl line for line; change
    /// both together. All colours here are in linear light, as in the shader.
    /// </summary>
    public static class IllustratedLightingReference
    {
        /// <summary>Same value as ILLUSTRATED_SHADOW_FADE in the shader.</summary>
        public const float ShadowFade = 8f;

        /// <summary>
        /// Share of a light that reaches a surface from its angle alone. <paramref name="ndl"/> is
        /// the cosine between the surface normal and the direction to the light.
        /// </summary>
        public static float Ramp(float ndl, float wrap)
        {
            float wrapped = Clamp01((ndl + wrap) / (1f + wrap));
            return wrapped * wrapped * (3f - 2f * wrapped);
        }

        /// <summary>The shadow map only counts on the lit side of the terminator, where it fades in.</summary>
        public static float Shadow(float ndl, float shadowAttenuation)
        {
            float weight = Clamp01(ndl * ShadowFade);
            return 1f + (shadowAttenuation - 1f) * weight;
        }

        public static float LightAmount(float ndl, float wrap, float shadowAttenuation, float distanceAttenuation = 1f)
        {
            return Ramp(ndl, wrap) * Shadow(ndl, shadowAttenuation) * distanceAttenuation;
        }

        /// <summary>Shape of the highlight, 0 to 1, from the cosine between normal and half vector.</summary>
        public static float HighlightShape(float ndh, float highlightSize)
        {
            double exponent = Math.Pow(2.0, 10.0 * highlightSize + 1.0);
            return (float)Math.Pow(Clamp01(ndh), exponent);
        }

        /// <summary>
        /// The colour of a surface under one main light and ambient light.
        /// </summary>
        /// <param name="albedo">Surface colour, linear.</param>
        /// <param name="lightColour">Light colour times intensity, linear.</param>
        /// <param name="ambient">Ambient light arriving at this normal, linear.</param>
        /// <param name="shadeColour">What ambient light is multiplied by where the main light is absent.</param>
        /// <param name="highlightColour">Highlight colour, linear.</param>
        /// <param name="highlightStrength">0 switches the highlight off.</param>
        public static Rgb Shade(Rgb albedo, Rgb lightColour, Rgb ambient, Rgb shadeColour, float ndl, float ndh,
            float wrap, float shadowAttenuation, Rgb highlightColour, float highlightStrength, float highlightSize)
        {
            float amount = LightAmount(ndl, wrap, shadowAttenuation);
            Rgb direct = lightColour.Scaled(amount);
            Rgb fill = ambient * Rgb.Lerp(shadeColour, new Rgb(1f, 1f, 1f), amount);
            Rgb colour = albedo * (direct + fill);

            if (highlightStrength > 0f)
            {
                float shape = HighlightShape(ndh, highlightSize);
                colour += (lightColour * highlightColour).Scaled(highlightStrength * shape * amount);
            }

            return colour;
        }

        /// <summary>
        /// The shader's paint variation: a slow drift of value (<paramref name="paintR"/>) and of
        /// warmth (<paramref name="paintG"/>), both read from the variation map as 0 to 1.
        /// </summary>
        public static Rgb ApplyPaintVariation(Rgb albedo, float paintR, float paintG, float strength)
        {
            float value = 1f + (paintR - 0.5f) * (2f * strength);
            float warmth = (paintG - 0.5f) * strength;
            return new Rgb(
                Math.Max(0f, albedo.R * value * (1f + warmth)),
                Math.Max(0f, albedo.G * value * (1f + warmth * 0.2f)),
                Math.Max(0f, albedo.B * value * (1f - warmth)));
        }

        /// <summary>Where the shader samples the variation map for a point in the world.</summary>
        public static void PaintVariationUv(float worldX, float worldY, float worldZ, float repeatsPerMetre, out float u,
            out float v)
        {
            u = (worldX + worldY * 0.37f) * repeatsPerMetre;
            v = (worldZ + worldY * 0.61f) * repeatsPerMetre;
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }

    /// <summary>
    /// The contour line's width, as the outline pass of the shader computes it. Mirrors
    /// IllustratedOutlinePass.hlsl; change both together.
    /// </summary>
    public static class OutlineRules
    {
        /// <summary>1 up to the fade start, 0 from the fade end, smooth in between.</summary>
        public static float Fade(float distance, float fadeStart, float fadeEnd)
        {
            if (fadeEnd <= fadeStart)
            {
                return distance < fadeEnd ? 1f : 0f;
            }

            float t = (distance - fadeStart) / (fadeEnd - fadeStart);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>Line width as a share of the screen's height.</summary>
        /// <param name="suppress">The game-wide switch: 0 draws outlines, 1 hides them.</param>
        public static float WidthShare(float width, float distance, float fadeStart, float fadeEnd, float suppress)
        {
            suppress = suppress < 0f ? 0f : (suppress > 1f ? 1f : suppress);
            return width * Fade(distance, fadeStart, fadeEnd) * (1f - suppress);
        }

        /// <summary>Line width in pixels on a screen of the given height. It does not depend on distance until the fade.</summary>
        public static float WidthPixels(float width, float screenHeight, float distance, float fadeStart, float fadeEnd,
            float suppress = 0f)
        {
            return WidthShare(width, distance, fadeStart, fadeEnd, suppress) * screenHeight;
        }
    }
}
