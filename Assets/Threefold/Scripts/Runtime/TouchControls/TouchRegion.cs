using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>
    /// A rectangular area of the screen that one finger at a time can hold: a joystick zone, the
    /// camera look area, or a button. <see cref="TouchControlsInput"/> decides which finger
    /// belongs to which region, which is what makes simultaneous multi-touch work.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class TouchRegion : MonoBehaviour
    {
        /// <summary>Value of <see cref="PointerId"/> when no finger holds this region.</summary>
        public const int NoPointer = int.MinValue;

        private RectTransform region;

        /// <summary>Identifier of the finger holding this region, or <see cref="NoPointer"/>.</summary>
        public int PointerId { get; private set; } = NoPointer;

        public bool IsCaptured => PointerId != NoPointer;

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

        public void Release()
        {
            if (!IsCaptured)
            {
                return;
            }

            PointerId = NoPointer;
            OnPointerEnded();
        }

        protected virtual void OnDisable()
        {
            Release();
        }

        /// <summary>Converts a screen position to this region's local space, in canvas units.</summary>
        protected Vector2 ToLocal(Vector2 screenPosition)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Region, screenPosition, null, out Vector2 local);
            return local;
        }

        protected abstract void OnPointerBegan(Vector2 screenPosition);

        protected abstract void OnPointerMoved(Vector2 screenPosition);

        protected abstract void OnPointerEnded();
    }
}
