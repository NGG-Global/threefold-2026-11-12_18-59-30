using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows magic arriving at an object and going into it: particles stream from where the magic struck to the
    /// object's centre, and (on the richer presets) gather in from a shell round the object, so charge appears
    /// to flow into it, or a brace to close round it. Every particle is timed to reach the centre at the end of
    /// its life. Emitted by script into a world-space particle system; visual only.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledEffect))]
    public sealed class InflowEffect : MonoBehaviour
    {
        [Tooltip("World-space particle system the particles are emitted into. Its own emission should be off.")]
        [SerializeField] private ParticleSystem particles;

        [Tooltip("Particles per second along the stream.")]
        [Min(0f)]
        [SerializeField] private float streamRate = 60f;

        [Tooltip("Particles emitted on the shell round the object, in total.")]
        [Min(0)]
        [SerializeField] private int shellCount = 10;

        [Tooltip("Seconds the stream keeps flowing.")]
        [Min(0.05f)]
        [SerializeField] private float flowSeconds = 0.3f;

        [Tooltip("Seconds each particle takes to reach the centre.")]
        [Min(0.05f)]
        [SerializeField] private float travelSeconds = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float tint = 0.7f;

        private PooledEffect effect;
        private Vector3 start;
        private Vector3 centre;
        private float elapsed;
        private float carry;
        private float rateScale = 1f;
        private bool playing;
        private Color32 color = new Color32(255, 255, 255, 255);

        /// <summary>Connects the effect. Called by the setup tooling.</summary>
        public void Bind(ParticleSystem system, float perSecond, int onShell, float flow, float travel)
        {
            particles = system;
            streamRate = perSecond;
            shellCount = onShell;
            flowSeconds = flow;
            travelSeconds = travel;
        }

        /// <summary>
        /// Starts the inflow from <paramref name="from"/> into <paramref name="into"/>. A shell of
        /// <paramref name="shellRadius"/> is shown only when <paramref name="withShell"/> is true (the richer presets).
        /// </summary>
        public void Play(Vector3 from, Vector3 into, float shellRadius, Color discipline, float rateMultiplier, bool withShell)
        {
            if (effect == null)
            {
                effect = GetComponent<PooledEffect>();
            }

            start = from;
            centre = into;
            rateScale = Mathf.Max(0f, rateMultiplier);
            elapsed = 0f;
            carry = 0f;
            color = Color.Lerp(Color.white, discipline, tint);
            playing = particles != null;

            if (playing && withShell && shellCount > 0 && shellRadius > 0f)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(shellCount * Mathf.Min(1f, rateScale)));
                for (int i = 0; i < count; i++)
                {
                    Vector3 point = MagicVfxGeometry.ShellPoint(centre, shellRadius, i, count);
                    Emit(point, (centre - point) / travelSeconds);
                }
            }
        }

        private void OnDisable()
        {
            playing = false;
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            if (effect.IsStopping)
            {
                playing = false;
                return;
            }

            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            int count = MagicVfxGeometry.ParticlesThisFrame(streamRate * rateScale, deltaTime, ref carry);
            Vector3 velocity = (centre - start) / travelSeconds;

            for (int i = 0; i < count; i++)
            {
                // A little spread so the stream reads as a flow, not a line.
                Emit(start + Random.insideUnitSphere * 0.06f, velocity);
            }

            if (elapsed >= flowSeconds)
            {
                playing = false;
                effect.StopEarly(travelSeconds);
            }
        }

        private void Emit(Vector3 position, Vector3 velocity)
        {
            var parameters = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startLifetime = travelSeconds,
                startColor = color,
                applyShapeToPosition = false,
            };

            particles.Emit(parameters, 1);
        }
    }
}
