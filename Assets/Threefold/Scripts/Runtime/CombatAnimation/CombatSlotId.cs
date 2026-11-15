using System;
using Threefold.Magic;

namespace Threefold.CombatAnimation
{
    /// <summary>The kinds of combat animation slot.</summary>
    public enum CombatSlotKind
    {
        Stance,
        Attack,
        ChargeStart,
        ChargeHold,
        ChargeRelease,
        ChargeCancel,
        AerialAttack,
        AerialRelease,
        BlockHold,
        BlockImpact,
        Stagger,
        HeavyStagger,
        Defeat,
    }

    /// <summary>
    /// Names one slot: a discipline's (or the shared) slot of a kind, and for attacks the combo step.
    /// The Animator state that plays the slot is named after it, so gameplay and the generated
    /// controller agree without either knowing a clip name.
    /// </summary>
    public readonly struct CombatSlotId : IEquatable<CombatSlotId>
    {
        private const int SharedOwner = -1;
        private const int NoOwner = -2;

        private readonly int owner;

        public readonly CombatSlotKind Kind;

        /// <summary>The combo step for an attack slot, 0 otherwise.</summary>
        public readonly int Index;

        private CombatSlotId(int owner, CombatSlotKind kind, int index)
        {
            this.owner = owner;
            Kind = kind;
            Index = index;
        }

        /// <summary>No slot: the layer shows nothing.</summary>
        public static CombatSlotId None => new CombatSlotId(NoOwner, CombatSlotKind.Stance, 0);

        public static CombatSlotId For(MagicDiscipline discipline, CombatSlotKind kind, int index = 0)
        {
            return new CombatSlotId((int)discipline, kind, kind == CombatSlotKind.Attack ? Math.Max(0, index) : 0);
        }

        public static CombatSlotId Shared(CombatSlotKind kind)
        {
            return new CombatSlotId(SharedOwner, kind, 0);
        }

        public bool IsNone => owner == NoOwner;

        public bool IsShared => owner == SharedOwner;

        /// <summary>The discipline the slot belongs to. Meaningless for a shared slot.</summary>
        public MagicDiscipline Discipline => (MagicDiscipline)Math.Max(0, owner);

        /// <summary>The Animator state that plays this slot, for example "Arcane_Attack2" or "Shared_BlockHold".</summary>
        public string StateName
        {
            get
            {
                if (IsNone)
                {
                    return CombatAnimatorNames.EmptyState;
                }

                string prefix = IsShared ? "Shared" : Discipline.ToString();
                return Kind == CombatSlotKind.Attack ? $"{prefix}_Attack{Index + 1}" : $"{prefix}_{Kind}";
            }
        }

        /// <summary>
        /// The second state of a one-shot slot. Playing the same one-shot twice in a row alternates between
        /// the two, so the second play blends from the first instead of being ignored or snapping.
        /// </summary>
        public string TwinStateName => StateName + CombatAnimatorNames.TwinSuffix;

        /// <summary>Slots that hold a pose or loop for as long as they are wanted; the others play once.</summary>
        public bool IsHeld => Kind == CombatSlotKind.Stance || Kind == CombatSlotKind.ChargeHold ||
                              Kind == CombatSlotKind.BlockHold || Kind == CombatSlotKind.Defeat;

        public bool Equals(CombatSlotId other)
        {
            return owner == other.owner && Kind == other.Kind && Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            return obj is CombatSlotId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (owner * 397) ^ ((int)Kind * 31) ^ Index;
        }

        public static bool operator ==(CombatSlotId a, CombatSlotId b) => a.Equals(b);

        public static bool operator !=(CombatSlotId a, CombatSlotId b) => !a.Equals(b);

        public override string ToString()
        {
            return StateName;
        }
    }

    /// <summary>The layers the combat animations use, above the locomotion layer.</summary>
    public enum CombatLayer
    {
        /// <summary>The discipline's stance, upper body, faded by speed.</summary>
        Stance = 0,

        /// <summary>Committed attacks, aerial attacks and defeat, whole body.</summary>
        FullBody = 1,

        /// <summary>Attacks, charges, block and reactions on the upper body; the legs keep moving.</summary>
        UpperBody = 2,
    }

    /// <summary>Names shared by the generated Animator Controller and the code that plays it.</summary>
    public static class CombatAnimatorNames
    {
        public const string StanceLayer = "CombatStance";
        public const string FullBodyLayer = "CombatFullBody";
        public const string UpperBodyLayer = "CombatUpperBody";

        public const string FullBodySpeed = "CombatFullSpeed";
        public const string UpperBodySpeed = "CombatUpperSpeed";

        public const string EmptyState = "Empty";
        public const string TwinSuffix = "_B";

        public const int LayerCount = 3;

        public static string LayerName(CombatLayer layer)
        {
            switch (layer)
            {
                case CombatLayer.Stance: return StanceLayer;
                case CombatLayer.FullBody: return FullBodyLayer;
                default: return UpperBodyLayer;
            }
        }

        /// <summary>The speed parameter of a layer, or null for the stance layer, which plays at normal speed.</summary>
        public static string SpeedParameter(CombatLayer layer)
        {
            switch (layer)
            {
                case CombatLayer.FullBody: return FullBodySpeed;
                case CombatLayer.UpperBody: return UpperBodySpeed;
                default: return null;
            }
        }
    }
}
