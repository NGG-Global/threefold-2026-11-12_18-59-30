using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>A slot chosen for an action, and the layer it plays on.</summary>
    public readonly struct ResolvedSlot
    {
        public readonly CombatSlotId Id;
        public readonly CombatAnimationSlot Slot;
        public readonly CombatLayer Layer;

        /// <summary>True for a slot made for the air: it ends when the character lands.</summary>
        public readonly bool Aerial;

        public ResolvedSlot(CombatSlotId id, CombatAnimationSlot slot, CombatLayer layer, bool aerial)
        {
            Id = id;
            Slot = slot;
            Layer = layer;
            Aerial = aerial;
        }

        public bool IsValid => Slot != null && !Id.IsNone;

        public static ResolvedSlot None => new ResolvedSlot(CombatSlotId.None, null, CombatLayer.UpperBody, false);
    }

    /// <summary>
    /// Which slot plays for an action, with fallbacks so that a missing animation never stops one:
    /// - in the air: the discipline's aerial slot, then the shared aerial slot, then the grounded slot on the
    ///   upper body;
    /// - a melee step beyond the discipline's attack slots uses the last one;
    /// - a full-body slot plays on the whole body only where it cannot slide: on the ground below the
    ///   full-body speed, or in the air for an aerial slot. Otherwise it plays on the upper body.
    /// Plain C#, tested outside Unity.
    /// </summary>
    public static class CombatSlotResolver
    {
        public static bool IsAssigned(CombatAnimationSlot slot)
        {
            return slot != null && slot.IsAssigned;
        }

        public static ResolvedSlot ResolveAttack(CombatAnimationLibrary library, MagicDiscipline discipline, int step,
            bool airborne, float horizontalSpeed)
        {
            if (library == null)
            {
                return ResolvedSlot.None;
            }

            DisciplineAnimationSet set = library.GetDiscipline(discipline);
            if (airborne)
            {
                if (set != null && IsAssigned(set.aerialAttack))
                {
                    return Make(library, CombatSlotId.For(discipline, CombatSlotKind.AerialAttack), set.aerialAttack, true, true,
                        horizontalSpeed);
                }

                if (IsAssigned(library.shared?.aerialAttack))
                {
                    return Make(library, CombatSlotId.Shared(CombatSlotKind.AerialAttack), library.shared.aerialAttack, true, true,
                        horizontalSpeed);
                }
            }

            if (set == null || set.AttackCount == 0)
            {
                return ResolvedSlot.None;
            }

            int index = Mathf.Clamp(step, 0, set.AttackCount - 1);
            CombatAnimationSlot slot = set.attacks[index];
            if (!IsAssigned(slot))
            {
                return ResolvedSlot.None;
            }

            return Make(library, CombatSlotId.For(discipline, CombatSlotKind.Attack, index), slot, airborne, false, horizontalSpeed);
        }

        public static ResolvedSlot ResolveRelease(CombatAnimationLibrary library, MagicDiscipline discipline, bool airborne,
            float horizontalSpeed)
        {
            if (library == null)
            {
                return ResolvedSlot.None;
            }

            DisciplineAnimationSet set = library.GetDiscipline(discipline);
            if (airborne)
            {
                if (set != null && IsAssigned(set.aerialRelease))
                {
                    return Make(library, CombatSlotId.For(discipline, CombatSlotKind.AerialRelease), set.aerialRelease, true, true,
                        horizontalSpeed);
                }

                if (IsAssigned(library.shared?.aerialRelease))
                {
                    return Make(library, CombatSlotId.Shared(CombatSlotKind.AerialRelease), library.shared.aerialRelease, true, true,
                        horizontalSpeed);
                }
            }

            if (set == null || !IsAssigned(set.chargeRelease))
            {
                return ResolvedSlot.None;
            }

            return Make(library, CombatSlotId.For(discipline, CombatSlotKind.ChargeRelease), set.chargeRelease, airborne, false,
                horizontalSpeed);
        }

        /// <summary>A discipline slot or a shared slot by kind, on the upper body; None if it is not assigned.</summary>
        public static ResolvedSlot ResolveSimple(CombatAnimationLibrary library, CombatSlotId id)
        {
            CombatAnimationSlot slot = library?.GetSlot(id);
            return IsAssigned(slot) ? new ResolvedSlot(id, slot, CombatLayer.UpperBody, false) : ResolvedSlot.None;
        }

        /// <summary>The hand a discipline's charge glows in: its hold's, else its start's.</summary>
        public static CombatEffector ChargeEffector(CombatAnimationLibrary library, MagicDiscipline discipline)
        {
            DisciplineAnimationSet set = library?.GetDiscipline(discipline);
            if (set == null)
            {
                return CombatEffector.None;
            }

            if (IsAssigned(set.chargeHold))
            {
                return set.chargeHold.effector;
            }

            return IsAssigned(set.chargeStart) ? set.chargeStart.effector : CombatEffector.None;
        }

        /// <summary>The layer a slot plays on, from its scope and the body's state.</summary>
        public static CombatLayer LayerFor(CombatAnimationSlot slot, bool airborne, bool aerialSlot, float horizontalSpeed,
            float fullBodyMaxSpeed)
        {
            if (slot == null || slot.scope != CombatBodyScope.FullBody)
            {
                return CombatLayer.UpperBody;
            }

            if (aerialSlot)
            {
                return airborne ? CombatLayer.FullBody : CombatLayer.UpperBody;
            }

            return !airborne && horizontalSpeed <= fullBodyMaxSpeed ? CombatLayer.FullBody : CombatLayer.UpperBody;
        }

        private static ResolvedSlot Make(CombatAnimationLibrary library, CombatSlotId id, CombatAnimationSlot slot,
            bool airborne, bool aerialSlot, float horizontalSpeed)
        {
            float limit = library.tuning != null ? library.tuning.fullBodyMaxSpeed : 0.6f;
            CombatLayer layer = LayerFor(slot, airborne, aerialSlot, horizontalSpeed, limit);
            return new ResolvedSlot(id, slot, layer, aerialSlot && layer == CombatLayer.FullBody);
        }
    }
}
