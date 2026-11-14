using System;
using Threefold.InputHandling;
using Threefold.Interface;
using Threefold.TouchControls;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Threefold.VisualStyle
{
    /// <summary>
    /// The player's graphics menu: a small Graphics button that opens a panel with the three presets
    /// (Performance, Balanced, Quality; see <see cref="GraphicsPresets"/>). The chosen preset is applied
    /// at once and kept between launches. Present in every build, release builds included.
    ///
    /// While the panel is open the touch controls are hidden, so a tap on the panel does not also move
    /// the Mage or the camera; they come back once every finger has left the screen. The game is not
    /// paused. The Back button (Escape) closes the panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GraphicsMenu : MonoBehaviour
    {
        private static readonly string[] Descriptions =
        {
            "A steady 30 frames per second, with lighter shadows and resolution. The painted look and the contour lines stay.",
            "60 frames per second where the device allows, with the full painted look. The default.",
            "60 frames per second with a sharper image, more shadow detail and every effect. For recent, capable devices.",
        };

        [SerializeField] private TapButton openButton;
        [SerializeField] private GameObject panel;
        [SerializeField] private TapButton[] presetButtons = new TapButton[0];
        [SerializeField] private Image[] presetBackgrounds = new Image[0];
        [SerializeField] private Text[] presetLabels = new Text[0];
        [SerializeField] private TapButton closeButton;
        [SerializeField] private Text note;

        [Tooltip("Optional. Hidden while the panel is open.")]
        [SerializeField] private TouchControlsInput touchControls;

        [Tooltip("Optional. Decides whether the touch controls come back when the panel closes.")]
        [SerializeField] private ControlSchemeSelector controlScheme;

        [SerializeField] private Color normalColor = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        [SerializeField] private Color selectedColor = new Color(0.62f, 0.44f, 0.18f, 0.92f);

        private VisualStyleController controller;
        private Action[] choosers = new Action[0];
        private bool open;
        private bool restoreTouchControls;
        private bool touchWasVisible;
        private bool enhancedTouchEnabledHere;

        public bool IsOpen => open;

        /// <summary>Connects the menu. Called by the setup tooling; empty arguments leave what is set.</summary>
        public void BindMissing(TapButton open, GameObject menuPanel, TapButton[] presets, Image[] backgrounds, Text[] labels,
            TapButton close, Text noteText, TouchControlsInput touch, ControlSchemeSelector scheme)
        {
            openButton = openButton != null ? openButton : open;
            panel = panel != null ? panel : menuPanel;
            if ((presetButtons == null || presetButtons.Length == 0) && presets != null) presetButtons = presets;
            if ((presetBackgrounds == null || presetBackgrounds.Length == 0) && backgrounds != null) presetBackgrounds = backgrounds;
            if ((presetLabels == null || presetLabels.Length == 0) && labels != null) presetLabels = labels;
            closeButton = closeButton != null ? closeButton : close;
            note = note != null ? note : noteText;
            touchControls = touchControls != null ? touchControls : touch;
            controlScheme = controlScheme != null ? controlScheme : scheme;
        }

        private void Awake()
        {
            int count = presetButtons != null ? presetButtons.Length : 0;
            choosers = new Action[count];
            for (int i = 0; i < count; i++)
            {
                int level = i;
                choosers[i] = () => Choose(level);
            }

            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
                enhancedTouchEnabledHere = true;
            }

            if (openButton != null) openButton.Pressed += Toggle;
            if (closeButton != null) closeButton.Pressed += Close;
            for (int i = 0; i < choosers.Length; i++)
            {
                if (presetButtons[i] != null) presetButtons[i].Pressed += choosers[i];
            }
        }

        private void OnDisable()
        {
            if (openButton != null) openButton.Pressed -= Toggle;
            if (closeButton != null) closeButton.Pressed -= Close;
            for (int i = 0; i < choosers.Length; i++)
            {
                if (presetButtons[i] != null) presetButtons[i].Pressed -= choosers[i];
            }

            Watch(null);
            if (open)
            {
                Close();
            }

            ShowTouchControls();

            if (enhancedTouchEnabledHere)
            {
                EnhancedTouchSupport.Disable();
                enhancedTouchEnabledHere = false;
            }
        }

        private void Update()
        {
            if (open)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    Close();
                }
            }

            // The touch controls come back only once the finger that closed the panel has lifted, so
            // that touch does not start a joystick or a camera drag.
            if (restoreTouchControls && Touch.activeTouches.Count == 0)
            {
                ShowTouchControls();
            }
        }

        public void Toggle()
        {
            if (open)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            if (open || panel == null)
            {
                return;
            }

            open = true;
            panel.SetActive(true);
            Watch(VisualStyleController.Active);

            if (touchControls != null)
            {
                if (!restoreTouchControls)
                {
                    touchWasVisible = touchControls.Visible;
                }

                restoreTouchControls = false;
                touchControls.SetVisible(false);
            }

            Refresh();
        }

        public void Close()
        {
            if (!open)
            {
                return;
            }

            open = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            restoreTouchControls = touchControls != null;
        }

        private void Choose(int level)
        {
            if (!open)
            {
                return;
            }

            Watch(VisualStyleController.Active);
            if (controller != null)
            {
                controller.ChoosePreset(level);
            }

            Refresh();
        }

        private void ShowTouchControls()
        {
            if (!restoreTouchControls)
            {
                return;
            }

            restoreTouchControls = false;
            if (touchControls == null)
            {
                return;
            }

            bool show = controlScheme != null ? controlScheme.ActiveScheme == ControlScheme.Touch : touchWasVisible;
            if (show)
            {
                touchControls.SetVisible(true);
            }
        }

        private void Watch(VisualStyleController styleController)
        {
            if (controller == styleController)
            {
                return;
            }

            if (controller != null)
            {
                controller.Changed -= Refresh;
            }

            controller = styleController;
            if (controller != null)
            {
                controller.Changed += Refresh;
            }
        }

        private void Refresh()
        {
            int current = controller != null ? controller.QualityLevel : -1;
            int count = presetButtons != null ? presetButtons.Length : 0;
            for (int i = 0; i < count; i++)
            {
                bool available = controller != null && i < controller.QualityLevelCount;
                if (presetButtons[i] != null && presetButtons[i].gameObject.activeSelf != available)
                {
                    presetButtons[i].gameObject.SetActive(available);
                }

                if (presetBackgrounds != null && i < presetBackgrounds.Length && presetBackgrounds[i] != null)
                {
                    presetBackgrounds[i].color = i == current ? selectedColor : normalColor;
                }

                if (available && presetLabels != null && i < presetLabels.Length && presetLabels[i] != null)
                {
                    presetLabels[i].text = controller.QualityLevelName(i);
                }
            }

            if (note == null)
            {
                return;
            }

            if (controller == null)
            {
                note.text = "Graphics settings are not available in this scene.";
            }
            else if (controller.FrameRateFellBack)
            {
                note.text = "This device could not keep 60 frames per second, so the game runs at a steady 30 for now. " +
                            "Choose a preset to try again.";
            }
            else
            {
                note.text = current >= 0 && current < Descriptions.Length && controller.QualityLevelCount == Descriptions.Length
                    ? Descriptions[current]
                    : string.Empty;
            }
        }
    }
}
