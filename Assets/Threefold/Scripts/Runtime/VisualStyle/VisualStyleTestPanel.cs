using Threefold.Interface;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.VisualStyle
{
    /// <summary>
    /// Three small buttons for judging the style on a device: quality level, contour lines on or
    /// off, and lighting condition. Each shows its current setting. The panel appears in the
    /// Editor and in development builds only, unless told otherwise.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisualStyleTestPanel : MonoBehaviour
    {
        [SerializeField] private VisualStyleController controller;

        [SerializeField] private TapButton qualityButton;
        [SerializeField] private TapButton outlineButton;
        [SerializeField] private TapButton lightingButton;

        [Tooltip("Optional labels. Without a font the buttons still work.")]
        [SerializeField] private Text qualityLabel;
        [SerializeField] private Text outlineLabel;
        [SerializeField] private Text lightingLabel;

        [Tooltip("The part that is hidden in release builds.")]
        [SerializeField] private GameObject content;

        [Tooltip("Show the panel in release builds as well.")]
        [SerializeField] private bool showInReleaseBuilds = false;

        /// <summary>Connects the panel. Called by the setup tooling.</summary>
        public void Bind(VisualStyleController styleController, TapButton quality, TapButton outline, TapButton lighting,
            Text qualityText, Text outlineText, Text lightingText, GameObject panelContent)
        {
            controller = styleController;
            qualityButton = quality;
            outlineButton = outline;
            lightingButton = lighting;
            qualityLabel = qualityText;
            outlineLabel = outlineText;
            lightingLabel = lightingText;
            content = panelContent;
        }

        private void OnEnable()
        {
            bool visible = showInReleaseBuilds || Application.isEditor || Debug.isDebugBuild;
            if (content != null && content.activeSelf != visible)
            {
                content.SetActive(visible);
            }

            if (controller != null)
            {
                controller.Changed += Refresh;
            }

            if (qualityButton != null)
            {
                qualityButton.Pressed += NextQuality;
            }

            if (outlineButton != null)
            {
                outlineButton.Pressed += ToggleOutlines;
            }

            if (lightingButton != null)
            {
                lightingButton.Pressed += NextLighting;
            }
        }

        private void Start()
        {
            // After the controller has chosen its starting level.
            Refresh();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.Changed -= Refresh;
            }

            if (qualityButton != null)
            {
                qualityButton.Pressed -= NextQuality;
            }

            if (outlineButton != null)
            {
                outlineButton.Pressed -= ToggleOutlines;
            }

            if (lightingButton != null)
            {
                lightingButton.Pressed -= NextLighting;
            }
        }

        private void NextQuality()
        {
            if (controller != null)
            {
                controller.NextQuality();
            }
        }

        private void ToggleOutlines()
        {
            if (controller != null)
            {
                controller.SetOutlines(!controller.OutlinesEnabled);
            }
        }

        private void NextLighting()
        {
            if (controller != null)
            {
                controller.NextLighting();
            }
        }

        private void Refresh()
        {
            if (controller == null)
            {
                return;
            }

            if (qualityLabel != null)
            {
                VisualQualityPreset quality = controller.CurrentQuality;
                qualityLabel.text = "Quality: " + (quality != null ? quality.displayName : "-");
            }

            if (outlineLabel != null)
            {
                outlineLabel.text = controller.OutlinesEnabled ? "Lines: on" : "Lines: off";
            }

            if (lightingLabel != null)
            {
                LightingPreset lighting = controller.CurrentLighting;
                lightingLabel.text = "Light: " + (lighting != null ? lighting.displayName : "-");
            }
        }
    }
}
