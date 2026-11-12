using Threefold.Pooling;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// A short one-shot visual effect, such as an impact burst. It plays when it is spawned and
    /// returns itself to its pool when it has finished.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledObject))]
    public sealed class PooledEffect : MonoBehaviour
    {
        [Tooltip("Optional. Restarted every time the effect is spawned.")]
        [SerializeField] private ParticleSystem particles;

        [Tooltip("Seconds until the effect is returned to its pool.")]
        [Min(0.05f)]
        [SerializeField] private float lifetime = 0.8f;

        private PooledObject pooled;
        private float remaining;

        /// <summary>Connects the effect. Called by the setup tooling.</summary>
        public void Bind(ParticleSystem effectParticles, float seconds)
        {
            particles = effectParticles;
            lifetime = Mathf.Max(0.05f, seconds);
        }

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
        }

        private void OnEnable()
        {
            remaining = lifetime;

            if (particles != null)
            {
                particles.Clear(true);
                particles.Play(true);
            }
        }

        private void Update()
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                pooled.ReturnToPool();
            }
        }
    }
}
