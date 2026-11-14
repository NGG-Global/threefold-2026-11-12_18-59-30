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
    /// What is chosen with them, or on the test panel, is kept for the rest of the run, so it
    /// survives a change of scene through a portal.
    ///
    /// Since LM-08 the quality levels are the player's graphics presets (Performance, Balanced,
    /// Quality; see <see cref="GraphicsPresets"/>). Each sets a frame-rate target: Unity on Android
    /// otherwise runs at its platform default, which is 30 frames per second. A choice made in the
    /// graphics menu is kept between launches. On a phone a target of 60 that cannot be kept is
    /// lowered to a steady 30 for the session (<see cref="FramePacingGuard"/>).
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

        [Tooltip("On a phone, lower a 60 frames-per-second target to a steady 30 if the device cannot keep 60.")]
        [SerializeField] private bool guardFrameRate = true;

        [Tooltip("The version of the quality levels' values. Raised by the setup tooling when it brings them up to date.")]
        [SerializeField] private int presetVersion;

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

        // The controller whose settings are in force. During a change of scene the new scene's
        // controller may be enabled before the old one is disabled; only the one in force may put
        // the shared settings back, or it would undo what the new one has just applied.
        private static VisualStyleController active;

        // Watches the frame rate for the whole run, so a step down holds across a change of scene.
        private static FramePacingGuard pacing;

        // What was chosen by hand in this run. -1 means nothing was chosen.
        private static int chosenQuality = -1;
        private static int chosenLighting = -1;
        private static int chosenOutlines = -1;

        private Material runtimeSky;
        private Material originalSky;
        private bool outlinesOverride;
        private bool outlinesOverrideValue;
        private bool softShadowWarningGiven;

        /// <summary>Raised after the quality level, the lighting or the contour lines changed.</summary>
        public event Action Changed;

        /// <summary>The controller of the open scene, or null. The graphics menu finds it here.</summary>
        public static VisualStyleController Active => active;

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

        /// <summary>The player-facing name of the current level: Performance, Balanced or Quality.</summary>
        public string PresetName => QualityLevelName(qualityLevel);

        public string QualityLevelName(int level)
        {
            VisualQualityPreset preset = QualityLevelCount > 0 ? qualityLevels[Mathf.Clamp(level, 0, QualityLevelCount - 1)] : null;
            return GraphicsPresets.Name(level, QualityLevelCount, preset != null ? preset.displayName : null);
        }

        /// <summary>The frame rate the current level asks for.</summary>
        public int RequestedFrameRate => GraphicsPresets.FrameRate(CurrentQuality, qualityLevel, QualityLevelCount);

        /// <summary>The frame rate aimed for now: the requested one, or 30 once the guard has stepped down.</summary>
        public int TargetFrameRate => pacing != null ? pacing.Current : RequestedFrameRate;

        /// <summary>True when the device could not keep 60 and the target was lowered to 30 for this session.</summary>
        public bool FrameRateFellBack => pacing != null && pacing.FellBack;

        /// <summary>The average frame time of the guard's last window, in milliseconds; 0 before the first.</summary>
        public float LastPacingWindowMs => pacing != null ? pacing.LastWindowMs : 0f;

        public int PresetVersion => presetVersion;

        /// <summary>
        /// Brings quality levels made before LM-08 up to date once: player-facing names, frame-rate targets and
        /// contour lines on the lowest level. Other values are kept. Called by the setup tooling. Returns true if
        /// any value changed (the version alone does not count).
        /// </summary>
        public bool UpgradePresets()
        {
            if (presetVersion >= GraphicsPresets.DataVersion)
            {
                return false;
            }

            // Kept at 0 by default, so a controller saved before LM-08 (which has no version) is upgraded once.
            presetVersion = GraphicsPresets.DataVersion;
            return GraphicsPresets.Upgrade(qualityLevels);
        }

        /// <summary>
        /// The player chose a graphics preset: it is applied, its frame-rate target gets a fresh chance, and the
        /// choice is kept between launches.
        /// </summary>
        public void ChoosePreset(int level)
        {
            SetQuality(level);
            PlayerPrefs.SetInt(GraphicsPresets.PreferenceKey, qualityLevel);
            PlayerPrefs.Save();
        }

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
            chosenQuality = qualityLevel;
            chosenOutlines = -1;
            EnsurePacing().Request(RequestedFrameRate);
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
            chosenLighting = lighting;
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
            chosenOutlines = enabled ? 1 : 0;
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

            active = this;

            // A choice made earlier in this run comes first, then the player's saved choice, then the device.
            if (QualityLevelCount > 0)
            {
                int suggested = chooseQualityForDevice
                    ? VisualQualityPreset.SuggestLevel(Application.isMobilePlatform, SystemInfo.systemMemorySize, SystemInfo.graphicsShaderLevel)
                    : qualityLevel;
                int saved = PlayerPrefs.GetInt(GraphicsPresets.PreferenceKey, -1);
                qualityLevel = GraphicsPresets.ChooseLevel(chosenQuality, saved, suggested, QualityLevelCount);
            }

            // The first scene of a run sets the target; a later one keeps a step-down and skips its loading hitch.
            if (pacing == null)
            {
                EnsurePacing().Request(RequestedFrameRate);
            }
            else if (pacing.Requested != RequestedFrameRate)
            {
                pacing.Request(RequestedFrameRate);
            }
            else
            {
                pacing.Rewarm();
            }

            if (chosenLighting >= 0 && LightingCount > 0)
            {
                lighting = Mathf.Clamp(chosenLighting, 0, LightingCount - 1);
            }

            if (chosenOutlines >= 0)
            {
                outlinesOverride = true;
                outlinesOverrideValue = chosenOutlines == 1;
            }

            ApplyLighting();
            ApplyQuality();
        }

        private void OnDisable()
        {
            if (active == this)
            {
                active = null;
                UrpQualityBridge.Restore();
                Shader.SetGlobalFloat(OutlineSuppressId, 0f);
                Application.targetFrameRate = -1;
            }

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
            // Only on a phone: in the Editor or on a desktop, frame times say little about the device.
            if (guardFrameRate && Application.isMobilePlatform && active == this && pacing != null &&
                pacing.Observe(Time.unscaledDeltaTime))
            {
                ApplyFrameRate();
                Debug.Log($"[Threefold] This device could not keep {pacing.Requested} frames per second " +
                          $"(about {pacing.LastWindowMs:0.0} ms a frame); aiming for a steady {pacing.Current} for this session.", this);
                Changed?.Invoke();
            }

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
            ApplyFrameRate();

            if (preset.shadows == StyleShadows.Soft && !UrpQualityBridge.SoftShadowsAvailable && !softShadowWarningGiven)
            {
                softShadowWarningGiven = true;
                Debug.Log("[Threefold] Soft shadows are switched off in the active URP asset, so shadows have crisp edges. " +
                          "Threefold > Visual Style > Check Rendering Settings explains how to enable them.", this);
            }
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active = null;
            pacing = null;
            chosenQuality = -1;
            chosenLighting = -1;
            chosenOutlines = -1;
        }

        /// <summary>
        /// Sets the frame-rate target. Without it Unity on Android runs at its platform default of 30. Vertical
        /// sync is left off, as the quality level has it, so the target is what paces the game.
        /// </summary>
        private void ApplyFrameRate()
        {
            Application.targetFrameRate = TargetFrameRate;
        }

        private static FramePacingGuard EnsurePacing()
        {
            return pacing ?? (pacing = new FramePacingGuard());
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
