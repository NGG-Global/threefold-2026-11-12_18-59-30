using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Plays sounds for a puzzle element's moments: switching on, switching off, a refused attempt,
    /// and for a sequence each completed step, lost progress and the solution. Every slot takes a
    /// clip; an empty slot plays a generated placeholder (<see cref="PuzzleSounds"/>), or nothing
    /// if its tone is None. Generated step chimes rise with each step; a step clip set by hand is
    /// played as it is for every step.
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

        private void Start()
        {
            // Builds the generated sounds now, while the scene starts, not at the moment they are needed.
            Prepare(activatedClip, activatedTone);
            Prepare(deactivatedClip, deactivatedTone);
            Prepare(rejectedClip, rejectedTone);

            if (source is PuzzleSequence steps)
            {
                Prepare(progressLostClip, progressLostTone);
                Prepare(solvedClip, solvedTone);
                if (stepClip == null && stepTone != PuzzleTone.None)
                {
                    for (int i = 1; i < steps.StepCount; i++)
                    {
                        PuzzleSounds.GetStepChime(i);
                    }
                }
            }
        }

        private static void Prepare(AudioClip clip, PuzzleTone tone)
        {
            if (clip == null)
            {
                PuzzleSounds.Get(tone);
            }
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
                Play(activatedClip, activatedTone);
            }
            else
            {
                Play(deactivatedClip, deactivatedTone);
            }
        }

        private void HandleRejected(PuzzleElement element)
        {
            Play(rejectedClip, rejectedTone);
        }

        private void HandleStepCompleted(int completed)
        {
            // The last step is announced by the solved sound instead.
            if (sequence == null || completed >= sequence.StepCount)
            {
                return;
            }

            if (stepClip != null)
            {
                PlayClip(stepClip);
            }
            else if (stepTone != PuzzleTone.None)
            {
                PlayClip(stepTone == PuzzleTone.Chime ? PuzzleSounds.GetStepChime(completed) : PuzzleSounds.Get(stepTone));
            }
        }

        private void HandleProgressLost(int remaining)
        {
            Play(progressLostClip, progressLostTone);
        }

        private void HandleSolved()
        {
            Play(solvedClip, solvedTone);
        }

        private void Play(AudioClip clip, PuzzleTone tone)
        {
            PlayClip(clip != null ? clip : PuzzleSounds.Get(tone));
        }

        /// <summary>The source's pitch is never changed, because that would also bend sounds still ringing.</summary>
        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip, volume);
            }
        }
    }
}
