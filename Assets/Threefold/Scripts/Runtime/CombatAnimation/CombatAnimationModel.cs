using System.Collections.Generic;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>What the model needs to know about the body each frame.</summary>
    public struct CombatBodyState
    {
        public bool Grounded;

        /// <summary>Metres per second along the ground.</summary>
        public float HorizontalSpeed;

        public CombatBodyState(bool grounded, float horizontalSpeed)
        {
            Grounded = grounded;
            HorizontalSpeed = horizontalSpeed;
        }
    }

    /// <summary>What one layer should show. A new <see cref="Version"/> means: cross-fade to it now.</summary>
    public readonly struct CombatLayerState
    {
        /// <summary>The slot to show, or <see cref="CombatSlotId.None"/> for nothing.</summary>
        public readonly CombatSlotId Slot;

        /// <summary>Play the slot's second state; see <see cref="CombatSlotId.TwinStateName"/>.</summary>
        public readonly bool Twin;

        /// <summary>Where the clip starts, in normalised clip time.</summary>
        public readonly float StartNormalized;

        public readonly float BlendSeconds;

        public readonly int Version;

        public CombatLayerState(CombatSlotId slot, bool twin, float startNormalized, float blendSeconds, int version)
        {
            Slot = slot;
            Twin = twin;
            StartNormalized = startNormalized;
            BlendSeconds = blendSeconds;
            Version = version;
        }
    }

    /// <summary>
    /// Decides which combat animation each layer shows, how fast it plays, and when the cues of each action
    /// are sent. It is told what the combat rules did (a swing started, landed or was interrupted; a charge
    /// began, was released or cancelled; the guard went up, stopped a hit; damage; defeat; hands busy) and
    /// answers with layer states, speeds, weights and cues. It never touches a Unity object, so it is tested
    /// outside Unity; <see cref="CombatAnimationDirector"/> carries its answers out on the Animator.
    ///
    /// It follows the rules and never leads them: an animation never deals damage, never delays a hit, and
    /// never keeps an action alive that the rules have ended.
    ///
    /// Upper body, first that applies: nothing while defeated or while the hands are busy (climbing); the
    /// block (its impact, then its hold) while blocking; a release; an attack; a stagger; a charge cancel; the
    /// charge (its start, then its hold). Full body: defeat; a full-body release or attack. Stance: the
    /// selected discipline's stance while in combat, faded out by speed. An action pushed aside by a more
    /// important one does not come back; a hold does.
    /// </summary>
    public sealed class CombatAnimationModel
    {
        private const float BlockedHitWindow = 0.05f;

        private sealed class Playback
        {
            public bool Active;
            public bool Hidden;
            public bool Aerial;
            public int Instance;
            public int ActionId;
            public ResolvedSlot Resolved;
            public CombatTimeFit Fit;
            public float Start;
            public float End;
            public float Speed;
            public float StartNormalized;

            public void Clear()
            {
                Active = false;
                Hidden = false;
                Aerial = false;
                ActionId = 0;
                Resolved = ResolvedSlot.None;
            }
        }

        private readonly CombatAnimationLibrary library;
        private readonly CombatCueTracker tracker = new CombatCueTracker();
        private readonly List<CombatCue> cues = new List<CombatCue>(16);

        private readonly Playback attack = new Playback();
        private readonly Playback release = new Playback();
        private readonly Playback cancel = new Playback();
        private readonly Playback reaction = new Playback();
        private readonly Playback blockImpact = new Playback();

        private readonly CombatLayerState[] states = new CombatLayerState[CombatAnimatorNames.LayerCount];
        private readonly int[] shownInstance = new int[CombatAnimatorNames.LayerCount];
        private readonly float[] speeds = { 1f, 1f, 1f };
        private readonly float[] weights = new float[CombatAnimatorNames.LayerCount];
        private readonly float[] pendingBlendOut = new float[CombatAnimatorNames.LayerCount];

        private CombatBodyState body = new CombatBodyState(true, 0f);
        private MagicDiscipline discipline;
        private bool blocking;
        private bool defeated;
        private bool handsBusy;

        private bool charging;
        private bool chargeStartSkipped;
        private MagicDiscipline chargeDiscipline;
        private int chargeActionId;
        private int chargeInstance;
        private float chargeStartEnd;

        private int meleeActionId;
        private ResolvedSlot meleeSlot;
        private CombatTimeFit meleeFit;
        private float meleeRecoverySeconds;

        private float stanceTarget;
        private int nextInstance = 1;
        private float lastTick = float.NegativeInfinity;
        private float lastCombatTime = float.NegativeInfinity;
        private float lastBlockedTime = float.NegativeInfinity;

        public CombatAnimationModel(CombatAnimationLibrary library)
        {
            this.library = library ?? new CombatAnimationLibrary();
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = new CombatLayerState(CombatSlotId.None, false, 0f, 0f, 0);
            }
        }

        public CombatAnimationLibrary Library => library;

        private CombatAnimationTuning Tuning => library.tuning ?? (library.tuning = new CombatAnimationTuning());

        /// <summary>Cues produced since they were last cleared, in order. The owner reads and clears them.</summary>
        public List<CombatCue> Cues => cues;

        /// <summary>The action whose cues are being sent, or 0.</summary>
        public int CurrentActionId => tracker.CurrentActionId;

        public bool IsBlocking => blocking;
        public bool IsCharging => charging;
        public MagicDiscipline Discipline => discipline;

        public CombatLayerState GetLayerState(CombatLayer layer) => states[(int)layer];
        public float GetLayerSpeed(CombatLayer layer) => speeds[(int)layer];
        public float GetLayerWeight(CombatLayer layer) => weights[(int)layer];

        // ------------------------------------------------------------------ what happened

        public void SetDiscipline(MagicDiscipline selected)
        {
            discipline = selected;
        }

        /// <summary>A melee swing began, with the step's start-up and recovery from the rules.</summary>
        public void SwingStarted(MagicDiscipline swingDiscipline, int step, float startupSeconds, float recoverySeconds, float now)
        {
            lastCombatTime = now;
            meleeSlot = CombatSlotResolver.ResolveAttack(library, swingDiscipline, step, !body.Grounded, body.HorizontalSpeed);
            meleeRecoverySeconds = Mathf.Max(0f, recoverySeconds);
            CombatEffector hand = meleeSlot.IsValid ? meleeSlot.Slot.effector : CombatEffector.None;
            meleeActionId = tracker.Begin(CombatActionKind.Melee, (int)swingDiscipline, step, hand, now, cues);

            if (!meleeSlot.IsValid)
            {
                Stop(attack, true);
                return;
            }

            CombatAnimationSlot slot = meleeSlot.Slot;
            meleeFit = CombatTimeWarp.Fit(slot, startupSeconds, recoverySeconds, Tuning.minPlaybackSpeed, Tuning.maxPlaybackSpeed);
            Begin(attack, meleeSlot, meleeFit, meleeFit.Entry, meleeFit.WindupSpeed, float.PositiveInfinity, now, meleeActionId);

            if (slot.effect < meleeFit.Contact)
            {
                tracker.Schedule(meleeActionId, CombatCueKind.Effect, now + meleeFit.SecondsToBeforeContact(slot.effect));
            }

            float open = Mathf.Min(slot.hitWindowOpen, meleeFit.Contact);
            tracker.Schedule(meleeActionId, CombatCueKind.HitWindowOpened, now + meleeFit.SecondsToBeforeContact(open));
        }

        /// <summary>The rules resolved the swing's hit now. A repeat, or a landing for an older swing, is ignored.</summary>
        public void SwingLanded(float now)
        {
            if (tracker.CurrentAction != CombatActionKind.Melee || !tracker.Anchor(meleeActionId, CombatCueKind.Contact, now, cues))
            {
                return;
            }

            lastCombatTime = now;
            if (!meleeSlot.IsValid)
            {
                tracker.Schedule(meleeActionId, CombatCueKind.Recovered, now + meleeRecoverySeconds);
                return;
            }

            CombatAnimationSlot slot = meleeSlot.Slot;
            if (slot.effect >= meleeFit.Contact)
            {
                tracker.Schedule(meleeActionId, CombatCueKind.Effect, now + meleeFit.SecondsAfterContact(slot.effect));
            }

            tracker.Schedule(meleeActionId, CombatCueKind.HitWindowClosed,
                now + meleeFit.SecondsAfterContact(Mathf.Max(slot.hitWindowClose, meleeFit.Contact)));

            float recoveryLeft = meleeFit.RecoverySeconds;
            if (attack.Active && attack.ActionId == meleeActionId)
            {
                // From wherever the clip really is now, which differs from the contact frame only if the
                // wind-up could not be fitted exactly.
                float position = attack.StartNormalized + (now - attack.Start) * attack.Speed / slot.Seconds;
                recoveryLeft = Mathf.Max(0f, (meleeFit.RecoveryEnd - position) * slot.Seconds / meleeFit.RecoverySpeed);
                attack.Speed = meleeFit.RecoverySpeed;
                attack.End = now + recoveryLeft;
            }

            tracker.Schedule(meleeActionId, CombatCueKind.Recovered, now + recoveryLeft);
        }

        /// <summary>The rules abandoned the swing before it landed.</summary>
        public void SwingInterrupted(float now)
        {
            if (tracker.CurrentActionId == meleeActionId && tracker.CurrentAction == CombatActionKind.Melee)
            {
                tracker.End(now, cues);
            }

            if (attack.Active && attack.ActionId == meleeActionId)
            {
                Stop(attack, true);
            }
        }

        public void ChargeBegan(MagicDiscipline chargedDiscipline, float now)
        {
            lastCombatTime = now;
            charging = true;
            chargeStartSkipped = false;
            chargeDiscipline = chargedDiscipline;
            chargeInstance = nextInstance++;
            Stop(cancel, true);

            DisciplineAnimationSet set = library.GetDiscipline(chargedDiscipline);
            CombatAnimationSlot start = set != null && CombatSlotResolver.IsAssigned(set.chargeStart) ? set.chargeStart : null;
            chargeStartEnd = start != null
                ? now + Mathf.Max(0f, Mathf.Max(start.recoveryEnd, start.entry) - start.entry) * start.Seconds
                : now;

            chargeActionId = tracker.Begin(CombatActionKind.Charged, (int)chargedDiscipline, 0,
                CombatSlotResolver.ChargeEffector(library, chargedDiscipline), now, cues);

            float effectDelay = start != null ? Mathf.Max(0f, start.effect - start.entry) * start.Seconds : 0f;
            tracker.Schedule(chargeActionId, CombatCueKind.Effect, now + effectDelay);
        }

        /// <summary>The charge ended: released (the attack fired now) or cancelled.</summary>
        public void ChargeEnded(bool released, float now)
        {
            if (!charging)
            {
                return;
            }

            charging = false;
            lastCombatTime = now;
            bool ours = tracker.CurrentActionId == chargeActionId && tracker.CurrentAction == CombatActionKind.Charged;

            if (released)
            {
                if (ours)
                {
                    tracker.Anchor(chargeActionId, CombatCueKind.Release, now, cues);
                }

                ResolvedSlot resolved = CombatSlotResolver.ResolveRelease(library, chargeDiscipline, !body.Grounded,
                    body.HorizontalSpeed);
                float recovery = 0f;
                if (resolved.IsValid)
                {
                    CombatTimeFit fit = CombatTimeWarp.FitImmediate(resolved.Slot);
                    recovery = fit.RecoverySeconds;
                    Begin(release, resolved, fit, fit.Entry, 1f, now + recovery, now, chargeActionId);
                }

                if (ours)
                {
                    tracker.Schedule(chargeActionId, CombatCueKind.Recovered, now + recovery);
                }

                return;
            }

            if (ours)
            {
                tracker.End(now, cues);
            }

            ResolvedSlot cancelSlot = CombatSlotResolver.ResolveSimple(library,
                CombatSlotId.For(chargeDiscipline, CombatSlotKind.ChargeCancel));
            if (cancelSlot.IsValid)
            {
                CombatAnimationSlot slot = cancelSlot.Slot;
                float duration = Mathf.Max(0f, Mathf.Max(slot.recoveryEnd, slot.entry) - slot.entry) * slot.Seconds;
                Begin(cancel, cancelSlot, default, slot.entry, 1f, now + duration, now, 0);
            }
        }

        public void SetBlocking(bool value, float now)
        {
            if (value != blocking)
            {
                lastCombatTime = now;
            }

            blocking = value;
            if (!value)
            {
                Stop(blockImpact, false);
            }
        }

        /// <summary>The guard stopped a hit.</summary>
        public void BlockedHit(float now)
        {
            lastBlockedTime = now;
            lastCombatTime = now;
            PlayReaction(blockImpact, CombatSlotId.Shared(CombatSlotKind.BlockImpact), now);
        }

        /// <summary>The character took damage (after the guard). A hit the guard just stopped plays its impact instead.</summary>
        public void Damaged(float amount, float now)
        {
            lastCombatTime = now;
            if (defeated || handsBusy || now - lastBlockedTime <= BlockedHitWindow)
            {
                return;
            }

            CombatAnimationTuning tuning = Tuning;
            if (amount >= tuning.heavyStaggerDamage &&
                CombatSlotResolver.IsAssigned(library.shared?.heavyStagger))
            {
                PlayReaction(reaction, CombatSlotId.Shared(CombatSlotKind.HeavyStagger), now);
            }
            else if (amount >= tuning.staggerDamage)
            {
                PlayReaction(reaction, CombatSlotId.Shared(CombatSlotKind.Stagger), now);
            }
        }

        public void SetDefeated(bool value, float now)
        {
            if (value == defeated)
            {
                return;
            }

            defeated = value;
            if (value)
            {
                StopEverything(now);
            }
        }

        /// <summary>Hanging, climbing or vaulting: the hands cannot fight, and every combat animation stops.</summary>
        public void SetHandsBusy(bool value, float now)
        {
            if (value == handsBusy)
            {
                return;
            }

            handsBusy = value;
            if (value)
            {
                StopEverything(now);
            }
        }

        /// <summary>Forgets everything without sending cues, for when the presentation is switched off.</summary>
        public void Reset()
        {
            tracker.Reset();
            cues.Clear();
            attack.Clear();
            release.Clear();
            cancel.Clear();
            reaction.Clear();
            blockImpact.Clear();
            charging = false;
            blocking = false;
            defeated = false;
            handsBusy = false;
            lastCombatTime = float.NegativeInfinity;
            lastTick = float.NegativeInfinity;
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = new CombatLayerState(CombatSlotId.None, false, 0f, 0f, states[i].Version + 1);
                shownInstance[i] = 0;
                weights[i] = 0f;
                speeds[i] = 1f;
            }
        }

        // ------------------------------------------------------------------ each frame

        /// <summary>Advances to the given time: sends due cues, ends finished animations, and updates the layers.</summary>
        public void Tick(float now, CombatBodyState state)
        {
            float deltaTime = float.IsNegativeInfinity(lastTick) ? 0f : Mathf.Clamp(now - lastTick, 0f, 0.25f);
            lastTick = now;
            body = state;

            if (body.Grounded)
            {
                // An aerial pose on the whole body ends on landing, so the landing shows.
                if (attack.Active && attack.Aerial)
                {
                    Stop(attack, true);
                }

                if (release.Active && release.Aerial)
                {
                    Stop(release, true);
                }
            }

            Expire(attack, now);
            Expire(release, now);
            Expire(cancel, now);
            Expire(reaction, now);
            Expire(blockImpact, now);

            tracker.Tick(now, cues);

            UpdateUpperBody(now);
            UpdateFullBody();
            UpdateStance(now);
            UpdateWeights(deltaTime);
        }

        // ------------------------------------------------------------------ layers

        private void UpdateUpperBody(float now)
        {
            Playback chosen = null;
            CombatSlotId held = CombatSlotId.None;
            ResolvedSlot chargeStart = ResolvedSlot.None;

            if (defeated || handsBusy)
            {
                // Nothing: the traversal or defeat animations show.
            }
            else if (blocking)
            {
                if (blockImpact.Active && !blockImpact.Hidden)
                {
                    chosen = blockImpact;
                }
                else
                {
                    held = CombatSlotId.Shared(CombatSlotKind.BlockHold);
                }
            }
            else if (Visible(release, CombatLayer.UpperBody))
            {
                chosen = release;
            }
            else if (Visible(attack, CombatLayer.UpperBody))
            {
                chosen = attack;
            }
            else if (reaction.Active && !reaction.Hidden)
            {
                chosen = reaction;
            }
            else if (cancel.Active && !cancel.Hidden)
            {
                chosen = cancel;
            }
            else if (charging)
            {
                ResolvedSlot start = CombatSlotResolver.ResolveSimple(library,
                    CombatSlotId.For(chargeDiscipline, CombatSlotKind.ChargeStart));
                CombatSlotId hold = CombatSlotId.For(chargeDiscipline, CombatSlotKind.ChargeHold);
                bool hasHold = CombatSlotResolver.ResolveSimple(library, hold).IsValid;

                if (start.IsValid && !chargeStartSkipped && (now < chargeStartEnd || !hasHold))
                {
                    chargeStart = start;
                }
                else if (hasHold)
                {
                    held = hold;
                }
            }

            // A one-shot that is not shown now has been pushed aside, and never comes back half-way through.
            HideUnlessShown(release, chosen, CombatLayer.UpperBody);
            HideUnlessShown(attack, chosen, CombatLayer.UpperBody);
            HideUnlessShown(reaction, chosen, CombatLayer.UpperBody);
            HideUnlessShown(cancel, chosen, CombatLayer.UpperBody);
            HideUnlessShown(blockImpact, chosen, CombatLayer.UpperBody);

            // A charge start pushed aside is not replayed: the charge returns to its hold.
            if (charging && !chargeStart.IsValid && held.Kind != CombatSlotKind.ChargeHold)
            {
                chargeStartSkipped = true;
            }

            if (chosen != null)
            {
                Show(CombatLayer.UpperBody, chosen.Resolved, chosen.Instance, chosen.StartNormalized, chosen.Speed);
            }
            else if (chargeStart.IsValid)
            {
                Show(CombatLayer.UpperBody, chargeStart, chargeInstance, chargeStart.Slot.entry, 1f);
            }
            else
            {
                ShowHeld(CombatLayer.UpperBody, held);
            }
        }

        private void UpdateFullBody()
        {
            Playback chosen = null;
            CombatSlotId held = CombatSlotId.None;

            if (defeated)
            {
                held = CombatSlotId.Shared(CombatSlotKind.Defeat);
            }
            else if (handsBusy)
            {
                // Nothing.
            }
            else if (Visible(release, CombatLayer.FullBody))
            {
                chosen = release;
            }
            else if (Visible(attack, CombatLayer.FullBody))
            {
                chosen = attack;
            }

            HideUnlessShown(release, chosen, CombatLayer.FullBody);
            HideUnlessShown(attack, chosen, CombatLayer.FullBody);

            if (chosen != null)
            {
                Show(CombatLayer.FullBody, chosen.Resolved, chosen.Instance, chosen.StartNormalized, chosen.Speed);
            }
            else
            {
                ShowHeld(CombatLayer.FullBody, held);
            }
        }

        private void UpdateStance(float now)
        {
            CombatAnimationTuning tuning = Tuning;
            bool inCombat = blocking || charging || now - lastCombatTime <= tuning.stanceLingerSeconds;
            bool allowed = !defeated && !handsBusy && body.Grounded && inCombat;

            CombatSlotId stance = CombatSlotId.For(discipline, CombatSlotKind.Stance);
            bool assigned = CombatSlotResolver.ResolveSimple(library, stance).IsValid;
            ShowHeld(CombatLayer.Stance, assigned ? stance : CombatSlotId.None);

            float fade = 1f - Mathf.Clamp01(body.HorizontalSpeed / Mathf.Max(0.1f, tuning.stanceFadeSpeed));
            stanceTarget = allowed && assigned ? fade : 0f;
        }

        private void UpdateWeights(float deltaTime)
        {
            CombatAnimationTuning tuning = Tuning;
            for (int i = 0; i < weights.Length; i++)
            {
                float target;
                float seconds;
                if (i == (int)CombatLayer.Stance)
                {
                    target = stanceTarget;

                    // Climbing or defeat takes the arms at once; otherwise the stance eases in and out.
                    seconds = handsBusy || defeated ? tuning.interruptBlendSeconds : tuning.stanceBlendSeconds;
                }
                else
                {
                    target = states[i].Slot.IsNone ? 0f : 1f;
                    seconds = states[i].BlendSeconds;
                }

                weights[i] = seconds <= 0.0001f
                    ? target
                    : Mathf.MoveTowards(weights[i], target, deltaTime / seconds);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void Begin(Playback playback, ResolvedSlot resolved, CombatTimeFit fit, float startNormalized, float speed,
            float end, float now, int actionId)
        {
            playback.Active = true;
            playback.Hidden = false;
            playback.Aerial = resolved.Aerial;
            playback.Instance = nextInstance++;
            playback.ActionId = actionId;
            playback.Resolved = resolved;
            playback.Fit = fit;
            playback.Start = now;
            playback.End = end;
            playback.Speed = speed;
            playback.StartNormalized = startNormalized;
        }

        private void PlayReaction(Playback playback, CombatSlotId id, float now)
        {
            ResolvedSlot resolved = CombatSlotResolver.ResolveSimple(library, id);
            if (!resolved.IsValid)
            {
                return;
            }

            CombatAnimationSlot slot = resolved.Slot;
            float duration = Mathf.Max(0f, Mathf.Max(slot.recoveryEnd, slot.entry) - slot.entry) * slot.Seconds;
            Begin(playback, resolved, default, slot.entry, 1f, now + duration, now, 0);
        }

        private void Expire(Playback playback, float now)
        {
            if (playback.Active && now >= playback.End)
            {
                Stop(playback, false);
            }
        }

        private void Stop(Playback playback, bool interrupted)
        {
            if (!playback.Active)
            {
                return;
            }

            int layer = (int)playback.Resolved.Layer;
            if (states[layer].Slot == playback.Resolved.Id && shownInstance[layer] == playback.Instance)
            {
                pendingBlendOut[layer] = interrupted || playback.Resolved.Slot == null
                    ? Tuning.interruptBlendSeconds
                    : playback.Resolved.Slot.blendOutSeconds;
            }

            playback.Clear();
        }

        private void StopEverything(float now)
        {
            tracker.End(now, cues);
            charging = false;
            Stop(attack, true);
            Stop(release, true);
            Stop(cancel, true);
            Stop(reaction, true);
            Stop(blockImpact, true);
            for (int i = 0; i < pendingBlendOut.Length; i++)
            {
                pendingBlendOut[i] = Tuning.interruptBlendSeconds;
            }
        }

        private static bool Visible(Playback playback, CombatLayer layer)
        {
            return playback.Active && !playback.Hidden && playback.Resolved.Layer == layer;
        }

        private static void HideUnlessShown(Playback playback, Playback shown, CombatLayer layer)
        {
            if (playback != shown && playback.Active && playback.Resolved.Layer == layer)
            {
                playback.Hidden = true;
            }
        }

        /// <summary>Shows a slot that holds or loops. It keeps one identity, so asking for it again does not restart it.</summary>
        private void ShowHeld(CombatLayer layer, CombatSlotId id)
        {
            ResolvedSlot resolved = CombatSlotResolver.ResolveSimple(library, id);
            if (resolved.IsValid)
            {
                Show(layer, resolved, 0, resolved.Slot.entry, 1f);
            }
            else
            {
                Show(layer, ResolvedSlot.None, 0, 0f, 1f);
            }
        }

        /// <summary>Asks a layer to show a slot. A new slot or a new instance of one is a new request.</summary>
        private void Show(CombatLayer layer, ResolvedSlot resolved, int instance, float startNormalized, float speed)
        {
            int index = (int)layer;
            CombatLayerState current = states[index];
            CombatSlotId id = resolved.IsValid ? resolved.Id : CombatSlotId.None;
            speeds[index] = resolved.IsValid ? speed : 1f;

            if (current.Slot == id && shownInstance[index] == instance)
            {
                return;
            }

            float blend;
            if (id.IsNone)
            {
                blend = pendingBlendOut[index] > 0f ? pendingBlendOut[index] : Tuning.interruptBlendSeconds;
            }
            else
            {
                blend = resolved.Slot.blendInSeconds;
            }

            pendingBlendOut[index] = 0f;
            bool twin = !id.IsNone && !id.IsHeld && current.Slot == id && !current.Twin;
            shownInstance[index] = instance;
            states[index] = new CombatLayerState(id, twin, startNormalized, blend, current.Version + 1);
        }
    }
}
