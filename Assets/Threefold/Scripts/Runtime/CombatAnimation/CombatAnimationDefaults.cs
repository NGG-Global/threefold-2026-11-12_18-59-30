using Threefold.Magic;

namespace Threefold.CombatAnimation
{
    /// <summary>
    /// The Mage's starting combat animation slots. The setup command writes them into the Mage's
    /// animation set asset once; after that the asset is the truth and can be tuned freely.
    ///
    /// Every clip comes from Quaternius' Universal Animation Library 1 or 2 (CC0), built into the Mage's
    /// model by Tools/blender/build_mage.py. The moments in each clip were measured by
    /// Tools/blender/analyse_combat_clips.py (30 frames per second):
    /// - contact of a punch or palm: the frame of the striking hand's furthest reach after its fastest frame;
    /// - contact of a swing: the striking hand's fastest frame;
    /// - hit window: from the fastest frame (or two frames before contact for a swing) to two frames after
    ///   contact; the effect cue leads the hit window by two frames;
    /// - recovery end: the frame from which the striking hand stays within 5 cm of where the clip leaves it,
    ///   or earlier where the clip goes on into a second movement that is not wanted.
    /// None of these clips was made for the Mage. They are placeholders chosen for silhouette and timing,
    /// listed with the reasons in Docs/CombatAnimationArchitecture.md. Nothing here has been seen in Unity.
    /// </summary>
    public static class CombatAnimationDefaults
    {
        /// <summary>The version of the slots a newly generated set holds.</summary>
        public const int DataVersion = 1;

        /// <summary>Every clip the defaults use. The Mage model must contain all of them.</summary>
        public static readonly string[] RequiredClips =
        {
            "Punch_Jab", "Punch_Cross", "Spell_Simple_Enter", "Spell_Simple_Idle_Loop", "SpellShoot", "Spell_Simple_Exit",
            "Sword_Idle", "Sword_Attack", "Sword_Regular_B", "OverhandThrow",
            "Sword_Block", "Shield_OneShot", "Melee_Hook", "Push_Loop",
            "Idle_Shield_Loop", "Idle_Shield_Break", "Hit_Chest", "Hit_Head", "NinjaJump_Start", "Death01",
        };

        public static CombatAnimationLibrary CreateMage()
        {
            return new CombatAnimationLibrary
            {
                disciplines = new[] { Arcane(), Flame(), Aegis() },
                shared = Shared(),
                tuning = new CombatAnimationTuning(),
            };
        }

        // ------------------------------------------------------------ Arcane

        /// <summary>
        /// Precision and direction: a boxer's jab and cross with the casting (left) hand leading, then a reaching
        /// thrust of the casting hand. The charge raises and holds the casting hand; the release is the existing
        /// casting gesture. All upper body: Arcane strikes at full movement speed.
        /// </summary>
        private static DisciplineAnimationSet Arcane()
        {
            return new DisciplineAnimationSet
            {
                discipline = MagicDiscipline.Arcane,
                // The guard the jab and cross start and end in, held.
                stance = Hold("Punch_Jab", 0.867f, 0f, CombatEffector.LeftHand),
                attacks = new[]
                {
                    // Arcane Jab. 27 frames; fastest frame 6, full reach frame 7, settled frame 22.
                    Strike("Punch_Jab", 0.867f, CombatEffector.LeftHand, open: 0.192f, contact: 0.231f, close: 0.308f,
                        recoveryEnd: 0.808f),
                    // Arcane Cut. 31 frames; fastest frame 7, full reach frame 10, settled frame 29.
                    Strike("Punch_Cross", 1f, CombatEffector.RightHand, open: 0.2f, contact: 0.3f, close: 0.367f,
                        recoveryEnd: 0.933f),
                    // Arcane Lance. 17 frames; fastest frame 9, full reach frame 14; ends extended, so it holds
                    // the reach and blends out.
                    Strike("Spell_Simple_Enter", 0.533f, CombatEffector.LeftHand, open: 0.5f, contact: 0.812f, close: 0.937f,
                        recoveryEnd: 1f, blendOut: 0.2f),
                },
                chargeStart = Motion("Spell_Simple_Enter", 0.533f, CombatEffector.LeftHand, effect: 0.5f),
                chargeHold = Loop("Spell_Simple_Idle_Loop", 2.1f, CombatEffector.LeftHand),
                // The casting gesture: the hand is already extended, so contact is its second frame.
                chargeRelease = Release("SpellShoot", 0.5f, CombatEffector.LeftHand, contact: 0.067f, recoveryEnd: 0.533f),
                chargeCancel = Motion("Spell_Simple_Exit", 0.433f, CombatEffector.LeftHand, effect: 0f, recoveryEnd: 0.923f),
            };
        }

        // ------------------------------------------------------------- Flame

