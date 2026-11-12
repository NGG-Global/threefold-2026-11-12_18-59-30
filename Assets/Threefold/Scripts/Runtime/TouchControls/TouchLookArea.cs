using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>
    /// Screen area in which dragging a finger turns the camera. It has no visuals; it only
    /// measures how far the finger holding it moved since the last frame.
    /// </summary>
    public sealed class TouchLookArea : TouchRegion
    {
        private Vector2 lastPosition;
        private Vector2 pendingDelta;

        /// <summary>Returns the finger travel in screen pixels since the previous call, and resets it.</summary>
        public Vector2 ConsumeDelta()
        {
            Vector2 delta = pendingDelta;
            pendingDelta = Vector2.zero;
            return delta;
        }

        protected override void OnPointerBegan(Vector2 screenPosition)
        {
            lastPosition = screenPosition;
            pendingDelta = Vector2.zero;
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
            pendingDelta += screenPosition - lastPosition;
            lastPosition = screenPosition;
        }

        protected override void OnPointerEnded()
        {
            pendingDelta = Vector2.zero;
        }
    }
}
