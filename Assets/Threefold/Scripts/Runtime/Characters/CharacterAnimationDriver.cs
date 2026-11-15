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
    /// Cast (trigger), LandImpact (float, metres per second), Hanging (bool), Shimmy (float, -1 to 1),
    /// Climb (trigger, climbing up or onto something) and Vault (trigger).
    ///
    /// The combat animations (attacks, charges, block, reactions) are played by a combat animation
    /// director on layers of their own (Docs/CombatAnimationArchitecture.md). While one has claimed this
    /// driver, the casting gesture of a charged attack is left to it, so the two never play at once.
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
        public const string LandImpactParameter = "LandImpact";
        public const string HangingParameter = "Hanging";
        public const string ShimmyParameter = "Shimmy";
        public const string ClimbParameter = "Climb";
        public const string VaultParameter = "Vault";

        /// <summary>Int: the aerial move (0 none, 1 ascension, 2 glide, 3 dash), set every frame from gameplay.</summary>
        public const string AerialMoveParameter = "AerialMove";

        /// <summary>The Animator layer that plays the casting gesture on the upper body.</summary>
        public const string CastLayer = "Cast";

        private static readonly int SpeedId = Animator.StringToHash(SpeedParameter);
        private static readonly int GroundedId = Animator.StringToHash(GroundedParameter);
        private static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeedParameter);
        private static readonly int JumpId = Animator.StringToHash(JumpParameter);
        private static readonly int CastId = Animator.StringToHash(CastParameter);
        private static readonly int LandImpactId = Animator.StringToHash(LandImpactParameter);
        private static readonly int HangingId = Animator.StringToHash(HangingParameter);
        private static readonly int ShimmyId = Animator.StringToHash(ShimmyParameter);
        private static readonly int ClimbId = Animator.StringToHash(ClimbParameter);
        private static readonly int VaultId = Animator.StringToHash(VaultParameter);
        private static readonly int AerialMoveId = Animator.StringToHash(AerialMoveParameter);

        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("The Animator on the visual model.")]
        [SerializeField] private Animator animator;

        [Tooltip("Optional. Plays the cast animation whenever an ability is used.")]
        [SerializeField] private AbilityCaster caster;

        [Tooltip("Optional. Plays climbing, hanging and vaulting.")]
        [SerializeField] private TraversalController traversal;

        [Tooltip("Smoothing of the speed sent to the Animator, in seconds. Avoids flicker between walk and run.")]
        [Min(0f)]
        [SerializeField] private float speedDampTime = 0.08f;

        private bool parametersKnown;
        private System.Collections.Generic.HashSet<int> triggerIds;
        private bool hasSpeed;
        private bool hasGrounded;
        private bool hasVerticalSpeed;
        private bool hasJump;
        private bool hasCast;
        private bool hasLandImpact;
        private bool hasHanging;
        private bool hasShimmy;
        private bool hasClimb;
        private bool hasVault;
        private bool hasAerialMove;
        private IAerialMoveSource aerialSource;
        private bool subscribed;
        private Object combatAnimationOwner;

        public Animator Animator => animator;

        /// <summary>
        /// True while a combat animation director plays the attacks on this Animator. The casting gesture is
        /// then not played for charged attacks, and the attack triggers named in discipline data are not set.
        /// </summary>
        public bool CombatAnimationsActive => combatAnimationOwner != null;

        /// <summary>Called by the combat animation director when it starts playing on this Animator.</summary>
        public void ClaimCombatAnimations(Object owner)
        {
            combatAnimationOwner = owner;
        }

        /// <summary>Called by the combat animation director when it stops. Only its owner can release the claim.</summary>
        public void ReleaseCombatAnimations(Object owner)
        {
            if (combatAnimationOwner == owner)
            {
                combatAnimationOwner = null;
            }
        }

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
            triggerIds = null;

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

            // A visual model replaced after this was connected (a change of look) leaves the reference empty
            // until the level's setup runs again. The model is always a child, so it can be found.
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (traversal == null)
            {
                traversal = GetComponent<TraversalController>();
            }

            aerialSource = GetComponent<IAerialMoveSource>();
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

            // A state, not a trigger: when the ability ends the value returns to 0 and the pose ends with it.
            if (hasAerialMove)
            {
                animator.SetInteger(AerialMoveId, aerialSource != null ? aerialSource.AerialMove : 0);
            }

            if (traversal != null)
            {
                if (hasHanging)
                {
                    animator.SetBool(HangingId, traversal.IsHanging);
                }

                if (hasShimmy)
                {
                    animator.SetFloat(ShimmyId, traversal.ShimmyAmount, speedDampTime, Time.deltaTime);
                }
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
                motor.Landed += HandleLanded;
            }

            if (caster != null)
            {
                caster.CastSucceeded += HandleCastSucceeded;
            }

            if (traversal != null)
            {
                traversal.MoveStarted += HandleTraversal;
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
                motor.Landed -= HandleLanded;
            }

            if (caster != null)
            {
                caster.CastSucceeded -= HandleCastSucceeded;
            }

            if (traversal != null)
            {
                traversal.MoveStarted -= HandleTraversal;
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

        private void HandleLanded(float impactSpeed)
        {
            if (IsAnimatorReady() && hasLandImpact)
            {
                animator.SetFloat(LandImpactId, impactSpeed);
            }
        }

        private void HandleTraversal(TraversalMove move)
        {
            if (!IsAnimatorReady())
            {
                return;
            }

            switch (move)
            {
                case TraversalMove.ClimbUp:
                case TraversalMove.Mantle:
                    if (hasClimb)
                    {
                        animator.SetTrigger(ClimbId);
                    }

                    break;

                case TraversalMove.Vault:
                    if (hasVault)
                    {
                        animator.SetTrigger(VaultId);
                    }

                    break;

                case TraversalMove.Grab:
                case TraversalMove.DropToHang:
                    if (hasHanging)
                    {
                        animator.SetBool(HangingId, true);
                    }

                    break;
            }
        }

        /// <summary>
        /// Sets a trigger by name if the Animator has it. Used for attack animations named in data:
        /// a controller without the trigger is simply left alone, with no warning.
        /// </summary>
        /// <returns>True if the trigger exists and was set.</returns>
        public bool TrySetTrigger(string triggerName)
        {
            if (string.IsNullOrEmpty(triggerName) || !IsAnimatorReady())
            {
                return false;
            }

            int id = Animator.StringToHash(triggerName);
            if (triggerIds == null)
            {
                triggerIds = new System.Collections.Generic.HashSet<int>();
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    if (parameter.type == AnimatorControllerParameterType.Trigger)
                    {
                        triggerIds.Add(parameter.nameHash);
                    }
                }
            }

            if (!triggerIds.Contains(id))
            {
                return false;
            }

            animator.SetTrigger(id);
            return true;
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            // The combat animation director plays the discipline's own release instead.
            if (CombatAnimationsActive && ability is ChargedAbilityDefinition)
            {
                return;
            }

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
                hasLandImpact = hasHanging = hasShimmy = hasClimb = hasVault = hasAerialMove = false;

                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    int id = parameter.nameHash;
                    hasSpeed |= id == SpeedId && parameter.type == AnimatorControllerParameterType.Float;
                    hasGrounded |= id == GroundedId && parameter.type == AnimatorControllerParameterType.Bool;
                    hasVerticalSpeed |= id == VerticalSpeedId && parameter.type == AnimatorControllerParameterType.Float;
                    hasJump |= id == JumpId && parameter.type == AnimatorControllerParameterType.Trigger;
                    hasCast |= id == CastId && parameter.type == AnimatorControllerParameterType.Trigger;
                    hasLandImpact |= id == LandImpactId && parameter.type == AnimatorControllerParameterType.Float;
                    hasHanging |= id == HangingId && parameter.type == AnimatorControllerParameterType.Bool;
                    hasShimmy |= id == ShimmyId && parameter.type == AnimatorControllerParameterType.Float;
                    hasClimb |= id == ClimbId && parameter.type == AnimatorControllerParameterType.Trigger;
                    hasVault |= id == VaultId && parameter.type == AnimatorControllerParameterType.Trigger;
                    hasAerialMove |= id == AerialMoveId && parameter.type == AnimatorControllerParameterType.Int;
                }
            }

            return true;
        }
    }
}
