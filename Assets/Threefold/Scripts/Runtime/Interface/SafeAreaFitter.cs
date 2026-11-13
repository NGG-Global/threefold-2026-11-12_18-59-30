using UnityEngine;

namespace Threefold.Interface
{
    /// <summary>
    /// Keeps a RectTransform inside the screen's safe area, clear of notches, camera cut-outs,
    /// rounded corners and system gesture bars. Place it on a child of the canvas and put the
    /// interface inside it. It follows rotation and resolution changes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect appliedSafeArea;
        private int appliedWidth;
        private int appliedHeight;
        private bool hasApplied;

        /// <summary>
        /// Converts a safe area in pixels into normalised anchors. An unusable input yields the full screen.
        /// </summary>
        public static void CalculateAnchors(Rect safeArea, int screenWidth, int screenHeight,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            anchorMin = Vector2.zero;
            anchorMax = Vector2.one;

            if (screenWidth <= 0 || screenHeight <= 0 || safeArea.width <= 0f || safeArea.height <= 0f)
            {
                return;
            }

            anchorMin = new Vector2(
                Mathf.Clamp01(safeArea.xMin / screenWidth),
                Mathf.Clamp01(safeArea.yMin / screenHeight));
            anchorMax = new Vector2(
                Mathf.Clamp01(safeArea.xMax / screenWidth),
                Mathf.Clamp01(safeArea.yMax / screenHeight));
        }

        private void OnEnable()
        {
            rectTransform = (RectTransform)transform;
            hasApplied = false;
            ApplyIfChanged();
        }

        private void Update()
        {
            ApplyIfChanged();
        }

        private void ApplyIfChanged()
        {
            Rect safeArea = Screen.safeArea;
            int width = Screen.width;
            int height = Screen.height;

            if (hasApplied && safeArea == appliedSafeArea && width == appliedWidth && height == appliedHeight)
            {
                return;
            }

            hasApplied = true;
            appliedSafeArea = safeArea;
            appliedWidth = width;
            appliedHeight = height;

            CalculateAnchors(safeArea, width, height, out Vector2 anchorMin, out Vector2 anchorMax);
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
