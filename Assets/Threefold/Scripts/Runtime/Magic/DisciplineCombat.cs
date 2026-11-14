using System;
using System.Collections.Generic;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>Where a charge that is being held would go if it were released now.</summary>
    public readonly struct ChargePreview
    {
        public readonly DisciplineDefinition Discipline;

        /// <summary>0 for no charge, 1 for a full charge.</summary>
        public readonly float Power;

        public readonly ChargedTargetingMode Mode;
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        /// <summary>Camera aim: where the shot would land, or the end of its range.</summary>
        public readonly Vector3 Point;

        /// <summary>Camera aim: true if <see cref="Point"/> is on something.</summary>
        public readonly bool HasHit;

        /// <summary>Ground direction: the area that would be struck.</summary>
        public readonly AreaShape Area;

        public ChargePreview(DisciplineDefinition discipline, float power, ChargedTargetingMode mode, Vector3 origin,
            Vector3 direction, Vector3 point, bool hasHit, AreaShape area)
        {
            Discipline = discipline;
            Power = power;
            Mode = mode;
            Origin = origin;
            Direction = direction;
            Point = point;
            HasHit = hasHit;
            Area = area;
        }
    }

    /// <summary>A melee swing, as told to whatever shows it.</summary>
    public readonly struct MeleeSwing
    {
        public readonly DisciplineDefinition Discipline;
        public readonly MeleeStepProfile Step;
        public readonly int StepIndex;

        /// <summary>Where the swing started and which way it goes, along the ground.</summary>
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        /// <summary>How many targets it hit. 0 until it lands, and for a miss.</summary>
        public readonly int HitCount;

        public MeleeSwing(DisciplineDefinition discipline, MeleeStepProfile step, int stepIndex, Vector3 origin,
            Vector3 direction, int hitCount)
        {
            Discipline = discipline;
            Step = step;
            StepIndex = stepIndex;
            Origin = origin;
            Direction = direction;
            HitCount = hitCount;
        }
    }

    /// <summary>
    /// The Mage's combat with the three disciplines. It reads the attack gesture, lets
    /// <see cref="DisciplineCombatModel"/> decide what happens, and carries the decisions out with
    /// the existing systems: melee swings through <see cref="AreaStrike"/>, charged attacks through
    /// the <see cref="AbilityCaster"/> (which enforces mana and cooldown and drives the HUD and
    /// camera feedback), movement limits through the motor. It announces swings and charges with
    /// events and draws nothing itself.
    ///
    /// While this component is enabled, the slot attacks of <see cref="PlayerAbilityInput"/> (the
    /// Staff Strike and the placeholder bolt of the previous sprint) are switched off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisciplineCombat : MonoBehaviour, IChargedAttackGate, IProjectileDeflector
    {
        [Header("References")]
        [SerializeField] private CharacterInputSource inputSource;
        [SerializeField] private PlayerDisciplines disciplines;
        [SerializeField] private AbilityCaster caster;

        [Tooltip("Optional. Melee hits return a little mana.")]
        [SerializeField] private Mana mana;

        [Tooltip("Optional. A defeated character does not attack, and a heavy hit breaks a charge.")]
        [SerializeField] private Health health;

        [Tooltip("Optional. Turned towards swings and charged attacks, and slowed where a discipline says so.")]
        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("Optional. Needed only for the mild camera help in melee.")]
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Header("Rules")]
        [Tooltip("Damage in one frame that breaks a charge. 0 means damage never breaks it.")]
        [Min(0f)]
        [SerializeField] private float chargeInterruptDamage = 10f;

        [Tooltip("Movement speed while blocking, relative to normal.")]
        [Range(0f, 1f)]
        [SerializeField] private float blockMoveSpeedMultiplier = 0.55f;

        [Tooltip("How far above and below the Mage a guard's effect on the world reaches, in metres.")]
        [Min(0.5f)]
        [SerializeField] private float guardVerticalReach = 3f;

        [Tooltip("How far a melee swing can be heard by enemies, in metres.")]
        [Min(0f)]
        [SerializeField] private float meleeNoiseRadius = 7f;

        [Header("Camera help in melee")]
        [Tooltip("While swinging at a target and not turning the camera, the view drifts towards the target at up to " +
                 "this many degrees per second. It never snaps. 0 switches it off.")]
        [Range(0f, 120f)]
        [SerializeField] private float cameraAssistDegreesPerSecond = 35f;

        [Tooltip("A target this close to the centre of the view is left alone.")]
        [Range(0f, 30f)]
        [SerializeField] private float cameraAssistDeadZone = 6f;

        [Tooltip("A target further off to the side than this is not followed.")]
        [Range(10f, 120f)]
        [SerializeField] private float cameraAssistMaxAngle = 55f;

        private const int MaxCandidates = 24;

        private readonly List<CombatCommand> commands = new List<CombatCommand>(6);
        private readonly List<Health> struck = new List<Health>(8);
        private readonly TargetCandidate[] candidates = new TargetCandidate[MaxCandidates];
        private readonly RaycastHit[] previewHits = new RaycastHit[24];

        private DisciplineCombatModel model;
        private TraversalController traversal;
        private PlayerGuard guard;
        private AimProvider aimProvider;
        private PlayerAbilityInput legacyInput;
        private Health subscribedHealth;
        private float damageTaken;

        private Vector3 swingDirection = Vector3.forward;
        private Vector3 swingTarget;
        private bool swingHasTarget;

        private ChargePreview preview;
        private bool hasPreview;

        /// <summary>A swing began. Show it.</summary>
        public event Action<MeleeSwing> SwingStarted;

        /// <summary>A swing landed, with the number of targets it hit (0 for a miss).</summary>
        public event Action<MeleeSwing> SwingLanded;

        /// <summary>A swing was abandoned before it landed.</summary>
        public event Action<MeleeSwing> SwingInterrupted;

        /// <summary>A charge began.</summary>
        public event Action<DisciplineDefinition> ChargeBegan;

        /// <summary>A charge ended. True if the charged attack was released, false if it was cancelled or broken.</summary>
        public event Action<bool> ChargeEnded;

        /// <summary>A charge could not begin, with the reason.</summary>
        public event Action<DisciplineDefinition, CastResult> ChargeRefused;

        /// <summary>The guard sent its effect out, with the number of objects it braced.</summary>
        public event Action<DisciplineDefinition, int> GuardPulsed;

        /// <summary>The rules and their state: combo step, charge time, movement limits.</summary>
        public DisciplineCombatModel Model
        {
            get
            {
                if (model == null)
                {
                    model = new DisciplineCombatModel(disciplines, this);
                }

                return model;
            }
        }

        public bool IsCharging => model != null && model.IsCharging;

        /// <summary>The targets of the swing that just landed. Valid only during <see cref="SwingLanded"/>.</summary>
        public IReadOnlyList<Health> LastStruck => struck;

        public PlayerDisciplines Disciplines => disciplines;
        public AbilityCaster Caster => caster;
        public CharacterInputSource InputSource => inputSource;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(CharacterInputSource input, PlayerDisciplines playerDisciplines, AbilityCaster abilityCaster,
            Mana playerMana, Health playerHealth, ThirdPersonMotor characterMotor, ThirdPersonCamera rig)
        {
            inputSource = input;
            disciplines = playerDisciplines;
            caster = abilityCaster;
            mana = playerMana;
            health = playerHealth;
            motor = characterMotor;
            cameraRig = rig;
            model = null;
        }

        /// <summary>Where the charge being held would go. False when no charge is held.</summary>
        public bool TryGetChargePreview(out ChargePreview current)
        {
            current = preview;
            return hasPreview;
        }

        /// <summary>Whether a discipline's charged attack could be used now. Asked when a charge is about to begin.</summary>
        public CastResult Check(int slot)
        {
            DisciplineDefinition definition = disciplines != null ? disciplines.GetDefinition(slot) : null;
            if (definition == null || definition.ChargedAbility == null || caster == null)
            {
                return CastResult.NoAbility;
            }

            return caster.CheckCast(definition.ChargedAbility);
        }

        private void Awake()
        {
            traversal = GetComponent<TraversalController>();
            guard = GetComponent<PlayerGuard>();
            aimProvider = GetComponent<AimProvider>();
            legacyInput = GetComponent<PlayerAbilityInput>();
        }

        private void OnEnable()
        {
            if (legacyInput != null)
            {
                legacyInput.SlotAttacksSuppressed = true;
            }

            subscribedHealth = health;
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged += HandleDamaged;
            }
        }

        private void OnDisable()
        {
            if (legacyInput != null)
            {
                legacyInput.SlotAttacksSuppressed = false;
            }

            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged -= HandleDamaged;
                subscribedHealth = null;
            }

            if (model != null && model.IsCharging)
            {
                ChargeEnded?.Invoke(false);
            }

            model?.Reset();
            hasPreview = false;
            damageTaken = 0f;
        }

        private void Update()
        {
            if (inputSource == null || disciplines == null)
            {
                return;
            }

            DisciplineCombatModel rules = Model;
            rules.ChargeInterruptDamage = chargeInterruptDamage;
            rules.BlockMoveSpeedMultiplier = blockMoveSpeedMultiplier;

            var frame = new CombatFrameInput
            {
                Gesture = inputSource.AttackGesture,
                BlockHeld = inputSource.BlockHeld,
                HandsBusy = traversal != null && (traversal.IsHanging || traversal.IsMoving),
                Alive = health == null || health.IsAlive,
                DisciplineSlot = disciplines.CurrentSlot,
                DamageTaken = damageTaken,
            };
            damageTaken = 0f;

            commands.Clear();
            rules.Tick(frame, Time.time, commands);

            for (int i = 0; i < commands.Count; i++)
            {
                Execute(commands[i], frame);
            }

            if (motor != null && (rules.MoveSpeedMultiplier < 0.999f || !rules.SprintAllowed))
            {
                motor.LimitSpeedThisFrame(rules.MoveSpeedMultiplier, rules.SprintAllowed);
            }

            UpdateChargePreview(frame);
            UpdateCameraAssist(rules);
        }

        // ----------------------------------------------------------- commands

        private void Execute(in CombatCommand command, in CombatFrameInput frame)
        {
            DisciplineDefinition definition = disciplines.GetDefinition(command.Slot);

            switch (command.Kind)
            {
                case CombatCommandKind.MeleeStepStarted:
                    StartSwing(definition, command);
                    break;

                case CombatCommandKind.MeleeHit:
                    LandSwing(definition, command);
                    break;

                case CombatCommandKind.MeleeInterrupted:
                    swingHasTarget = false;
                    SwingInterrupted?.Invoke(new MeleeSwing(definition, command.Step, command.StepIndex, transform.position,
                        swingDirection, 0));
                    break;

                case CombatCommandKind.ChargeBegan:
                    ChargeBegan?.Invoke(definition);
                    break;

                case CombatCommandKind.ChargeRefused:
                    if (caster != null && definition != null)
                    {
                        // The same feedback as a failed cast: the mana bar and the crosshair flash.
                        caster.ReportRefusal(definition.ChargedAbility, command.Reason);
                    }

                    ChargeRefused?.Invoke(definition, command.Reason);
                    break;

                case CombatCommandKind.ChargeCancelled:
                    ChargeEnded?.Invoke(false);
                    break;

                case CombatCommandKind.ChargedReleased:
                    ReleaseCharged(definition, command);
                    break;

                case CombatCommandKind.InterruptInput:
                    inputSource.InterruptAttack();
                    break;

                case CombatCommandKind.GuardPulse:
                    PulseGuard(definition, command.Slot);
                    break;
            }
        }

        private void StartSwing(DisciplineDefinition definition, in CombatCommand command)
        {
            MeleeStepProfile step = command.Step;
            Vector3 origin = transform.position;

            // The swing goes where the player steers, or where the character faces when standing still.
            Vector3 wanted = transform.forward;
            if (motor != null && inputSource.Move.sqrMagnitude > 0.04f)
            {
                Vector3 steer = motor.GetCameraRelativeMoveDirection(inputSource.Move);
                if (steer.sqrMagnitude > 0.0001f)
                {
                    wanted = steer.normalized;
                }
            }

            wanted.y = 0f;
            swingDirection = wanted.sqrMagnitude > 0.0001f ? wanted.normalized : Vector3.forward;
            swingHasTarget = false;

            MeleeComboProfile combo = definition != null ? definition.Melee : null;
            if (combo != null && combo.facingAssistDegrees > 0f)
            {
                LayerMask layers = definition.MeleeHitLayers;
                float range = step.reach + combo.facingAssistExtraRange;
                int count = AreaStrike.CollectCandidates(gameObject, origin, range, step.verticalReach, layers, candidates);
                int best = TargetSelection.FindFacingTarget(origin, swingDirection, candidates, count, range,
                    combo.facingAssistDegrees);

                if (best >= 0)
                {
                    Vector3 toTarget = candidates[best].Position - origin;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 0.0001f)
                    {
                        swingDirection = toTarget.normalized;
                    }

                    swingTarget = candidates[best].Position;
                    swingHasTarget = true;
                }
            }

            if (motor != null)
            {
                motor.FaceDirection(swingDirection, step.startupSeconds + step.recoverySeconds * 0.5f + 0.1f);
            }

            SwingStarted?.Invoke(new MeleeSwing(definition, step, command.StepIndex, origin, swingDirection, 0));
        }

        private void LandSwing(DisciplineDefinition definition, in CombatCommand command)
        {
            MeleeStepProfile step = command.Step;
            Vector3 origin = transform.position;

            var request = new AreaStrikeRequest
            {
                User = gameObject,
                Shape = new AreaShape(origin, swingDirection, step.reach, step.arcDegrees, step.verticalReach),
                Damage = step.damage * (caster != null ? caster.DamageMultiplier : 1f),
                Stagger = step.stagger,
                Element = definition != null ? definition.Element : SpellElement.None,
                MaxTargets = step.maxTargets,
                Effects = step.Effects,
                Discipline = command.Slot,
                HitLayers = definition != null ? definition.MeleeHitLayers : (LayerMask)Physics.DefaultRaycastLayers,
                CoverCheckHeight = step.verticalReach * 0.5f,
            };

            int hits = AreaStrike.Resolve(request, struck);

            if (hits > 0 && mana != null && step.manaGainPerTarget > 0f)
            {
                mana.Restore(step.manaGainPerTarget * hits);
            }

            CombatEvents.RaiseNoise(new NoiseEvent(origin + swingDirection * (step.reach * 0.6f), meleeNoiseRadius, gameObject));
            SwingLanded?.Invoke(new MeleeSwing(definition, step, command.StepIndex, origin, swingDirection, hits));
            struck.Clear();
        }

        private void ReleaseCharged(DisciplineDefinition definition, in CombatCommand command)
        {
            ChargedAbilityDefinition ability = definition != null ? definition.ChargedAbility : null;
            bool fired = false;

            if (ability != null && caster != null)
            {
                CastOptions options = BuildOptions(ability, command.Aim, command.Power, command.Slot);
                fired = caster.TryCast(ability, options) == CastResult.Success;
            }

            hasPreview = false;
            ChargeEnded?.Invoke(fired);
        }

        // ---------------------------------------------------------------- aim

        private CastOptions BuildOptions(ChargedAbilityDefinition ability, Vector2 aim, float power, int slot)
        {
            ChargeProfile charge = ability.Charge;

            if (charge.targeting == ChargedTargetingMode.GroundDirection)
            {
                return CastOptions.Directed(power, ChargedAim.GroundDirection(aim, CameraYaw()), slot);
            }

            Vector2 offsets = ChargedAim.CameraOffsets(aim, charge.maxAimYawDegrees, charge.maxAimPitchDegrees);
            return CastOptions.Aimed(power, offsets.x, offsets.y, slot);
        }

        /// <summary>
        /// The guard of the selected discipline acts on the world: an Aegis guard braces unstable
        /// things around the Mage. It carries no damage. The protection block gives is not affected.
        /// </summary>
        private void PulseGuard(DisciplineDefinition definition, int slot)
        {
            if (definition == null || !definition.GuardEffect.HasEffect)
            {
                return;
            }

            GuardEffectProfile guard = definition.GuardEffect;
            var request = new AreaStrikeRequest
            {
                User = gameObject,
                Shape = new AreaShape(transform.position, transform.forward, guard.radius, 360f, guardVerticalReach),
                Damage = 0f,
                Stagger = 0f,
                Element = SpellElement.None,
                MaxTargets = 0,
                Effects = guard.Effects,
                Discipline = slot,
                HitLayers = definition.MeleeHitLayers,
                CoverCheckHeight = 1f,
            };

            AreaStrike.Resolve(request, null, out int braced);
            GuardPulsed?.Invoke(definition, braced);
        }

        /// <summary>
        /// Force + projectile = deflection. While the guard is up, the selected discipline's guard sets
        /// its force against a projectile that strikes it from the guarded side. The projectile decides
        /// whether that force turns it (<see cref="DeflectionRules"/>); if it does, it goes where the
        /// camera looks, or back at its sender when the sender is close to that line. Only the Aegis
        /// guard has force, so only it deflects. The protection block gives is unchanged.
        /// </summary>
        public bool TryDeflect(in IncomingProjectile incoming, out DeflectionPush push)
        {
            push = default;
            if (guard == null || !guard.IsBlocking || disciplines == null || !isActiveAndEnabled)
            {
                return false;
            }

            int slot = disciplines.CurrentSlot;
            DisciplineDefinition definition = disciplines.GetDefinition(slot);
            if (definition == null || !definition.GuardEffect.Deflects ||
                !DeflectionRules.GuardCatches(transform.forward, incoming.Direction, guard.GuardArcDegrees))
            {
                return false;
            }

            // Where the crosshair points, as the Arcane Arrow aims: from the projectile to what the camera
            // looks at. Without an aim provider, level along the camera's heading.
            Vector3 aim;
            if (aimProvider != null)
            {
                aim = aimProvider.GetAim(incoming.Position).Direction;
            }
            else
            {
                Transform view = motor != null ? motor.CameraTransform : null;
                aim = view != null ? view.forward : transform.forward;
                aim.y = 0f;
            }

            if (aim.sqrMagnitude < 0.0001f)
            {
                aim = transform.forward;
            }

            CombatTeam team = health != null ? health.Team : CombatTeam.Player;
            push = new DeflectionPush(gameObject, team, aim, definition.GuardEffect.deflectForce,
                definition.GuardEffect.deflectAssistDegrees, slot);
            return true;
        }

        /// <summary>The camera's heading, or the character's own without a camera.</summary>
        private float CameraYaw()
        {
            Transform view = motor != null ? motor.CameraTransform : null;
            Vector3 forward = view != null ? view.forward : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? ChargedAim.YawOf(forward) : ChargedAim.YawOf(transform.forward);
        }

        private void UpdateChargePreview(in CombatFrameInput frame)
        {
            hasPreview = false;

            DisciplineCombatModel rules = Model;
            if (!rules.IsCharging || caster == null)
            {
                return;
            }

            // The charge keeps the discipline it began with, whatever is selected now.
            DisciplineDefinition definition = disciplines.GetDefinition(rules.ChargeSlot);
            ChargedAbilityDefinition ability = definition != null ? definition.ChargedAbility : null;
            if (ability == null)
            {
                return;
            }

            float power = rules.CurrentChargePower;
            CastOptions options = BuildOptions(ability, frame.Gesture.Aim, power, rules.ChargeSlot);
            AbilityContext context = caster.PreviewContext(options);

            Vector3 point = context.AimPoint;
            bool hasHit = false;
            AreaShape area = default;

            if (ability is ChargedAreaDefinition areaAbility)
            {
                area = areaAbility.GetShape(transform.position, context.Direction, power);
                point = area.Origin + area.Forward * area.Reach;
            }
            else if (ability is ChargedProjectileDefinition projectile)
            {
                hasHit = FindImpactPoint(context.Origin, context.Direction, projectile.Radius(power),
                    projectile.Projectile.range, projectile.HitLayers, out point);
            }

            // The character commits to the aim: it faces where the attack will go.
            if (motor != null)
            {
                motor.FaceDirection(context.Direction, 0.2f);
            }

            preview = new ChargePreview(definition, power, ability.Charge.targeting, context.Origin, context.Direction,
                point, hasHit, area);
            hasPreview = true;
        }

        private bool FindImpactPoint(Vector3 origin, Vector3 direction, float radius, float range, LayerMask layers,
            out Vector3 point)
        {
            point = origin + direction * range;
            float nearest = range;
            bool found = false;

            int count = Physics.SphereCastNonAlloc(origin, radius, direction, previewHits, range, layers,
                QueryTriggerInteraction.Ignore);

            // The same exclusions as the projectile itself: the caster and the caster's allies are passed through.
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = previewHits[i];
                if (hit.distance >= nearest || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (health != null)
                {
                    Health other = hit.collider.GetComponentInParent<Health>();
                    if (other != null && health.IsAlliedWith(other))
                    {
                        continue;
                    }
                }

                nearest = hit.distance;

                // A sweep reports distance 0 for something it starts inside: the shot would strike at once.
                point = hit.distance > 0f ? hit.point : origin;
                found = true;
            }

            return found;
        }

        // ------------------------------------------------------------- camera

        /// <summary>
        /// While a swing is aimed at a target and the player is not turning the camera, the view drifts
        /// towards the target. It stops the moment the player touches the camera.
        /// </summary>
        private void UpdateCameraAssist(DisciplineCombatModel rules)
        {
            if (cameraRig == null || cameraAssistDegreesPerSecond <= 0f || !swingHasTarget || !rules.Combo.IsSwinging)
            {
                return;
            }

            if (inputSource.LookDelta.sqrMagnitude > 0.01f)
            {
                swingHasTarget = false;
                return;
            }

            Vector3 toTarget = swingTarget - cameraRig.Pivot;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.25f)
            {
                return;
            }

            float step = CameraAssist.YawStep(cameraRig.Yaw, ChargedAim.YawOf(toTarget), cameraAssistDegreesPerSecond,
                Time.deltaTime, cameraAssistDeadZone, cameraAssistMaxAngle);
            if (step != 0f)
            {
                cameraRig.AddYaw(step);
            }
        }

        private void HandleDamaged(DamageInfo damage)
        {
            damageTaken += damage.Amount;
        }
    }
}
