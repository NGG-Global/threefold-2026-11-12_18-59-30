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
    /// Announcements from the combat rules to anything that presents them: damage numbers, camera
    /// shake, sound. Combat code raises these and knows nothing about who listens.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>Raised after damage has been applied to any target.</summary>
        public static event Action<DamageEvent> DamageDealt;

        internal static void RaiseDamageDealt(in DamageEvent damage)
        {
            DamageDealt?.Invoke(damage);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DamageDealt = null;
        }
    }
}
