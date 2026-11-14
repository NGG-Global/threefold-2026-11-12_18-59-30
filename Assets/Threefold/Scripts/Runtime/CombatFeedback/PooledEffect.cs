using Threefold.Pooling;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// A short visual effect used for impacts, slashes and charge loops. It plays when spawned and
    /// returns itself to its pool when finished. <see cref="StopEarly"/> ends a loop or cancels a
    /// swing trail without waiting for the full lifetime, so interrupted attacks leave no orphan FX.
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
        private float remaining;
        private bool stopping;

        /// <summary>Connects the effect. Called by the setup tooling.</summary>
        public void Bind(ParticleSystem effectParticles, float seconds, bool loop = false)
        {
            particles = effectParticles;
            lifetime = Mathf.Max(0.05f, seconds);
            looping = loop;
            systems = null;
        }

        public bool IsLooping => looping;

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
        }

        private void OnEnable()
        {
            remaining = lifetime;
            stopping = false;
            EnsureSystems();

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
            EnsureSystems();

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null)
                {
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private void EnsureSystems()
        {
            if (systems != null)
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
        }
    }
}
