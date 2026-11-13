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

        /// <summary>
        /// How hard the hit pushes against the target's poise, in the same units as damage. Negative
        /// means "the same as the damage", which is what attacks without a special stagger value use.
        /// </summary>
        public readonly float Stagger;

        /// <summary>The kind of magic the damage carries, or None for a plain hit.</summary>
        public readonly SpellElement Element;

        /// <param name="amount">Damage before the target applies its own rules.</param>
        /// <param name="point">World position of the hit.</param>
        /// <param name="direction">Direction the damage travelled in.</param>
        /// <param name="source">Who caused the damage. May be null.</param>
        public DamageInfo(float amount, Vector3 point, Vector3 direction, GameObject source)
            : this(amount, point, direction, source, -1f, SpellElement.None)
        {
        }

        /// <param name="stagger">Poise damage; negative uses the damage amount.</param>
        /// <param name="element">The kind of magic the damage carries.</param>
        public DamageInfo(float amount, Vector3 point, Vector3 direction, GameObject source, float stagger,
            SpellElement element)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Source = source;
            Stagger = stagger;
            Element = element;
        }

        /// <summary>The poise damage this hit deals: its stagger value, or its damage if none was given.</summary>
        public float PoiseDamage => Stagger >= 0f ? Stagger : Amount;

        /// <summary>The same hit with a different amount, for rules that reduce or cap damage.</summary>
        public DamageInfo WithAmount(float amount)
        {
            return new DamageInfo(amount, Point, Direction, Source, Stagger, Element);
        }
    }

    /// <summary>
    /// Something that changes damage before a <see cref="Health"/> takes it, such as a raised guard.
    /// Registered with <see cref="Health.AddModifier"/>.
    /// </summary>
    public interface IDamageModifier
    {
        /// <summary>Returns the amount that should still be applied; 0 or less stops the hit entirely.</summary>
        float ModifyIncomingDamage(in DamageInfo damage, float amount);
    }

    /// <summary>Anything that can be damaged: characters, enemies, destructible objects.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        void ApplyDamage(in DamageInfo damage);
    }
}
