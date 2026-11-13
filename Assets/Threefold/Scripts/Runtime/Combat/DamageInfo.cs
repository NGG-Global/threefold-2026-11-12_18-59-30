using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>One instance of damage on its way to a target.</summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly GameObject Source;

        /// <param name="amount">Damage before the target applies its own rules.</param>
        /// <param name="point">World position of the hit.</param>
        /// <param name="direction">Direction the damage travelled in.</param>
        /// <param name="source">Who caused the damage. May be null.</param>
        public DamageInfo(float amount, Vector3 point, Vector3 direction, GameObject source)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Source = source;
        }
    }

    /// <summary>Anything that can be damaged: characters, enemies, destructible objects.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        void ApplyDamage(in DamageInfo damage);
    }
}
