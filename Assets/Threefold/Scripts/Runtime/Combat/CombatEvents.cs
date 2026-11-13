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

    public enum StatusKind
    {
        /// <summary>The target reached full heat and started to burn.</summary>
        Ignited,

        /// <summary>A burning target took a tick of burn damage.</summary>
        BurnTick,

        /// <summary>The target was shoved by a force impulse.</summary>
        Pushed,
    }

    /// <summary>A status effect that started or ticked on a target, for anything that shows it.</summary>
    public readonly struct StatusEvent
    {
        public readonly GameObject Target;
        public readonly StatusKind Kind;
        public readonly Vector3 Point;

        /// <summary>How long the status lasts, in seconds, where that applies.</summary>
        public readonly float Seconds;

        public StatusEvent(GameObject target, StatusKind kind, Vector3 point, float seconds)
        {
            Target = target;
            Kind = kind;
            Point = point;
            Seconds = seconds;
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

        /// <summary>Raised when a status effect starts or ticks on any target.</summary>
        public static event Action<StatusEvent> Status;

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

        public static void RaiseStatus(in StatusEvent status)
        {
            Status?.Invoke(status);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DamageDealt = null;
            Noise = null;
            Status = null;
        }
    }
}
