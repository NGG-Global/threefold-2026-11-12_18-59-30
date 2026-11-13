using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Threefold.VisualStyle
{
    /// <summary>
    /// Runs the visual style in a scene: it applies a quality level (resolution, shadows,
    /// anti-aliasing, post-processing, contour lines) and a lighting condition (sun, ambient light,
    /// haze, sky), and lets both be switched while playing, so the style can be compared with and
    /// without each feature on a device.
    ///
    /// Everything it changes is put back when it is disabled: the pipeline asset's values, the
    /// global outline switch and the sky material. It never writes to a project file.
    ///
    /// Keys, for testing: F6 next quality level, F7 contour lines on or off, F8 next lighting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisualStyleController : MonoBehaviour
    {
        private static readonly int OutlineSuppressId = Shader.PropertyToID("_ThreefoldOutlineSuppress");
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int SkyExposureId = Shader.PropertyToID("_Exposure");

        [Header("Scene")]
        [Tooltip("The directional light that acts as the sun.")]
        [SerializeField] private Light sun;

        [Tooltip("The gameplay camera. If empty, the main camera is used.")]
        [SerializeField] private Camera viewCamera;

        [Header("Quality")]
        [SerializeField] private VisualQualityPreset[] qualityLevels =
        {
            VisualQualityPreset.Low(), VisualQualityPreset.Medium(), VisualQualityPreset.High(),
        };

        [Tooltip("Level used at the start, counted from 0, unless one is chosen for the device.")]
        [Min(0)]
        [SerializeField] private int qualityLevel = 1;

        [Tooltip("Pick the starting level from the device's memory and graphics level. A rough rule; see the documentation.")]
        [SerializeField] private bool chooseQualityForDevice = true;

        [Header("Lighting")]
        [SerializeField] private LightingPreset[] lightingPresets =
        {
            LightingPreset.GoldenAfternoon(), LightingPreset.OvercastMorning(),
        };

        [Min(0)]
        [SerializeField] private int lighting;

        [Header("Testing")]
        [Tooltip("F6, F7 and F8 switch quality, contour lines and lighting.")]
        [SerializeField] private bool testKeys = true;

        private Material runtimeSky;
        private Material originalSky;
        private bool outlinesOverride;
        private bool outlinesOverrideValue;
        private bool softShadowWarningGiven;

        /// <summary>Raised after the quality level, the lighting or the contour lines changed.</summary>
        public event Action Changed;

        public int QualityLevel => qualityLevel;
        public int QualityLevelCount => qualityLevels != null ? qualityLevels.Length : 0;
        public int Lighting => lighting;
        public int LightingCount => lightingPresets != null ? lightingPresets.Length : 0;

        public VisualQualityPreset CurrentQuality => QualityLevelCount > 0
            ? qualityLevels[Mathf.Clamp(qualityLevel, 0, QualityLevelCount - 1)]
            : null;

        public LightingPreset CurrentLighting => LightingCount > 0
            ? lightingPresets[Mathf.Clamp(lighting, 0, LightingCount - 1)]
            : null;

        /// <summary>Whether contour lines are drawn: the quality level's setting, unless switched by hand.</summary>
        public bool OutlinesEnabled
        {
            get
            {
                if (outlinesOverride)
                {
                    return outlinesOverrideValue;
                }

                VisualQualityPreset preset = CurrentQuality;
                return preset == null || preset.outlines;
            }
        }

        public Light Sun => sun;

        /// <summary>Connects the controller to the scene. Called by the setup tooling.</summary>
        public void Bind(Light sunLight, Camera playerCamera)
        {
            sun = sunLight;
            viewCamera = playerCamera;
        }

        /// <summary>Fills only empty references. Called by the setup tooling.</summary>
        public void BindMissing(Light sunLight, Camera playerCamera)
        {
            if (sun == null)
            {
                sun = sunLight;
            }

            if (viewCamera == null)
            {
                viewCamera = playerCamera;
            }
        }

        public void SetQuality(int level)
        {
            if (QualityLevelCount == 0)
            {
                return;
            }

            qualityLevel = Mathf.Clamp(level, 0, QualityLevelCount - 1);
            outlinesOverride = false;
            ApplyQuality();
            Changed?.Invoke();
        }

        public void NextQuality()
        {
            if (QualityLevelCount > 0)
            {
                SetQuality((qualityLevel + 1) % QualityLevelCount);
            }
        }

        public void SetLighting(int index)
        {
            if (LightingCount == 0)
            {
                return;
            }

            lighting = Mathf.Clamp(index, 0, LightingCount - 1);
            ApplyLighting();

            // The lighting preset sets shadow strength; the quality level decides whether shadows exist.
            ApplyQuality();
            Changed?.Invoke();
        }

        public void NextLighting()
        {
            if (LightingCount > 0)
            {
                SetLighting((lighting + 1) % LightingCount);
            }
        }

        /// <summary>Switches contour lines by hand, until the next change of quality level.</summary>
        public void SetOutlines(bool enabled)
        {
            outlinesOverride = true;
            outlinesOverrideValue = enabled;
            ApplyOutlines();
            Changed?.Invoke();
        }

        /// <summary>
        /// Applies a lighting condition to the scene: sun, ambient light, haze and sky tint. Also
        /// used by the setup tooling in the Editor, where the result is saved with the scene.
        /// </summary>
        /// <param name="sky">The sky material to tint, or null to leave the sky alone.</param>
        public static void ApplyLighting(LightingPreset preset, Light sunLight, Material sky)
        {
            if (preset == null)
            {
                return;
            }

            if (sunLight != null)
            {
                sunLight.color = preset.sunColor;
                sunLight.intensity = preset.sunIntensity;
                sunLight.shadowStrength = preset.shadowStrength;
                sunLight.transform.rotation = Quaternion.Euler(preset.sunElevation, preset.sunHeading, 0f);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = preset.skyColor;
            RenderSettings.ambientEquatorColor = preset.horizonColor;
            RenderSettings.ambientGroundColor = preset.groundColor;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = preset.fogColor;
            RenderSettings.fogStartDistance = preset.fogStart;
            RenderSettings.fogEndDistance = Mathf.Max(preset.fogStart + 1f, preset.fogEnd);

            if (sky != null)
            {
                if (sky.HasProperty(SkyTintId))
                {
                    sky.SetColor(SkyTintId, preset.skyTint);
                }

                if (sky.HasProperty(SkyExposureId))
                {
                    sky.SetFloat(SkyExposureId, preset.skyExposure);
                }
            }
        }

        private void OnEnable()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (chooseQualityForDevice && QualityLevelCount > 0)
            {
                int suggested = VisualQualityPreset.SuggestLevel(Application.isMobilePlatform, SystemInfo.systemMemorySize,
                    SystemInfo.graphicsShaderLevel);
                qualityLevel = Mathf.Clamp(suggested, 0, QualityLevelCount - 1);
            }

            ApplyLighting();
            ApplyQuality();
        }

        private void OnDisable()
        {
            UrpQualityBridge.Restore();
            Shader.SetGlobalFloat(OutlineSuppressId, 0f);

            if (runtimeSky != null)
            {
                if (RenderSettings.skybox == runtimeSky)
                {
                    RenderSettings.skybox = originalSky;
                }

                Destroy(runtimeSky);
                runtimeSky = null;
            }
        }

        private void Update()
        {
            if (!testKeys)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f6Key.wasPressedThisFrame)
            {
                NextQuality();
            }
            else if (keyboard.f7Key.wasPressedThisFrame)
            {
                SetOutlines(!OutlinesEnabled);
            }
            else if (keyboard.f8Key.wasPressedThisFrame)
            {
                NextLighting();
            }
        }

        private void ApplyQuality()
        {
            VisualQualityPreset preset = CurrentQuality;
            if (preset == null)
            {
                return;
            }

            UrpQualityBridge.Apply(preset);
            UrpQualityBridge.SetPostProcessing(viewCamera, preset.postProcessing);
            UrpQualityBridge.SetSunShadows(sun, preset.shadows);
            ApplyOutlines();

            if (preset.shadows == StyleShadows.Soft && !UrpQualityBridge.SoftShadowsAvailable && !softShadowWarningGiven)
            {
                softShadowWarningGiven = true;
                Debug.Log("[Threefold] Soft shadows are switched off in the active URP asset, so shadows have crisp edges. " +
                          "Threefold > Visual Style > Check Rendering Settings explains how to enable them.", this);
            }
        }

        private void ApplyOutlines()
        {
            Shader.SetGlobalFloat(OutlineSuppressId, OutlinesEnabled ? 0f : 1f);
        }

        private void ApplyLighting()
        {
            LightingPreset preset = CurrentLighting;
            if (preset == null)
            {
                return;
            }

            // The sky material is a project file. While playing, a copy is tinted instead.
            if (runtimeSky == null && RenderSettings.skybox != null)
            {
                originalSky = RenderSettings.skybox;
                runtimeSky = new Material(originalSky) { name = originalSky.name + " (Runtime)" };
                RenderSettings.skybox = runtimeSky;
            }

            ApplyLighting(preset, sun, runtimeSky);
        }
    }
}
