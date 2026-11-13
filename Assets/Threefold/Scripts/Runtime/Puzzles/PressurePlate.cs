using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A stone plate that is active while something stands on it. Its top sinks a little when
    /// pressed. The character stands on the plate's fixed base, so the moving top never jolts it.
    /// </summary>
    public sealed class PressurePlate : PresenceZone
    {
        [Header("Plate")]
        [Tooltip("Optional. The part that sinks when pressed.")]
        [SerializeField] private Transform plateTop;

        [Tooltip("How far the top sinks, in metres.")]
        [Min(0f)]
        [SerializeField] private float pressDepth = 0.05f;

        [Tooltip("Metres per second the top moves.")]
        [Min(0.01f)]
        [SerializeField] private float pressSpeed = 0.4f;

        private Vector3 topRest;
        private bool restKnown;

        public Transform PlateTop
        {
            get => plateTop;
            set
            {
                plateTop = value;
                restKnown = false;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            CaptureRest();
        }

        protected override void OnTicked(float deltaTime)
        {
            if (plateTop == null)
            {
                return;
            }

            CaptureRest();
            Vector3 target = topRest - Vector3.up * (IsActive ? pressDepth : 0f);
            plateTop.localPosition = Vector3.MoveTowards(plateTop.localPosition, target, pressSpeed * deltaTime);
        }

        private void CaptureRest()
        {
            if (!restKnown && plateTop != null)
            {
                topRest = plateTop.localPosition;
                restKnown = true;
            }
        }
    }
}
