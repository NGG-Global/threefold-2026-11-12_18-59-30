using Threefold.Interface;
using Threefold.InputHandling;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.TouchControls
{
    /// <summary>
    /// The ATTACK button. A tap is the standard attack; holding past the threshold charges; dragging
    /// while charging aims; releasing fires; releasing over the cancel zone that appears above the
    /// thumb abandons the charge. The rules are in <see cref="AttackGestureRecognizer"/>; this
    /// component feeds it touches and shows each state differently:
    ///
    /// - Pressed: the button shrinks slightly and brightens.
    /// - Charging: a ring fills around the button in the discipline's colour, and the cancel zone appears.
    /// - Aiming: a marker inside the aim ring shows the direction and strength.
    /// - Cancel armed: the cancel zone turns red and grows, and the meter greys out.
    /// - Released: a short flash, white for a tap and in the discipline's colour for a charged release.
    /// - Cancelled: a short red flash.
    /// </summary>
    public sealed class AttackGestureButton : TouchRegion
    {
        [SerializeField] private AttackGestureSettings gesture = new AttackGestureSettings();

        [Header("Visuals")]
        [SerializeField] private Graphic background;

        [Tooltip("Outline tinted with the selected discipline's colour.")]
        [SerializeField] private Graphic accentRing;

        [Tooltip("Shows the selected discipline's symbol.")]
        [SerializeField] private ProceduralImage icon;

        [Tooltip("Image set to Filled (radial). Fills while charging.")]
        [SerializeField] private Image chargeMeter;

        [Tooltip("Centred on the button and not a child of it, so pressing does not scale it. Holds the aim ring and marker.")]
        [SerializeField] private RectTransform aimRoot;

        [SerializeField] private CanvasGroup aimGroup;
        [SerializeField] private RectTransform aimMarker;
        [SerializeField] private Graphic aimMarkerGraphic;

        [Tooltip("Placed above the thumb when a charge starts. A sibling of this button.")]
        [SerializeField] private RectTransform cancelZone;

        [SerializeField] private CanvasGroup cancelGroup;
        [SerializeField] private Graphic cancelGraphic;

        [SerializeField] private Color normalColor = new Color(1f, 0.96f, 0.86f, 0.3f);
        [SerializeField] private Color pressedColor = new Color(1f, 0.93f, 0.78f, 0.62f);
        [SerializeField] private Color cancelColor = new Color(0.9f, 0.3f, 0.26f, 0.85f);
        [SerializeField] private Color cancelIdleColor = new Color(1f, 0.96f, 0.86f, 0.35f);
        [SerializeField] private Color disabledMeterColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);

        [Range(0.5f, 1.2f)]
        [SerializeField] private float pressedScale = 0.94f;

        [Tooltip("Seconds the release and cancel flashes last.")]
        [Min(0.01f)]
        [SerializeField] private float flashSeconds = 0.18f;

        [Header("Touch area")]
        [Tooltip("Extra touchable margin around the button, in canvas units.")]
        [Min(0f)]
        [SerializeField] private float hitPadding = 10f;

        [Header("Cancel zone")]
        [Tooltip("Where the cancel zone appears relative to the press point, in canvas units.")]
        [SerializeField] private Vector2 cancelOffset = new Vector2(0f, 150f);

        [Min(1f)]
        [SerializeField] private float cancelRadius = 36f;

        [Tooltip("Space kept between the cancel zone and the edge of the safe area.")]
        [Min(0f)]
        [SerializeField] private float cancelEdgeMargin = 8f;

        [Tooltip("Extra margin around the cancel zone that still counts as over it.")]
        [Min(0f)]
        [SerializeField] private float cancelHitPadding = 6f;

        private AttackGestureRecognizer recognizer;
        private Vector2 lastScreenPosition;
        private Vector2 cancelCentre;
        private Color disciplineColor = new Color(0.62f, 0.64f, 1f, 1f);
        private ProceduralShape disciplineIcon = ProceduralShape.Diamond;
        private bool disciplineDirty = true;
        private float flashRemaining;
        private Color flashColor = Color.white;

        /// <summary>The gesture rules. Shared with the input source, which reads the result each frame.</summary>
        public AttackGestureRecognizer Recognizer => recognizer ?? (recognizer = new AttackGestureRecognizer(gesture));

        public AttackGestureSettings Gesture => gesture;

        /// <summary>When false, a new press is held by the finger but does not start an attack (for example while blocking).</summary>
        public bool PressAllowed { get; set; } = true;

        public float HitPadding
        {
            get => hitPadding;
            set => hitPadding = Mathf.Max(0f, value);
        }

        /// <summary>Connects the visual parts. Called by the setup tooling.</summary>
        public void Bind(Graphic buttonBackground, Graphic ring, ProceduralImage symbol, Image meter)
        {
            background = buttonBackground;
            accentRing = ring;
            icon = symbol;
            chargeMeter = meter;
            disciplineDirty = true;
        }

        /// <summary>Connects the aim display and the cancel zone. Called by the setup tooling.</summary>
        public void BindAimAndCancel(RectTransform aim, CanvasGroup aimFade, RectTransform marker, Graphic markerGraphic,
            RectTransform cancel, CanvasGroup cancelFade, Graphic cancelCircle)
        {
            aimRoot = aim;
            aimGroup = aimFade;
            aimMarker = marker;
            aimMarkerGraphic = markerGraphic;
            cancelZone = cancel;
            cancelGroup = cancelFade;
            cancelGraphic = cancelCircle;
        }

        /// <summary>Shows the selected discipline on the button.</summary>
        public void SetDiscipline(Color color, ProceduralShape symbol)
        {
            if (color == disciplineColor && symbol == disciplineIcon && !disciplineDirty)
            {
                return;
            }

            disciplineColor = color;
            disciplineIcon = symbol;
            disciplineDirty = true;
        }

        public override bool Contains(Vector2 screenPosition)
        {
            return CircleContains(screenPosition, hitPadding);
        }

        /// <summary>Clears last frame's events. Called by the input source before touches are routed.</summary>
        public void BeginFrame()
        {
            Recognizer.BeginFrame();
        }

        /// <summary>Advances the hold timer. Called by the input source after touches are routed.</summary>
        public void Tick(float time)
        {
            Recognizer.Update(time);
        }

        private void OnEnable()
        {
            disciplineDirty = true;
            RefreshVisuals(0f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Recognizer.Reset();
            flashRemaining = 0f;
        }

        protected override void OnPointerBegan(Vector2 screenPosition)
        {
            lastScreenPosition = screenPosition;
            if (!PressAllowed)
            {
                return;
            }

            Vector2 press = ToParent(screenPosition);
            PlaceCancelZone(press);
            Recognizer.Press(Time.unscaledTime, press);
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
            lastScreenPosition = screenPosition;
            Recognizer.Move(Time.unscaledTime, ToParent(screenPosition), IsOverCancelZone(screenPosition));
        }

        protected override void OnPointerEnded(bool cancelled)
        {
            if (cancelled)
            {
                Recognizer.Cancel();
                return;
            }

            Recognizer.Release(Time.unscaledTime, IsOverCancelZone(lastScreenPosition));
        }

        /// <summary>Updates every visual from the current state. Called once per frame by the input source.</summary>
        public void RefreshVisuals(float deltaTime)
        {
            AttackGestureRecognizer state = Recognizer;
            AttackGesturePhase phase = state.Phase;

            if (phase == AttackGesturePhase.Released)
            {
                flashRemaining = flashSeconds;
                flashColor = state.LastRelease == AttackReleaseKind.Charged ? disciplineColor : Color.white;
            }
            else if (phase == AttackGesturePhase.Cancelled)
            {
                flashRemaining = flashSeconds;
                flashColor = cancelColor;
            }
            else
            {
                flashRemaining = Mathf.Max(0f, flashRemaining - deltaTime);
            }

            bool charging = state.IsCharging;
            bool armed = state.CancelArmed;
            float progress = state.ChargeProgress;

            Color fill = normalColor;
            if (phase == AttackGesturePhase.Pressed)
            {
                fill = pressedColor;
            }
            else if (charging)
            {
                Color tint = disciplineColor;
                tint.a = pressedColor.a;
                fill = armed ? disabledMeterColor : Color.Lerp(pressedColor, tint, 0.35f + 0.45f * progress);
            }

            if (flashRemaining > 0f)
            {
                Color flash = flashColor;
                flash.a = Mathf.Max(fill.a, 0.75f);
                fill = Color.Lerp(fill, flash, flashRemaining / flashSeconds);
            }

            if (background != null)
            {
                background.color = fill;
            }

            float scale = state.IsActive ? pressedScale : 1f;
            transform.localScale = new Vector3(scale, scale, 1f);

            if (disciplineDirty)
            {
                disciplineDirty = false;
                if (accentRing != null)
                {
                    Color ring = disciplineColor;
                    ring.a = 0.9f;
                    accentRing.color = ring;
                }

                if (icon != null)
                {
                    icon.Shape = disciplineIcon;
                }
            }

            if (chargeMeter != null)
            {
                chargeMeter.enabled = charging || (phase == AttackGesturePhase.Released && state.LastRelease == AttackReleaseKind.Charged);
                chargeMeter.fillAmount = charging ? Mathf.Max(0.02f, progress) : progress;
                chargeMeter.color = armed ? disabledMeterColor : disciplineColor;
            }

            if (aimRoot != null)
            {
                aimRoot.position = transform.position;
            }

            if (aimGroup != null)
            {
                aimGroup.alpha = charging ? 1f : 0f;
            }

            if (aimMarker != null)
            {
                Vector2 offset = state.Aim * gesture.aimRadius;
                aimMarker.localPosition = new Vector3(offset.x, offset.y, 0f);
            }

            if (aimMarkerGraphic != null)
            {
                aimMarkerGraphic.color = armed ? disabledMeterColor : disciplineColor;
            }

            if (cancelGroup != null)
            {
                bool cancelFlash = phase == AttackGesturePhase.Cancelled || (flashRemaining > 0f && flashColor == cancelColor);
                cancelGroup.alpha = charging || cancelFlash ? 1f : 0f;
            }

            if (cancelGraphic != null)
            {
                cancelGraphic.color = armed || phase == AttackGesturePhase.Cancelled ? cancelColor : cancelIdleColor;
            }

            if (cancelZone != null)
            {
                float zoneScale = armed ? 1.15f : 1f;
                cancelZone.localScale = new Vector3(zoneScale, zoneScale, 1f);
            }
        }

        /// <summary>Screen position in the parent's space (the safe area), which pressing the button does not scale.</summary>
        private Vector2 ToParent(Vector2 screenPosition)
        {
            var parent = transform.parent as RectTransform;
            if (parent == null)
            {
                return ToLocal(screenPosition);
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, null, out Vector2 local);
            return local;
        }

        private void PlaceCancelZone(Vector2 press)
        {
            var parent = transform.parent as RectTransform;
            Rect bounds = parent != null ? parent.rect : new Rect(press - Vector2.one * 1000f, Vector2.one * 2000f);
            cancelCentre = MobileControlsLayout.PlaceCancelZone(press, cancelOffset, cancelRadius, cancelEdgeMargin, bounds);

            if (cancelZone != null && cancelZone.parent == transform.parent)
            {
                cancelZone.localPosition = new Vector3(cancelCentre.x, cancelCentre.y, 0f);
            }
        }

        private bool IsOverCancelZone(Vector2 screenPosition)
        {
            if (!Recognizer.IsActive)
            {
                return false;
            }

            float reach = cancelRadius + cancelHitPadding;
            return (ToParent(screenPosition) - cancelCentre).sqrMagnitude <= reach * reach;
        }
    }
}
