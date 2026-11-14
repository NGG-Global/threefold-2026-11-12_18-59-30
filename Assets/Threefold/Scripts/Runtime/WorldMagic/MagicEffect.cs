using System;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>The ways magic can change something in the world, apart from damaging it.</summary>
    public enum MagicEffectKind
    {
        /// <summary>Warms the target. Magnitude is heat; a flammable target ignites at its capacity.</summary>
        Heat = 0,

        /// <summary>Feeds magical charge into the target. Magnitude is charge.</summary>
        ArcaneCharge = 1,

        /// <summary>Shoves the target. Magnitude is the distance in metres an unresisting target would move.</summary>
        Force = 2,

        /// <summary>Braces the target for a time. Magnitude is the strength of the brace, duration its length.</summary>
        Stabilize = 3,
    }

    /// <summary>Which kinds of target an effect is meant for.</summary>
    [Flags]
    public enum EffectTargets
    {
        None = 0,

        /// <summary>Things with health: enemies, training dummies.</summary>
        Creatures = 1,

        /// <summary>Things without: crystals, barricades, platforms, doors.</summary>
        Objects = 2,

        All = Creatures | Objects,
    }

    /// <summary>What a target did with an effect.</summary>
    public enum EffectResponse
    {
        /// <summary>A property took the effect.</summary>
        Applied,

        /// <summary>The target has no property that answers this kind of effect.</summary>
        NoProperty,

        /// <summary>The effect was not meant for this kind of target.</summary>
        Filtered,

        /// <summary>The target already took this effect from the same event.</summary>
        Duplicate,

        /// <summary>The target has the property, but the effect was too weak or the target is immune just now.</summary>
        Resisted,

        /// <summary>The target is destroyed; nothing is left to affect.</summary>
        Spent,
    }

    /// <summary>
    /// One magical effect arriving at one target. It changes the target's properties (heat, charge,
    /// position, bracing); it is not damage. An attack delivers its damage through
    /// <see cref="DamageInfo"/> and its effects through this, so the two can be tuned apart and a
    /// target may take either or both.
    /// </summary>
    public readonly struct MagicEffect
    {
        public readonly MagicEffectKind Kind;
        public readonly float Magnitude;

        /// <summary>Direction of a force, along the ground. Unused by the other kinds.</summary>
        public readonly Vector3 Direction;

        /// <summary>Seconds a brace lasts. 0 lets the target use its own default. Unused by the other kinds.</summary>
        public readonly float Duration;

        /// <summary>World position where the effect arrived.</summary>
        public readonly Vector3 Point;

        /// <summary>Who caused it. May be null.</summary>
        public readonly GameObject Source;

        /// <summary>The discipline it came from, as its selector slot, or -1 if none.</summary>
        public readonly int Discipline;

        public readonly EffectTargets Targets;

        /// <summary>
        /// Identifies the attack that produced the effect. A target takes each kind of effect once per
        /// event, however many of its colliders the attack touches. 0 means no identity: never deduplicated.
        /// </summary>
        public readonly int EventId;

        public MagicEffect(MagicEffectKind kind, float magnitude, Vector3 direction, float duration, Vector3 point,
            GameObject source, int discipline, EffectTargets targets, int eventId)
        {
            Kind = kind;
            Magnitude = magnitude;
            Direction = direction;
            Duration = duration;
            Point = point;
            Source = source;
            Discipline = discipline;
            Targets = targets;
            EventId = eventId;
        }

        /// <summary>A plain effect with no source, for tests and simple callers.</summary>
        public static MagicEffect Of(MagicEffectKind kind, float magnitude, int eventId = 0)
        {
            return new MagicEffect(kind, magnitude, Vector3.zero, 0f, Vector3.zero, null, -1, EffectTargets.All, eventId);
        }
    }

    /// <summary>
    /// The effects one attack carries and how strong each is. Authored as plain numbers on a melee
    /// swing or a charged attack; zero means the attack does not carry that effect.
    /// </summary>
    [Serializable]
    public struct EffectPayload
    {
        [Min(0f)]
        public float heat;

        [Min(0f)]
        public float arcaneCharge;

        [Tooltip("Metres an unresisting target is moved.")]
        [Min(0f)]
        public float force;

        [Tooltip("Strength of the brace. 0 means the attack does not stabilise.")]
        [Min(0f)]
        public float stabilize;

        [Tooltip("Seconds the brace lasts.")]
        [Min(0f)]
        public float stabilizeSeconds;

        [Tooltip("Which kinds of target take these effects.")]
        public EffectTargets targets;

        public bool IsEmpty => heat <= 0f && arcaneCharge <= 0f && force <= 0f && stabilize <= 0f;

        public float Get(MagicEffectKind kind)
        {
            switch (kind)
            {
                case MagicEffectKind.Heat: return heat;
                case MagicEffectKind.ArcaneCharge: return arcaneCharge;
                case MagicEffectKind.Force: return force;
                case MagicEffectKind.Stabilize: return stabilize;
                default: return 0f;
            }
        }

        /// <summary>
        /// The payload of a spell that only names its kind of magic, such as an enemy's bolt: arcane
        /// magic charges, fire heats, and raw force pushes, each with the given magnitude.
        /// </summary>
        public static EffectPayload FromElement(SpellElement element, float magnitude)
        {
            var payload = new EffectPayload { targets = EffectTargets.All };
            if (magnitude <= 0f)
            {
                return payload;
            }

            if ((element & SpellElement.Arcane) != 0) payload.arcaneCharge = magnitude;
            if ((element & SpellElement.Fire) != 0) payload.heat = magnitude;
            if ((element & SpellElement.Force) != 0) payload.force = magnitude;
            return payload;
        }
    }

    /// <summary>Anything that responds to magical effects: an object with magical properties, an enemy, a puzzle crystal.</summary>
    public interface IMagicEffectReceiver
    {
        /// <summary>Takes one effect. The answer says what became of it; callers may ignore it.</summary>
        EffectResponse ReceiveEffect(in MagicEffect effect);
    }

    /// <summary>Hands out the identities that tie the effects of one attack together.</summary>
    public static class MagicEffectEvents
    {
        private static int last;

        /// <summary>A new identity, never 0.</summary>
        public static int NextId()
        {
            last = last == int.MaxValue ? 1 : last + 1;
            return last;
        }
    }

    /// <summary>
    /// Remembers the last few effects a target took, so the same event does not act on it twice. A
    /// short fixed ring: no memory is allocated when effects arrive.
    /// </summary>
    public sealed class EffectEventLog
    {
        private const int Capacity = 8;

        private readonly int[] ids = new int[Capacity];
        private readonly MagicEffectKind[] kinds = new MagicEffectKind[Capacity];
        private int next;

        /// <summary>
        /// True if this kind of effect from this event was already recorded. Otherwise records it and
        /// returns false. An event id of 0 is never recorded and never a duplicate.
        /// </summary>
        public bool CheckAndRecord(int eventId, MagicEffectKind kind)
        {
            if (eventId == 0)
            {
                return false;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (ids[i] == eventId && kinds[i] == kind)
                {
                    return true;
                }
            }

            ids[next] = eventId;
            kinds[next] = kind;
            next = (next + 1) % Capacity;
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                ids[i] = 0;
            }

            next = 0;
        }
    }
}
