using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Threefold.VisualStyle
{
    /// <summary>
    /// The only place where the style touches the Universal Render Pipeline's settings at runtime.
    ///
    /// A quality level changes a few values on the active pipeline asset: render scale, shadow
    /// distance, shadow map size, anti-aliasing and HDR. These have public setters in URP 17.6.
    /// The asset is a project file, so the values found there are remembered before the first
    /// change and put back by <see cref="Restore"/>. Nothing here is ever saved to disk.
    /// </summary>
    internal static class UrpQualityBridge
    {
        private static UniversalRenderPipelineAsset captured;
        private static float renderScale;
        private static float shadowDistance;
        private static int shadowResolution;
        private static int antiAliasing;
        private static bool hdr;

        /// <summary>The active pipeline asset, or null if the project is not rendering with URP.</summary>
        public static UniversalRenderPipelineAsset ActiveAsset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        /// <summary>True if the active pipeline asset allows soft shadows. They cannot be enabled at runtime.</summary>
        public static bool SoftShadowsAvailable
        {
            get
            {
                UniversalRenderPipelineAsset asset = ActiveAsset;
                return asset != null && asset.supportsSoftShadows;
            }
        }

        /// <summary>Applies the pipeline part of a quality level. Returns false without URP.</summary>
        public static bool Apply(VisualQualityPreset preset)
        {
            UniversalRenderPipelineAsset asset = ActiveAsset;
            if (asset == null || preset == null)
            {
                return false;
            }

            if (captured != asset)
            {
                // A different asset became active (the quality level changed): put the old one back first.
                Restore();
                captured = asset;
                renderScale = asset.renderScale;
                shadowDistance = asset.shadowDistance;
                shadowResolution = asset.mainLightShadowmapResolution;
                antiAliasing = asset.msaaSampleCount;
                hdr = asset.supportsHDR;
            }

            asset.renderScale = Mathf.Clamp(preset.renderScale, 0.5f, 1f);
            asset.shadowDistance = Mathf.Max(0f, preset.shadowDistance);
            asset.mainLightShadowmapResolution = VisualQualityPreset.NearestShadowResolution(preset.shadowResolution);
            asset.msaaSampleCount = VisualQualityPreset.NearestSampleCount(preset.antiAliasing);
            asset.supportsHDR = preset.hdr;
            return true;
        }

        /// <summary>Puts back the values the pipeline asset had before the first <see cref="Apply"/>.</summary>
        public static void Restore()
        {
            if (captured == null)
            {
                return;
            }

            captured.renderScale = renderScale;
            captured.shadowDistance = shadowDistance;
            captured.mainLightShadowmapResolution = shadowResolution;
            captured.msaaSampleCount = antiAliasing;
            captured.supportsHDR = hdr;
            captured = null;
        }

        /// <summary>Switches post-processing for one camera.</summary>
        public static void SetPostProcessing(Camera camera, bool enabled)
        {
            if (camera == null)
            {
                return;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = enabled;
            }
        }

        /// <summary>Sets how the sun casts shadows. Soft shadows use the four-sample filter URP recommends for mobile.</summary>
        public static void SetSunShadows(Light sun, StyleShadows mode)
        {
            if (sun == null)
            {
                return;
            }

            switch (mode)
            {
                case StyleShadows.Off:
                    sun.shadows = LightShadows.None;
                    break;

                case StyleShadows.Hard:
                    sun.shadows = LightShadows.Hard;
                    break;

                default:
                    sun.shadows = LightShadows.Soft;
                    if (sun.TryGetComponent(out UniversalAdditionalLightData data))
                    {
                        data.softShadowQuality = SoftShadowQuality.Low;
                    }

                    break;
            }
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            captured = null;
        }
    }
}
