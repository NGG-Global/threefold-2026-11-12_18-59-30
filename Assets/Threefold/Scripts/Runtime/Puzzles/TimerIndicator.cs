using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Shows a timer as a ring of glowing stones that go out one by one, each with a tick that rises
    /// in pitch as time runs short; the last stones blink. While the timer is idle the stones are
    /// dark, and once its puzzle is solved they all stay lit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimerIndicator : MonoBehaviour
    {
        [SerializeField] private TimedMechanism timer;

        [Tooltip("The indicator stones, in the order they go out.")]
        [SerializeField] private Renderer[] segments = new Renderer[0];

        [ColorUsage(false, true)]
        [SerializeField] private Color idleColor = new Color(0.03f, 0.04f, 0.09f);

        [ColorUsage(false, true)]
        [SerializeField] private Color litColor = new Color(0.8f, 1.1f, 2.8f);

        [ColorUsage(false, true)]
        [SerializeField] private Color warningColor = new Color(2.4f, 0.9f, 0.5f);

        [Tooltip("Below this share of the time left, the remaining stones turn warm and blink.")]
        [Range(0f, 1f)]
        [SerializeField] private float warningFraction = 0.34f;

        [Header("Sound")]
        [Tooltip("Optional. Plays a tick each time a stone goes out.")]
        [SerializeField] private AudioSource tickSource;

        [Tooltip("Optional. If empty, a generated tick is used.")]
        [SerializeField] private AudioClip tickClip = null;

        [Range(0f, 1f)]
        [SerializeField] private float tickVolume = 0.6f;

        private MaterialPropertyBlock block;
        private int lastLit = -1;

        public TimedMechanism Timer => timer;
        public Renderer[] Segments => segments;

        /// <summary>Connects the indicator. Called by the setup tooling.</summary>
        public void Bind(TimedMechanism shownTimer, Renderer[] stones, AudioSource source)
        {
            timer = shownTimer;
            segments = stones ?? new Renderer[0];
            tickSource = source;
        }

        private void Update()
        {
            if (timer == null)
            {
                return;
            }

            int count = segments.Length;
            bool complete = timer.IsLocked && timer.IsActive;
            bool running = timer.IsRunning;
            int lit = complete ? count : running ? timer.SegmentsLit(count) : 0;

            if (running && lastLit > lit)
            {
                PlayTick(count - lit);
            }

            lastLit = lit;

            bool warning = running && timer.RemainingFraction < warningFraction;
            bool blinkOn = Mathf.Repeat(Time.time * 4f, 1f) < 0.6f;

            for (int i = 0; i < count; i++)
            {
                Color color = idleColor;
                if (i < lit)
                {
                    color = warning ? (blinkOn ? warningColor : warningColor * 0.35f) : litColor;
                }

                GlowUtility.SetEmission(segments[i], -1, color, ref block);
            }
        }

        private void PlayTick(int gone)
        {
            if (tickSource == null)
            {
                return;
            }

            AudioClip clip = tickClip != null ? tickClip : PuzzleSounds.Get(PuzzleTone.Tick);
            if (clip == null)
            {
                return;
            }

            tickSource.pitch = 1f + 0.06f * gone;
            tickSource.PlayOneShot(clip, tickVolume);
        }
    }
}
