using Threefold.Pooling;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// A short visual effect used for impacts, slashes, trails and charge loops. It plays when spawned and
    /// returns itself to its pool when finished. <see cref="StopEarly"/> ends a loop or cancels a swing trail
    /// without waiting for the full lifetime, so interrupted attacks leave no orphan effects.
    ///
    /// Trail renderers under it are cleared when it is spawned (a reused trail never draws a streak from its
    /// previous place) and stop drawing when it stops. Its strength can follow an action
    /// (<see cref="SetIntensity"/>), its gathering radius a charge (<see cref="SetShapeRadius"/>), and it can
    /// follow a pooled object such as a projectile until that object is gone (<see cref="Follow"/>). It never
    /// deals damage and never touches the rules.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledObject))]
    public sealed class PooledEffect : MonoBehaviour
    {
        [Tooltip("Optional primary system. When empty, every ParticleSystem under this object is played.")]
        [SerializeField] private ParticleSystem particles;

        [Tooltip("Seconds until the effect is returned to its pool, unless stopped earlier.")]
        [Min(0.05f)]
        [SerializeField] private float lifetime = 0.8f;

        [Tooltip("When true, particle systems keep looping until StopEarly is called.")]
        [SerializeField] private bool looping;

        private PooledObject pooled;
        private ParticleSystem[] systems;
        private TrailRenderer[] trails;
        private float[] baseRateOverTime;
        private float[] baseRateOverDistance;
        private float[] baseSize;
        private float[] baseRadius;
        private float[] baseSpeed;
        private float[] baseTrailWidth;
        private float[] baseTrailTime;
        private float remaining;
        private bool stopping;
        private PooledObject followed;
        private int followedGeneration;
        private float followDrain;

        /// <summary>Connects the effect. Called by the setup tooling.</summary>
        public void Bind(ParticleSystem effectParticles, float seconds, bool loop = false)
        {
            particles = effectParticles;
            lifetime = Mathf.Max(0.05f, seconds);
            looping = loop;
            systems = null;
            trails = null;
        }

        public bool IsLooping => looping;

        /// <summary>True from the moment it is stopped until it returns to its pool.</summary>
        public bool IsStopping => stopping;

        public float Lifetime => lifetime;

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
        }

        private void OnEnable()
        {
            remaining = lifetime;
            stopping = false;
            followed = null;
            EnsureParts();
            RestoreAuthored();

            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].Clear();
                    trails[i].emitting = true;
                }
            }

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null)
                {
                    continue;
                }

                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        private void OnDisable()
        {
            stopping = false;
            followed = null;
        }

        private void Update()
        {
            if (looping && !stopping)
            {
                return;
            }

            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                pooled.ReturnToPool();
            }
        }

        private void LateUpdate()
        {
            if (followed == null)
            {
                return;
            }

            if (!followed.IsSpawn(followedGeneration))
            {
                // The projectile struck or was returned: the ribbon stays where it ended and fades.
                followed = null;
                StopEarly(followDrain);
                return;
            }

            transform.SetPositionAndRotation(followed.transform.position, followed.transform.rotation);
        }

        /// <summary>
        /// Stops emission and returns the effect to the pool shortly after. Safe to call when the
        /// effect was never started or has already finished.
        /// </summary>
        public void StopEarly(float drainSeconds = 0.08f)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            stopping = true;
            remaining = Mathf.Min(remaining, Mathf.Max(0.02f, drainSeconds));
            EnsureParts();

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null)
                {
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].emitting = false;
                }
            }
        }

        /// <summary>
        /// Strength from 0 to 1 for an effect that grows with an action: emission rates and trail widths scale
        /// with it, particle sizes a little. 1 is the effect as authored.
        /// </summary>
        public void SetIntensity(float intensity)
        {
            EnsureParts();
            float t = Mathf.Clamp01(intensity);
            float sizeScale = Mathf.Lerp(0.6f, 1f, t);

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTimeMultiplier = baseRateOverTime[i] * t;
                emission.rateOverDistanceMultiplier = baseRateOverDistance[i] * t;
                ParticleSystem.MainModule main = systems[i].main;
                main.startSizeMultiplier = baseSize[i] * sizeScale;
            }

            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].widthMultiplier = baseTrailWidth[i] * Mathf.Lerp(0.45f, 1f, t);
                }
            }
        }

        /// <summary>
        /// Sets the radius particles gather from, for a charge that concentrates as it grows. Their inward speed
        /// scales with it, so they still reach the centre within their lifetime.
        /// </summary>
        public void SetShapeRadius(float radius)
        {
            EnsureParts();
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null || baseRadius[i] <= 0f)
                {
                    continue;
                }

                ParticleSystem.ShapeModule shape = systems[i].shape;
                float scale = Mathf.Max(0.01f, radius) / baseRadius[i];
                shape.radius = baseRadius[i] * scale;
                ParticleSystem.MainModule main = systems[i].main;
                main.startSpeedMultiplier = baseSpeed[i] * scale;
            }
        }

        /// <summary>Trails last this share of their authored time: shorter on the Performance preset.</summary>
        public void SetTrailTimeScale(float scale)
        {
            EnsureParts();
            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].time = baseTrailTime[i] * Mathf.Clamp(scale, 0.1f, 2f);
                }
            }
        }

        /// <summary>
        /// Follows a pooled object, such as a projectile, until it is returned to its pool or reused; then stops
        /// and fades over <paramref name="drainSeconds"/> where the object was last seen.
        /// </summary>
        public void Follow(PooledObject target, float drainSeconds)
        {
            if (target == null)
            {
                return;
            }

            followed = target;
            followedGeneration = target.Generation;
            followDrain = Mathf.Max(0.02f, drainSeconds);
            transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
        }

        /// <summary>The longest trail time under this effect, in seconds: how long a stopped trail takes to fade.</summary>
        public float LongestTrailSeconds
        {
            get
            {
                EnsureParts();
                float longest = 0f;
                for (int i = 0; i < trails.Length; i++)
                {
                    if (trails[i] != null)
                    {
                        longest = Mathf.Max(longest, trails[i].time);
                    }
                }

                return longest;
            }
        }

        private void RestoreAuthored()
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTimeMultiplier = baseRateOverTime[i];
                emission.rateOverDistanceMultiplier = baseRateOverDistance[i];
                ParticleSystem.MainModule main = systems[i].main;
                main.startSizeMultiplier = baseSize[i];
                main.startSpeedMultiplier = baseSpeed[i];
                if (baseRadius[i] > 0f)
                {
                    ParticleSystem.ShapeModule shape = systems[i].shape;
                    shape.radius = baseRadius[i];
                }
            }

            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].widthMultiplier = baseTrailWidth[i];
                    trails[i].time = baseTrailTime[i];
                }
            }
        }

        private void EnsureParts()
        {
            if (systems != null && trails != null)
            {
                return;
            }

            if (particles != null)
            {
                systems = particles.GetComponentsInChildren<ParticleSystem>(true);
                if (systems.Length == 0)
                {
                    systems = new[] { particles };
                }
            }
            else
            {
                systems = GetComponentsInChildren<ParticleSystem>(true);
            }

            trails = GetComponentsInChildren<TrailRenderer>(true);

            // The authored values, so a reused effect starts as authored whatever its last use changed.
            baseRateOverTime = new float[systems.Length];
            baseRateOverDistance = new float[systems.Length];
            baseSize = new float[systems.Length];
            baseRadius = new float[systems.Length];
            baseSpeed = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = systems[i].emission;
                baseRateOverTime[i] = emission.rateOverTimeMultiplier;
                baseRateOverDistance[i] = emission.rateOverDistanceMultiplier;
                ParticleSystem.MainModule main = systems[i].main;
                baseSize[i] = main.startSizeMultiplier;
                baseSpeed[i] = main.startSpeedMultiplier;
                ParticleSystem.ShapeModule shape = systems[i].shape;
                baseRadius[i] = shape.enabled && IsRadial(shape.shapeType) ? shape.radius : 0f;
            }

            baseTrailWidth = new float[trails.Length];
            baseTrailTime = new float[trails.Length];
            for (int i = 0; i < trails.Length; i++)
            {
                baseTrailWidth[i] = trails[i].widthMultiplier;
                baseTrailTime[i] = trails[i].time;
            }
        }

        private static bool IsRadial(ParticleSystemShapeType type)
        {
            return type == ParticleSystemShapeType.Sphere || type == ParticleSystemShapeType.Hemisphere ||
                   type == ParticleSystemShapeType.Circle;
        }
    }
}
