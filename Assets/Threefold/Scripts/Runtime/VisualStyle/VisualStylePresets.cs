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
        /// <summary>
        /// Raised when the values of the two provided presets change, so that a scene's controller made
        /// earlier takes the new values (VisualStyleController.UpgradeLighting).
        /// 2: warmer sun against cooler shade, haze and sky brought together (Docs/MaterialCohesion.md).
        /// </summary>
        public const int Revision = 2;

        public string displayName = "Golden Afternoon";

        [Header("Sun")]
        public Color sunColor = new Color(1f, 0.90f, 0.74f);

        [Min(0f)]
        public float sunIntensity = 1.15f;

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
        public Color skyColor = new Color(0.55f, 0.68f, 0.93f);
        public Color horizonColor = new Color(0.60f, 0.60f, 0.58f);
        public Color groundColor = new Color(0.50f, 0.42f, 0.31f);

        [Header("Atmosphere")]
        public Color fogColor = new Color(0.69f, 0.76f, 0.82f);

        [Tooltip("Distance at which haze begins, in metres. Kept beyond fighting distance so it never hides a silhouette that matters.")]
        [Min(0f)]
        public float fogStart = 26f;

        [Min(1f)]
        public float fogEnd = 360f;

        [Header("Sky")]
        public Color skyTint = new Color(0.45f, 0.53f, 0.66f);

        [Range(0f, 4f)]
        public float skyExposure = 1.1f;

        [Tooltip("The sky's colour below the horizon. Near the haze's colour, so that far ridges sink into the sky instead of standing cut out against it.")]
        public Color skyGroundColor = new Color(0.63f, 0.70f, 0.76f);

        [Tooltip("How thick the sky's air is. Higher gives a yellower, heavier horizon.")]
        [Range(0f, 5f)]
        public float skyAtmosphere = 1.0f;

        /// <summary>
        /// Replaces every preset that carries the name of a provided one (Golden Afternoon, Overcast Morning)
        /// with that preset's present values. Presets with other names, which somebody added, are kept.
        /// Returns true if any was replaced.
        /// </summary>
        public static bool Upgrade(LightingPreset[] presets)
        {
            if (presets == null)
            {
                return false;
            }

            bool changed = false;
            LightingPreset[] provided = { GoldenAfternoon(), OvercastMorning() };
            for (int i = 0; i < presets.Length; i++)
            {
                foreach (LightingPreset current in provided)
                {
                    if (presets[i] != null && presets[i].displayName == current.displayName)
                    {
                        presets[i] = current;
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

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
                skyGroundColor = new Color(0.70f, 0.74f, 0.77f),
                skyAtmosphere = 1.2f,
            };
        }
    }

    /// <summary>
    /// A quality level: which rendering features of the style are on, and how much they may cost.
    /// The player sees the three as graphics presets: Performance, Balanced and Quality
    /// (<see cref="GraphicsPresets"/>). They are starting points chosen by reasoning about cost.
    /// They have not been measured on a device; the art bible asks for tiers to be settled by profiling.
    /// </summary>
    [Serializable]
    public sealed class VisualQualityPreset
    {
        public string displayName = "Balanced";

        [Header("Frame rate")]
        [Tooltip("Frames per second this preset aims for: 30 or 60. 0: the level's default (30 for the lowest, 60 for the others). " +
                 "A target of 60 that the device cannot keep is lowered to 30 for the session.")]
        [Min(0)]
        public int targetFrameRate;

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

        /// <summary>
        /// Performance, for less capable phones: 30 frames per second, a lower resolution, hard short shadows,
        /// no anti-aliasing or post-processing. The contour lines around the characters stay: they are part
        /// of the illustrated look and cost one extra draw of a few meshes.
        /// </summary>
        public static VisualQualityPreset Low()
        {
            return new VisualQualityPreset
            {
                displayName = "Performance",
                targetFrameRate = GraphicsPresets.LowFrameRate,
                outlines = true,
                shadows = StyleShadows.Hard,
                shadowDistance = 22f,
                shadowResolution = 512,
                renderScale = 0.7f,
                antiAliasing = 1,
                postProcessing = false,
                hdr = false,
            };
        }

        /// <summary>Balanced, the intended preset for a capable Android phone: 60 frames per second.</summary>
        public static VisualQualityPreset Medium()
        {
            return new VisualQualityPreset { displayName = "Balanced", targetFrameRate = GraphicsPresets.HighFrameRate };
        }

        /// <summary>Quality, for high-end phones and the Editor: full resolution, finer shadows and post-processing, at 60.</summary>
        public static VisualQualityPreset High()
        {
            return new VisualQualityPreset
            {
                displayName = "Quality",
                targetFrameRate = GraphicsPresets.HighFrameRate,
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
