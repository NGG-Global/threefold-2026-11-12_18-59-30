using UnityEngine;
using UnityEngine.UI;

namespace Threefold.TouchControls
{
    /// <summary>
    /// On-screen button operated by touch. It reports whether it is held and whether it was
    /// pressed during the current frame, and shows pressed and highlighted states.
    /// </summary>
    public sealed class TouchButton : TouchRegion
    {
        [Header("Visuals")]
        [Tooltip("Optional. Graphic whose colour shows the button state.")]
        [SerializeField] private Graphic background;

        [SerializeField] private Color normalColor = new Color(1f, 0.96f, 0.86f, 0.28f);
        [SerializeField] private Color pressedColor = new Color(1f, 0.9f, 0.68f, 0.6f);
        [SerializeField] private Color highlightedColor = new Color(1f, 0.84f, 0.5f, 0.5f);

        [Tooltip("Size of the button while pressed, relative to its normal size.")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float pressedScale = 0.92f;

        [Header("Touch area")]
        [Tooltip("Extra touchable margin around the button, in canvas units, so it is easy to hit.")]
        [Min(0f)]
        [SerializeField] private float hitPadding = 10f;

        private int pressedFrame = -1;
        private int releasedFrame = -1;
        private bool highlighted;

        /// <summary>True while a finger holds the button.</summary>
        public bool IsHeld => IsCaptured;

        /// <summary>True only during the frame in which the button was pressed.</summary>
        public bool WasPressedThisFrame => pressedFrame == Time.frameCount;

        /// <summary>True only during the frame in which the finger lifted or was cancelled.</summary>
        public bool WasReleasedThisFrame => releasedFrame == Time.frameCount;

        /// <summary>Extra touchable margin around the button, in canvas units.</summary>
        public float HitPadding
        {
            get => hitPadding;
            set => hitPadding = Mathf.Max(0f, value);
        }

        /// <summary>Shows the button as switched on, for buttons that act as a toggle.</summary>
        public bool Highlighted
        {
            get => highlighted;
            set
            {
                if (highlighted == value)
                {
                    return;
                }

                highlighted = value;
                RefreshVisuals();
            }
        }

        /// <summary>Connects the visual part. Called by the setup tooling.</summary>
        public void Bind(Graphic backgroundGraphic)
        {
            background = backgroundGraphic;
            RefreshVisuals();
        }

        /// <summary>The button is round: it responds within its circle plus the padding.</summary>
        public override bool Contains(Vector2 screenPosition)
        {
            return CircleContains(screenPosition, hitPadding);
        }

        private void OnEnable()
        {
            RefreshVisuals();
        }

        protected override void OnPointerBegan(Vector2 screenPosition)
        {
            pressedFrame = Time.frameCount;
            RefreshVisuals();
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
        }

        protected override void OnPointerEnded(bool cancelled)
        {
            releasedFrame = Time.frameCount;
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            if (background != null)
            {
                background.color = IsCaptured ? pressedColor : highlighted ? highlightedColor : normalColor;
            }

            float scale = IsCaptured ? pressedScale : 1f;
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
