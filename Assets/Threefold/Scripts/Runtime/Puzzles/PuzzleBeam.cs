using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A thread of light between two points that appears while an element is active. It shows a
    /// connection the player could not otherwise see: standing on the lookout links it to the
    /// crystal, which is the cue that the crystal can now be struck.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class PuzzleBeam : MonoBehaviour
    {
        [SerializeField] private PuzzleElement source;
        [SerializeField] private Transform from;
        [SerializeField] private Transform to;

        [Tooltip("Width of the beam when fully shown, in metres.")]
        [Min(0.001f)]
        [SerializeField] private float width = 0.08f;

        [Tooltip("How quickly the beam appears and fades, per second.")]
        [Min(0.1f)]
        [SerializeField] private float fadeSpeed = 3f;

        private LineRenderer line;
        private float amount;

        public PuzzleElement Source => source;
        public Transform From => from;
        public Transform To => to;

        /// <summary>Connects the beam. Called by the setup tooling.</summary>
        public void Bind(PuzzleElement element, Transform start, Transform end)
        {
            source = element;
            from = start;
            to = end;
        }

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.enabled = false;
        }

        private void LateUpdate()
        {
            float target = source != null && source.IsActive ? 1f : 0f;
            amount = Mathf.MoveTowards(amount, target, fadeSpeed * Time.deltaTime);

            bool visible = amount > 0.001f && from != null && to != null;
            if (line.enabled != visible)
            {
                line.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            line.SetPosition(0, from.position);
            line.SetPosition(1, to.position);

            // A slight shimmer keeps it from looking like a solid rod.
            float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 9f);
            line.widthMultiplier = width * amount * shimmer;
        }
    }
}
