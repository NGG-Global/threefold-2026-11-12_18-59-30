using UnityEngine;

namespace Threefold.Interface
{
    /// <summary>
    /// Scales a Screen Space canvas by the screen's pixel density, so one canvas unit is one
    /// Android density-independent pixel (1/160 inch). Touch controls then have the same physical
    /// size under the thumb on a small phone and on a large tablet, whatever the resolution or
    /// aspect ratio. Use it instead of a CanvasScaler, not together with one.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class DensityCanvasScaler : MonoBehaviour
    {
        private const float ReferenceDpi = 160f;

        [Tooltip("Overall size adjustment. 1 is the designed size.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float scaleMultiplier = 1f;

        [Tooltip("The canvas is never scaled so far that the screen is shorter than this many canvas units. " +
                 "Protects small or very dense screens from oversized controls.")]
        [Min(1f)]
        [SerializeField] private float minimumCanvasHeight = 340f;

        [Tooltip("Canvas height assumed when the device does not report its pixel density.")]
        [Min(1f)]
        [SerializeField] private float fallbackCanvasHeight = 420f;

        private Canvas canvas;
        private float appliedScale = -1f;

        public float ScaleMultiplier
        {
            get => scaleMultiplier;
            set => scaleMultiplier = Mathf.Clamp(value, 0.5f, 2f);
        }

        /// <summary>Calculates the canvas scale factor for a screen.</summary>
        /// <param name="dpi">Pixel density reported by the device, or 0 if unknown.</param>
        /// <param name="screenHeight">Screen height in pixels.</param>
        public static float CalculateScale(float dpi, int screenHeight, float scaleMultiplier,
            float minimumCanvasHeight, float fallbackCanvasHeight)
        {
            float height = Mathf.Max(screenHeight, 1);

            float scale = dpi > 0f
                ? dpi / ReferenceDpi
                : height / Mathf.Max(fallbackCanvasHeight, 1f);

            scale *= Mathf.Max(scaleMultiplier, 0.01f);

            float largestAllowed = height / Mathf.Max(minimumCanvasHeight, 1f);
            return Mathf.Max(Mathf.Min(scale, largestAllowed), 0.01f);
        }

        private void OnEnable()
        {
            canvas = GetComponent<Canvas>();
            appliedScale = -1f;
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (canvas == null)
            {
                return;
            }

            float scale = CalculateScale(Screen.dpi, Screen.height, scaleMultiplier, minimumCanvasHeight,
                fallbackCanvasHeight);

            if (Mathf.Approximately(scale, appliedScale))
            {
                return;
            }

            appliedScale = scale;
            canvas.scaleFactor = scale;
        }
    }
}
