using System;
using System.Collections.Generic;
using Threefold.Characters;
using Threefold.Combat;
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
    public sealed class CombatAnimationDirector : MonoBehaviour
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

        private static readonly int EmptyHash = Animator.StringToHash(CombatAnimatorNames.EmptyState);

        private readonly int[] layerIndex = new int[CombatAnimatorNames.LayerCount];
        private readonly int[] speedParameter = new int[CombatAnimatorNames.LayerCount];
        private readonly int[] appliedVersion = { -1, -1, -1 };
        private readonly Dictionary<CombatSlotId, int> stateHashes = new Dictionary<CombatSlotId, int>(64);
        private readonly Dictionary<CombatSlotId, int> twinHashes = new Dictionary<CombatSlotId, int>(64);
        private readonly List<CombatCue> dispatch = new List<CombatCue>(16);

        private CombatAnimationModel model;
        private Animator animator;
        private Animator lastTried;
        private int castLayerIndex = -1;
        private bool subscribed;
        private DisciplineCombat subscribedCombat;
        private PlayerGuard subscribedGuard;
        private Health subscribedHealth;

        /// <summary>A moment of an attack. See <see cref="CombatCueKind"/> for the guarantees.</summary>
        public event Action<CombatCue> Cue;

        /// <summary>True while it plays on an Animator whose controller has the combat layers.</summary>
        public bool IsActive => animator != null;

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
            if (IsActive && (!wanted || driver.Animator != animator))
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

            if (IsActive && animator.isActiveAndEnabled)
            {
                Apply();
            }
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

            for (int i = 0; i < CombatAnimatorNames.LayerCount; i++)
            {
                var layer = (CombatLayer)i;
                layerIndex[i] = candidate.GetLayerIndex(CombatAnimatorNames.LayerName(layer));
                if (layerIndex[i] < 0)
                {
                    Debug.LogWarning("[Threefold] The Mage's Animator Controller has no combat animation layers, so the " +
                                     "combat animations are off. Run Threefold > Animation > Apply Combat Animation Set.", this);
                    return;
                }

                string speed = CombatAnimatorNames.SpeedParameter(layer);
                speedParameter[i] = speed != null && HasFloat(candidate, speed) ? Animator.StringToHash(speed) : 0;
                appliedVersion[i] = -1;
            }

            stateHashes.Clear();
            twinHashes.Clear();
            animationSet.Library.ForEachSlot((id, slot) =>
            {
                if (slot != null && slot.IsAssigned)
                {
                    stateHashes[id] = Animator.StringToHash(id.StateName);
                    twinHashes[id] = Animator.StringToHash(id.TwinStateName);
                }
            });

            animator = candidate;
            if (driver != null)
            {
                driver.ClaimCombatAnimations(this);
            }

            // The casting gesture's layer stays silent while the combat layers play; it returns when they stop.
            castLayerIndex = animator.GetLayerIndex(CharacterAnimationDriver.CastLayer);
            if (castLayerIndex >= 0)
            {
                animator.SetLayerWeight(castLayerIndex, 0f);
            }
        }

        private void Deactivate()
        {
            if (driver != null)
            {
                driver.ReleaseCombatAnimations(this);
            }

            if (animator != null)
            {
                for (int i = 0; i < CombatAnimatorNames.LayerCount; i++)
                {
                    if (layerIndex[i] >= 0 && layerIndex[i] < animator.layerCount)
                    {
                        animator.SetLayerWeight(layerIndex[i], 0f);
                    }
                }

                if (castLayerIndex >= 0 && castLayerIndex < animator.layerCount)
                {
                    animator.SetLayerWeight(castLayerIndex, 1f);
                }
            }

            castLayerIndex = -1;
            animator = null;
        }

        private void Apply()
        {
            for (int i = 0; i < CombatAnimatorNames.LayerCount; i++)
            {
                var layer = (CombatLayer)i;
                int index = layerIndex[i];
                CombatLayerState state = model.GetLayerState(layer);

                if (state.Version != appliedVersion[i])
                {
                    appliedVersion[i] = state.Version;
                    int hash = StateHash(state, index, out float seconds);
                    animator.CrossFadeInFixedTime(hash, state.BlendSeconds, index, state.StartNormalized * seconds);
                }

                if (speedParameter[i] != 0)
                {
                    animator.SetFloat(speedParameter[i], model.GetLayerSpeed(layer));
                }

                animator.SetLayerWeight(index, model.GetLayerWeight(layer));
            }
        }

        /// <summary>The state to cross-fade to: the slot's (or its twin), or Empty if the controller lacks it.</summary>
        private int StateHash(in CombatLayerState state, int index, out float seconds)
        {
            seconds = 0f;
            if (state.Slot.IsNone)
            {
                return EmptyHash;
            }

            CombatAnimationSlot slot = animationSet.Library.GetSlot(state.Slot);
            if (slot == null)
            {
                return EmptyHash;
            }

            seconds = slot.Seconds;
            if (state.Twin && twinHashes.TryGetValue(state.Slot, out int twin) && animator.HasState(index, twin))
            {
                return twin;
            }

            if (stateHashes.TryGetValue(state.Slot, out int main) && animator.HasState(index, main))
            {
                return main;
            }

            return EmptyHash;
        }

        private static bool HasFloat(Animator target, string name)
        {
            int id = Animator.StringToHash(name);
            foreach (AnimatorControllerParameter parameter in target.parameters)
            {
                if (parameter.nameHash == id && parameter.type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }

            return false;
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
