using Threefold.Combat;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>
    /// Feeds an Animator from the character's gameplay state: speed, ground contact, vertical
    /// speed, jumps and casts. Gameplay never reads the Animator, so the visual model can be
    /// replaced, or removed, without changing movement, combat or input.
    ///
    /// The Animator Controller is expected to use these parameters; any that it lacks are skipped:
    /// Speed (float, metres per second), Grounded (bool), VerticalSpeed (float), Jump (trigger),
    /// Cast (trigger).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // After the motor has moved the character this frame.
    public sealed class CharacterAnimationDriver : MonoBehaviour
    {
        public const string SpeedParameter = "Speed";
        public const string GroundedParameter = "Grounded";
        public const string VerticalSpeedParameter = "VerticalSpeed";
        public const string JumpParameter = "Jump";
        public const string CastParameter = "Cast";

        private static readonly int SpeedId = Animator.StringToHash(SpeedParameter);
        private static readonly int GroundedId = Animator.StringToHash(GroundedParameter);
        private static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeedParameter);
        private static readonly int JumpId = Animator.StringToHash(JumpParameter);
        private static readonly int CastId = Animator.StringToHash(CastParameter);

        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("The Animator on the visual model.")]
        [SerializeField] private Animator animator;

        [Tooltip("Optional. Plays the cast animation whenever an ability is used.")]
        [SerializeField] private AbilityCaster caster;

        [Tooltip("Smoothing of the speed sent to the Animator, in seconds. Avoids flicker between walk and run.")]
        [Min(0f)]
        [SerializeField] private float speedDampTime = 0.08f;

        private bool parametersKnown;
        private bool hasSpeed;
        private bool hasGrounded;
        private bool hasVerticalSpeed;
        private bool hasJump;
        private bool hasCast;
        private bool subscribed;

        public Animator Animator => animator;

        /// <summary>Connects the driver. Called by the setup tooling.</summary>
        public void Bind(ThirdPersonMotor characterMotor, Animator visualAnimator, AbilityCaster abilityCaster)
        {
            bool resubscribe = subscribed;
            if (resubscribe)
            {
                Unsubscribe();
            }

            motor = characterMotor;
            animator = visualAnimator;
            caster = abilityCaster;
            parametersKnown = false;

            if (resubscribe)
            {
                Subscribe();
            }
        }

        private void OnEnable()
        {
            if (motor == null)
            {
                motor = GetComponent<ThirdPersonMotor>();
            }

            if (caster == null)
            {
                caster = GetComponent<AbilityCaster>();
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (motor == null || !IsAnimatorReady())
            {
                return;
            }

            if (hasSpeed)
            {
                animator.SetFloat(SpeedId, motor.HorizontalSpeed, speedDampTime, Time.deltaTime);
            }

            if (hasGrounded)
            {
                animator.SetBool(GroundedId, motor.IsGrounded);
            }

            if (hasVerticalSpeed)
            {
                animator.SetFloat(VerticalSpeedId, motor.VerticalSpeed);
            }
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            if (motor != null)
            {
                motor.Jumped += HandleJumped;
            }

            if (caster != null)
            {
                caster.CastSucceeded += HandleCastSucceeded;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (motor != null)
            {
                motor.Jumped -= HandleJumped;
            }

            if (caster != null)
            {
                caster.CastSucceeded -= HandleCastSucceeded;
            }

            subscribed = false;
        }

        private void HandleJumped()
        {
            if (IsAnimatorReady() && hasJump)
            {
                animator.SetTrigger(JumpId);
            }
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            if (IsAnimatorReady() && hasCast)
            {
                animator.SetTrigger(CastId);
            }
        }

        /// <summary>
        /// True when the Animator can take parameters. Which parameters exist is read once, so a
        /// controller without one of them does not log a warning every frame.
        /// </summary>
        private bool IsAnimatorReady()
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            if (!parametersKnown)
            {
                parametersKnown = true;
                hasSpeed = hasGrounded = hasVerticalSpeed = hasJump = hasCast = false;

                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    int id = parameter.nameHash;
                    hasSpeed |= id == SpeedId && parameter.type == AnimatorControllerParameterType.Float;
                    hasGrounded |= id == GroundedId && parameter.type == AnimatorControllerParameterType.Bool;
                    hasVerticalSpeed |= id == VerticalSpeedId && parameter.type == AnimatorControllerParameterType.Float;
                    hasJump |= id == JumpId && parameter.type == AnimatorControllerParameterType.Trigger;
                    hasCast |= id == CastId && parameter.type == AnimatorControllerParameterType.Trigger;
                }
            }

            return true;
        }
    }
}
