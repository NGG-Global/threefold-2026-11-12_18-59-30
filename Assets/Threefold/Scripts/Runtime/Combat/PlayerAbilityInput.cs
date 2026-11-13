using System;
using Threefold.Characters;
using Threefold.InputHandling;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>What the player asked for when releasing a charged attack.</summary>
    public readonly struct ChargedAttackRequest
    {
        /// <summary>The discipline selected at the moment of release.</summary>
        public readonly MagicDiscipline Discipline;

        /// <summary>Aim from dragging, length 0 to 1, in screen directions (X right, Y up). Zero means the camera's aim.</summary>
        public readonly Vector2 Aim;

        /// <summary>0 at the start of the charge, 1 at full charge.</summary>
        public readonly float ChargeProgress;

        public ChargedAttackRequest(MagicDiscipline discipline, Vector2 aim, float chargeProgress)
        {
            Discipline = discipline;
            Aim = aim;
            ChargeProgress = chargeProgress;
        }
    }

    /// <summary>
    /// Connects the player's attack gesture to an <see cref="AbilityCaster"/>:
    ///
    /// - A tap performs the standard melee attack (the melee slot).
    /// - A charge raises <see cref="ChargeStarted"/>; releasing it raises <see cref="ChargedAttackReleased"/>
    ///   with the discipline, aim and charge. Until the Living Magic abilities are connected to that
    ///   event, a placeholder fires the ability in the charged slot (Arcane Bolt), steered sideways by the aim.
    /// - A cancelled press or charge raises <see cref="AttackCancelled"/> and fires nothing.
    ///
    /// Every <see cref="ChargeStarted"/> is followed by exactly one <see cref="ChargedAttackReleased"/>
    /// or <see cref="AttackCancelled"/>, even when the charge ends without an event from the input
    /// (the controls were hidden or disabled, the app lost focus) or while the hands are busy climbing.
    ///
    /// Spells turn the character to face their aim; a melee strike follows the body's facing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAbilityInput : MonoBehaviour
    {
        public const int DefaultChargedSlot = 0;
        public const int DefaultMeleeSlot = 1;

        [SerializeField] private CharacterInputSource inputSource;
        [SerializeField] private AbilityCaster caster;

        [Tooltip("Optional. Turned towards the aim direction when a spell is cast.")]
        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("Optional. Supplies the discipline reported with a charged attack.")]
        [SerializeField] private PlayerDisciplines disciplines;

        [Header("Slots")]
        [Tooltip("Ability slot used by a tap: the standard melee attack. If the slot is empty, the charged slot is used instead.")]
        [SerializeField] private int meleeSlot = DefaultMeleeSlot;

        [Tooltip("Ability slot fired by the placeholder charged attack.")]
        [SerializeField] private int chargedSlot = DefaultChargedSlot;

        [Tooltip("While on, releasing a charge fires the ability in the charged slot. Turn it off once the " +
                 "discipline abilities respond to ChargedAttackReleased.")]
        [SerializeField] private bool placeholderChargedAttack = true;

        [Tooltip("Degrees the placeholder charged attack turns for a full sideways drag.")]
        [Range(0f, 90f)]
        [SerializeField] private float maxAimYawDegrees = 35f;

        [Header("Feel")]
        [Tooltip("Seconds the character keeps facing its aim after a spell.")]
        [Min(0f)]
        [SerializeField] private float faceAimSeconds = 0.45f;

        [Tooltip("A tap made this long before the cooldown ends is still performed when it ends. Makes quick taps reliable.")]
        [Min(0f)]
        [SerializeField] private float pressBufferSeconds = 0.2f;

        private TraversalController traversal;
        private float bufferedUntil = -1f;
        private bool chargeOpen;

        /// <summary>Raised when a charge begins, with the discipline selected at that moment.</summary>
        public event Action<MagicDiscipline> ChargeStarted;

        /// <summary>Raised when a charge is released. The connection point for the Living Magic abilities.</summary>
        public event Action<ChargedAttackRequest> ChargedAttackReleased;

        /// <summary>Raised when an attack press or charge is abandoned without firing.</summary>
        public event Action AttackCancelled;

        /// <summary>True while the player holds a charge.</summary>
        public bool IsCharging => inputSource != null && inputSource.AttackGesture.IsCharging;

        public bool PlaceholderChargedAttack
        {
            get => placeholderChargedAttack;
            set => placeholderChargedAttack = value;
        }

        public int MeleeSlot => meleeSlot;

        public int ChargedSlot => chargedSlot;

        /// <summary>Connects the bridge. Called by the setup tooling.</summary>
        public void Bind(CharacterInputSource input, AbilityCaster abilityCaster, ThirdPersonMotor characterMotor)
        {
            inputSource = input;
            caster = abilityCaster;
            motor = characterMotor;
        }

        /// <summary>Connects the discipline state. Called by the setup tooling.</summary>
        public void BindDisciplines(PlayerDisciplines source)
        {
            disciplines = source;
        }

        public PlayerDisciplines Disciplines => disciplines;

        private void Awake()
        {
            traversal = GetComponent<TraversalController>();
        }

        private void OnEnable()
        {
            if (caster != null)
            {
                caster.CastSucceeded += HandleCastSucceeded;
            }
        }

        private void OnDisable()
        {
            if (caster != null)
            {
                caster.CastSucceeded -= HandleCastSucceeded;
            }

            if (chargeOpen)
            {
                CloseChargeAsCancelled();
            }
        }

        private void Update()
        {
            if (inputSource == null || caster == null)
            {
                return;
            }

            AttackGestureFrame gesture = inputSource.AttackGesture;

            // Hands are busy while hanging, climbing or vaulting: nothing fires, and a charge that ends
            // now is reported as cancelled so that listeners are never left in the charging state.
            if (traversal != null && (traversal.IsHanging || traversal.IsMoving))
            {
                bufferedUntil = -1f;
                if (chargeOpen && (gesture.ChargeReleased || gesture.Cancelled || !gesture.IsCharging))
                {
                    CloseChargeAsCancelled();
                }

                return;
            }

            // A charge that began while the hands were busy is opened as soon as they are free.
            bool charging = gesture.ChargeStarted || gesture.IsCharging || gesture.ChargeReleased;
            if (charging && !chargeOpen)
            {
                chargeOpen = true;
                ChargeStarted?.Invoke(CurrentDiscipline);
            }

            if (gesture.ChargeReleased)
            {
                chargeOpen = false;
                ReleaseCharge(gesture);
            }
            else if (gesture.Cancelled)
            {
                chargeOpen = false;
                AttackCancelled?.Invoke();
            }
            else if (chargeOpen && !gesture.IsCharging)
            {
                // The charge ended without an event, for example because the touch controls were hidden.
                CloseChargeAsCancelled();
            }

            if (gesture.Tapped)
            {
                bufferedUntil = Time.time + pressBufferSeconds;
            }

            if (Time.time > bufferedUntil)
            {
                return;
            }

            // A tap during the cooldown is remembered and performed the moment the cooldown ends.
            int slot = MeleeSlotInUse;
            if (!caster.IsReady(slot))
            {
                return;
            }

            bufferedUntil = -1f;
            caster.TryCast(slot);
        }

        private void CloseChargeAsCancelled()
        {
            chargeOpen = false;
            AttackCancelled?.Invoke();
        }

        /// <summary>The melee slot, or the charged slot if the character has no melee ability yet.</summary>
        private int MeleeSlotInUse => caster.GetAbility(meleeSlot) != null ? meleeSlot : chargedSlot;

        private MagicDiscipline CurrentDiscipline => disciplines != null ? disciplines.Current : MagicDiscipline.Arcane;

        private void ReleaseCharge(in AttackGestureFrame gesture)
        {
            var request = new ChargedAttackRequest(CurrentDiscipline, gesture.Aim, gesture.ChargeProgress);
            ChargedAttackReleased?.Invoke(request);

            if (placeholderChargedAttack)
            {
                caster.TryCast(chargedSlot, gesture.Aim.x * maxAimYawDegrees);
            }
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            // A melee strike lands where the body faces; turning afterwards would look like a miss.
            if (motor != null && !(ability is MeleeStrikeDefinition))
            {
                motor.FaceDirection(context.Direction, faceAimSeconds);
            }
        }
    }
}
