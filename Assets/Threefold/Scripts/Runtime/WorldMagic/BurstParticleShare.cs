using Threefold.Pooling;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Plays a pooled burst with only a share of its particles, by limiting how many of them may be
    /// alive at once to that share of what the burst emits. It remembers each particle system's own
    /// values the first time, so an instance that comes back from the pool at another share is set
    /// from the original, never compounded. Added to a pooled instance only when it is first played
    /// at less than its full share.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class BurstParticleShare : MonoBehaviour
    {
        private ParticleSystem[] systems;
        private int[] originalLimits;
        private int[] emitted;

        public static void Apply(PooledObject burst, float share)
        {
            if (burst == null)
            {
                return;
            }

            // Nothing to do at full share for an instance that was never limited.
            if (!burst.TryGetComponent(out BurstParticleShare limiter))
            {
                if (share >= 0.999f)
                {
                    return;
                }

                limiter = burst.gameObject.AddComponent<BurstParticleShare>();
            }

            limiter.SetShare(share);
        }

        private void SetShare(float share)
        {
            if (systems == null)
            {
                Remember();
            }

            share = Mathf.Clamp01(share);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = systems[i].main;
                main.maxParticles = share >= 0.999f
                    ? originalLimits[i]
                    : Mathf.Max(1, Mathf.RoundToInt(emitted[i] * share));
            }
        }

        private void Remember()
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            originalLimits = new int[systems.Length];
            emitted = new int[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                int limit = systems[i].main.maxParticles;
                originalLimits[i] = limit;

                // What its bursts emit; a system without bursts is measured by its own limit.
                ParticleSystem.EmissionModule emission = systems[i].emission;
                float total = 0f;
                for (int b = 0; b < emission.burstCount; b++)
                {
                    ParticleSystem.MinMaxCurve count = emission.GetBurst(b).count;
                    total += count.mode == ParticleSystemCurveMode.Constant ? count.constant : count.constantMax;
                }

                emitted[i] = total > 0f ? Mathf.Min(limit, Mathf.CeilToInt(total)) : limit;
            }
        }
    }
}
