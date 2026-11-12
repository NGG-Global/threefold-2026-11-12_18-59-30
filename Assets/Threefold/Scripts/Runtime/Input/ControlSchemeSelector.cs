using System;
using Threefold.TouchControls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Threefold.InputHandling
{
    public enum ControlScheme
    {
        /// <summary>Keyboard and mouse, or a gamepad. Touch controls are hidden.</summary>
        Desktop,

        /// <summary>On-screen touch controls are shown.</summary>
        Touch,
    }

    /// <summary>
    /// Chooses between the desktop and the touch control scheme and applies the choice: it shows or
    /// hides the on-screen controls and decides whether the mouse may be captured for camera look.
    ///
    /// In Automatic mode the game starts with touch controls on a phone or tablet and with desktop
    /// controls elsewhere, then follows whichever kind of device was used last. All input sources
    /// stay active the whole time, so switching never drops an input.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class ControlSchemeSelector : MonoBehaviour
    {
        public enum SelectionMode
        {
            Automatic,
            AlwaysTouch,
            AlwaysDesktop,
        }

        /// <summary>
        /// Editor preference that forces the touch scheme in Play mode, so the on-screen controls
        /// can be tried with the mouse. Set by the menu Threefold > Controls.
        /// </summary>
        public const string EditorPreviewPreferenceKey = "Threefold.PreviewTouchControls";

        private const float StickSwitchThreshold = 0.5f;

        [SerializeField] private SelectionMode mode = SelectionMode.Automatic;

        [Tooltip("Optional. Desktop input whose mouse capture is switched off while touch controls are shown.")]
        [SerializeField] private InputSystemCharacterInput desktopInput;

        [Tooltip("On-screen touch controls to show and hide.")]
        [SerializeField] private TouchControlsInput touchControls;

        private bool applied;

        /// <summary>Raised whenever the active scheme changes.</summary>
        public event Action<ControlScheme> SchemeChanged;

        public ControlScheme ActiveScheme { get; private set; } = ControlScheme.Desktop;

        public SelectionMode Mode
        {
            get => mode;
            set => mode = value;
        }

        /// <summary>Connects the two input implementations. Called by the setup tooling.</summary>
        public void Bind(InputSystemCharacterInput desktop, TouchControlsInput touch)
        {
            desktopInput = desktop;
            touchControls = touch;
        }

        private void OnEnable()
        {
            applied = false;
        }

        private void Start()
        {
            Apply(GetStartingScheme(), true);
        }

        private void Update()
        {
            SelectionMode effectiveMode = GetEffectiveMode();

            if (effectiveMode == SelectionMode.AlwaysTouch)
            {
                Apply(ControlScheme.Touch, false);
            }
            else if (effectiveMode == SelectionMode.AlwaysDesktop)
            {
                Apply(ControlScheme.Desktop, false);
            }
            else if (WasTouchUsedThisFrame())
            {
                Apply(ControlScheme.Touch, false);
            }
            else if (WasDesktopDeviceUsedThisFrame())
            {
                Apply(ControlScheme.Desktop, false);
            }
        }

        private ControlScheme GetStartingScheme()
        {
            switch (GetEffectiveMode())
            {
                case SelectionMode.AlwaysTouch:
                    return ControlScheme.Touch;
                case SelectionMode.AlwaysDesktop:
                    return ControlScheme.Desktop;
                default:
                    return Application.isMobilePlatform ? ControlScheme.Touch : ControlScheme.Desktop;
            }
        }

        private SelectionMode GetEffectiveMode()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(EditorPreviewPreferenceKey, false))
            {
                return SelectionMode.AlwaysTouch;
            }
#endif
            return mode;
        }

        private static bool WasTouchUsedThisFrame()
        {
            Touchscreen touchscreen = Touchscreen.current;
            return touchscreen != null && touchscreen.primaryTouch.press.isPressed;
        }

        /// <summary>
        /// The mouse is deliberately not counted: in the Editor it may be standing in for a finger.
        /// </summary>
        private static bool WasDesktopDeviceUsedThisFrame()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return false;
            }

            return gamepad.buttonSouth.wasPressedThisFrame
                   || gamepad.leftStick.ReadValue().sqrMagnitude > StickSwitchThreshold * StickSwitchThreshold
                   || gamepad.rightStick.ReadValue().sqrMagnitude > StickSwitchThreshold * StickSwitchThreshold;
        }

        private void Apply(ControlScheme scheme, bool immediate)
        {
            if (applied && scheme == ActiveScheme)
            {
                return;
            }

            applied = true;
            ActiveScheme = scheme;
            bool touch = scheme == ControlScheme.Touch;

            if (touchControls != null)
            {
                touchControls.SetVisible(touch, immediate);
            }

            if (desktopInput != null)
            {
                desktopInput.MouseCaptureEnabled = !touch;
            }

            SchemeChanged?.Invoke(scheme);
        }
    }
}
