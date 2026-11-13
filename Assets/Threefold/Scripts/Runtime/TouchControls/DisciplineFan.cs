using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>
    /// The open discipline fan as a touch target. It takes a touch only while the fan is open by a
    /// tap and only on a slot, so it never intercepts camera or movement touches beside the slots.
    /// Everything it receives is passed to its <see cref="DisciplineSelector"/>.
    /// </summary>
    public sealed class DisciplineFan : TouchRegion
    {
        [SerializeField] private DisciplineSelector selector;

        private Vector2 lastScreenPosition;

        public override bool AcceptsPointers => base.AcceptsPointers && selector != null && selector.AcceptsFanTouches;

        /// <summary>Connects the selector. Called by <see cref="DisciplineSelector.Bind"/>.</summary>
        public void Bind(DisciplineSelector owner)
        {
            selector = owner;
        }

        public override bool Contains(Vector2 screenPosition)
        {
            return selector != null && selector.SlotAtScreen(screenPosition) >= 0;
        }

        protected override void OnPointerBegan(Vector2 screenPosition)
        {
            lastScreenPosition = screenPosition;
            selector.FanPressed(screenPosition);
        }

        protected override void OnPointerMoved(Vector2 screenPosition)
        {
            lastScreenPosition = screenPosition;
            selector.FanMoved(screenPosition);
        }

        protected override void OnPointerEnded(bool cancelled)
        {
            if (selector != null)
            {
                selector.FanReleased(lastScreenPosition, cancelled);
            }
        }
    }
}
