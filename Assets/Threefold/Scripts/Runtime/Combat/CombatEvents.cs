using System;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>Damage that was actually applied to a target.</summary>
    public readonly struct DamageEvent
    {
        public readonly GameObject Target;
        public readonly GameObject Source;
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly bool Defeated;

        public DamageEvent(GameObject target, GameObject source, float amount, Vector3 point, bool defeated)
        {
            Target = target;
            Source = source;
            Amount = amount;
            Point = point;
            Defeated = defeated;
        }
    }

    /// <summary>
    /// A sound that creatures could notice, such as a spell striking a wall. Enemies that hear it
    /// come to look. Only position and reach are modelled; nothing is played.
    /// </summary>
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;

        /// <summary>Distance in metres within which the noise can be heard.</summary>
        public readonly float Radius;

        /// <summary>Who made the noise. May be null.</summary>
        public readonly GameObject Source;

        public NoiseEvent(Vector3 position, float radius, GameObject source)
        {
            Position = position;
            Radius = radius;
            Source = source;
        }
    }

    /// <summary>
    /// Announcements from the combat rules to anything that presents them: damage numbers, camera
    /// shake, sound. Combat code raises these and knows nothing about who listens.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>Raised after damage has been applied to any target.</summary>
        public static event Action<DamageEvent> DamageDealt;

        /// <summary>Raised when something makes a noise that enemies could hear.</summary>
        public static event Action<NoiseEvent> Noise;

        internal static void RaiseDamageDealt(in DamageEvent damage)
        {
            DamageDealt?.Invoke(damage);
        }

        /// <summary>Announces a noise. Public so that any system, such as footsteps later, can make one.</summary>
        public static void RaiseNoise(in NoiseEvent noise)
        {
            if (noise.Radius > 0f)
            {
                Noise?.Invoke(noise);
            }
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DamageDealt = null;
            Noise = null;
        }
    }
}
