using System;
using System.Collections.Generic;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>An aerial ability event, with the discipline that made it and where it happened.</summary>
    public readonly struct AerialAbilityEvent
    {
        public readonly AerialEvent Event;

        /// <summary>The discipline slot whose ability it is.</summary>
        public readonly int Slot;

        /// <summary>Where it happened: the Mage's position, or the point on the wall that stopped a dash.</summary>
        public readonly Vector3 Point;

        public AerialAbilityEvent(AerialEvent aerialEvent, int slot, Vector3 point)
        {
            Event = aerialEvent;
            Slot = slot;
            Point = point;
        }
    }

    /// <summary>
    /// The disciplines' aerial abilities: Arcane Ascension, Flame Fire Glide, Aegis Force Dash. The selected
    /// discipline decides which ability a second press of Jump in the air starts. The rules are in
    /// <see cref="AerialAbilityModel"/> (plain C#); this component feeds them the frame (input, ground, combat
    /// state, walls) and hands their movement to the <see cref="ThirdPersonMotor"/>, whose first jump, gravity
    /// and collisions are otherwise unchanged. It runs before the motor, so a press it uses never also jumps.
    ///
    /// Every tuning value is serialized here; the starting values are in <see cref="AerialAbilityDefaults"/>.
    /// See Docs/AerialAbilities.md.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)] // After input (-100), discipline selection and traversal (-50); before the motor and combat (0).
    public sealed class AerialAbilities : MonoBehaviour, IAerialMoveSource
    {
        [Header("References")]
        [SerializeField] private ThirdPersonMotor motor;
        [SerializeField] private CharacterInputSource inputSource;
        [SerializeField] private PlayerDisciplines disciplines;

        [Tooltip("Optional. No ability starts while charging; a charge pauses a glide.")]
        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. No ability starts while blocking; blocking pauses a glide.")]
        [SerializeField] private PlayerGuard guard;

        [Tooltip("Optional. A defeated Mage uses none; a heavy hit staggers.")]
        [SerializeField] private Health health;

        [Tooltip("Optional. No ability while hanging, climbing or vaulting.")]
        [SerializeField] private TraversalController traversal;

        [Tooltip("Optional. Receives the restrained camera feedback.")]
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Header("Abilities")]
        [Tooltip("The aerial ability of each discipline, in selector order: Arcane, Flame, Aegis.")]
        [SerializeField] private AerialAbilityKind[] abilityBySlot =
        {
            AerialAbilityKind.Ascension, AerialAbilityKind.Glide, AerialAbilityKind.Dash,
        };

        [SerializeField] private AerialRulesTuning rules = AerialAbilityDefaults.Rules();
        [SerializeField] private AscensionTuning ascension = AerialAbilityDefaults.Ascension();
        [SerializeField] private GlideTuning glide = AerialAbilityDefaults.Glide();
        [SerializeField] private DashTuning dash = AerialAbilityDefaults.Dash();

        [Header("Camera")]
        [Tooltip("Strength of the camera's response to an ascension or a dash, 0 to 1. 0 turns it off.")]
        [Range(0f, 1f)]
        [SerializeField] private float cameraFeedback = 1f;

        [Tooltip("Camera shake when an ascension starts, before the strength above.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float ascensionTrauma = 0.06f;

        [Tooltip("Camera shake when a dash starts, before the strength above.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float dashTrauma = 0.1f;

        [Tooltip("Camera shake when a dash is stopped by a wall, before the strength above.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float wallTrauma = 0.12f;

        private readonly List<AerialEvent> events = new List<AerialEvent>(4);
        private AerialAbilityModel model;
        private AerialMotion motion;
        private Health subscribedHealth;
        private int seenGroundJumps;
        private int seenLaunches;
        private float staggeredUntil = float.NegativeInfinity;
        private int activeSlot = -1;

        /// <summary>An ability started, resumed, paused or ended.</summary>
        public event Action<AerialAbilityEvent> AbilityEvent;

        /// <summary>The rules and their state, for diagnostics and tests.</summary>
        public AerialAbilityModel Model => model ?? (model = new AerialAbilityModel(rules, ascension, glide, dash));

        /// <summary>The hands are used by an aerial ability: the combat rules start no attack.</summary>
        public bool HandsBusy => model != null && model.HandsBusy;

        /// <inheritdoc />
        public int AerialMove => model != null ? model.AnimationMove : 0;

        /// <summary>The discipline slot of the ability in progress, or -1.</summary>
        public int ActiveSlot => model != null && model.Active != AerialAbilityKind.None ? activeSlot : -1;

        public AscensionTuning Ascension => ascension;
        public GlideTuning Glide => glide;
        public DashTuning Dash => dash;
        public AerialRulesTuning Rules => rules;

        /// <summary>Strength of the camera feedback, 0 (off) to 1.</summary>
        public float CameraFeedback
        {
            get => cameraFeedback;
            set => cameraFeedback = Mathf.Clamp01(value);
        }

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(ThirdPersonMotor characterMotor, CharacterInputSource input, PlayerDisciplines playerDisciplines,
            DisciplineCombat disciplineCombat, PlayerGuard playerGuard, Health playerHealth, TraversalController traversalController,
            ThirdPersonCamera rig)
        {
            motor = characterMotor;
            inputSource = input;
            disciplines = playerDisciplines;
            combat = disciplineCombat;
            guard = playerGuard;
            health = playerHealth;
            traversal = traversalController;
            cameraRig = rig;
        }

        /// <summary>The ability of a discipline slot, from the data.</summary>
        public AerialAbilityKind AbilityOf(int slot)
        {
            return abilityBySlot != null && slot >= 0 && slot < abilityBySlot.Length ? abilityBySlot[slot] : AerialAbilityKind.None;
        }

        /// <summary>The discipline slot that owns an ability, or -1.</summary>
        public int SlotOf(AerialAbilityKind kind)
        {
            if (abilityBySlot != null)
            {
                for (int i = 0; i < abilityBySlot.Length; i++)
                {
                    if (abilityBySlot[i] == kind)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<ThirdPersonMotor>();
            if (inputSource == null && motor != null) inputSource = motor.InputSource;
            if (disciplines == null) disciplines = GetComponent<PlayerDisciplines>();
            if (combat == null) combat = GetComponent<DisciplineCombat>();
            if (guard == null) guard = GetComponent<PlayerGuard>();
            if (health == null) health = GetComponent<Health>();
            if (traversal == null) traversal = GetComponent<TraversalController>();
        }

        private void OnEnable()
        {
            Model.Reset();
            if (motor != null)
            {
                seenGroundJumps = motor.GroundJumpCount;
                seenLaunches = motor.LaunchCount;
            }

            subscribedHealth = health;
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged += HandleDamaged;
            }
        }

        private void OnDisable()
        {
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged -= HandleDamaged;
                subscribedHealth = null;
            }

            events.Clear();
            if (model != null && model.Active != AerialAbilityKind.None)
            {
                // Switched off mid-ability: whatever shows it is told it ended.
                AbilityEvent?.Invoke(new AerialAbilityEvent(new AerialEvent(AerialEventKind.Ended, model.Active,
                    AerialEndReason.Interrupted, Vector3.zero), activeSlot, transform.position));
            }

            model?.Reset();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (motor == null || deltaTime <= 0f)
            {
                return;
            }

            bool readInput = inputSource != null && motor.MovementInputEnabled;
            int slot = disciplines != null ? disciplines.CurrentSlot : 0;
            Vector2 stick = readInput ? inputSource.Move : Vector2.zero;

            var input = new AerialFrameInput
            {
                DeltaTime = deltaTime,
                Grounded = motor.IsGrounded,
                VerticalSpeed = motor.VerticalSpeed,
                Gravity = motor.Gravity,
                JumpedFromGround = motor.GroundJumpCount != seenGroundJumps,
                LaunchedFromLedge = motor.LaunchCount != seenLaunches,
                JumpPressed = readInput && inputSource.JumpPressedThisFrame,
                JumpHeld = readInput && inputSource.JumpHeld,
                Selected = AbilityOf(slot),
                Move = motor.GetCameraRelativeMoveDirection(stick),
                Facing = transform.forward,
                Charging = combat != null && combat.IsCharging,
                Blocking = guard != null && guard.IsBlocking,
                Staggered = Time.time < staggeredUntil,
                Defeated = health != null && !health.IsAlive,
                Traversing = motor.IsExternallyControlled ||
                             (traversal != null && (traversal.IsHanging || traversal.IsMoving)),
                HitWall = motor.HitWallLastMove,
                WallNormal = motor.LastWallNormal,
            };

            seenGroundJumps = motor.GroundJumpCount;
            seenLaunches = motor.LaunchCount;

            events.Clear();
            Model.Tick(input, ref motion, events);
            motor.ApplyAerialMotion(motion);

            for (int i = 0; i < events.Count; i++)
            {
                Announce(events[i], slot);
            }
        }

        private void Announce(in AerialEvent aerialEvent, int selectedSlot)
        {
            if (aerialEvent.Kind == AerialEventKind.Activated || aerialEvent.Kind == AerialEventKind.Resumed)
            {
                // The ability belongs to the discipline that started it, whatever is selected later.
                activeSlot = aerialEvent.Kind == AerialEventKind.Resumed ? SlotOf(AerialAbilityKind.Glide) : selectedSlot;
            }

            Vector3 point = transform.position;
            bool wall = aerialEvent.Kind == AerialEventKind.Ended && aerialEvent.Reason == AerialEndReason.Wall;
            if (wall)
            {
                point = motor.LastWallPoint;
            }

            int slot = activeSlot >= 0 ? activeSlot : SlotOf(aerialEvent.Ability);
            AbilityEvent?.Invoke(new AerialAbilityEvent(aerialEvent, slot, point));

            if (cameraRig != null && cameraFeedback > 0f)
            {
                float trauma = 0f;
                if (aerialEvent.Kind == AerialEventKind.Activated && aerialEvent.Ability == AerialAbilityKind.Ascension)
                {
                    trauma = ascensionTrauma;
                }
                else if (aerialEvent.Kind == AerialEventKind.Activated && aerialEvent.Ability == AerialAbilityKind.Dash)
                {
                    trauma = dashTrauma;
                }
                else if (wall)
                {
                    trauma = wallTrauma;
                }

                if (trauma > 0f)
                {
                    cameraRig.AddTrauma(trauma * cameraFeedback);
                }
            }
        }

        private void HandleDamaged(DamageInfo damage)
        {
            if (damage.Amount >= rules.staggerDamage)
            {
                staggeredUntil = Time.time + rules.staggerSeconds;
            }
        }
    }
}
