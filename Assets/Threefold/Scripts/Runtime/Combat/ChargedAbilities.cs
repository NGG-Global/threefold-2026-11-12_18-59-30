using System.Collections.Generic;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// An ability that is charged by holding and released: the special attack of a discipline. The
    /// longer it was held, up to the full charge time, the stronger it is. Mana cost and cooldown are
    /// the ordinary ability values, enforced by <see cref="AbilityCaster"/>; the charge arrives in
    /// <see cref="AbilityContext.Charge"/>.
    /// </summary>
    public abstract class ChargedAbilityDefinition : AbilityDefinition
    {
        [SerializeField] private ChargeProfile charge = new ChargeProfile();

        public ChargeProfile Charge => charge;

        /// <summary>Sets the charge values. Called by the setup tooling and by tests.</summary>
        public void ConfigureCharge(ChargeProfile profile)
        {
            charge = profile ?? new ChargeProfile();
        }

        protected float Damage(float power) => ChargeScaling.Scale(charge.damageMin, charge.damageMax, power);

        protected float Stagger(float power) => ChargeScaling.Scale(charge.staggerMin, charge.staggerMax, power);
    }

    /// <summary>A charged shot: one projectile whose damage, speed and size grow with the charge (Arcane Arrow).</summary>
    [CreateAssetMenu(menuName = "Threefold/Abilities/Charged Projectile", fileName = "NewChargedProjectile")]
    public sealed class ChargedProjectileDefinition : ChargedAbilityDefinition
    {
        [Header("Projectile")]
        [Tooltip("Prefab with a Projectile and a PooledObject component.")]
        [SerializeField] private Projectile projectilePrefab;

        [Tooltip("Optional. Effect spawned where the projectile hits.")]
        [SerializeField] private PooledObject impactEffectPrefab;

        [SerializeField] private ProjectileProfile projectile = new ProjectileProfile();

        [Tooltip("Layers the projectile can hit.")]
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Most projectiles of this ability that may exist at once.")]
        [Min(1)]
        [SerializeField] private int maxActiveProjectiles = 6;

        public Projectile ProjectilePrefab => projectilePrefab;
        public PooledObject ImpactEffectPrefab => impactEffectPrefab;
        public ProjectileProfile Projectile => projectile;
        public LayerMask HitLayers => hitLayers;

        /// <summary>Sets the projectile values. Called by the setup tooling.</summary>
        public void ConfigureProjectile(Projectile prefab, PooledObject impactEffect, ProjectileProfile profile)
        {
            projectilePrefab = prefab;
            impactEffectPrefab = impactEffect;
            projectile = profile ?? new ProjectileProfile();
        }

        public float Radius(float power) => ChargeScaling.Scale(projectile.radiusMin, projectile.radiusMax, power);

        public override void Execute(in AbilityContext context)
        {
            if (projectilePrefab == null)
            {
                Debug.LogError($"Ability '{name}' has no projectile prefab assigned.", this);
                return;
            }

            if (!projectilePrefab.TryGetComponent(out PooledObject pooledPrefab))
            {
                Debug.LogError($"Projectile prefab '{projectilePrefab.name}' has no PooledObject component.", this);
                return;
            }

            float power = context.Charge;
            Quaternion rotation = Quaternion.LookRotation(context.Direction);
            PooledObject instance = Pools.Spawn(pooledPrefab, context.Origin, rotation, maxActiveProjectiles);

            if (instance != null && instance.TryGetComponent(out Projectile launched))
            {
                launched.Launch(new ProjectileLaunch(context.Caster, context.Direction,
                    ChargeScaling.Scale(projectile.speedMin, projectile.speedMax, power), projectile.range, Radius(power),
                    Damage(power) * context.DamageScale, hitLayers, impactEffectPrefab, projectile.noiseRadius,
                    Stagger(power), Charge.element, 0f, ChargeScaling.Scale(projectile.scaleMin, projectile.scaleMax, power)));
            }
        }
    }

    /// <summary>
    /// A charged attack on a sector of ground in front of the caster. With heat it is the Flame Wave;
    /// with a push it is the Force Pulse. Reach, damage, heat and push grow with the charge. One
    /// overlap query finds the targets; nothing is simulated.
    /// </summary>
    [CreateAssetMenu(menuName = "Threefold/Abilities/Charged Area", fileName = "NewChargedArea")]
    public sealed class ChargedAreaDefinition : ChargedAbilityDefinition
    {
        private const int MaxImpactEffects = 8;

        // Shared by every area attack. A release that happens while another is still resolving
        // (which nothing does today) gets a list of its own.
        private static readonly List<Health> Struck = new List<Health>(16);
        private static bool resolving;

        [Header("Area")]
        [SerializeField] private AreaProfile area = new AreaProfile();

        [Tooltip("Optional. Effect spawned on each target that was hit.")]
        [SerializeField] private PooledObject impactEffectPrefab;

        [Tooltip("Layers the attack can hit.")]
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        public AreaProfile Area => area;
        public PooledObject ImpactEffectPrefab => impactEffectPrefab;

        /// <summary>How many targets the most recent release hit. For feedback and tests.</summary>
        public static int LastHitCount { get; private set; }

        /// <summary>Sets the area values. Called by the setup tooling.</summary>
        public void ConfigureArea(AreaProfile profile, PooledObject impactEffect)
        {
            area = profile ?? new AreaProfile();
            impactEffectPrefab = impactEffect;
        }

        public float Reach(float power) => ChargeScaling.Scale(area.reachMin, area.reachMax, power);

        /// <summary>The ground the attack covers when released by the given caster in the given direction.</summary>
        public AreaShape GetShape(Vector3 casterPosition, Vector3 direction, float power)
        {
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            return new AreaShape(casterPosition, direction, Reach(power), area.arcDegrees, area.verticalReach);
        }

        public override void Execute(in AbilityContext context)
        {
            GameObject user = context.Caster;
            if (user == null)
            {
                return;
            }

            float power = context.Charge;
            AreaShape shape = GetShape(user.transform.position, context.Direction, power);

            var request = new AreaStrikeRequest
            {
                User = user,
                Shape = shape,
                Damage = Damage(power) * context.DamageScale,
                Stagger = Stagger(power),
                Element = Charge.element,
                MaxTargets = area.maxTargets,
                Heat = ChargeScaling.Scale(area.heatMin, area.heatMax, power),
                PushDistance = ChargeScaling.Scale(area.pushMin, area.pushMax, power),
                HitLayers = hitLayers,
                CoverCheckHeight = area.verticalReach * 0.5f,
            };

            List<Health> struck = resolving ? new List<Health>(8) : Struck;
            bool outermost = !resolving;
            resolving = true;

            try
            {
                LastHitCount = AreaStrike.Resolve(request, struck);

                if (impactEffectPrefab != null)
                {
                    for (int i = 0; i < struck.Count; i++)
                    {
                        if (struck[i] != null)
                        {
                            Pools.Spawn(impactEffectPrefab, struck[i].AimPoint, Quaternion.identity, MaxImpactEffects);
                        }
                    }
                }
            }
            finally
            {
                struck.Clear();
                if (outermost)
                {
                    resolving = false;
                }
            }

            CombatEvents.RaiseNoise(new NoiseEvent(shape.Origin + shape.Forward * (shape.Reach * 0.5f), area.noiseRadius, user));
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Struck.Clear();
            resolving = false;
            LastHitCount = 0;
        }
    }
}
