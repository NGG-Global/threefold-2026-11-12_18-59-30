using System.Collections.Generic;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
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
                    Stagger(power), Charge.element, projectile.Effects(power),
                    ChargeScaling.Scale(projectile.scaleMin, projectile.scaleMax, power), context.Discipline));
            }
        }
    }
}
