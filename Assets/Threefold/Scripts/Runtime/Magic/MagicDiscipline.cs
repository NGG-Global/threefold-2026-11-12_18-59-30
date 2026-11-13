using System.Collections.Generic;
using Threefold.Interface;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>The schools of Living Magic the player switches between. The value is the selector slot.</summary>
    public enum MagicDiscipline
    {
        Arcane = 0,
        Flame = 1,
        Aegis = 2,
    }

    /// <summary>How a discipline is named and shown on the controls.</summary>
    public readonly struct DisciplineInfo
    {
        public readonly string Name;

        /// <summary>Accent colour on the attack button, the indicator and the selector.</summary>
        public readonly Color Color;

        public readonly ProceduralShape Icon;

        public DisciplineInfo(string name, Color color, ProceduralShape icon)
        {
            Name = name;
            Color = color;
            Icon = icon;
        }
    }

    /// <summary>
    /// The disciplines and their presentation. Three entries today; the selector and the loadout
    /// work with any count, so a fourth is added here without changing them.
    ///
    /// The colours are a proposal drawn from the Art Bible's palette families (a cool blue-violet
    /// for Arcane, weathered terracotta and amber for Flame, a gold accent for Aegis); they are not
    /// approved swatches. Discipline assets, once generated, carry their own colour and icon.
    /// </summary>
    public static class DisciplineCatalog
    {
        private static readonly DisciplineInfo[] Entries =
        {
            new DisciplineInfo("Arcane", new Color(0.62f, 0.64f, 1f, 1f), ProceduralShape.Star),
            new DisciplineInfo("Flame", new Color(1f, 0.56f, 0.27f, 1f), ProceduralShape.Flame),
            new DisciplineInfo("Aegis", new Color(0.96f, 0.84f, 0.46f, 1f), ProceduralShape.Hexagon),
        };

        public static int Count => Entries.Length;

        public static IReadOnlyList<DisciplineInfo> All => Entries;

        /// <summary>The entry for a slot. An unknown slot yields a neutral entry rather than an error.</summary>
        public static DisciplineInfo Get(int slot)
        {
            return slot >= 0 && slot < Entries.Length
                ? Entries[slot]
                : new DisciplineInfo("None", new Color(1f, 0.96f, 0.86f, 1f), ProceduralShape.Diamond);
        }

        public static DisciplineInfo Get(MagicDiscipline discipline)
        {
            return Get((int)discipline);
        }
    }
}
