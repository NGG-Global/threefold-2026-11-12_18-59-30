using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>
    /// An area of the screen that one finger at a time can hold: the joystick, the camera look
    /// area, a button, the discipline selector. <see cref="PointerRouter"/> decides which finger
    /// belongs to which region, which is what makes simultaneous multi-touch work.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class TouchRegion : MonoBehaviour, IPointerTarget
    {
        /// <summary>Value of <see cref="PointerId"/> when no finger holds this region.</summary>
        public const int NoPointer = int.MinValue;

        private RectTransform region;

        /// <summary>Identifier of the finger holding this region, or <see cref="NoPointer"/>.</summary>
        public int PointerId { get; private set; } = NoPointer;

        public bool IsCaptured => PointerId != NoPointer;

        /// <summary>False while the region is inactive or disabled; subclasses may add conditions.</summary>
        public virtual bool AcceptsPointers => isActiveAndEnabled;

        protected RectTransform Region
        {
            get
            {
                if (region == null)
                {
                    region = (RectTransform)transform;
                }

                return region;
            }
        }

        /// <summary>True if the screen position lies inside this region. Assumes a Screen Space Overlay canvas.</summary>
        public virtual bool Contains(Vector2 screenPosition)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(Region, screenPosition, null);
        }

        public void Capture(int pointerId, Vector2 screenPosition)
        {
            PointerId = pointerId;
            OnPointerBegan(screenPosition);
        }

        public void Drag(Vector2 screenPosition)
        {
            if (IsCaptured)
            {
                OnPointerMoved(screenPosition);
            }
        }

        /// <summary>Lets the finger go. Cancelled means the touch was aborted and nothing should fire.</summary>
        public void Release(bool cancelled)
        {
            if (!IsCaptured)
            {
                return;
            }

            PointerId = NoPointer;
            OnPointerEnded(cancelled);
        }

        protected virtual void OnDisable()
        {
            Release(true);
        }

        /// <summary>Converts a screen position to this region's local space, in canvas units.</summary>
        protected Vector2 ToLocal(Vector2 screenPosition)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Region, screenPosition, null, out Vector2 local);
            return local;
        }

        /// <summary>True if the point lies within a circle filling the rectangle, widened by the padding.</summary>
        protected bool CircleContains(Vector2 screenPosition, float padding)
        {
            Rect rect = Region.rect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f + Mathf.Max(0f, padding);
            return (ToLocal(screenPosition) - rect.center).sqrMagnitude <= radius * radius;
        }

        protected abstract void OnPointerBegan(Vector2 screenPosition);

        protected abstract void OnPointerMoved(Vector2 screenPosition);

        protected abstract void OnPointerEnded(bool cancelled);
    }
}
