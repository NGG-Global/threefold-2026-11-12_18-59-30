using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.Discovery
{
    /// <summary>
    /// Optional sound for discovery: a quiet tone when an object begins to resonate, and one for each
    /// kind of answer. Nothing in the game depends on it; every cue is complete without sound.
    ///
    /// There are no audio files in the project. Each slot accepts a clip; an empty slot plays one of
    /// the placeholder tones that the puzzle sounds generate in code (<see cref="PuzzleSounds"/>).
    /// They are stand-ins to make the hooks audible, not final audio, and are not licensed assets.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class DiscoveryAudio : MonoBehaviour
    {
        [SerializeField] private DiscoveryDirector director;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.5f;

        [Header("Clips (empty: a generated placeholder)")]
        [SerializeField] private AudioClip resonance = null;
        [SerializeField] private AudioClip refusal = null;
        [SerializeField] private AudioClip resistance = null;
        [SerializeField] private AudioClip progress = null;
        [SerializeField] private AudioClip transition = null;
        [SerializeField] private AudioClip completion = null;
        [SerializeField] private AudioClip held = null;

        private readonly AudioClip[] clips = new AudioClip[8];
        private AudioSource source;
        private DiscoveryDirector subscribed;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(DiscoveryDirector discoveryDirector)
        {
            director = discoveryDirector;
        }

        /// <summary>The placeholder tone that stands in for a discovery sound until a clip is assigned.</summary>
        public static PuzzleTone PlaceholderFor(DiscoverySound sound)
        {
            switch (sound)
            {
                case DiscoverySound.Resonance: return PuzzleTone.Tick;
                case DiscoverySound.Refusal: return PuzzleTone.Fizzle;
                case DiscoverySound.Resistance: return PuzzleTone.Rumble;
                case DiscoverySound.Progress: return PuzzleTone.Click;
                case DiscoverySound.Transition: return PuzzleTone.Chime;
                case DiscoverySound.Completion: return PuzzleTone.Fanfare;
                case DiscoverySound.Held: return PuzzleTone.Fade;
                default: return PuzzleTone.None;
            }
        }

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;

            if (director == null)
            {
                director = GetComponent<DiscoveryDirector>();
            }
        }

        private void Start()
        {
            // Built when the scene starts, so making one never interrupts play.
            Prepare(DiscoverySound.Resonance, resonance);
            Prepare(DiscoverySound.Refusal, refusal);
            Prepare(DiscoverySound.Resistance, resistance);
            Prepare(DiscoverySound.Progress, progress);
            Prepare(DiscoverySound.Transition, transition);
            Prepare(DiscoverySound.Completion, completion);
            Prepare(DiscoverySound.Held, held);
        }

        private void OnEnable()
        {
            subscribed = director;
            if (subscribed != null)
            {
                subscribed.ResonanceBegan += HandleResonance;
                subscribed.AnswerPresented += HandleAnswer;
            }
        }

        private void OnDisable()
        {
            if (subscribed != null)
            {
                subscribed.ResonanceBegan -= HandleResonance;
                subscribed.AnswerPresented -= HandleAnswer;
                subscribed = null;
            }
        }

        private void Prepare(DiscoverySound sound, AudioClip assigned)
        {
            clips[(int)sound] = assigned != null ? assigned : PuzzleSounds.Get(PlaceholderFor(sound));
        }

        private void HandleResonance(DiscoveryClues clues, float strength)
        {
            // Quiet: it is a hint, not an event.
            Play(DiscoverySound.Resonance, clues, 0.35f * strength);
        }

        private void HandleAnswer(DiscoveryClues clues, FeedbackResult result, FeedbackCue cue)
        {
            Play(cue.Sound, clues, Mathf.Lerp(0.5f, 1f, cue.Amplitude));
        }

        private void Play(DiscoverySound sound, DiscoveryClues clues, float loudness)
        {
            AudioClip clip = clips[(int)sound];
            if (clip == null || source == null || clues == null)
            {
                return;
            }

            // One source, moved to where the sound comes from: no object is created per sound.
            source.transform.position = clues.Position;
            source.PlayOneShot(clip, volume * Mathf.Clamp01(loudness));
        }
    }
}
