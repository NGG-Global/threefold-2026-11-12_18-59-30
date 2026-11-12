using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>
    /// Floating virtual joystick. The stick appears where the thumb first touches inside this
    /// component's rectangle, and returns to its resting place when the thumb lifts.
    /// </summary>
    public sealed class VirtualJoystick : TouchRegion
    {
        [Header("Visuals")]
        [Tooltip("The stick's outer ring. Must be a child of this object.")]
        [SerializeField] private RectTransform baseRect;

        [Tooltip("The part that follows the thumb. Must be a child of the base.")]
        [SerializeField] private RectTransform knobRect;

        [Tooltip("Optional. Fades the stick between its idle and active opacity.")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Range(0f, 1f)]
        [SerializeField] private float idleOpacity = 0.45f;

        [Range(0f, 1f)]
        [SerializeField] private float activeOpacity = 1f;

        [Tooltip("Opacity change per second.")]
        [Min(0f)]
        [SerializeField] private float fadeSpeed = 8f;

        [Header("Feel")]
        [Tooltip("Thumb travel from the centre that gives full speed, in canvas units. Smaller is more sensitive.")]
        [Min(1f)]
        [SerializeField] private float radius = 55f;

        [Tooltip("Share of the travel around the centre that is ignored.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float deadZone = 0.12f;

        [Tooltip("1 is linear. Higher values give finer control at low speed.")]
        [Range(0.25f, 4f)]
        [SerializeField] private float responseExponent = 1.4f;

        [Tooltip("The stick follows the thumb when it moves beyond the stick's travel.")]
        [SerializeField] private bool followThumb = true;

        private Vector2 restPosition;
        private Vector2 origin;
        private bool restPositionKnown;

        /// <summary>Processed movement value. Length is 0 to 1.</summary>
        public Vector2 Value { get; private set; }

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(1f, value);
        }

        public float DeadZone
        {
            get => deadZone;
            set => deadZone = Mathf.Clamp(value, 0f, 0.9f);
        }

        public float ResponseExponent
        {
            get => responseExponent;
            set => responseExponent = Mathf.Clamp(value, 0.25f, 4f);
        }

        /// <summary>Connects the visual parts. Called by the setup tooling.</summary>
        public void Bind(RectTransform stickBase, RectTransform stickKnob, CanvasGroup stickFadeGroup)
        {
            baseRect = stickBase;
            knobRect = stickKnob;
            fadeGroup = stickFadeGroup;
            restPositionKnown = false;
        }

        private void OnEnable()
        {
            RememberRestPosition();

            if (fadeGroup != null)
            {
                fadeGroup.alpha = idleOpacity;
            }
        }

        private void Update()
        {
            if (fadeGroup == null)
            {
                return;
            }

            float target = IsCaptured ? activeOpacity : idleOpacity;
            fadeGroup.alpha = Mathf.MoveTowards(fadeGroup.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        }

        protected override void OnPointerBegan(Vector2 screenPosition)
        {
            RememberRestPosition();
            origin = ClampToRegion(ToLocal(screenPosition));
            Value = Vector2.zero;
            ApplyVisuals(Vector2.zero);
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
            Vector2 pointer = ToLocal(screenPosition);

            if (followThumb)
            {
                origin = ClampToRegion(JoystickMath.FollowOrigin(origin, pointer, radius));
            }

            Vector2 offset = Vector2.ClampMagnitude(pointer - origin, radius);
            Value = JoystickMath.Process(offset / radius, deadZone, responseExponent);
            ApplyVisuals(offset);
        }

        protected override void OnPointerEnded()
        {
            Value = Vector2.zero;
            origin = restPosition;
            ApplyVisuals(Vector2.zero);
        }

        private void RememberRestPosition()
        {
            if (restPositionKnown || baseRect == null)
            {
                return;
            }

            restPosition = baseRect.localPosition;
            origin = restPosition;
            restPositionKnown = true;
        }

        /// <summary>Keeps the whole stick inside this region so it is never cut off at the screen edge.</summary>
        private Vector2 ClampToRegion(Vector2 localPosition)
        {
            Rect rect = Region.rect;
            float margin = baseRect != null ? baseRect.rect.width * 0.5f : radius;

            // A region narrower than the stick leaves no room to clamp; centre the stick instead.
            float x = rect.width > margin * 2f
                ? Mathf.Clamp(localPosition.x, rect.xMin + margin, rect.xMax - margin)
                : rect.center.x;
            float y = rect.height > margin * 2f
                ? Mathf.Clamp(localPosition.y, rect.yMin + margin, rect.yMax - margin)
                : rect.center.y;

            return new Vector2(x, y);
        }

        private void ApplyVisuals(Vector2 knobOffset)
        {
            if (baseRect != null)
            {
                baseRect.localPosition = new Vector3(origin.x, origin.y, 0f);
            }

            if (knobRect != null)
            {
                knobRect.localPosition = new Vector3(knobOffset.x, knobOffset.y, 0f);
            }
        }
    }
}
