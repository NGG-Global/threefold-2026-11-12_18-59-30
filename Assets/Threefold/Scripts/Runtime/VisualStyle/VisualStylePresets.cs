using System;
using UnityEngine;

namespace Threefold.VisualStyle
{
    /// <summary>How the sun casts shadows at a quality level.</summary>
    public enum StyleShadows
    {
        /// <summary>No shadows. The cheapest; forms are still shaped by the soft light response.</summary>
        Off,

        /// <summary>Shadows with a crisp edge.</summary>
        Hard,

        /// <summary>Shadows with a softened edge (four samples). Needs soft shadows enabled in the URP asset.</summary>
        Soft,
    }

    /// <summary>
    /// A lighting condition: sun, ambient light and atmosphere. The art direction asks for soft,
    /// directional light, warm against cool, with distance haze (Docs/ArtBible.md, section 7).
    /// Two are provided so the style can be judged under more than one light, as the art bible's
    /// prototype test requires.
    /// </summary>
    [Serializable]
    public sealed class LightingPreset
    {
        public string displayName = "Golden Afternoon";

        [Header("Sun")]
        public Color sunColor = new Color(1f, 0.93f, 0.80f);

        [Min(0f)]
        public float sunIntensity = 1.1f;

        [Tooltip("Angle above the horizon, in degrees.")]
        [Range(5f, 90f)]
        public float sunElevation = 38f;

        [Tooltip("Compass direction the light travels towards, in degrees.")]
        public float sunHeading = -38f;

        [Tooltip("1 gives the darkest shadows. Lower values let more light into them.")]
        [Range(0f, 1f)]
        public float shadowStrength = 0.8f;

        // Ambient light is kept strong against the sun: shade stays about half as bright as lit
        // surfaces on screen, so painted colour remains readable in shadow.
        [Header("Ambient light (sky, horizon, ground)")]
        public Color skyColor = new Color(0.60f, 0.70f, 0.88f);
        public Color horizonColor = new Color(0.60f, 0.60f, 0.58f);
        public Color groundColor = new Color(0.44f, 0.39f, 0.32f);

        [Header("Atmosphere")]
        public Color fogColor = new Color(0.66f, 0.72f, 0.78f);

        [Tooltip("Distance at which haze begins, in metres. Kept beyond fighting distance so it never hides a silhouette that matters.")]
        [Min(0f)]
        public float fogStart = 26f;

        [Min(1f)]
        public float fogEnd = 360f;

        [Header("Sky")]
        public Color skyTint = new Color(0.45f, 0.53f, 0.66f);

        [Range(0f, 4f)]
        public float skyExposure = 1.1f;

        /// <summary>Late afternoon: a low, warm sun against cool shade. The default.</summary>
        public static LightingPreset GoldenAfternoon()
        {
            return new LightingPreset();
        }

        /// <summary>A bright overcast morning: cooler, flatter light with soft, pale shadows.</summary>
        public static LightingPreset OvercastMorning()
        {
            return new LightingPreset
            {
                displayName = "Overcast Morning",
                sunColor = new Color(0.92f, 0.95f, 1f),
                sunIntensity = 0.7f,
                sunElevation = 58f,
                sunHeading = 25f,
                shadowStrength = 0.5f,
                skyColor = new Color(0.76f, 0.80f, 0.86f),
                horizonColor = new Color(0.68f, 0.69f, 0.70f),
                groundColor = new Color(0.46f, 0.46f, 0.44f),
                fogColor = new Color(0.74f, 0.78f, 0.81f),
                fogStart = 20f,
                fogEnd = 280f,
                skyTint = new Color(0.62f, 0.66f, 0.72f),
                skyExposure = 0.95f,
            };
        }
    }

    /// <summary>
    /// A quality level: which rendering features of the style are on, and how much they may cost.
    /// The three levels below are starting points chosen by reasoning about cost. They have not
    /// been measured on a device; the art bible asks for tiers to be settled by profiling.
    /// </summary>
    [Serializable]
    public sealed class VisualQualityPreset
    {
        public string displayName = "Medium";

        [Header("Linework")]
        [Tooltip("Contour lines around selected characters. One extra draw of each outlined mesh.")]
        public bool outlines = true;

        [Header("Shadows")]
        public StyleShadows shadows = StyleShadows.Soft;

        [Tooltip("How far from the camera shadows are drawn, in metres. Shorter is sharper and cheaper.")]
        [Min(0f)]
        public float shadowDistance = 35f;

        [Tooltip("Size of the shadow map: 256, 512, 1024, 2048 or 4096.")]
        public int shadowResolution = 1024;

        [Header("Resolution")]
        [Tooltip("Share of the screen's resolution that the 3D view is drawn at. The largest single lever on a phone.")]
        [Range(0.5f, 1f)]
        public float renderScale = 0.85f;

        [Tooltip("Anti-aliasing samples: 1 (off), 2 or 4. Thin lines shimmer less with it.")]
        public int antiAliasing = 2;

        [Header("Post-processing")]
        [Tooltip("Bloom, tone mapping and vignette from the scene's volume. A full-screen cost.")]
        public bool postProcessing;

        [Tooltip("High dynamic range rendering. Only needed for post-processing.")]
        public bool hdr;

        /// <summary>For older or low-end phones: no outlines, no shadows filtering, lower resolution.</summary>
        public static VisualQualityPreset Low()
        {
            return new VisualQualityPreset
            {
                displayName = "Low",
                outlines = false,
                shadows = StyleShadows.Hard,
                shadowDistance = 22f,
                shadowResolution = 512,
                renderScale = 0.7f,
                antiAliasing = 1,
                postProcessing = false,
                hdr = false,
            };
        }

        /// <summary>The intended level for a mid-range Android phone.</summary>
        public static VisualQualityPreset Medium()
        {
            return new VisualQualityPreset();
        }

        /// <summary>For high-end phones and the Editor: full resolution and post-processing.</summary>
        public static VisualQualityPreset High()
        {
            return new VisualQualityPreset
            {
                displayName = "High",
                outlines = true,
                shadows = StyleShadows.Soft,
                shadowDistance = 50f,
                shadowResolution = 2048,
                renderScale = 1f,
                antiAliasing = 4,
                postProcessing = true,
                hdr = true,
            };
        }

        /// <summary>The shadow map sizes URP accepts are powers of two from 256 to 4096; returns the smallest that fits.</summary>
        public static int NearestShadowResolution(int requested)
        {
            int size = 256;
            while (size < 4096 && size < requested)
            {
                size *= 2;
            }

            return size;
        }

        /// <summary>URP accepts 1, 2, 4 or 8 anti-aliasing samples; returns the largest not above the request.</summary>
        public static int NearestSampleCount(int requested)
        {
            if (requested >= 8)
            {
                return 8;
            }

            if (requested >= 4)
            {
                return 4;
            }

            return requested >= 2 ? 2 : 1;
        }

        /// <summary>
        /// Chooses a starting level from what the device reports. A rough rule, not a measurement:
        /// little memory or an old graphics level gives Low, everything else Medium. High is never
        /// chosen automatically on a phone.
        /// </summary>
        /// <returns>0 for Low, 1 for Medium, 2 for High.</returns>
        public static int SuggestLevel(bool isMobile, int systemMemoryMegabytes, int graphicsShaderLevel)
        {
            if (!isMobile)
            {
                return 2;
            }

            if (systemMemoryMegabytes > 0 && systemMemoryMegabytes < 3500)
            {
                return 0;
            }

            if (graphicsShaderLevel > 0 && graphicsShaderLevel < 35)
            {
                return 0;
            }

            return 1;
        }
    }
}
