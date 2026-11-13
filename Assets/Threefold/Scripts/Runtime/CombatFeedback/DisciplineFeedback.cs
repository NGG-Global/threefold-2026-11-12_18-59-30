using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.Magic;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows the disciplines' attacks: the arc of each melee swing, a burst on every target struck,
    /// the sweep of a Flame Wave or Force Pulse, embers on a burning target, a spark where the guard
    /// stops a hit, a little camera shake, and the attack animation where the character has one.
    ///
    /// It listens to <see cref="DisciplineCombat"/>, the caster, the guard and the combat events and
    /// changes nothing in the rules. Effects are pooled, short, unlit and few: a handful of soft
    /// particles and one flat shape per attack, tinted with the discipline's colour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisciplineFeedback : MonoBehaviour
    {
        private const int MaxSweeps = 6;
        private const int MaxImpacts = 10;
        private const int MaxStatusEffects = 8;

        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. Shaken a little when a swing connects or a charged attack is released.")]
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Tooltip("Optional. Receives the attack animation triggers.")]
        [SerializeField] private CharacterAnimationDriver animationDriver;

        [Tooltip("Optional. A spark is shown where the guard stops a hit.")]
        [SerializeField] private PlayerGuard guard;

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

        public DisciplineCombat Combat => combat;
        public PooledObject BurnEffect => burnEffect;

        private void OnEnable()
        {
            subscribedCombat = combat;
            if (subscribedCombat != null)
            {
                subscribedCombat.SwingStarted += HandleSwingStarted;
                subscribedCombat.SwingLanded += HandleSwingLanded;

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
            if (subscribedCombat != null)
            {
                subscribedCombat.SwingStarted -= HandleSwingStarted;
                subscribedCombat.SwingLanded -= HandleSwingLanded;
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

            // The arc sweeps out during the start-up, so its edge arrives as the hit lands.
            Color color = discipline.Color;
            color.a = swingOpacity;
            PlaySweep(look.sweepEffect, swing.Origin, swing.Direction, swing.Step.reach, swing.Step.arcDegrees, color,
                swing.Step.startupSeconds + 0.14f);

            if (animationDriver != null && !animationDriver.TrySetTrigger(swing.Step.animationTrigger) &&
                look.castGestureForMelee)
            {
                animationDriver.TrySetTrigger(CharacterAnimationDriver.CastParameter);
            }
        }

        private void HandleSwingLanded(MeleeSwing swing)
        {
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
                        Pools.Spawn(impact, struck[i].AimPoint, Quaternion.identity, MaxImpacts);
                    }
                }
            }

            if (cameraRig != null && swing.Step != null)
            {
                cameraRig.AddTrauma(swing.Step.hitTrauma);
            }
        }

        // ------------------------------------------------------------- charged

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

            if (look.releaseEffect != null)
            {
                Pools.Spawn(look.releaseEffect, context.Origin, Quaternion.LookRotation(context.Direction), MaxImpacts);
            }

            if (cameraRig != null)
            {
                cameraRig.AddTrauma(charged.Charge.releaseTrauma * Mathf.Lerp(0.6f, 1f, context.Charge));
            }

            // The caster's own cast event already plays the casting gesture; a different trigger is set here.
            if (animationDriver != null && look.chargedAnimationTrigger != CharacterAnimationDriver.CastParameter)
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
                        Pools.Spawn(burnEffect, status.Point, Quaternion.identity, MaxStatusEffects);
                    }

                    break;

                case StatusKind.Pushed:
                    if (pushEffect != null)
                    {
                        Pools.Spawn(pushEffect, status.Point, Quaternion.identity, MaxStatusEffects);
                    }

                    break;
            }
        }

        private void HandleBlocked(BlockedHit hit)
        {
            if (guardEffect != null)
            {
                Pools.Spawn(guardEffect, hit.Point, Quaternion.identity, MaxStatusEffects);
            }

            if (cameraRig != null)
            {
                cameraRig.AddTrauma(blockTrauma);
            }
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
    }
}
