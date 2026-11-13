using Threefold.Interface;
using Threefold.Magic;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.TouchControls
{
    /// <summary>
    /// The small discipline indicator on the rim of the ATTACK button, and the fan of slots it
    /// opens. Press and drag onto a slot, then lift, to choose; or tap the indicator and then tap a
    /// slot. The rules are in <see cref="DisciplineSelectorModel"/>; this component feeds it touches
    /// and draws the indicator and the fan. The fan itself takes touches through <see cref="DisciplineFan"/>.
    /// </summary>
    public sealed class DisciplineSelector : TouchRegion
    {
        [SerializeField] private DisciplineFan fan;

        [Header("Visuals")]
        [SerializeField] private Graphic indicatorBackground;
        [SerializeField] private ProceduralImage indicatorIcon;

        [Tooltip("Fades the fan in and out. Must be on the fan object.")]
        [SerializeField] private CanvasGroup fanGroup;

        [Tooltip("One per discipline, in catalogue order. Children of the fan.")]
        [SerializeField] private RectTransform[] slots = new RectTransform[0];

        [SerializeField] private Graphic[] slotBackgrounds = new Graphic[0];
        [SerializeField] private ProceduralImage[] slotIcons = new ProceduralImage[0];

        [Tooltip("Optional. Ring drawn around the slot of the current discipline.")]
        [SerializeField] private Graphic[] slotCurrentRings = new Graphic[0];

        [Header("Feel")]
        [Tooltip("Extra touchable margin around the indicator, in canvas units.")]
        [Min(0f)]
        [SerializeField] private float hitPadding = 8f;

        [Tooltip("Extra touchable margin around each slot, in canvas units.")]
        [Min(0f)]
        [SerializeField] private float slotHitPadding = 6f;

        [Tooltip("Seconds a tap-opened fan waits for a choice. 0 waits until it is closed.")]
        [Min(0f)]
        [SerializeField] private float openTimeout = 3f;

        [Tooltip("Seconds for the fan to appear or disappear.")]
        [Min(0f)]
        [SerializeField] private float transitionSeconds = 0.08f;

        [Range(1f, 1.5f)]
        [SerializeField] private float highlightScale = 1.18f;

        [SerializeField] private Color slotColor = new Color(0.12f, 0.11f, 0.1f, 0.55f);

        private readonly DisciplineSelectorModel model = new DisciplineSelectorModel();
        private Vector2 lastScreenPosition;
        private int current;
        private bool slotsDirty = true;

        public DisciplineSelectorModel Model => model;

        public DisciplineFan Fan => fan;

        /// <summary>The slot chosen this frame, or -1.</summary>
        public int SelectedThisFrame => model.SelectedThisFrame;

        public bool IsOpen => model.IsOpen;

        /// <summary>True while the fan is open by a tap and waiting for a tap on a slot.</summary>
        public bool AcceptsFanTouches => model.State == DisciplineSelectorState.Open;

        /// <summary>Connects the parts. Called by the setup tooling.</summary>
        public void Bind(DisciplineFan slotFan, Graphic background, ProceduralImage symbol, CanvasGroup fanFade,
            RectTransform[] slotRects, Graphic[] slotFills, ProceduralImage[] slotSymbols, Graphic[] currentRings)
        {
            fan = slotFan;
            indicatorBackground = background;
            indicatorIcon = symbol;
            fanGroup = fanFade;
            slots = slotRects ?? new RectTransform[0];
            slotBackgrounds = slotFills ?? new Graphic[0];
            slotIcons = slotSymbols ?? new ProceduralImage[0];
            slotCurrentRings = currentRings ?? new Graphic[0];
            slotsDirty = true;

            if (fan != null)
            {
                fan.Bind(this);
            }
        }

        /// <summary>Which discipline the indicator and the attack button show.</summary>
        public void SetCurrent(int slot)
        {
            if (slot != current)
            {
                current = slot;
                slotsDirty = true;
            }
        }

        public override bool Contains(Vector2 screenPosition)
        {
            return CircleContains(screenPosition, hitPadding);
        }

        /// <summary>The slot under a screen position, or -1. Used by the fan to decide whether to take a touch.</summary>
        public int SlotAtScreen(Vector2 screenPosition)
        {
            SyncModel();
            return model.SlotAt(ToIndicator(screenPosition));
        }

        public void BeginFrame()
        {
            model.BeginFrame();
        }

        public void Tick(float time)
        {
            model.Update(time);
        }

        /// <summary>A touch started on another control: an open fan closes and the touch goes on as normal.</summary>
        public void PressOutside()
        {
            model.PressOutside();
        }

        public void CancelAll()
        {
            model.CancelAll();
        }

        internal void FanPressed(Vector2 screenPosition)
        {
            SyncModel();
            model.PressFan(ToIndicator(screenPosition));
        }

        internal void FanMoved(Vector2 screenPosition)
        {
            model.MoveFan(ToIndicator(screenPosition));
        }

        internal void FanReleased(Vector2 screenPosition, bool cancelled)
        {
            model.ReleaseFan(Time.unscaledTime, ToIndicator(screenPosition), cancelled);
        }

        private void OnEnable()
        {
            slotsDirty = true;
            if (fanGroup != null)
            {
                fanGroup.alpha = 0f;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            model.CancelAll();
        }

        protected override void OnPointerBegan(Vector2 screenPosition)
        {
            lastScreenPosition = screenPosition;
            SyncModel();
            model.PressIndicator(Time.unscaledTime, ToIndicator(screenPosition));
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
            lastScreenPosition = screenPosition;
            model.MoveIndicator(ToIndicator(screenPosition));
        }

        protected override void OnPointerEnded(bool cancelled)
        {
            model.ReleaseIndicator(Time.unscaledTime, ToIndicator(lastScreenPosition), cancelled);
        }

        /// <summary>Updates the indicator and the fan. Called once per frame by the input source.</summary>
        public void RefreshVisuals(float deltaTime)
        {
            DisciplineInfo info = DisciplineCatalog.Get(current);

            if (slotsDirty)
            {
                slotsDirty = false;
                if (indicatorIcon != null)
                {
                    indicatorIcon.Shape = info.Icon;
                }

                for (int i = 0; i < slotIcons.Length; i++)
                {
                    if (slotIcons[i] != null)
                    {
                        slotIcons[i].Shape = DisciplineCatalog.Get(i).Icon;
                    }
                }
            }

            if (indicatorBackground != null)
            {
                Color fill = info.Color;
                fill.a = model.IsOpen ? 0.95f : 0.7f;
                indicatorBackground.color = fill;
            }

            if (fanGroup != null)
            {
                float target = model.IsOpen ? 1f : 0f;
                fanGroup.alpha = transitionSeconds > 0f
                    ? Mathf.MoveTowards(fanGroup.alpha, target, deltaTime / transitionSeconds)
                    : target;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                bool highlighted = model.Highlighted == i;
                if (slots[i] != null)
                {
                    float scale = highlighted ? highlightScale : 1f;
                    slots[i].localScale = new Vector3(scale, scale, 1f);
                }

                if (i < slotBackgrounds.Length && slotBackgrounds[i] != null)
                {
                    Color fill = highlighted ? DisciplineCatalog.Get(i).Color : slotColor;
                    if (highlighted)
                    {
                        fill.a = 0.9f;
                    }

                    slotBackgrounds[i].color = fill;
                }

                if (i < slotIcons.Length && slotIcons[i] != null && slotIcons[i].TryGetComponent(out Image image))
                {
                    Color tint = highlighted ? Color.white : DisciplineCatalog.Get(i).Color;
                    image.color = tint;
                }

                if (i < slotCurrentRings.Length && slotCurrentRings[i] != null)
                {
                    slotCurrentRings[i].enabled = i == current;
                }
            }
        }

        /// <summary>A screen position relative to the indicator's centre, in canvas units, as the model expects.</summary>
        private Vector2 ToIndicator(Vector2 screenPosition)
        {
            return ToLocal(screenPosition) - Region.rect.center;
        }

        /// <summary>Gives the model the slot positions and touch sizes as they are laid out now.</summary>
        private void SyncModel()
        {
            Rect rect = Region.rect;
            model.IndicatorRadius = Mathf.Min(rect.width, rect.height) * 0.5f + hitPadding;
            model.OpenTimeout = openTimeout;

            float slotRadius = 0f;
            var offsets = new Vector2[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    offsets[i] = new Vector2(1e6f, 1e6f);
                    continue;
                }

                // Slots scale when highlighted but never move, so their pivot is a stable reference.
                offsets[i] = (Vector2)Region.InverseTransformPoint(slots[i].position) - rect.center;
                slotRadius = Mathf.Max(slotRadius, Mathf.Min(slots[i].rect.width, slots[i].rect.height) * 0.5f);
            }

            model.SetSlots(offsets);
            model.SlotRadius = slotRadius + slotHitPadding;
        }
    }
}
