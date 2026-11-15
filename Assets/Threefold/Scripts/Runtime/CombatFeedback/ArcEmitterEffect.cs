using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>How an <see cref="ArcEmitterEffect"/> moves.</summary>
    public enum ArcEmitterMode
    {
        /// <summary>A front that travels outwards across an arc: a Flame Wave, a Force Pulse ring.</summary>
        Front = 0,

        /// <summary>A ribbon swept around the body along an arc at a fixed radius: a sweeping strike.</summary>
        Sweep = 1,
    }

    /// <summary>
    /// Emits particles along an arc that moves: the front of a wave travelling over the area of a charged
    /// attack, or a ribbon swept around the body during a strike. Positions come from
    /// <see cref="MagicVfxGeometry"/>; the particles are emitted by script into a world-space particle system, so
    /// the arc is exact and nothing depends on a shape module's orientation. Visual only: the attack's hit was
    /// resolved by the rules when it was released.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledEffect))]
    public sealed class ArcEmitterEffect : MonoBehaviour
    {
        [Tooltip("World-space particle system the particles are emitted into. Its own emission should be off.")]
        [SerializeField] private ParticleSystem particles;

        [SerializeField] private ArcEmitterMode mode = ArcEmitterMode.Front;

        [Tooltip("Particles per second for each metre of arc (front) or in total (sweep).")]
        [Min(0f)]
        [SerializeField] private float rate = 70f;

        [Tooltip("Radius the front starts from, in metres.")]
        [Min(0f)]
        [SerializeField] private float startRadius = 0.4f;

        [Tooltip("Sweep: the ribbon's radius as a share of the strike's reach.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float sweepRadiusShare = 0.32f;

        [Tooltip("Height of the arc above the origin, in metres.")]
        [SerializeField] private float height = 0.15f;

        [Tooltip("Outward speed of each particle, in metres per second.")]
        [SerializeField] private float outwardSpeed = 1.2f;

        [Tooltip("Upward speed of each particle, in metres per second.")]
        [SerializeField] private float riseSpeed = 0.6f;

        [Tooltip("How much of the discipline colour tints the particles, 0 to 1.")]
        [Range(0f, 1f)]
        [SerializeField] private float tint = 0.6f;

        private PooledEffect effect;
        private Vector3 origin;
        private Vector3 forward;
        private float reach;
        private float arcDegrees;
        private float seconds;
        private float elapsed;
        private float carry;
        private float lastAcross;
        private float rateScale = 1f;
        private bool reverse;
        private bool playing;
        private Color32 color = new Color32(255, 255, 255, 255);

        /// <summary>Connects the effect. Called by the setup tooling.</summary>
        public void Bind(ParticleSystem system, ArcEmitterMode emitterMode, float particlesPerSecond, float startAt,
            float arcHeight, float outward, float rise)
        {
            particles = system;
            mode = emitterMode;
            rate = particlesPerSecond;
            startRadius = startAt;
            height = arcHeight;
            outwardSpeed = outward;
            riseSpeed = rise;
        }

        public ArcEmitterMode Mode => mode;

        /// <summary>
        /// Starts the arc. <paramref name="direction"/> is flattened onto the ground. <paramref name="rateMultiplier"/>
        /// comes from the graphics preset and the intensity setting.
        /// </summary>
        public void Play(Vector3 from, Vector3 direction, float areaReach, float arcWidthDegrees, float travelSeconds,
            Color discipline, float rateMultiplier, bool sweepReverse = false)
        {
            if (effect == null)
            {
                effect = GetComponent<PooledEffect>();
            }

            origin = from;
            direction.y = 0f;
            forward = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            reach = Mathf.Max(0.1f, areaReach);
            arcDegrees = Mathf.Clamp(arcWidthDegrees, 5f, 360f);
            seconds = Mathf.Max(0.05f, travelSeconds);
            rateScale = Mathf.Max(0f, rateMultiplier);
            reverse = sweepReverse;
            elapsed = 0f;
            carry = 0f;
            lastAcross = MagicVfxGeometry.SweepAcross(0f, reverse);
            color = Color.Lerp(Color.white, discipline, tint);
            playing = particles != null;
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

            // Stopped from outside (its strike was cancelled): emit nothing more and let what is there fade.
            if (effect.IsStopping)
            {
                playing = false;
                return;
            }

            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            float progress = Mathf.Clamp01(elapsed / seconds);

            if (mode == ArcEmitterMode.Front)
            {
                EmitFront(progress, deltaTime);
            }
            else
            {
                EmitSweep(progress, deltaTime);
            }

            if (progress >= 1f)
            {
                playing = false;
                effect.StopEarly(LongestLifetime());
            }
        }

        private void EmitFront(float progress, float deltaTime)
        {
            float radius = MagicVfxGeometry.FrontRadius(startRadius, reach, progress);
            float arcLength = radius * arcDegrees * Mathf.Deg2Rad;
            int count = MagicVfxGeometry.ParticlesThisFrame(rate * rateScale * Mathf.Max(0.5f, arcLength), deltaTime, ref carry);

            for (int i = 0; i < count; i++)
            {
                float across = (i + Random.value) / count;
                Vector3 point = MagicVfxGeometry.ArcPoint(origin, forward, arcDegrees, radius, across);
                Vector3 outward = point - origin;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : forward;
                Emit(point + Vector3.up * height, outward * outwardSpeed + Vector3.up * riseSpeed);
            }
        }

        private void EmitSweep(float progress, float deltaTime)
        {
            float across = MagicVfxGeometry.SweepAcross(progress, reverse);
            float radius = reach * sweepRadiusShare;
            int count = MagicVfxGeometry.ParticlesThisFrame(rate * rateScale, deltaTime, ref carry);

            // Spread over the part of the arc covered since the last frame, so a slow frame leaves no gap.
            for (int i = 0; i < count; i++)
            {
                float at = Mathf.Lerp(lastAcross, across, (i + 1f) / count);
                Vector3 point = MagicVfxGeometry.ArcPoint(origin, forward, arcDegrees, radius, at);
                Vector3 outward = point - origin;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : forward;
                Emit(point + Vector3.up * height, outward * outwardSpeed + Vector3.up * riseSpeed);
            }

            lastAcross = across;
        }

        private void Emit(Vector3 position, Vector3 velocity)
        {
            var parameters = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startColor = color,
                applyShapeToPosition = false,
            };

            particles.Emit(parameters, 1);
        }

        private float LongestLifetime()
        {
            if (particles == null)
            {
                return 0.1f;
            }

            ParticleSystem.MinMaxCurve life = particles.main.startLifetime;
            return life.mode == ParticleSystemCurveMode.TwoConstants ? life.constantMax : life.constant;
        }
    }
}
