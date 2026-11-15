using Threefold.Cameras;
using Threefold.Characters;
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
    /// Shows the disciplines' attacks: oriented melee slashes, the ground arc of each swing, a burst
    /// on every target struck, charge buildup at the hands, the sweep of a Flame Wave or Force Pulse,
    /// embers on a burning target, a spark where the guard stops or deflects a hit, light camera shake,
    /// and the attack animation where the character has one. While a <see cref="CombatAnimationDirector"/>
    /// plays the combat animations, the animation triggers here are left alone, and a charge glows in the
    /// hand the discipline's animation holds it in.
    ///
    /// It listens to <see cref="DisciplineCombat"/>, the caster, the guard and the combat events and
    /// changes nothing in the rules. Effects are pooled and short. Interrupted swings and cancelled
    /// charges stop their looping or in-flight presentation so nothing keeps playing after the action.
    ///
    /// When a <see cref="DisciplineBodyEffects"/> on the same object shows the same combat, the effects on the
    /// body (the swing effect, the charge buildup and the release) are left to it, timed by the animation cues,
    /// so none is shown twice. The ground arcs, the impacts on what was struck, the status effects, the guard
    /// spark and the camera shake stay here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisciplineFeedback : MonoBehaviour
    {
        private const int MaxSweeps = 6;
        private const int MaxImpacts = 10;
        private const int MaxStatusEffects = 8;
        private const int MaxSwingEffects = 4;
        private const int MaxChargeEffects = 2;

        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. Shaken a little when a swing connects or a charged attack is released.")]
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Tooltip("Optional. Receives the attack animation triggers.")]
        [SerializeField] private CharacterAnimationDriver animationDriver;

        [Tooltip("Optional. Hand sockets for charge loops. Resolved from the Mage when empty.")]
        [SerializeField] private DisciplineVfxSockets sockets;

        [Tooltip("Optional. Says which hand holds a charge. Resolved from the Mage when empty.")]
        [SerializeField] private CombatAnimationDirector animationDirector;

        [Tooltip("Optional. A spark is shown where the guard stops a hit.")]
        [SerializeField] private PlayerGuard guard;

        [Tooltip("Optional. Scales particle counts with the player's graphics preset.")]
        [SerializeField] private VisualStyleController style;

        [Header("Shared effects")]
        [Tooltip("Small burst of embers on a target that ignites and on each burn tick.")]
        [SerializeField] private PooledObject burnEffect;

        [Tooltip("Burst on a target that is shoved by a force push.")]
        [SerializeField] private PooledObject pushEffect;

        [Tooltip("Spark where the guard stops a hit.")]
        [SerializeField] private PooledObject guardEffect;

        [Header("Look")]
        [Tooltip("Opacity of the swing arc on the ground.")]
        [Range(0f, 1f)]
        [SerializeField] private float swingOpacity = 0.42f;

        [Tooltip("Opacity of the wave or pulse on the ground.")]
        [Range(0f, 1f)]
        [SerializeField] private float areaOpacity = 0.5f;

        [Tooltip("Seconds a wave or pulse stays visible.")]
        [Min(0.05f)]
        [SerializeField] private float areaSeconds = 0.38f;

        [Tooltip("Camera shake when the guard stops a hit, 0 to 1.")]
        [Range(0f, 1f)]
        [SerializeField] private float blockTrauma = 0.14f;

        private DisciplineCombat subscribedCombat;
        private AbilityCaster subscribedCaster;
        private PlayerGuard subscribedGuard;
        private PooledObject activeSwing;
        private PooledObject activeCharge;
        private DisciplineBodyEffects bodyEffects;
        private bool styleSearched;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(DisciplineCombat disciplineCombat, ThirdPersonCamera rig, CharacterAnimationDriver driver,
            PlayerGuard playerGuard)
        {
            combat = disciplineCombat;
            cameraRig = rig;
            animationDriver = driver;
            guard = playerGuard;
        }

        /// <summary>Connects the effects shared by all disciplines. Called by the setup tooling.</summary>
        public void BindEffects(PooledObject burn, PooledObject push, PooledObject guardSpark)
        {
            burnEffect = burn;
            pushEffect = push;
            guardEffect = guardSpark;
        }

        /// <summary>Optional sockets and style. Called by the setup tooling.</summary>
        public void BindPresentation(DisciplineVfxSockets vfxSockets, VisualStyleController styleController)
        {
            sockets = vfxSockets;
            style = styleController;
        }

        /// <summary>
        /// Connects only the shared effects that are not set, leaving the others as they are. Returns
        /// true if anything changed.
        /// </summary>
        public bool FillEffects(PooledObject burn, PooledObject push, PooledObject guardSpark)
        {
            bool changed = false;

            if (burnEffect == null && burn != null)
            {
                burnEffect = burn;
                changed = true;
            }

            if (pushEffect == null && push != null)
            {
                pushEffect = push;
                changed = true;
            }

            if (guardEffect == null && guardSpark != null)
            {
                guardEffect = guardSpark;
                changed = true;
            }

            return changed;
        }

        /// <summary>Replaces the burn effect. Used by setup when a richer prefab should win over SoftGlow.</summary>
        public bool ReplaceBurnEffect(PooledObject burn)
        {
            if (burn == null || burnEffect == burn)
            {
                return false;
            }

            burnEffect = burn;
            return true;
        }

        /// <summary>Replaces the guard spark. Used by setup when a richer prefab should win over SoftGlow.</summary>
        public bool ReplaceGuardEffect(PooledObject guardSpark)
        {
            if (guardSpark == null || guardEffect == guardSpark)
            {
                return false;
            }

            guardEffect = guardSpark;
            return true;
        }

        public DisciplineCombat Combat => combat;
        public PooledObject BurnEffect => burnEffect;
        public PooledObject GuardEffect => guardEffect;

        private void OnEnable()
        {
            bodyEffects = GetComponent<DisciplineBodyEffects>();
            styleSearched = false;
            subscribedCombat = combat;
            if (subscribedCombat != null)
            {
                subscribedCombat.SwingStarted += HandleSwingStarted;
                subscribedCombat.SwingLanded += HandleSwingLanded;
                subscribedCombat.SwingInterrupted += HandleSwingInterrupted;
                subscribedCombat.ChargeBegan += HandleChargeBegan;
                subscribedCombat.ChargeEnded += HandleChargeEnded;
                subscribedCaster = subscribedCombat.Caster;
                if (subscribedCaster != null)
                {
                    subscribedCaster.CastSucceeded += HandleCastSucceeded;
                }
            }

            subscribedGuard = guard;
            if (subscribedGuard != null)
            {
                subscribedGuard.Blocked += HandleBlocked;
            }

            CombatEvents.Status += HandleStatus;
        }

        private void OnDisable()
        {
            StopSwingEffect();
            StopChargeEffect();

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

            if (subscribedGuard != null)
            {
                subscribedGuard.Blocked -= HandleBlocked;
                subscribedGuard = null;
            }

            CombatEvents.Status -= HandleStatus;
        }

        // --------------------------------------------------------------- melee

        private void HandleSwingStarted(MeleeSwing swing)
        {
            DisciplineDefinition discipline = swing.Discipline;
            if (discipline == null || swing.Step == null)
            {
                return;
            }

            DisciplinePresentation look = discipline.Presentation;

            Color color = discipline.Color;
            color.a = swingOpacity;
            PlaySweep(look.sweepEffect, swing.Origin, swing.Direction, swing.Step.reach, swing.Step.arcDegrees, color,
                swing.Step.startupSeconds + 0.14f);

            StopSwingEffect();
            if (look.meleeSwingEffect != null && !BodyEffectsShown)
            {
                Vector3 point = EffectPoint(swing.Origin, look.effectHeight);
                Quaternion rotation = LookAlong(swing.Direction);
                activeSwing = SpawnShared(look.meleeSwingEffect, point, rotation, MaxSwingEffects);

                // A ribbon swept by script needs its arc; it is stopped like any swing effect when the hit lands.
                if (activeSwing != null && activeSwing.TryGetComponent(out ArcEmitterEffect ribbon))
                {
                    ribbon.Play(point, swing.Direction, swing.Step.reach, swing.Step.arcDegrees,
                        swing.Step.startupSeconds + 0.12f, discipline.Color, CurrentBurstShare());
                }
            }

            // The combat animation director plays the swing itself when it is active.
            if (animationDriver != null && !animationDriver.CombatAnimationsActive &&
                !animationDriver.TrySetTrigger(swing.Step.animationTrigger) && look.castGestureForMelee)
            {
                animationDriver.TrySetTrigger(CharacterAnimationDriver.CastParameter);
            }
        }

        private void HandleSwingLanded(MeleeSwing swing)
        {
            // The slash is a start-up beat; once the hit lands it should not linger into recovery.
            StopSwingEffect(0.05f);

            if (swing.HitCount <= 0 || swing.Discipline == null)
            {
                return;
            }

            PooledObject impact = swing.Discipline.Presentation.meleeImpactEffect;
            if (impact != null && subscribedCombat != null)
            {
                var struck = subscribedCombat.LastStruck;
                for (int i = 0; i < struck.Count; i++)
                {
                    if (struck[i] != null)
                    {
                        // Along the swing, so a directional burst points the way the blow travelled.
                        SpawnShared(impact, struck[i].AimPoint, LookAlong(swing.Direction), MaxImpacts);
                    }
                }
            }

            if (cameraRig != null && swing.Step != null)
            {
                cameraRig.AddTrauma(swing.Step.hitTrauma);
            }
        }

        private void HandleSwingInterrupted(MeleeSwing swing)
        {
            StopSwingEffect();
        }

        // ------------------------------------------------------------- charged

        private void HandleChargeBegan(DisciplineDefinition discipline)
        {
            StopChargeEffect();
            if (discipline == null || discipline.Presentation.chargeLoopEffect == null || BodyEffectsShown)
            {
                return;
            }

            DisciplinePresentation look = discipline.Presentation;
            Vector3 point = HandsPoint(look.effectHeight);
            activeCharge = SpawnShared(look.chargeLoopEffect, point, Quaternion.identity, MaxChargeEffects);

            DisciplineVfxSockets hands = ResolveSockets();
            if (activeCharge != null && hands != null)
            {
                activeCharge.transform.SetParent(ChargeHand(discipline, hands), true);
                activeCharge.transform.localPosition = Vector3.zero;
                activeCharge.transform.localRotation = Quaternion.identity;
            }
        }

        private void HandleChargeEnded(bool fired)
        {
            // Release FX are spawned from CastSucceeded when the shot actually fires.
            StopChargeEffect(fired ? 0.04f : 0.1f);
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            if (!(ability is ChargedAbilityDefinition charged) || subscribedCombat == null)
            {
                return;
            }

            DisciplineDefinition discipline = FindDiscipline(charged);
            if (discipline == null)
            {
                return;
            }

            DisciplinePresentation look = discipline.Presentation;
            Transform body = subscribedCombat.transform;

            if (charged is ChargedAreaDefinition area)
            {
                AreaShape shape = area.GetShape(body.position, context.Direction, context.Charge);
                Color color = discipline.Color;
                color.a = areaOpacity;
                PlaySweep(look.sweepEffect, shape.Origin, shape.Forward, shape.Reach, shape.ArcDegrees, color, areaSeconds);
            }

            if (look.releaseEffect != null && !BodyEffectsShown)
            {
                Vector3 origin = look.chargeLoopEffect != null ? HandsPoint(look.effectHeight) : context.Origin;
                SpawnShared(look.releaseEffect, origin, Quaternion.LookRotation(context.Direction), MaxImpacts);
            }

            if (cameraRig != null)
            {
                cameraRig.AddTrauma(charged.Charge.releaseTrauma * Mathf.Lerp(0.6f, 1f, context.Charge));
            }

            if (animationDriver != null && !animationDriver.CombatAnimationsActive &&
                look.chargedAnimationTrigger != CharacterAnimationDriver.CastParameter)
            {
                animationDriver.TrySetTrigger(look.chargedAnimationTrigger);
            }
        }

        private DisciplineDefinition FindDiscipline(ChargedAbilityDefinition ability)
        {
            PlayerDisciplines disciplines = subscribedCombat.Disciplines;
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

        // -------------------------------------------------------------- status

        private void HandleStatus(StatusEvent status)
        {
            switch (status.Kind)
            {
                case StatusKind.Ignited:
                case StatusKind.BurnTick:
                    if (burnEffect != null)
                    {
                        SpawnShared(burnEffect, status.Point, Quaternion.identity, MaxStatusEffects);
                    }

                    break;

                case StatusKind.Pushed:
                case StatusKind.Knocked:
                case StatusKind.ArmourCracked:
                    if (pushEffect != null)
                    {
                        SpawnShared(pushEffect, status.Point, Quaternion.identity, MaxStatusEffects);
                    }

                    break;

                case StatusKind.Deflected:
                case StatusKind.WardDisrupted:
                case StatusKind.WardRestored:
                    SpawnDeflectOrGuard(status.Point);

                    if (status.Kind == StatusKind.Deflected && cameraRig != null && combat != null &&
                        status.Target == combat.gameObject)
                    {
                        cameraRig.AddTrauma(blockTrauma);
                    }

                    break;

                case StatusKind.ArmourBroken:
                    if (pushEffect != null)
                    {
                        SpawnShared(pushEffect, status.Point, Quaternion.identity, MaxStatusEffects);
                    }

                    SpawnDeflectOrGuard(status.Point);
                    break;
            }
        }

        private void HandleBlocked(BlockedHit hit)
        {
            SpawnDeflectOrGuard(hit.Point);

            if (cameraRig != null)
            {
                cameraRig.AddTrauma(blockTrauma);
            }
        }

        private void SpawnDeflectOrGuard(Vector3 point)
        {
            PooledObject deflect = null;
            if (subscribedCombat != null && subscribedCombat.Disciplines != null)
            {
                DisciplineDefinition current = subscribedCombat.Disciplines.CurrentDefinition;
                if (current != null)
                {
                    deflect = current.Presentation.deflectEffect;
                }
            }

            PooledObject prefab = deflect != null ? deflect : guardEffect;
            if (prefab != null)
            {
                SpawnShared(prefab, point, Quaternion.identity, MaxStatusEffects);
            }
        }

        // --------------------------------------------------------------- helpers

        private PooledObject SpawnShared(PooledObject prefab, Vector3 position, Quaternion rotation, int maxInstances)
        {
            PooledObject instance = Pools.Spawn(prefab, position, rotation, maxInstances);
            if (instance == null)
            {
                return null;
            }

            float share = CurrentBurstShare();
            if (share < 0.999f)
            {
                BurstParticleShare.Apply(instance, share);
            }

            return instance;
        }

        /// <summary>True while a body-effects component shows the swing, charge and release of this combat.</summary>
        private bool BodyEffectsShown => bodyEffects != null && bodyEffects.isActiveAndEnabled && bodyEffects.Combat == combat;

        private float CurrentBurstShare()
        {
            if (style == null && !styleSearched)
            {
                styleSearched = true;
                style = FindAnyObjectByType<VisualStyleController>();
            }

            if (style == null || style.QualityLevelCount <= 0)
            {
                return 1f;
            }

            return MagicVfxQuality.BurstShare(style.QualityLevel, style.QualityLevelCount);
        }

        private static void PlaySweep(PooledObject prefab, Vector3 origin, Vector3 direction, float reach, float arcDegrees,
            Color color, float seconds)
        {
            if (prefab == null)
            {
                return;
            }

            direction.y = 0f;
            Quaternion rotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
            PooledObject instance = Pools.Spawn(prefab, origin, rotation, MaxSweeps);

            if (instance != null && instance.TryGetComponent(out SweepEffect sweep))
            {
                sweep.Play(reach, arcDegrees, color, seconds);
            }
        }

        private void StopSwingEffect(float drainSeconds = 0.08f)
        {
            StopTracked(ref activeSwing, drainSeconds);
        }

        private void StopChargeEffect(float drainSeconds = 0.1f)
        {
            StopTracked(ref activeCharge, drainSeconds);
        }

        private static void StopTracked(ref PooledObject tracked, float drainSeconds)
        {
            if (tracked == null)
            {
                return;
            }

            if (tracked.TryGetComponent(out PooledEffect effect))
            {
                effect.StopEarly(drainSeconds);
            }
            else if (tracked.isActiveAndEnabled)
            {
                tracked.ReturnToPool();
            }

            tracked = null;
        }

        private Vector3 EffectPoint(Vector3 origin, float height)
        {
            return origin + Vector3.up * Mathf.Max(0f, height);
        }

        private Vector3 HandsPoint(float height)
        {
            DisciplineVfxSockets hands = ResolveSockets();
            if (hands != null)
            {
                return hands.HandsMidpoint;
            }

            if (subscribedCombat != null)
            {
                return EffectPoint(subscribedCombat.transform.position, height);
            }

            return EffectPoint(transform.position, height);
        }

        /// <summary>
        /// The hand a charge glows in: the one the discipline's charge animation holds it in, or the right hand
        /// when no animation says.
        /// </summary>
        private Transform ChargeHand(DisciplineDefinition discipline, DisciplineVfxSockets hands)
        {
            if (animationDirector == null && subscribedCombat != null)
            {
                animationDirector = subscribedCombat.GetComponent<CombatAnimationDirector>();
            }

            if (animationDirector != null && animationDirector.IsActive &&
                animationDirector.GetChargeEffector(discipline.Discipline) == CombatEffector.LeftHand)
            {
                return hands.LeftHand;
            }

            return hands.RightHand;
        }

        private DisciplineVfxSockets ResolveSockets()
        {
            if (sockets != null)
            {
                return sockets;
            }

            if (subscribedCombat != null)
            {
                // Not '??': a missing component compares equal to null only through Unity's own operator.
                sockets = subscribedCombat.GetComponentInChildren<DisciplineVfxSockets>(true);
            }

            return sockets;
        }

        private static Quaternion LookAlong(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        }
    }
}
