using System;
using Threefold.Characters;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>A hit that the guard reduced.</summary>
    public readonly struct BlockedHit
    {
        /// <summary>Damage the hit would have done.</summary>
        public readonly float Incoming;

        /// <summary>Damage that still got through.</summary>
        public readonly float Taken;

        public readonly Vector3 Point;

        /// <summary>Direction the damage travelled in.</summary>
        public readonly Vector3 Direction;

        public BlockedHit(float incoming, float taken, Vector3 point, Vector3 direction)
        {
            Incoming = incoming;
            Taken = taken;
            Point = point;
            Direction = direction;
        }
    }

    /// <summary>
    /// The player's block. It is the same whatever discipline is selected.
    ///
    /// While the block control is held and the hands are free, the guard is up at once, with no
    /// start-up: hits that arrive from within the guarded arc in front of the character lose a fixed
    /// share of their damage. Hits from behind, and damage without a direction such as a burn, are
    /// not reduced. While guarding, the character turns to face where the camera looks, so the
    /// player decides what is guarded by looking at it. There is no timed parry or counter.
    ///
    /// Blocking takes priority over attacking: the input sources cancel an attack press or charge
    /// when block is pressed (see <see cref="CombatInputRules"/>), and no swing starts while it is held.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGuard : MonoBehaviour, IDamageModifier
    {
        [SerializeField] private CharacterInputSource inputSource;

        [Tooltip("Optional. Guarded hits are reduced on this Health. Found on this object if empty.")]
        [SerializeField] private Health health;

        [Tooltip("Optional. Turned to face the camera's direction while guarding.")]
        [SerializeField] private ThirdPersonMotor motor;

        [Header("Guard")]
        [Tooltip("Share of a guarded hit's damage that is removed. 1 stops it completely.")]
        [Range(0f, 1f)]
        [SerializeField] private float damageReduction = 0.8f;

        [Tooltip("Width of the guarded arc in front of the character, in degrees.")]
        [Range(10f, 360f)]
        [SerializeField] private float guardArcDegrees = 160f;

        [Tooltip("While guarding, the character faces where the camera looks.")]
        [SerializeField] private bool faceCameraWhileGuarding = true;

        private TraversalController traversal;
        private Health registeredHealth;
        private float blockStartTime;

        /// <summary>Raised when the player starts blocking.</summary>
        public event Action BlockStarted;

        /// <summary>Raised when the player stops blocking, with how long the block lasted in seconds.</summary>
        public event Action<float> BlockEnded;

        /// <summary>Raised for every hit the guard reduced.</summary>
        public event Action<BlockedHit> Blocked;

        public bool IsBlocking { get; private set; }

        /// <summary>Seconds the current block has lasted, or 0.</summary>
        public float BlockSeconds => IsBlocking ? Time.time - blockStartTime : 0f;

        public float DamageReduction
        {
            get => damageReduction;
            set => damageReduction = Mathf.Clamp01(value);
        }

        public float GuardArcDegrees
        {
            get => guardArcDegrees;
            set => guardArcDegrees = Mathf.Clamp(value, 10f, 360f);
        }

        public CharacterInputSource InputSource
        {
            get => inputSource;
            set => inputSource = value;
        }

        public Health Health => health;

        /// <summary>Connects the input. Called by the setup tooling.</summary>
        public void Bind(CharacterInputSource input)
        {
            inputSource = input;
        }

        /// <summary>Connects what the guard protects and turns. Called by the setup tooling.</summary>
        public void BindBody(Health protectedHealth, ThirdPersonMotor characterMotor)
        {
            health = protectedHealth;
            motor = characterMotor;
        }

        /// <summary>
        /// Called by <see cref="Health"/> for every hit. Reduces hits that the guard covers and leaves
        /// all others as they are.
        /// </summary>
        public float ModifyIncomingDamage(in DamageInfo damage, float amount)
        {
            if (!IsBlocking || !BlockRules.Covers(transform.forward, damage.Direction, guardArcDegrees))
            {
                return amount;
            }

            float taken = BlockRules.Mitigate(amount, damageReduction);
            Blocked?.Invoke(new BlockedHit(amount, taken, damage.Point, damage.Direction));
            return taken;
        }

        private void Awake()
        {
            traversal = GetComponent<TraversalController>();
        }

        private void OnEnable()
        {
            if (health == null)
            {
                health = GetComponent<Health>();
            }

            if (motor == null)
            {
                motor = GetComponent<ThirdPersonMotor>();
            }

            registeredHealth = health;
            if (registeredHealth != null)
            {
                registeredHealth.AddModifier(this);
            }
        }

        private void OnDisable()
        {
            EndBlock();

            if (registeredHealth != null)
            {
                registeredHealth.RemoveModifier(this);
                registeredHealth = null;
            }
        }

        private void Update()
        {
            // Hands are busy while hanging, climbing or vaulting. A defeated character does not guard.
            bool handsFree = traversal == null || !(traversal.IsHanging || traversal.IsMoving);
            bool alive = health == null || health.IsAlive;
            bool wantsBlock = inputSource != null && inputSource.BlockHeld && handsFree && alive;

            if (wantsBlock && !IsBlocking)
            {
                IsBlocking = true;
                blockStartTime = Time.time;
                BlockStarted?.Invoke();
            }
            else if (!wantsBlock)
            {
                EndBlock();
            }

            if (IsBlocking && faceCameraWhileGuarding && motor != null && motor.CameraTransform != null)
            {
                motor.FaceDirection(motor.CameraTransform.forward, 0.2f);
            }
        }

        private void EndBlock()
        {
            if (!IsBlocking)
            {
                return;
            }

            float seconds = BlockSeconds;
            IsBlocking = false;
            BlockEnded?.Invoke(seconds);
        }
    }
}
