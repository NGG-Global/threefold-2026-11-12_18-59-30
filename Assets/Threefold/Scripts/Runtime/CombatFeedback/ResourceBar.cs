using UnityEngine;
using UnityEngine.UI;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// A horizontal bar for a value such as health or mana. The bar eases towards new values, and
    /// can flash to draw attention, for example when there is not enough mana for a spell.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceBar : MonoBehaviour
    {
        [Tooltip("The coloured part. It is stretched from the left edge to the current value.")]
        [SerializeField] private RectTransform fill;

        [Tooltip("Optional. Graphic of the coloured part, recoloured while flashing.")]
        [SerializeField] private Graphic fillGraphic;

        [Tooltip("Optional. Frame or background, recoloured while flashing.")]
        [SerializeField] private Graphic frameGraphic;

        [Tooltip("How quickly the bar follows the value, in bar lengths per second.")]
        [Min(0.1f)]
        [SerializeField] private float followSpeed = 2.5f;

        [SerializeField] private Color flashColor = new Color(1f, 0.32f, 0.26f, 1f);

        [Min(0.05f)]
        [SerializeField] private float flashSeconds = 0.45f;

        private Color fillRestColor;
        private Color frameRestColor;
        private float target = 1f;
        private float shown = 1f;
        private float flashRemaining;
        private bool coloursKnown;

        /// <summary>Connects the bar's parts. Called by the setup tooling.</summary>
        public void Bind(RectTransform fillRect, Graphic fillImage, Graphic frameImage)
        {
            fill = fillRect;
            fillGraphic = fillImage;
            frameGraphic = frameImage;
            coloursKnown = false;
        }

        /// <summary>Sets the value to show, from 0 to 1.</summary>
        public void SetFraction(float fraction)
        {
            target = Mathf.Clamp01(fraction);
        }

        /// <summary>Flashes the bar once.</summary>
        public void Flash()
        {
            RememberColours();
            flashRemaining = flashSeconds;
        }

        private void OnEnable()
        {
            RememberColours();
            shown = target;
            ApplyFill();
        }

        private void Update()
        {
            if (!Mathf.Approximately(shown, target))
            {
                shown = Mathf.MoveTowards(shown, target, followSpeed * Time.unscaledDeltaTime);
                ApplyFill();
            }

            if (flashRemaining > 0f)
            {
                flashRemaining -= Time.unscaledDeltaTime;

                // Two quick pulses read as a warning more clearly than a single fade.
                float progress = Mathf.Clamp01(flashRemaining / flashSeconds);
                float pulse = progress * Mathf.Abs(Mathf.Sin(progress * Mathf.PI * 2f));
                ApplyColours(flashRemaining > 0f ? pulse : 0f);
            }
        }

        private void RememberColours()
        {
            if (coloursKnown)
            {
                return;
            }

            coloursKnown = true;
            fillRestColor = fillGraphic != null ? fillGraphic.color : Color.white;
            frameRestColor = frameGraphic != null ? frameGraphic.color : Color.white;
        }

        private void ApplyFill()
        {
            if (fill == null)
            {
                return;
            }

            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(shown, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        private void ApplyColours(float amount)
        {
            if (fillGraphic != null)
            {
                fillGraphic.color = Color.Lerp(fillRestColor, flashColor, amount);
            }

            if (frameGraphic != null)
            {
                Color flashed = flashColor;
                flashed.a = frameRestColor.a;
                frameGraphic.color = Color.Lerp(frameRestColor, flashed, amount);
            }
        }
    }
}
