using System;
using System.Text;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>Which part of the body a combat animation moves.</summary>
    public enum CombatBodyScope
    {
        /// <summary>Spine, arms and head; the legs keep walking or running. Usable while moving.</summary>
        UpperBody = 0,

        /// <summary>
        /// The whole body. Played only when it cannot slide: on the ground while nearly standing still,
        /// or in the air for an aerial slot. Otherwise the same clip plays on the upper body.
        /// </summary>
        FullBody = 1,
    }

    /// <summary>The hand or foot that strikes or releases. Effects can attach there.</summary>
    public enum CombatEffector
    {
        None = 0,
        LeftHand = 1,
        RightHand = 2,
        BothHands = 3,
        LeftFoot = 4,
        RightFoot = 5,
    }

    /// <summary>
    /// One animation slot: which clip plays for one combat moment, and when, inside that clip, the
    /// moments that matter to gameplay happen. Times are normalised clip time (0 is the first frame,
    /// 1 the last), so they stay correct whatever speed the clip is played at, and the imported clip
    /// itself never has to be edited.
    ///
    /// Gameplay never reads this. The combat rules keep their own clock (<see cref="MeleeComboMachine"/>);
    /// <see cref="CombatTimeWarp"/> fits the clip to that clock, so the clip's contact frame is shown
    /// at the moment the hit is resolved.
    /// </summary>
    [Serializable]
    public sealed class CombatAnimationSlot
    {
        [Tooltip("Name of the animation clip in the character model. Empty: the slot is unused and a fallback plays.")]
        public string clip = string.Empty;

        [Tooltip("Play the clip mirrored, left and right swapped. Works for Humanoid clips.")]
        public bool mirror;

        [Tooltip("Upper body: the legs keep moving. Full body: only when grounded and nearly still, or in the air " +
                 "for an aerial slot; otherwise it plays on the upper body.")]
        public CombatBodyScope scope = CombatBodyScope.UpperBody;

        [Tooltip("The hand or foot that strikes or releases, as played (after mirroring). Effects can attach there.")]
        public CombatEffector effector = CombatEffector.RightHand;

        [Header("Moments in the clip (0 = first frame, 1 = last)")]
        [Tooltip("Where the clip starts. Moved later automatically when the wind-up cannot be shown fast enough.")]
        [Range(0f, 1f)]
        public float entry;

        [Tooltip("Melee: the frame where the strike connects. Charged release: the frame where the magic leaves the hand.")]
        [Range(0f, 1f)]
        public float contact = 0.5f;

        [Tooltip("The striking limb starts its fast, dangerous part.")]
        [Range(0f, 1f)]
        public float hitWindowOpen = 0.4f;

        [Tooltip("The strike's dangerous part is over; recovery begins.")]
        [Range(0f, 1f)]
        public float hitWindowClose = 0.6f;

        [Tooltip("When the attack's visual effect should appear.")]
        [Range(0f, 1f)]
        public float effect = 0.3f;

        [Tooltip("The pose is back to neutral enough to blend out or into the next action.")]
        [Range(0f, 1f)]
        public float recoveryEnd = 1f;

        [Tooltip("Show a still pose, the frame at Entry, instead of the motion. For stances and holds made from one frame.")]
        public bool holdPose;

        [Header("Blending")]
        [Min(0f)]
        public float blendInSeconds = 0.08f;

        [Min(0f)]
        public float blendOutSeconds = 0.15f;

        [Tooltip("Length of the clip at normal speed, in seconds. The setup fills it in from the model.")]
        [Min(0f)]
        public float clipSeconds = 1f;

        public bool IsAssigned => !string.IsNullOrEmpty(clip);

        /// <summary>The clip length, never zero.</summary>
        public float Seconds => Mathf.Max(clipSeconds, 0.01f);

        public CombatAnimationSlot Clone()
        {
            return (CombatAnimationSlot)MemberwiseClone();
        }
    }

    /// <summary>The animation slots of one discipline.</summary>
    [Serializable]
    public sealed class DisciplineAnimationSet
    {
        public MagicDiscipline discipline;

        [Tooltip("Upper-body pose while standing in combat with this discipline selected.")]
        public CombatAnimationSlot stance = new CombatAnimationSlot();

        [Tooltip("One per melee step, in combo order. A step beyond the list uses the last slot.")]
        public CombatAnimationSlot[] attacks = new CombatAnimationSlot[0];

        [Header("Charged attack")]
        public CombatAnimationSlot chargeStart = new CombatAnimationSlot();
        public CombatAnimationSlot chargeHold = new CombatAnimationSlot();
        public CombatAnimationSlot chargeRelease = new CombatAnimationSlot();
        public CombatAnimationSlot chargeCancel = new CombatAnimationSlot();

        [Header("In the air")]
        [Tooltip("Replaces the attack in the air. Empty: the shared aerial attack, then the attack on the upper body.")]
        public CombatAnimationSlot aerialAttack = new CombatAnimationSlot();

        [Tooltip("Replaces the release in the air. Empty: the shared aerial release, then the release on the upper body.")]
        public CombatAnimationSlot aerialRelease = new CombatAnimationSlot();

        public int AttackCount => attacks != null ? attacks.Length : 0;
    }

    /// <summary>Slots that are the same whatever discipline is selected.</summary>
    [Serializable]
    public sealed class SharedCombatAnimations
    {
        [Tooltip("Held while BLOCK is held. Block is the same for every discipline.")]
        public CombatAnimationSlot blockHold = new CombatAnimationSlot();

        [Tooltip("A hit stopped by the guard.")]
        public CombatAnimationSlot blockImpact = new CombatAnimationSlot();

        [Tooltip("An unguarded hit of at least the stagger damage.")]
        public CombatAnimationSlot stagger = new CombatAnimationSlot();

        [Tooltip("An unguarded hit of at least the heavy stagger damage.")]
        public CombatAnimationSlot heavyStagger = new CombatAnimationSlot();

        [Tooltip("Attack in the air for a discipline without an aerial attack of its own.")]
        public CombatAnimationSlot aerialAttack = new CombatAnimationSlot();

        [Tooltip("Charged release in the air for a discipline without an aerial release of its own.")]
        public CombatAnimationSlot aerialRelease = new CombatAnimationSlot();

        [Tooltip("Defeat. Held until the character is revived.")]
        public CombatAnimationSlot defeat = new CombatAnimationSlot();
    }

    /// <summary>How the combat animations are played. Presentation only: no value here changes a rule.</summary>
    [Serializable]
    public sealed class CombatAnimationTuning
    {
        [Tooltip("Slowest a clip is played to fit the combat timing.")]
        [Range(0.1f, 1f)]
        public float minPlaybackSpeed = 0.5f;

        [Tooltip("Fastest a clip is played to fit the combat timing. Above it, the start of the wind-up is skipped.")]
        [Range(1f, 6f)]
        public float maxPlaybackSpeed = 3f;

        [Tooltip("Blend used when an action is interrupted or cancelled, in seconds.")]
        [Min(0f)]
        public float interruptBlendSeconds = 0.1f;

        [Tooltip("An unguarded hit of at least this much damage plays the stagger.")]
        [Min(0f)]
        public float staggerDamage = 4f;

        [Tooltip("An unguarded hit of at least this much damage plays the heavy stagger. The same amount breaks a charge.")]
        [Min(0f)]
        public float heavyStaggerDamage = 10f;

        [Tooltip("The stance stays this long after the last combat action, in seconds.")]
        [Min(0f)]
        public float stanceLingerSeconds = 4f;

        [Tooltip("Speed in metres per second at which the stance has faded out completely.")]
        [Min(0.1f)]
        public float stanceFadeSpeed = 1.5f;

        [Tooltip("Seconds the stance takes to fade in or out.")]
        [Min(0.01f)]
        public float stanceBlendSeconds = 0.25f;

        [Tooltip("A full-body slot plays on the whole body only below this speed, in metres per second.")]
        [Min(0f)]
        public float fullBodyMaxSpeed = 0.6f;

        [Tooltip("Upper-body animations also move the spine. Untick if the legs twist or slide during upper-body " +
                 "attacks while running, then run the setup again.")]
        public bool upperBodyIncludesTorso = true;
    }

    /// <summary>All of a character's combat animation slots. Plain data, used by the rules and by the setup.</summary>
    [Serializable]
    public sealed class CombatAnimationLibrary
    {
        public DisciplineAnimationSet[] disciplines = new DisciplineAnimationSet[0];
        public SharedCombatAnimations shared = new SharedCombatAnimations();
        public CombatAnimationTuning tuning = new CombatAnimationTuning();

        /// <summary>The set of a discipline, or null if the library has none for it.</summary>
        public DisciplineAnimationSet GetDiscipline(MagicDiscipline discipline)
        {
            if (disciplines == null)
            {
                return null;
            }

            for (int i = 0; i < disciplines.Length; i++)
            {
                if (disciplines[i] != null && disciplines[i].discipline == discipline)
                {
                    return disciplines[i];
                }
            }

            return null;
        }

        /// <summary>The slot an id names, or null.</summary>
        public CombatAnimationSlot GetSlot(CombatSlotId id)
        {
            if (id.IsNone)
            {
                return null;
            }

            if (id.IsShared)
            {
                switch (id.Kind)
                {
                    case CombatSlotKind.BlockHold: return shared?.blockHold;
                    case CombatSlotKind.BlockImpact: return shared?.blockImpact;
                    case CombatSlotKind.Stagger: return shared?.stagger;
                    case CombatSlotKind.HeavyStagger: return shared?.heavyStagger;
                    case CombatSlotKind.AerialAttack: return shared?.aerialAttack;
                    case CombatSlotKind.AerialRelease: return shared?.aerialRelease;
                    case CombatSlotKind.Defeat: return shared?.defeat;
                    default: return null;
                }
            }

            DisciplineAnimationSet set = GetDiscipline(id.Discipline);
            if (set == null)
            {
                return null;
            }

            switch (id.Kind)
            {
                case CombatSlotKind.Stance: return set.stance;
                case CombatSlotKind.Attack:
                    return set.attacks != null && id.Index >= 0 && id.Index < set.attacks.Length ? set.attacks[id.Index] : null;
                case CombatSlotKind.ChargeStart: return set.chargeStart;
                case CombatSlotKind.ChargeHold: return set.chargeHold;
                case CombatSlotKind.ChargeRelease: return set.chargeRelease;
                case CombatSlotKind.ChargeCancel: return set.chargeCancel;
                case CombatSlotKind.AerialAttack: return set.aerialAttack;
                case CombatSlotKind.AerialRelease: return set.aerialRelease;
                default: return null;
            }
        }

        /// <summary>
        /// A short text that changes whenever something baked into the Animator Controller changes: a clip, a
        /// mirror, a scope, a held pose, or whether the upper body includes the torso. The setup stores it with
        /// the controller and rebuilds the controller when it differs. Moments, blends and tuning are read at
        /// run time and need no rebuild.
        /// </summary>
        public string ControllerSignature()
        {
            var text = new StringBuilder();
            ForEachSlot((id, slot) =>
            {
                if (slot != null && slot.IsAssigned)
                {
                    text.Append(id.StateName).Append('=').Append(slot.clip)
                        .Append(slot.mirror ? "|m" : string.Empty)
                        .Append(slot.scope == CombatBodyScope.FullBody ? "|f" : string.Empty)
                        .Append(slot.holdPose ? "|h" : string.Empty).Append(';');
                }
            });
            text.Append(tuning != null && !tuning.upperBodyIncludesTorso ? "arms" : "torso");

            // FNV-1a: stable across runs and platforms, unlike string.GetHashCode.
            uint hash = 2166136261;
            string all = text.ToString();
            for (int i = 0; i < all.Length; i++)
            {
                hash = (hash ^ all[i]) * 16777619;
            }

            return hash.ToString("x8");
        }

        /// <summary>
        /// Calls <paramref name="visit"/> for every slot of the library with its id, assigned or not, in a fixed
        /// order. Used by the setup to build the Animator Controller and by the validation.
        /// </summary>
        public void ForEachSlot(Action<CombatSlotId, CombatAnimationSlot> visit)
        {
            if (disciplines != null)
            {
                for (int d = 0; d < disciplines.Length; d++)
                {
                    DisciplineAnimationSet set = disciplines[d];
                    if (set == null)
                    {
                        continue;
                    }

                    MagicDiscipline which = set.discipline;
                    visit(CombatSlotId.For(which, CombatSlotKind.Stance), set.stance);
                    for (int i = 0; i < set.AttackCount; i++)
                    {
                        visit(CombatSlotId.For(which, CombatSlotKind.Attack, i), set.attacks[i]);
                    }

                    visit(CombatSlotId.For(which, CombatSlotKind.ChargeStart), set.chargeStart);
                    visit(CombatSlotId.For(which, CombatSlotKind.ChargeHold), set.chargeHold);
                    visit(CombatSlotId.For(which, CombatSlotKind.ChargeRelease), set.chargeRelease);
                    visit(CombatSlotId.For(which, CombatSlotKind.ChargeCancel), set.chargeCancel);
                    visit(CombatSlotId.For(which, CombatSlotKind.AerialAttack), set.aerialAttack);
                    visit(CombatSlotId.For(which, CombatSlotKind.AerialRelease), set.aerialRelease);
                }
            }

            if (shared != null)
            {
                visit(CombatSlotId.Shared(CombatSlotKind.BlockHold), shared.blockHold);
                visit(CombatSlotId.Shared(CombatSlotKind.BlockImpact), shared.blockImpact);
                visit(CombatSlotId.Shared(CombatSlotKind.Stagger), shared.stagger);
                visit(CombatSlotId.Shared(CombatSlotKind.HeavyStagger), shared.heavyStagger);
                visit(CombatSlotId.Shared(CombatSlotKind.AerialAttack), shared.aerialAttack);
                visit(CombatSlotId.Shared(CombatSlotKind.AerialRelease), shared.aerialRelease);
                visit(CombatSlotId.Shared(CombatSlotKind.Defeat), shared.defeat);
            }
        }
    }
}
