using Threefold.InputHandling;
using Threefold.TouchControls;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.Interaction
{
    /// <summary>
    /// The context-sensitive prompt: a small label beside the thing the player can use, showing one
    /// verb and, on keyboard or gamepad, the key that does it ("E  Open"). With touch controls the
    /// key is left out and the interact button beside the attack button appears instead, carrying
    /// the same verb. Nothing is shown when there is nothing to use.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPrompt : MonoBehaviour
    {
        [SerializeField] private Interactor interactor;

        [Tooltip("Camera that shows the world. If empty, the main camera is used.")]
        [SerializeField] private Camera viewCamera;

        [Header("Display")]
        [Tooltip("The canvas the prompt is drawn on. Must be a Screen Space Overlay canvas.")]
        [SerializeField] private RectTransform canvasRect;

        [Tooltip("The prompt itself. It follows the interaction point on screen.")]
        [SerializeField] private RectTransform marker;

        [SerializeField] private CanvasGroup markerGroup;
        [SerializeField] private Text verbLabel;

        [Tooltip("Shows the key or button that interacts. Hidden with touch controls.")]
        [SerializeField] private GameObject keyBadge;

        [SerializeField] private Text keyLabel;

        [Tooltip("Shown on the key badge if the binding cannot be named.")]
        [SerializeField] private string fallbackKey = "E";

        [Tooltip("Offset from the interaction point on screen, in canvas units.")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 36f);

        [Tooltip("Opacity change per second.")]
        [Min(0.1f)]
        [SerializeField] private float fadeSpeed = 8f;

        [Header("Controls")]
        [Tooltip("Optional. Decides whether the touch layout is in use.")]
        [SerializeField] private ControlSchemeSelector schemeSelector;

        [Tooltip("Optional. Names the interact key.")]
        [SerializeField] private InputSystemCharacterInput desktopInput;

        [Tooltip("Optional. Its interact button is shown while something can be used.")]
        [SerializeField] private TouchControlsInput touchControls;

        private const float EdgeMargin = 60f;

        /// <summary>Connects the prompt. Called by the setup tooling.</summary>
        public void Bind(Interactor playerInteractor, RectTransform canvas, RectTransform promptMarker,
            CanvasGroup promptGroup, Text verb, GameObject badge, Text key, ControlSchemeSelector selector,
            InputSystemCharacterInput desktop, TouchControlsInput touch)
        {
            interactor = playerInteractor;
            canvasRect = canvas;
            marker = promptMarker;
            markerGroup = promptGroup;
            verbLabel = verb;
            keyBadge = badge;
            keyLabel = key;
            schemeSelector = selector;
            desktopInput = desktop;
            touchControls = touch;
        }

        public Interactor Interactor => interactor;
        public TouchControlsInput TouchControls => touchControls;

        private void OnEnable()
        {
            if (markerGroup != null)
            {
                markerGroup.alpha = 0f;
                markerGroup.interactable = false;
                markerGroup.blocksRaycasts = false;
            }
        }

        private void OnDisable()
        {
            if (touchControls != null)
            {
                touchControls.SetInteractAvailable(false, string.Empty);
            }
        }

        private void LateUpdate()
        {
            IInteractable focus = interactor != null && interactor.isActiveAndEnabled ? interactor.Focus : null;
            bool touch = schemeSelector != null && schemeSelector.ActiveScheme == ControlScheme.Touch;

            if (touchControls != null)
            {
                touchControls.SetInteractAvailable(focus != null && touch, focus != null ? focus.InteractionVerb : string.Empty);
            }

            bool visible = focus != null && PlaceMarker(focus.InteractionPoint);
            if (visible)
            {
                // The verb can change while the same thing is in focus, such as Open becoming Close.
                SetText(verbLabel, focus.InteractionVerb);

                if (keyBadge != null && keyBadge.activeSelf == touch)
                {
                    keyBadge.SetActive(!touch);
                }

                if (!touch)
                {
                    string key = desktopInput != null ? desktopInput.InteractBindingLabel : string.Empty;
                    SetText(keyLabel, string.IsNullOrEmpty(key) ? fallbackKey : key);
                }
            }

            if (markerGroup != null)
            {
                float target = visible ? 1f : 0f;
                markerGroup.alpha = Mathf.MoveTowards(markerGroup.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
            }
        }

        /// <summary>Moves the prompt next to a world position.</summary>
        /// <returns>False if the position is behind the camera.</returns>
        private bool PlaceMarker(Vector3 worldPoint)
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera == null || marker == null || canvasRect == null)
            {
                return false;
            }

            Vector3 screen = viewCamera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f)
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
            {
                return false;
            }

            Rect bounds = canvasRect.rect;
            local += screenOffset;
            local.x = Mathf.Clamp(local.x, bounds.xMin + EdgeMargin, bounds.xMax - EdgeMargin);
            local.y = Mathf.Clamp(local.y, bounds.yMin + EdgeMargin, bounds.yMax - EdgeMargin);
            marker.anchoredPosition = local;
            return true;
        }

        private static void SetText(Text label, string value)
        {
            if (label != null && label.text != value)
            {
                label.text = value;
            }
        }
    }
}