        /// <summary>
        /// Heat and aggression: wide sweeping arm swings, the second a mirror of the first, then a lunging
        /// sweep that ends with the arms flung wide. The charge mirrors the Arcane gesture into the right hand;
        /// the release is a low overhand throw along the ground, like the Flame Wave.
        /// </summary>
        private static DisciplineAnimationSet Flame()
        {
            return new DisciplineAnimationSet
            {
                discipline = MagicDiscipline.Flame,
                stance = Loop("Sword_Idle", 1.667f, CombatEffector.RightHand),
                attacks = new[]
                {
                    // Ember Swipe. 47 frames; fastest frame 13, settled frame 40. In place.
                    Strike("Sword_Attack", 1.533f, CombatEffector.RightHand, open: 0.196f, contact: 0.261f, close: 0.326f,
                        recoveryEnd: 0.848f),
                    // Ember Backswing: the same swing, mirrored.
                    Mirrored(Strike("Sword_Attack", 1.533f, CombatEffector.LeftHand, open: 0.196f, contact: 0.261f, close: 0.326f,
                        recoveryEnd: 0.848f)),
                    // Flare. 17 frames; fastest frame 9; ends with the arms wide (settled frame 15).
                    Strike("Sword_Regular_B", 0.533f, CombatEffector.RightHand, open: 0.375f, contact: 0.5f, close: 0.625f,
                        recoveryEnd: 0.875f, blendOut: 0.2f),
                },
                chargeStart = Mirrored(Motion("Spell_Simple_Enter", 0.533f, CombatEffector.RightHand, effect: 0.5f)),
                chargeHold = Mirrored(Loop("Spell_Simple_Idle_Loop", 2.1f, CombatEffector.RightHand)),
                // 41 frames; the throw's fastest frame 13, settled frame 38. Full body when standing still.
                chargeRelease = FullBody(Release("OverhandThrow", 1.333f, CombatEffector.RightHand, contact: 0.3f,
                    recoveryEnd: 0.925f)),
                chargeCancel = Mirrored(Motion("Spell_Simple_Exit", 0.433f, CombatEffector.RightHand, effect: 0f,
                    recoveryEnd: 0.923f)),
            };
        }

        // ------------------------------------------------------------- Aegis

        /// <summary>
        /// Force and stability: a low braced stance, a forearm bash, then a heavy lunging slam that commits the
        /// whole body when standing. The charge raises the forearm and holds it; the release is a two-handed
        /// shove. Two attack slots, as Aegis has two strikes.
        /// </summary>
        private static DisciplineAnimationSet Aegis()
        {
            return new DisciplineAnimationSet
            {
                discipline = MagicDiscipline.Aegis,
                // A low, braced guard: frame 12 of the parry, held.
                stance = Hold("Sword_Block", 1.233f, 0.297f, CombatEffector.BothHands),
                attacks = new[]
                {
                    // Force Palm. 26 frames; fastest frame and full reach frame 4; the forearm stays up until
                    // frame 16, then a second movement lowers it, so recovery ends before it.
                    Strike("Shield_OneShot", 0.833f, CombatEffector.LeftHand, open: 0.08f, contact: 0.12f, close: 0.2f,
                        recoveryEnd: 0.6f, blendOut: 0.2f),
                    // Bulwark Slam. 15 frames; fastest frame 9 (41 m/s), ends low in a lunge (travels 0.36 m).
                    FullBody(Strike("Melee_Hook", 0.467f, CombatEffector.RightHand, open: 0.5f, contact: 0.571f, close: 0.643f,
                        recoveryEnd: 1f, blendOut: 0.3f)),
                },
                // The forearm rises (frames 1 to 8), then holds there.
                chargeStart = Motion("Shield_OneShot", 0.833f, CombatEffector.LeftHand, effect: 0.12f, recoveryEnd: 0.28f),
                chargeHold = Hold("Shield_OneShot", 0.833f, 0.28f, CombatEffector.LeftHand),
                // A two-handed shove: the push pose, held for about 0.4 s.
                chargeRelease = WithEntry(Release("Push_Loop", 2.667f, CombatEffector.BothHands, contact: 0.45f,
                    recoveryEnd: 0.6f), 0.45f),
                // The forearm lowers: the second half of the bash.
                chargeCancel = WithEntry(Motion("Shield_OneShot", 0.833f, CombatEffector.LeftHand, effect: 0.6f,
                    recoveryEnd: 0.96f), 0.6f),
            };
        }

        // ------------------------------------------------------------- shared

