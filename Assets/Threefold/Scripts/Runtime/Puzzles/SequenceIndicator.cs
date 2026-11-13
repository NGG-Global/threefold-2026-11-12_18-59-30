using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A row of lamps that shows how far a sequence has got: one lamp per step, lit as steps are
    /// done and dimmed again if progress is lost. The number of lamps tells the player how many
    /// things are needed without a word of text. A lamp that has just lit flares briefly; when the
    /// sequence is solved, all lamps glow together.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SequenceIndicator : MonoBehaviour
    {
        [SerializeField] private PuzzleSequence sequence;

        [Tooltip("One per step, in order.")]
        [SerializeField] private Renderer[] lamps = new Renderer[0];

        [ColorUsage(false, true)]
        [SerializeField] private Color offColor = new Color(0.05f, 0.06f, 0.14f);

        [ColorUsage(false, true)]
        [SerializeField] private Color onColor = new Color(0.8f, 1.1f, 2.8f);

        [ColorUsage(false, true)]
        [SerializeField] private Color solvedColor = new Color(1.4f, 1.9f, 4f);

        [ColorUsage(false, true)]
        [SerializeField] private Color flareColor = new Color(3f, 3.4f, 6f);

        [Min(0.1f)]
        [SerializeField] private float fadeSpeed = 7f;

        [Min(0.05f)]
        [SerializeField] private float flareSeconds = 0.5f;

        private MaterialPropertyBlock block;
        private Color[] current = new Color[0];
        private float[] flare = new float[0];
        private int shownCount = -1;

        public PuzzleSequence Sequence => sequence;
        public Renderer[] Lamps => lamps;

        /// <summary>Connects the lamps. Called by the setup tooling.</summary>
        public void Bind(PuzzleSequence shownSequence, Renderer[] stepLamps)
        {
            sequence = shownSequence;
            lamps = stepLamps ?? new Renderer[0];
        }

        private void OnEnable()
        {
            EnsureBuffers();
            int lit = sequence != null ? sequence.CompletedSteps : 0;
            for (int i = 0; i < lamps.Length; i++)
            {
                current[i] = i < lit ? onColor : offColor;
                GlowUtility.SetEmission(lamps[i], -1, current[i], ref block);
            }

            // The first frame takes over the progress without a flare, so a puzzle solved earlier does not flash on load.
            shownCount = -1;
        }

        private void Update()
        {
            if (sequence == null)
            {
                return;
            }

            EnsureBuffers();
            float deltaTime = Time.deltaTime;
            int lit = sequence.CompletedSteps;
            bool solved = sequence.IsSolved;

            if (shownCount >= 0 && lit > shownCount)
            {
                for (int i = shownCount; i < lit && i < flare.Length; i++)
                {
                    flare[i] = flareSeconds;
                }
            }

            shownCount = lit;
            float blend = 1f - Mathf.Exp(-fadeSpeed * deltaTime);

            for (int i = 0; i < lamps.Length; i++)
            {
                Color target = solved ? solvedColor : i < lit ? onColor : offColor;
                current[i] = Color.Lerp(current[i], target, blend);

                Color shown = current[i];
                if (flare[i] > 0f)
                {
                    flare[i] -= deltaTime;
                    shown = Color.Lerp(current[i], flareColor, Mathf.Clamp01(flare[i] / flareSeconds));
                }

                GlowUtility.SetEmission(lamps[i], -1, shown, ref block);
            }
        }

        private void EnsureBuffers()
        {
            if (current.Length != lamps.Length)
            {
                current = new Color[lamps.Length];
                flare = new float[lamps.Length];
                for (int i = 0; i < current.Length; i++)
                {
                    current[i] = offColor;
                }
            }
        }
    }
}
