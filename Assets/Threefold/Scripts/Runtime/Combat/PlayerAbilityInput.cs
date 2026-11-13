using Threefold.Characters;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Connects the player's attack input to an <see cref="AbilityCaster"/>. Holding the attack
    /// control keeps attacking as fast as the cooldown allows, which suits both a mouse button and
    /// a thumb on a touchscreen. The character turns to face its aim when it attacks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAbilityInput : MonoBehaviour
    {
        private const int PrimarySlot = 0;

        [SerializeField] private CharacterInputSource inputSource;
        [SerializeField] private AbilityCaster caster;

        [Tooltip("Optional. Turned towards the aim direction when attacking.")]
        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("Seconds the character keeps facing its aim after an attack.")]
        [Min(0f)]
        [SerializeField] private float faceAimSeconds = 0.45f;

        [Tooltip("While the attack control is held without enough mana, seconds between repeated attempts. " +
                 "Keeps the out-of-mana feedback from firing every frame.")]
        [Min(0.05f)]
        [SerializeField] private float retryInterval = 0.5f;

        private float retryTime;

        /// <summary>Connects the bridge. Called by the setup tooling.</summary>
        public void Bind(CharacterInputSource input, AbilityCaster abilityCaster, ThirdPersonMotor characterMotor)
        {
            inputSource = input;
            caster = abilityCaster;
            motor = characterMotor;
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
        }

        private void Update()
        {
            if (inputSource == null || caster == null)
            {
                return;
            }

            bool pressed = inputSource.AttackPressedThisFrame;
            if (!pressed && !inputSource.AttackHeld)
            {
                return;
            }

            // A fresh press always tries, so the player gets feedback. A held control waits for the
            // cooldown, and after a refusal waits a moment before trying again.
            if (!pressed && (!caster.IsReady(PrimarySlot) || Time.time < retryTime))
            {
                return;
            }

            CastResult result = caster.TryCast(PrimarySlot);
            if (result == CastResult.NotEnoughMana || result == CastResult.CasterDefeated)
            {
                retryTime = Time.time + retryInterval;
            }
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            if (motor != null)
            {
                motor.FaceDirection(context.Direction, faceAimSeconds);
            }
        }
    }
}