        private static SharedCombatAnimations Shared()
        {
            return new SharedCombatAnimations
            {
                // The forearm across the chest: the clearest guard silhouette in the libraries.
                blockHold = Loop("Idle_Shield_Loop", 2.5f, CombatEffector.LeftHand),
                // The guard knocked aside and raised again (fastest frame 4; back up by frame 23).
                blockImpact = Reaction("Idle_Shield_Break", 1.067f, contact: 0.094f, recoveryEnd: 0.7f),
                stagger = Reaction("Hit_Chest", 0.333f, contact: 0.3f, recoveryEnd: 0.8f),
                heavyStagger = Reaction("Hit_Head", 0.433f, contact: 0.46f, recoveryEnd: 1f),
                // A leap with one knee drawn up and the right arm thrown out: the crouch before it (frames 1
                // to 5) is skipped. Whole body, in the air only; it ends on landing.
                aerialAttack = FullBody(WithEntry(Strike("NinjaJump_Start", 0.967f, CombatEffector.RightHand, open: 0.2f,
                    contact: 0.241f, close: 0.31f, recoveryEnd: 0.759f), 0.172f)),
                aerialRelease = FullBody(WithEntry(Release("NinjaJump_Start", 0.967f, CombatEffector.RightHand, contact: 0.241f,
                    recoveryEnd: 0.759f), 0.172f)),
                // Ends lying down and stays there until revived. Travels about 0.9 m from where it starts.
                defeat = FullBody(Motion("Death01", 2.4f, CombatEffector.None, effect: 0f, recoveryEnd: 1f)),
            };
        }

        // ------------------------------------------------------------ builders

        private static CombatAnimationSlot Strike(string clip, float seconds, CombatEffector effector, float open, float contact,
            float close, float recoveryEnd, float blendOut = 0.15f)
        {
            return new CombatAnimationSlot
            {
                clip = clip, clipSeconds = seconds, effector = effector, scope = CombatBodyScope.UpperBody,
                entry = 0f, hitWindowOpen = open, contact = contact, hitWindowClose = close,
                effect = System.Math.Max(0f, open - 2f / 30f / seconds), recoveryEnd = recoveryEnd,
                blendInSeconds = 0.06f, blendOutSeconds = blendOut,
            };
        }

        private static CombatAnimationSlot Release(string clip, float seconds, CombatEffector effector, float contact,
            float recoveryEnd)
        {
            return new CombatAnimationSlot
            {
                clip = clip, clipSeconds = seconds, effector = effector, scope = CombatBodyScope.UpperBody,
                entry = 0f, hitWindowOpen = contact, contact = contact, hitWindowClose = contact, effect = contact,
                recoveryEnd = recoveryEnd, blendInSeconds = 0.06f, blendOutSeconds = 0.2f,
            };
        }

        private static CombatAnimationSlot Motion(string clip, float seconds, CombatEffector effector, float effect,
            float recoveryEnd = 1f)
        {
            return new CombatAnimationSlot
            {
                clip = clip, clipSeconds = seconds, effector = effector, scope = CombatBodyScope.UpperBody,
                entry = 0f, contact = 0f, hitWindowOpen = 0f, hitWindowClose = 0f, effect = effect,
                recoveryEnd = recoveryEnd, blendInSeconds = 0.1f, blendOutSeconds = 0.2f,
            };
        }

        private static CombatAnimationSlot Reaction(string clip, float seconds, float contact, float recoveryEnd)
        {
            return new CombatAnimationSlot
            {
                clip = clip, clipSeconds = seconds, effector = CombatEffector.None, scope = CombatBodyScope.UpperBody,
                entry = 0f, contact = contact, hitWindowOpen = contact, hitWindowClose = contact, effect = contact,
                recoveryEnd = recoveryEnd, blendInSeconds = 0.05f, blendOutSeconds = 0.2f,
            };
        }

        private static CombatAnimationSlot Loop(string clip, float seconds, CombatEffector effector)
        {
            return new CombatAnimationSlot
            {
                clip = clip, clipSeconds = seconds, effector = effector, scope = CombatBodyScope.UpperBody,
                entry = 0f, contact = 0f, hitWindowOpen = 0f, hitWindowClose = 0f, effect = 0f, recoveryEnd = 1f,
                blendInSeconds = 0.15f, blendOutSeconds = 0.2f,
            };
        }

        private static CombatAnimationSlot Hold(string clip, float seconds, float frame, CombatEffector effector)
        {
            CombatAnimationSlot slot = Loop(clip, seconds, effector);
            slot.holdPose = true;
            slot.entry = frame;
            slot.contact = frame;
            slot.hitWindowOpen = frame;
            slot.hitWindowClose = frame;
            slot.effect = frame;
            return slot;
        }

        private static CombatAnimationSlot Mirrored(CombatAnimationSlot slot)
        {
            slot.mirror = true;
            return slot;
        }

        private static CombatAnimationSlot FullBody(CombatAnimationSlot slot)
        {
            slot.scope = CombatBodyScope.FullBody;
            return slot;
        }

        /// <summary>Starts the clip later. Moments before the new entry move to it.</summary>
        private static CombatAnimationSlot WithEntry(CombatAnimationSlot slot, float entry)
        {
            slot.entry = entry;
            if (slot.contact < entry) slot.contact = entry;
            if (slot.hitWindowOpen < entry) slot.hitWindowOpen = entry;
            if (slot.hitWindowClose < entry) slot.hitWindowClose = entry;
            if (slot.effect < entry) slot.effect = entry;
            if (slot.recoveryEnd < entry) slot.recoveryEnd = entry;
            return slot;
        }
    }
}
