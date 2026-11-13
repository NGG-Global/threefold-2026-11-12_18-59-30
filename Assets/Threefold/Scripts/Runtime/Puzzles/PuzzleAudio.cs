using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Plays sounds for a puzzle element's moments: switching on, switching off, a refused attempt,
    /// and for a sequence each completed step (higher in pitch with each), lost progress and the
    /// solution. Every slot takes a clip; an empty slot plays a generated placeholder
    /// (<see cref="PuzzleSounds"/>), or nothing if its tone is None.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class PuzzleAudio : MonoBehaviour
    {
        [SerializeField] private PuzzleElement source;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.8f;

        [Header("Element")]
        [SerializeField] private AudioClip activatedClip = null;
        [SerializeField] private PuzzleTone activatedTone = PuzzleTone.Chime;
        [SerializeField] private AudioClip deactivatedClip = null;
        [SerializeField] private PuzzleTone deactivatedTone = PuzzleTone.Fade;
        [SerializeField] private AudioClip rejectedClip = null;
        [SerializeField] private PuzzleTone rejectedTone = PuzzleTone.Fizzle;

        [Header("Sequence (only if the source is a Puzzle Sequence)")]
        [SerializeField] private AudioClip stepClip = null;
        [SerializeField] private PuzzleTone stepTone = PuzzleTone.Chime;
        [SerializeField] private AudioClip progressLostClip = null;
        [SerializeField] private PuzzleTone progressLostTone = PuzzleTone.Fade;
        [SerializeField] private AudioClip solvedClip = null;
        [SerializeField] private PuzzleTone solvedTone = PuzzleTone.Fanfare;

        private AudioSource audioSource;
        private PuzzleElement subscribed;
        private PuzzleSequence sequence;

        public PuzzleElement Source => source;

        /// <summary>Connects the sounds. Called by the setup tooling.</summary>
        public void Bind(PuzzleElement element, PuzzleTone onActivated, PuzzleTone onDeactivated, PuzzleTone onRejected)
        {
            source = element;
            activatedTone = onActivated;
            deactivatedTone = onDeactivated;
            rejectedTone = onRejected;
        }

        private void OnEnable()
        {
            audioSource = GetComponent<AudioSource>();
            subscribed = source;
            if (subscribed == null)
            {
                return;
            }

            sequence = subscribed as PuzzleSequence;
            if (sequence != null)
            {
                sequence.StepCompleted += HandleStepCompleted;
                sequence.ProgressLost += HandleProgressLost;
                sequence.Solved += HandleSolved;
            }
            else
            {
                subscribed.StateChanged += HandleStateChanged;
            }

            subscribed.Rejected += HandleRejected;
        }

        private void OnDisable()
        {
            if (subscribed == null)
            {
                return;
            }

            if (sequence != null)
            {
                sequence.StepCompleted -= HandleStepCompleted;
                sequence.ProgressLost -= HandleProgressLost;
                sequence.Solved -= HandleSolved;
                sequence = null;
            }
            else
            {
                subscribed.StateChanged -= HandleStateChanged;
            }

            subscribed.Rejected -= HandleRejected;
            subscribed = null;
        }

        private void HandleStateChanged(PuzzleElement element)
        {
            if (element.IsActive)
            {
                Play(activatedClip, activatedTone, 1f);
            }
            else
            {
                Play(deactivatedClip, deactivatedTone, 1f);
            }
        }

        private void HandleRejected(PuzzleElement element)
        {
            Play(rejectedClip, rejectedTone, 1f);
        }

        private void HandleStepCompleted(int completed)
        {
            // The last step is announced by the solved sound instead.
            if (sequence != null && completed < sequence.StepCount)
            {
                Play(stepClip, stepTone, 1f + 0.12f * (completed - 1));
            }
        }

        private void HandleProgressLost(int remaining)
        {
            Play(progressLostClip, progressLostTone, 1f);
        }

        private void HandleSolved()
        {
            Play(solvedClip, solvedTone, 1f);
        }

        private void Play(AudioClip clip, PuzzleTone tone, float pitch)
        {
            if (audioSource == null)
            {
                return;
            }

            AudioClip chosen = clip != null ? clip : PuzzleSounds.Get(tone);
            if (chosen == null)
            {
                return;
            }

            audioSource.pitch = pitch;
            audioSource.PlayOneShot(chosen, volume);
        }
    }
}
