using System;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// The kind of magic a spell carries. World objects such as magical activators respond only to
    /// the kinds they are tuned to. The three disciplines use Arcane, Fire and Force; Frost is
    /// reserved for a later spell and can be selected on activators already.
    /// </summary>
    [Flags]
    public enum SpellElement
    {
        None = 0,
        Arcane = 1 << 0,
        Fire = 1 << 1,
        Frost = 1 << 2,

        /// <summary>Raw force: the Aegis discipline's pulse.</summary>
        Force = 1 << 3,
    }

    public static class SpellElements
    {
        /// <summary>True if <paramref name="element"/> is one of the <paramref name="accepted"/> kinds.</summary>
        public static bool Matches(SpellElement accepted, SpellElement element)
        {
            return element != SpellElement.None && (accepted & element) == element;
        }
    }

    /// <summary>A spell that has just struck something.</summary>
    public readonly struct SpellImpact
    {
        public readonly SpellElement Element;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;

        /// <summary>Who cast the spell. May be null.</summary>
        public readonly GameObject Caster;

        public SpellImpact(SpellElement element, Vector3 point, Vector3 direction, GameObject caster)
        {
            Element = element;
            Point = point;
            Direction = direction;
            Caster = caster;
        }
    }

    /// <summary>
    /// Anything that reacts to being struck by a spell, beyond taking damage: a crystal that lights
    /// up, a brazier that ignites, a seal that breaks.
    /// </summary>
    public interface ISpellReceiver
    {
        void ReceiveSpell(in SpellImpact impact);
    }
}
