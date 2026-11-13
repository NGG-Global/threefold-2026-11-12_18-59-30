using UnityEngine;
using UnityEngine.UI;

namespace Threefold.TouchControls
{
    /// <summary>
    /// Stationary movement joystick in the bottom-left corner. It never moves: a touch takes it only
    /// within the activation radius around its ring, and touches anywhere else are left to the
    /// camera and the buttons. Pushing past the ring sprints. The behaviour is in
    /// <see cref="FixedJoystickModel"/>; this component converts touches and draws the stick.
    /// </summary>
    public sealed class VirtualJoystick : TouchRegion
    {
        [Header("Visuals")]
        [Tooltip("The stick's ring. Must be a child of this object. Its position is the joystick's fixed centre.")]
        [SerializeField] private RectTransform baseRect;

        [Tooltip("The part that follows the thumb. Must be a child of the base.")]
        [SerializeField] private RectTransform knobRect;

        [Tooltip("Optional. Fades the stick between its idle and active opacity.")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Tooltip("Optional. Lit while sprinting.")]
        [SerializeField] private Graphic sprintIndicator;

        [Range(0f, 1f)]
        [SerializeField] private float idleOpacity = 0.55f;

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

        [Tooltip("A touch takes the joystick only this close to its centre, in canvas units. Touches further away " +
                 "neither move nor activate it.")]
        [Min(1f)]
        [SerializeField] private float activationRadius = 105f;

        [Tooltip("Pushing the thumb this many times the travel away from the centre starts sprinting. 0 switches it off.")]
        [Min(0f)]
        [SerializeField] private float sprintThreshold = 1.45f;

        [Tooltip("Once sprinting, the sprint stops when the stick is eased below this share of full travel.")]
        [Range(0f, 1f)]
        [SerializeField] private float sprintReleaseDeflection = 0.6f;

        private readonly FixedJoystickModel model = new FixedJoystickModel();

        /// <summary>Processed movement value. Length is 0 to 1.</summary>
        public Vector2 Value => model.Value;

        /// <summary>True while the thumb has pushed past the ring and not yet eased back.</summary>
        public bool Sprinting => model.Sprinting;

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

        public float ActivationRadius
        {
            get => activationRadius;
            set => activationRadius = Mathf.Max(1f, value);
        }

        public float SprintThreshold
        {
            get => sprintThreshold;
            set => sprintThreshold = Mathf.Max(0f, value);
        }

        /// <summary>Connects the visual parts. Called by the setup tooling.</summary>
        public void Bind(RectTransform stickBase, RectTransform stickKnob, CanvasGroup stickFadeGroup)
        {
            baseRect = stickBase;
            knobRect = stickKnob;
            fadeGroup = stickFadeGroup;
        }

        /// <summary>Connects the sprint highlight. Called by the setup tooling.</summary>
        public void BindSprintIndicator(Graphic indicator)
        {
            sprintIndicator = indicator;
            ApplyVisuals();
        }

        public override bool Contains(Vector2 screenPosition)
        {
            SyncModel();
            return model.CanActivate(ToLocal(screenPosition));
        }

        private void OnEnable()
        {
            if (fadeGroup != null)
            {
                fadeGroup.alpha = idleOpacity;
            }

            ApplyVisuals();
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
            SyncModel();
            model.End();
            model.Begin(ToLocal(screenPosition));
            ApplyVisuals();
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
            model.Move(ToLocal(screenPosition));
            ApplyVisuals();
        }

        protected override void OnPointerEnded(bool cancelled)
        {
            model.End();
            ApplyVisuals();
        }

        /// <summary>
        /// Copies the tuning into the model and reads the centre from the ring's current position, so the
        /// joystick follows safe-area and resolution changes without caching a stale position.
        /// </summary>
        private void SyncModel()
        {
            model.Centre = baseRect != null ? (Vector2)baseRect.localPosition : Region.rect.center;
            model.Radius = radius;
            model.DeadZone = deadZone;
            model.ResponseExponent = responseExponent;
            model.ActivationRadius = activationRadius;
            model.SprintThreshold = sprintThreshold;
            model.SprintReleaseDeflection = sprintReleaseDeflection;
        }

        private void ApplyVisuals()
        {
            if (knobRect != null)
            {
                Vector2 offset = model.KnobOffset;
                knobRect.localPosition = new Vector3(offset.x, offset.y, 0f);
            }

            if (sprintIndicator != null)
            {
                sprintIndicator.enabled = model.Sprinting;
            }
        }
    }
}
