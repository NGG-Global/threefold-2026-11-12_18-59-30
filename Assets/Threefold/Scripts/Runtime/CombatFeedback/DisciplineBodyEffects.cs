using System.Collections.Generic;
using Threefold.Combat;
using Threefold.CombatAnimation;
using Threefold.Magic;
using Threefold.Pooling;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows the disciplines' magic on the Mage's body, timed to the moments of each attack: trails on the
    /// striking hand or foot, force gathering before a strike, concentrated energy at contact, embers left
    /// after it, the charge building in the hands, arms or chest as it grows, the release from the hand, a
    /// ribbon following the charged projectile, the wave or pulse travelling over a charged area, and the
    /// barrier held while blocking.
    ///
    /// The moments come from the <see cref="CombatAnimationDirector"/>'s cues, which follow the combat rules'
    /// clock, so effects line up with the animation and with the hit. Without an active director the same
    /// moments are taken from the combat events directly. What to show when is decided by
    /// <see cref="BodyEffectSequencer"/> (plain C#); this component only carries it out with pooled effects
    /// from the discipline's presentation. It deals no damage and changes no rule.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)] // After the combat animation director has sent this frame's cues.
    public sealed class DisciplineBodyEffects : MonoBehaviour
    {
        private const int MaxAttached = 4;
        private const int MaxBursts = 8;
        private const int MaxWaves = 3;
        private const int MaxRibbons = 4;

        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. Its cues time the effects to the animation. Found on the Mage when empty.")]
        [SerializeField] private CombatAnimationDirector director;

        [Tooltip("Optional. Where hands, forearms, feet and chest are. Found on the Mage when empty.")]
        [SerializeField] private DisciplineVfxSockets sockets;

        [Tooltip("Optional. Shows the discipline's barrier while blocking. Found on the Mage when empty.")]
        [SerializeField] private PlayerGuard guard;

        [Tooltip("Optional. Scales particle counts and trail lengths with the player's graphics preset.")]
        [SerializeField] private VisualStyleController style;

        [Tooltip("Strength of all magical effects, from 0.25 (restrained) to 1 (as authored). Gameplay information " +
                 "(contact, release, waves) is shown at every setting.")]
        [Range(MagicVfxQuality.MinIntensity, 1f)]
        [SerializeField] private float intensity = 1f;

        private readonly BodyEffectSequencer sequencer = new BodyEffectSequencer();
        private readonly List<BodyEffectCommand> commands = new List<BodyEffectCommand>(16);
        private readonly Handle[] trail = new Handle[2];
        private readonly Handle[] gather = new Handle[2];
        private readonly Handle[] charge = new Handle[2];
        private readonly Handle[] swingEffect = new Handle[1];
        private Handle barrier;
        private bool subscribed;

        private DisciplineCombat subscribedCombat;
        private CombatAnimationDirector subscribedDirector;
        private AbilityCaster subscribedCaster;
        private PlayerGuard subscribedGuard;
        private PlayerDisciplines subscribedDisciplines;
        private bool usingCues;
        private bool styleSearched;
        private int fallbackActionId;

        private MeleeSwing lastSwing;
        private bool hasSwing;

        // The swing the action being shown began with, kept apart from the latest swing: a long frame can start
        // the next swing before this one's contact is shown.
        private MeleeSwing actionSwing;
        private bool hasActionSwing;

        // The last swing that landed, with its hit count, until its contact is shown.
        private MeleeSwing landedSwing;
        private bool hasLandedSwing;
        private Vector3 releaseDirection = Vector3.forward;
        private int barrierSlot = -1;

        private struct Handle
        {
            public PooledObject Instance;
            public int Generation;

            public bool IsLive => Instance != null && Instance.IsSpawn(Generation);
        }

        /// <summary>The combat these effects show. Used by <see cref="DisciplineFeedback"/> to avoid showing the same effect twice.</summary>
        public DisciplineCombat Combat => combat;

        /// <summary>Strength of all magical effects, 0.25 to 1.</summary>
        public float Intensity
        {
            get => intensity;
            set => intensity = MagicVfxQuality.ClampIntensity(value);
        }

        /// <summary>The rules that decide what is shown, for diagnostics and tests.</summary>
        public BodyEffectSequencer Sequencer => sequencer;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(DisciplineCombat disciplineCombat, CombatAnimationDirector animationDirector,
            DisciplineVfxSockets vfxSockets, PlayerGuard playerGuard, VisualStyleController styleController)
        {
            bool resubscribe = subscribed;
            if (resubscribe)
            {
                Unsubscribe();
            }

            combat = disciplineCombat;
            director = animationDirector;
            sockets = vfxSockets;
            guard = playerGuard;
            style = styleController;

            // In edit mode (the setup command) nothing is subscribed: OnEnable does it when the game runs.
            if (resubscribe && Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed || combat == null)
            {
                return;
            }

            subscribed = true;

            if (director == null) director = combat.GetComponent<CombatAnimationDirector>();
            if (sockets == null) sockets = combat.GetComponentInChildren<DisciplineVfxSockets>(true);
            if (guard == null) guard = combat.GetComponent<PlayerGuard>();

            subscribedCombat = combat;
            subscribedCombat.SwingStarted += HandleSwingStarted;
            subscribedCombat.SwingLanded += HandleSwingLanded;
            subscribedCombat.SwingInterrupted += HandleSwingInterrupted;
            subscribedCombat.ChargeBegan += HandleChargeBegan;
            subscribedCombat.ChargeEnded += HandleChargeEnded;

            subscribedCaster = combat.Caster;
            if (subscribedCaster != null)
            {
                subscribedCaster.CastSucceeded += HandleCastSucceeded;
            }

            subscribedDirector = director;
            if (subscribedDirector != null)
            {
                subscribedDirector.Cue += HandleCue;
            }

            subscribedGuard = guard;
            if (subscribedGuard != null)
            {
                subscribedGuard.BlockStarted += HandleBlockStarted;
                subscribedGuard.BlockEnded += HandleBlockEnded;
            }

            subscribedDisciplines = combat.Disciplines;
            if (subscribedDisciplines != null)
            {
                subscribedDisciplines.Changed += HandleDisciplineChanged;
            }

            Projectile.Launched += HandleProjectileLaunched;
            usingCues = DirectorActive;
            styleSearched = false;

            if (subscribedGuard != null && subscribedGuard.IsBlocking)
            {
                HandleBlockStarted();
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            subscribed = false;
            commands.Clear();
            sequencer.Reset(commands);
            Execute();
            StopBarrier(0.05f);

            if (subscribedCombat != null)
            {
                subscribedCombat.SwingStarted -= HandleSwingStarted;
                subscribedCombat.SwingLanded -= HandleSwingLanded;
                subscribedCombat.SwingInterrupted -= HandleSwingInterrupted;
                subscribedCombat.ChargeBegan -= HandleChargeBegan;
                subscribedCombat.ChargeEnded -= HandleChargeEnded;
                subscribedCombat = null;
            }

            if (subscribedCaster != null)
            {
                subscribedCaster.CastSucceeded -= HandleCastSucceeded;
                subscribedCaster = null;
            }

            if (subscribedDirector != null)
            {
                subscribedDirector.Cue -= HandleCue;
                subscribedDirector = null;
            }

            if (subscribedGuard != null)
            {
                subscribedGuard.BlockStarted -= HandleBlockStarted;
                subscribedGuard.BlockEnded -= HandleBlockEnded;
                subscribedGuard = null;
            }

            if (subscribedDisciplines != null)
            {
                subscribedDisciplines.Changed -= HandleDisciplineChanged;
                subscribedDisciplines = null;
            }

            Projectile.Launched -= HandleProjectileLaunched;
            hasSwing = hasActionSwing = hasLandedSwing = false;
        }

        private void Update()
        {
            if (subscribedCombat == null)
            {
                return;
            }

            // The director can start or stop playing (its animation set assigned, its Animator replaced). An action
            // shown from one source is not finished from the other: everything stops and the next action starts clean.
            bool cues = DirectorActive;
            if (cues != usingCues)
            {
                usingCues = cues;
                commands.Clear();
                sequencer.Reset(commands);
                Execute();
            }

            float power = subscribedCombat.IsCharging ? subscribedCombat.Model.CurrentChargePower : 0f;
            commands.Clear();
            sequencer.Tick(Time.time, power, commands);
            Execute();
        }

        private bool DirectorActive => subscribedDirector != null && subscribedDirector.isActiveAndEnabled && subscribedDirector.IsActive;

        // -------------------------------------------------------------- sources

        private void HandleCue(CombatCue cue)
        {
            if (!usingCues)
            {
                return;
            }

            commands.Clear();
            if (cue.Kind == CombatCueKind.Windup)
            {
                sequencer.Begin(Describe(cue.Action, cue.ActionId, cue.Discipline, cue.StepIndex, cue.Effector), cue.Time,
                    commands);
            }
            else if (cue.Kind == CombatCueKind.Contact)
            {
                sequencer.Cue(cue.Kind, cue.ActionId, commands, HitsOf(cue.Discipline, cue.StepIndex));
            }
            else
            {
                sequencer.Cue(cue.Kind, cue.ActionId, commands);
            }

            Execute();
        }

        private void HandleSwingStarted(MeleeSwing swing)
        {
            lastSwing = swing;
            hasSwing = true;

            if (usingCues)
            {
                return;
            }

            // No animation timing: the strike's effect moment and hit window are taken as its start.
            int slot = swing.Discipline != null ? (int)swing.Discipline.Discipline : 0;
            fallbackActionId++;
            commands.Clear();
            sequencer.Begin(Describe(CombatActionKind.Melee, fallbackActionId, slot, swing.StepIndex, FallbackEffector(slot)),
                Time.time, commands);
            sequencer.Cue(CombatCueKind.Effect, fallbackActionId, commands);
            sequencer.Cue(CombatCueKind.HitWindowOpened, fallbackActionId, commands);
            Execute();
        }

        private void HandleSwingLanded(MeleeSwing swing)
        {
            // The rules announce the landing before the director sends its contact cue (it runs later in the frame).
            landedSwing = swing;
            hasLandedSwing = true;

            if (usingCues)
            {
                return;
            }

            commands.Clear();
            sequencer.Cue(CombatCueKind.Contact, fallbackActionId, commands, swing.HitCount);
            sequencer.Cue(CombatCueKind.HitWindowClosed, fallbackActionId, commands);
            sequencer.Cue(CombatCueKind.Recovered, fallbackActionId, commands);
            Execute();
        }

        private void HandleSwingInterrupted(MeleeSwing swing)
        {
            if (usingCues)
            {
                return;
            }

            commands.Clear();
            sequencer.Cue(CombatCueKind.Cancelled, fallbackActionId, commands);
            Execute();
        }

        private void HandleChargeBegan(DisciplineDefinition discipline)
        {
            if (usingCues)
            {
                return;
            }

            int slot = discipline != null ? (int)discipline.Discipline : 0;
            fallbackActionId++;
            commands.Clear();
            sequencer.Begin(Describe(CombatActionKind.Charged, fallbackActionId, slot, 0, FallbackEffector(slot)), Time.time,
                commands);
            Execute();
        }

        private void HandleChargeEnded(bool fired)
        {
            if (usingCues)
            {
                return;
            }

            commands.Clear();
            if (fired)
            {
                sequencer.Cue(CombatCueKind.Release, fallbackActionId, commands);
                sequencer.Cue(CombatCueKind.Recovered, fallbackActionId, commands);
            }
            else
            {
                sequencer.Cue(CombatCueKind.Cancelled, fallbackActionId, commands);
            }

            Execute();
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            if (!(ability is ChargedAbilityDefinition charged))
            {
                return;
            }

            if (context.Direction.sqrMagnitude > 0.0001f)
            {
                releaseDirection = context.Direction.normalized;
            }

            DisciplineDefinition definition = FindDiscipline(charged);
            if (definition == null || !(charged is ChargedAreaDefinition area))
            {
                return;
            }

            DisciplinePresentation look = definition.Presentation;
            if (look.areaWaveEffect == null)
            {
                return;
            }

            AreaShape shape = area.GetShape(subscribedCombat.transform.position, context.Direction, context.Charge);
            PooledObject wave = Spawn(look.areaWaveEffect, shape.Origin, LookAlong(shape.Forward), MaxWaves, true);
            if (wave != null && wave.TryGetComponent(out ArcEmitterEffect emitter))
            {
                emitter.Play(shape.Origin, shape.Forward, shape.Reach, shape.ArcDegrees, look.areaWaveSeconds,
                    definition.Color, CurrentLevel().EmitterRateScale);
            }
        }

        private void HandleProjectileLaunched(Projectile projectile)
        {
            if (subscribedCombat == null || projectile == null || projectile.Current.Owner != subscribedCombat.gameObject)
            {
                return;
            }

            DisciplineDefinition definition = Definition(projectile.Current.Discipline);
            PooledObject ribbonPrefab = definition != null ? definition.Presentation.projectileTrailEffect : null;
            if (ribbonPrefab == null || !projectile.TryGetComponent(out PooledObject body))
            {
                return;
            }

            PooledObject ribbon = Spawn(ribbonPrefab, projectile.transform.position, projectile.transform.rotation,
                MaxRibbons, true);
            if (ribbon != null && ribbon.TryGetComponent(out PooledEffect effect))
            {
                effect.Follow(body, Mathf.Max(0.1f, effect.LongestTrailSeconds));
            }
        }

        private void HandleBlockStarted()
        {
            StopBarrier(0.05f);
            DisciplineDefinition definition = subscribedDisciplines != null ? subscribedDisciplines.CurrentDefinition : null;
            PooledObject prefab = definition != null ? definition.Presentation.blockBarrierEffect : null;
            if (prefab == null || subscribedCombat == null)
            {
                return;
            }

            Transform body = subscribedCombat.transform;
            Vector3 point = body.position + body.forward * 0.55f + Vector3.up * 1.05f;
            PooledObject instance = Spawn(prefab, point, LookAlong(body.forward), 2, true);
            if (instance != null)
            {
                instance.transform.SetParent(body, true);
                barrier = new Handle { Instance = instance, Generation = instance.Generation };
                barrierSlot = (int)definition.Discipline;
            }
        }

        private void HandleBlockEnded(float heldSeconds)
        {
            StopBarrier(0.15f);
        }

        private void HandleDisciplineChanged(MagicDiscipline discipline)
        {
            // The barrier is the selected discipline's: switching while blocking shows the new one.
            if (subscribedGuard != null && subscribedGuard.IsBlocking && barrierSlot != (int)discipline)
            {
                HandleBlockStarted();
            }
        }

        // ------------------------------------------------------------ commands

        private void Execute()
        {
            for (int i = 0; i < commands.Count; i++)
            {
                BodyEffectCommand command = commands[i];
                switch (command.Kind)
                {
                    case BodyEffectCommandKind.Start when command.Layer == BodyEffectLayer.Swing:
                        swingEffect[0] = PlaySwing(command);
                        break;
                    case BodyEffectCommandKind.Stop when command.Layer == BodyEffectLayer.Swing:
                        if (command.DrainSeconds < 0f)
                        {
                            swingEffect[0] = default; // not cut short: it finishes on its own
                        }
                        else
                        {
                            StopLayer(swingEffect, command.DrainSeconds, false);
                        }

                        break;
                    case BodyEffectCommandKind.Start:
                        StartLayer(command);
                        break;
                    case BodyEffectCommandKind.SetIntensity:
                        SetLayerIntensity(command);
                        break;
                    case BodyEffectCommandKind.Stop:
                        StopLayer(Layer(command.Layer), command.DrainSeconds, command.Layer == BodyEffectLayer.Trail);
                        break;
                    case BodyEffectCommandKind.Burst:
                        PlayBurst(command);
                        break;
                }
            }

            commands.Clear();
        }

        private void StartLayer(in BodyEffectCommand command)
        {
            DisciplineDefinition definition = Definition(command.Discipline);
            if (definition == null)
            {
                return;
            }

            DisciplinePresentation look = definition.Presentation;
            MagicVfxLevel level = CurrentLevel();
            Handle[] handles = Layer(command.Layer);
            StopLayer(handles, 0.02f, false);

            PooledObject prefab;
            Transform first;
            Transform second;
            int count;

            switch (command.Layer)
            {
                case BodyEffectLayer.Trail:
                    prefab = look.limbTrailEffect;
                    count = ResolveLimb(command.Effector, out first, out second);
                    break;

                case BodyEffectLayer.Gather:
                    prefab = level.Secondaries ? look.gatherEffect : null;
                    count = ResolveLimb(command.Effector, out first, out second);
                    break;

                default:
                    prefab = look.chargeLoopEffect;
                    count = ResolveCharge(Timing(definition).chargePlacement, command.Effector, out first, out second);
                    break;
            }

            if (prefab == null || count == 0)
            {
                return;
            }

            handles[0] = Attach(prefab, first, level);
            if (count > 1)
            {
                handles[1] = Attach(prefab, second, level);
            }

            ApplyIntensity(handles, command.Intensity, command.Layer == BodyEffectLayer.Charge ? command.Radius : -1f);
        }

        private void SetLayerIntensity(in BodyEffectCommand command)
        {
            ApplyIntensity(Layer(command.Layer), command.Intensity, command.Layer == BodyEffectLayer.Charge ? command.Radius : -1f);
        }

        private void PlayBurst(in BodyEffectCommand command)
        {
            DisciplineDefinition definition = Definition(command.Discipline);
            if (definition == null || subscribedCombat == null)
            {
                return;
            }

            DisciplinePresentation look = definition.Presentation;
            Transform body = subscribedCombat.transform;
            Vector3 swingDirection = hasActionSwing ? actionSwing.Direction : body.forward;

            switch (command.Burst)
            {
                case BodyEffectBurst.Contact:
                    Spawn(look.contactEffect, LimbPoint(command.Effector), LookAlong(swingDirection), MaxBursts, false);
                    break;

                case BodyEffectBurst.HeavyContact:
                {
                    PooledObject heavy = look.heavyContactEffect != null ? look.heavyContactEffect : look.contactEffect;
                    float reach = hasActionSwing && actionSwing.Step != null ? actionSwing.Step.reach : 2f;
                    Vector3 ground = body.position + Flat(swingDirection) * Mathf.Min(reach * 0.45f, 1.2f) + Vector3.up * 0.05f;
                    Spawn(heavy, ground, LookAlong(swingDirection), MaxBursts, false);
                    if (look.contactEffect != null && heavy != look.contactEffect)
                    {
                        Spawn(look.contactEffect, LimbPoint(command.Effector), LookAlong(swingDirection), MaxBursts, false);
                    }

                    break;
                }

                case BodyEffectBurst.Linger:
                    if (CurrentLevel().Secondaries)
                    {
                        Spawn(look.lingerEffect, LimbPoint(command.Effector), Quaternion.identity, MaxBursts, false);
                    }

                    break;

                case BodyEffectBurst.Release:
                {
                    ResolveCharge(ChargePlacement.Effector, command.Effector, out Transform first, out Transform second);
                    Vector3 point = second != null ? (first.position + second.position) * 0.5f
                        : first != null ? first.position : body.position + Vector3.up * look.effectHeight;
                    Vector3 direction = releaseDirection.sqrMagnitude > 0.0001f ? releaseDirection : body.forward;
                    Spawn(look.releaseEffect, point, Quaternion.LookRotation(direction, Vector3.up), MaxBursts, false);
                    break;
                }
            }
        }

        private Handle PlaySwing(in BodyEffectCommand command)
        {
            DisciplineDefinition definition = Definition(command.Discipline);
            if (definition == null || subscribedCombat == null || definition.Presentation.meleeSwingEffect == null)
            {
                return default;
            }

            DisciplinePresentation look = definition.Presentation;
            Transform body = subscribedCombat.transform;
            Vector3 swingDirection = hasActionSwing ? actionSwing.Direction : body.forward;
            Vector3 origin = body.position + Vector3.up * Mathf.Max(0f, look.effectHeight);
            PooledObject instance = Spawn(look.meleeSwingEffect, origin, LookAlong(swingDirection), MaxBursts, false);
            if (instance == null)
            {
                return default;
            }

            if (instance.TryGetComponent(out ArcEmitterEffect ribbon) && hasActionSwing && actionSwing.Step != null)
            {
                // A right-hand strike sweeps from right to left in front of the body; a left-hand one the other way.
                bool reverse = command.Effector == CombatEffector.RightHand || command.Effector == CombatEffector.RightFoot;
                float seconds = Mathf.Max(0.08f, actionSwing.Step.startupSeconds + 0.12f);
                ribbon.Play(origin, swingDirection, actionSwing.Step.reach, actionSwing.Step.arcDegrees, seconds,
                    definition.Color, CurrentLevel().EmitterRateScale, reverse);
            }

            return new Handle { Instance = instance, Generation = instance.Generation };
        }

        // ---------------------------------------------------------- helpers

        private BodyActionInfo Describe(CombatActionKind action, int actionId, int slot, int stepIndex, CombatEffector effector)
        {
            DisciplineDefinition definition = Definition(slot);
            var info = new BodyActionInfo
            {
                Action = action,
                ActionId = actionId,
                Discipline = slot,
                StepIndex = stepIndex,
                Effector = effector,
                Timing = definition != null ? Timing(definition) : MagicVfxDefaults.For((MagicDiscipline)Mathf.Max(0, slot)),
            };

            hasActionSwing = false;
            if (action == CombatActionKind.Melee && hasSwing && lastSwing.Step != null && lastSwing.StepIndex == stepIndex &&
                lastSwing.Discipline == definition)
            {
                info.SecondsToContact = lastSwing.Step.startupSeconds;
                info.Heavy = lastSwing.Step.hitTrauma >= info.Timing.heavyHitTrauma;
                actionSwing = lastSwing;
                hasActionSwing = true;
            }

            return info;
        }

        /// <summary>How many targets the landed swing of this discipline and step hit, or -1 when it is not known.</summary>
        private int HitsOf(int slot, int stepIndex)
        {
            if (!hasLandedSwing || landedSwing.StepIndex != stepIndex || landedSwing.Discipline != Definition(slot))
            {
                return -1;
            }

            hasLandedSwing = false;
            return landedSwing.HitCount;
        }

        private static BodyEffectTiming Timing(DisciplineDefinition definition)
        {
            return MagicVfxDefaults.Resolve(definition.Presentation.bodyTiming, definition.Discipline);
        }

        private DisciplineDefinition Definition(int slot)
        {
            PlayerDisciplines disciplines = subscribedCombat != null ? subscribedCombat.Disciplines : null;
            return disciplines != null ? disciplines.GetDefinition(slot) : null;
        }

        private DisciplineDefinition FindDiscipline(ChargedAbilityDefinition ability)
        {
            PlayerDisciplines disciplines = subscribedCombat != null ? subscribedCombat.Disciplines : null;
            if (disciplines == null)
            {
                return null;
            }

            for (int i = 0; i < disciplines.Count; i++)
            {
                DisciplineDefinition definition = disciplines.GetDefinition(i);
                if (definition != null && definition.ChargedAbility == ability)
                {
                    return definition;
                }
            }

            return null;
        }

        /// <summary>Without the director's slot data, strikes are shown on the right hand and charges on the left.</summary>
        private static CombatEffector FallbackEffector(int slot)
        {
            return slot == (int)MagicDiscipline.Aegis ? CombatEffector.BothHands : CombatEffector.RightHand;
        }

        private Handle[] Layer(BodyEffectLayer layer)
        {
            switch (layer)
            {
                case BodyEffectLayer.Trail:
                    return trail;
                case BodyEffectLayer.Gather:
                    return gather;
                case BodyEffectLayer.Swing:
                    return swingEffect;
                default:
                    return charge;
            }
        }

        private int ResolveLimb(CombatEffector effector, out Transform first, out Transform second)
        {
            if (sockets == null)
            {
                first = second = null;
                return 0;
            }

            return sockets.Resolve(effector, out first, out second);
        }

        private int ResolveCharge(ChargePlacement placement, CombatEffector effector, out Transform first, out Transform second)
        {
            first = second = null;
            if (sockets == null)
            {
                return 0;
            }

            switch (placement)
            {
                case ChargePlacement.BothForearms:
                    first = sockets.LeftForearm;
                    second = sockets.RightForearm;
                    return 2;
                case ChargePlacement.Chest:
                    first = sockets.Chest;
                    return 1;
                default:
                    return sockets.Resolve(effector == CombatEffector.None ? CombatEffector.RightHand : effector,
                        out first, out second);
            }
        }

        private Vector3 LimbPoint(CombatEffector effector)
        {
            int count = ResolveLimb(effector, out Transform first, out Transform second);
            if (count == 2 && first != null && second != null)
            {
                return (first.position + second.position) * 0.5f;
            }

            if (count >= 1 && first != null)
            {
                return first.position;
            }

            Transform body = subscribedCombat != null ? subscribedCombat.transform : transform;
            return body.position + Vector3.up * 1.15f + body.forward * 0.4f;
        }

        private Handle Attach(PooledObject prefab, Transform socket, in MagicVfxLevel level)
        {
            if (socket == null)
            {
                return default;
            }

            Vector3 position = socket.position;
            Transform body = subscribedCombat != null ? subscribedCombat.transform : null;

            // A chest charge sits in front of the body, between the hands.
            if (body != null && socket == (sockets != null ? sockets.Chest : null))
            {
                position += body.forward * 0.32f;
            }

            PooledObject instance = Spawn(prefab, position, socket.rotation, MaxAttached, false);
            if (instance == null)
            {
                return default;
            }

            instance.transform.SetParent(socket, true);
            if (instance.TryGetComponent(out PooledEffect effect))
            {
                effect.SetTrailTimeScale(level.TrailTimeScale);
            }

            return new Handle { Instance = instance, Generation = instance.Generation };
        }

        private static void ApplyIntensity(Handle[] handles, float value, float radius)
        {
            for (int i = 0; i < handles.Length; i++)
            {
                if (!handles[i].IsLive || !handles[i].Instance.TryGetComponent(out PooledEffect effect))
                {
                    continue;
                }

                effect.SetIntensity(value);
                if (radius >= 0f)
                {
                    effect.SetShapeRadius(radius);
                }
            }
        }

        private static void StopLayer(Handle[] handles, float drainSeconds, bool letTrailsFade)
        {
            for (int i = 0; i < handles.Length; i++)
            {
                Stop(ref handles[i], drainSeconds, letTrailsFade);
            }
        }

        private void StopBarrier(float drainSeconds)
        {
            Stop(ref barrier, drainSeconds, false);
            barrierSlot = -1;
        }

        private static void Stop(ref Handle handle, float drainSeconds, bool letTrailsFade)
        {
            if (handle.IsLive)
            {
                if (handle.Instance.TryGetComponent(out PooledEffect effect))
                {
                    float drain = letTrailsFade ? Mathf.Max(drainSeconds, effect.LongestTrailSeconds) : drainSeconds;
                    effect.StopEarly(drain);
                }
                else
                {
                    handle.Instance.ReturnToPool();
                }
            }

            handle = default;
        }

        private PooledObject Spawn(PooledObject prefab, Vector3 position, Quaternion rotation, int maxInstances, bool keepFull)
        {
            if (prefab == null)
            {
                return null;
            }

            PooledObject instance = Pools.Spawn(prefab, position, rotation, maxInstances);
            if (instance == null)
            {
                return null;
            }

            MagicVfxLevel level = CurrentLevel();
            if (!keepFull)
            {
                // Scripted emitters (waves, ribbons) take their rate from the level instead.
                BurstParticleShare.Apply(instance, level.BurstShare);
            }

            if (instance.TryGetComponent(out PooledEffect effect))
            {
                effect.SetTrailTimeScale(level.TrailTimeScale);
            }

            return instance;
        }

        private MagicVfxLevel CurrentLevel()
        {
            if (style == null && !styleSearched)
            {
                styleSearched = true;
                style = FindAnyObjectByType<VisualStyleController>();
            }

            return style != null
                ? MagicVfxQuality.For(style.QualityLevel, style.QualityLevelCount, intensity)
                : MagicVfxQuality.For(0, 0, intensity);
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        private static Quaternion LookAlong(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        }
    }
}
