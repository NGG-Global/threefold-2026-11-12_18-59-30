using System;
using System.Collections.Generic;
using System.Text;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.DevTools;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>
    /// Plays the Mage's combat animations. It listens to what the combat rules already announce (swings,
    /// charges, the guard, damage, defeat, climbing), lets <see cref="CombatAnimationModel"/> decide what each
    /// combat layer shows, and carries that out on the Animator with cross-fades, layer weights and playback
    /// speeds. It changes no rule: damage, timing, movement and input are exactly as without it.
    ///
    /// It also sends <see cref="Cue"/>s, the moments of each attack (wind-up, effect, hit window, contact or
    /// release, recovery, cancellation), timed from normalised moments in the clips and anchored to the rules'
    /// own clock. Effects can use them instead of Animation Events, which the imported clips do not have and
    /// which keep firing on a clip that is being blended out.
    ///
    /// Without its animation set, or on an Animator Controller without the combat layers, it stays inactive and
    /// the character animates as before (locomotion and the casting gesture).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)] // After the combat rules and the animation driver have run this frame.
    public sealed class CombatAnimationDirector : MonoBehaviour, IDevReport
    {
        [SerializeField] private CombatAnimationSet animationSet;

        [Tooltip("The driver of the visual model's Animator.")]
        [SerializeField] private CharacterAnimationDriver driver;

        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private PlayerGuard guard;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private Health health;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private TraversalController traversal;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private PlayerDisciplines disciplines;

        private readonly CombatAnimatorBinding binding = new CombatAnimatorBinding();
        private readonly List<CombatCue> dispatch = new List<CombatCue>(16);

        private CombatAnimationModel model;
        private Animator lastTried;
        private int castLayerIndex = -1;
        private bool subscribed;
        private DisciplineCombat subscribedCombat;
        private PlayerGuard subscribedGuard;
        private Health subscribedHealth;

        /// <summary>A moment of an attack. See <see cref="CombatCueKind"/> for the guarantees.</summary>
        public event Action<CombatCue> Cue;

        /// <summary>True while it plays on an Animator whose controller has the combat layers.</summary>
        public bool IsActive => binding.IsBound;

        public CombatAnimationSet AnimationSet => animationSet;

        public CharacterAnimationDriver Driver => driver;

        public DisciplineCombat Combat => combat;

        /// <summary>The rules that decide what plays, for diagnostics and tests.</summary>
        public CombatAnimationModel Model => model;

        /// <summary>Connects only the references that are empty. Returns true if anything changed. Called by the setup tooling.</summary>
        public bool FillReferences(CombatAnimationSet set, CharacterAnimationDriver animationDriver, DisciplineCombat disciplineCombat)
        {
            bool changed = (animationSet == null && set != null) || (driver == null && animationDriver != null) ||
                           (combat == null && disciplineCombat != null);
            if (changed)
            {
                Bind(animationSet != null ? animationSet : set, driver != null ? driver : animationDriver,
                    combat != null ? combat : disciplineCombat);
            }

            return changed;
        }

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(CombatAnimationSet set, CharacterAnimationDriver animationDriver, DisciplineCombat disciplineCombat)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled)
            {
                OnDisable();
            }

            animationSet = set;
            driver = animationDriver;
            combat = disciplineCombat;
            model = null;

            if (wasEnabled)
            {
                OnEnable();
            }
        }

        /// <summary>
        /// The hand a discipline's charge is held in, as its animations show it, so a charge effect can sit there.
        /// None when the set does not say.
        /// </summary>
        public CombatEffector GetChargeEffector(MagicDiscipline discipline)
        {
            return CombatSlotResolver.ChargeEffector(animationSet != null ? animationSet.Library : null, discipline);
        }

        private void OnEnable()
        {
            if (guard == null) guard = GetComponent<PlayerGuard>();
            if (health == null) health = GetComponent<Health>();
            if (motor == null) motor = GetComponent<ThirdPersonMotor>();
            if (traversal == null) traversal = GetComponent<TraversalController>();
            if (disciplines == null) disciplines = GetComponent<PlayerDisciplines>();
            if (combat == null) combat = GetComponent<DisciplineCombat>();
            if (driver == null) driver = GetComponent<CharacterAnimationDriver>();

            if (animationSet != null)
            {
                model = new CombatAnimationModel(animationSet.Library);
            }

            Subscribe();
            lastTried = null;
            TryActivate();
        }

        private void OnDisable()
        {
            Unsubscribe();
            Deactivate();
            model?.Reset();
        }

        private void Update()
        {
            if (model == null)
            {
                return;
            }

            // Active only while the disciplines fight: with discipline combat off, the earlier slot attacks
            // cast instead, and their casting gesture must stay visible.
            bool wanted = combat != null && combat.isActiveAndEnabled && driver != null && driver.Animator != null;
            if (IsActive && (!wanted || driver.Animator != binding.Animator))
            {
                Deactivate();
                lastTried = null;
            }

            if (!IsActive && wanted)
            {
                TryActivate();
            }

            float now = Time.time;
            model.SetHandsBusy(traversal != null && (traversal.IsHanging || traversal.IsMoving), now);
            model.SetDefeated(health != null && !health.IsAlive, now);
            model.SetBlocking(guard != null && guard.IsBlocking, now);
            if (disciplines != null)
            {
                model.SetDiscipline(disciplines.Current);
            }

            var body = new CombatBodyState(motor == null || motor.IsGrounded, motor != null ? motor.HorizontalSpeed : 0f);
            model.Tick(now, body);

            DispatchCues();

            binding.Apply(model);
        }

        // ------------------------------------------------------------- Animator

        private void TryActivate()
        {
            Animator candidate = driver != null ? driver.Animator : null;
            if (candidate == null || candidate == lastTried || animationSet == null || model == null)
            {
                return;
            }

            lastTried = candidate;
            if (candidate.runtimeAnimatorController == null)
            {
                return;
            }

            if (!binding.Bind(candidate, animationSet.Library, out string problem))
            {
                Debug.LogWarning($"[Threefold] The Mage's combat animations are off: {problem}", this);
                return;
            }

            if (driver != null)
            {
                driver.ClaimCombatAnimations(this);
            }

            // The casting gesture's layer stays silent while the combat layers play; it returns when they stop.
            castLayerIndex = candidate.GetLayerIndex(CharacterAnimationDriver.CastLayer);
            if (castLayerIndex >= 0)
            {
                candidate.SetLayerWeight(castLayerIndex, 0f);
            }
        }

        private void Deactivate()
        {
            if (driver != null)
            {
                driver.ReleaseCombatAnimations(this);
            }

            Animator animator = binding.Animator;
            binding.Release();
            if (animator != null && castLayerIndex >= 0 && castLayerIndex < animator.layerCount)
            {
                animator.SetLayerWeight(castLayerIndex, 1f);
            }

            castLayerIndex = -1;
        }

        // ------------------------------------------------------------- diagnostics

        public string ReportTitle => "Combat animation";

        /// <summary>
        /// What each combat layer shows, its speed and weight, and the swing the rules are carrying out, for the development
        /// diagnostics (shown with the Mage's discipline). Asked only while the diagnostics are visible.
        /// </summary>
        public void AppendReport(StringBuilder text)
        {
            if (model == null)
            {
                text.AppendLine("  off: no combat animation set");
                return;
            }

            text.Append("  ").Append(IsActive ? "playing" : "not playing (the controller has no combat layers)").AppendLine();
            AppendLayer(text, "upper body", CombatLayer.UpperBody);
            AppendLayer(text, "whole body", CombatLayer.FullBody);
            AppendLayer(text, "stance", CombatLayer.Stance);
            AppendLayer(text, "stance, legs", CombatLayer.StanceFull);
            if (combat != null)
            {
                DisciplineCombatModel rules = combat.Model;
                text.Append("  swing ").Append(rules.Combo.Phase.ToString());
                if (rules.Combo.IsSwinging)
                {
                    text.Append(", step ").Append(rules.Combo.StepIndex + 1).Append(", ")
                        .Append(rules.Combo.SwingSeconds.ToString("0.00")).Append(" s in");
                }

                text.Append(", movement x").Append(rules.MoveSpeedMultiplier.ToString("0.00")).AppendLine();
            }
        }

        private void AppendLayer(StringBuilder text, string label, CombatLayer layer)
        {
            CombatLayerState state = model.GetLayerState(layer);
            text.Append("  ").Append(label).Append(": ").Append(state.Slot.IsNone ? "-" : state.Slot.StateName)
                .Append(" x").Append(model.GetLayerSpeed(layer).ToString("0.0"))
                .Append(" weight ").Append(model.GetLayerWeight(layer).ToString("0.00")).AppendLine();
        }

        private void DispatchCues()
        {
            List<CombatCue> produced = model.Cues;
            if (produced.Count == 0)
            {
                return;
            }

            dispatch.Clear();
            dispatch.AddRange(produced);
            produced.Clear();

            Action<CombatCue> handlers = Cue;
            if (handlers == null)
            {
                return;
            }

            for (int i = 0; i < dispatch.Count; i++)
            {
                handlers(dispatch[i]);
            }
        }

        // ------------------------------------------------------------- events

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            subscribedCombat = combat;
            if (subscribedCombat != null)
            {
                subscribedCombat.SwingStarted += HandleSwingStarted;
                subscribedCombat.SwingLanded += HandleSwingLanded;
                subscribedCombat.SwingInterrupted += HandleSwingInterrupted;
                subscribedCombat.ChargeBegan += HandleChargeBegan;
                subscribedCombat.ChargeEnded += HandleChargeEnded;
            }

            subscribedGuard = guard;
            if (subscribedGuard != null)
            {
                subscribedGuard.Blocked += HandleBlocked;
            }

            subscribedHealth = health;
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged += HandleDamaged;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (subscribedCombat != null)
            {
                subscribedCombat.SwingStarted -= HandleSwingStarted;
                subscribedCombat.SwingLanded -= HandleSwingLanded;
                subscribedCombat.SwingInterrupted -= HandleSwingInterrupted;
                subscribedCombat.ChargeBegan -= HandleChargeBegan;
                subscribedCombat.ChargeEnded -= HandleChargeEnded;
                subscribedCombat = null;
            }

            if (subscribedGuard != null)
            {
                subscribedGuard.Blocked -= HandleBlocked;
                subscribedGuard = null;
            }

            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged -= HandleDamaged;
                subscribedHealth = null;
            }

            subscribed = false;
        }

        private void HandleSwingStarted(MeleeSwing swing)
        {
            if (model == null || swing.Discipline == null || swing.Step == null)
            {
                return;
            }

            model.SwingStarted(swing.Discipline.Discipline, swing.StepIndex, swing.Step.startupSeconds, swing.Step.recoverySeconds,
                Time.time);
        }

        private void HandleSwingLanded(MeleeSwing swing)
        {
            model?.SwingLanded(Time.time);
        }

        private void HandleSwingInterrupted(MeleeSwing swing)
        {
            model?.SwingInterrupted(Time.time);
        }

        private void HandleChargeBegan(DisciplineDefinition discipline)
        {
            if (model != null && discipline != null)
            {
                model.ChargeBegan(discipline.Discipline, Time.time);
            }
        }

        private void HandleChargeEnded(bool released)
        {
            model?.ChargeEnded(released, Time.time);
        }

        private void HandleBlocked(BlockedHit hit)
        {
            model?.BlockedHit(Time.time);
        }

        private void HandleDamaged(DamageInfo damage)
        {
            model?.Damaged(damage.Amount, Time.time);
        }
    }
}
