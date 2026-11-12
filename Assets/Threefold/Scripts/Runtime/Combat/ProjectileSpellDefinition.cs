using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>A spell that launches a projectile towards the aim point.</summary>
    [CreateAssetMenu(menuName = "Threefold/Abilities/Projectile Spell", fileName = "NewProjectileSpell")]
    public sealed class ProjectileSpellDefinition : AbilityDefinition
    {
        [Header("Projectile")]
        [Tooltip("Prefab with a Projectile and a PooledObject component.")]
        [SerializeField] private Projectile projectilePrefab;

        [Tooltip("Optional. Effect spawned where the projectile hits.")]
        [SerializeField] private PooledObject impactEffectPrefab;

        [Min(0f)]
        [SerializeField] private float damage = 20f;

        [Tooltip("Metres per second.")]
        [Min(0.1f)]
        [SerializeField] private float speed = 26f;

        [Tooltip("Distance after which the projectile fades out, in metres.")]
        [Min(0.1f)]
        [SerializeField] private float maxRange = 45f;

        [Tooltip("Collision radius of the projectile, in metres. Larger is more forgiving.")]
        [Min(0.01f)]
        [SerializeField] private float radius = 0.18f;

        [Tooltip("Layers the projectile can hit.")]
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Most projectiles of this spell that may exist at once. The oldest is reused beyond this.")]
        [Min(1)]
        [SerializeField] private int maxActiveProjectiles = 12;

        public Projectile ProjectilePrefab => projectilePrefab;
        public PooledObject ImpactEffectPrefab => impactEffectPrefab;
        public float Damage => damage;
        public float Speed => speed;
        public float MaxRange => maxRange;
        public float Radius => radius;

        /// <summary>Sets the projectile values. Called by the setup tooling.</summary>
        public void ConfigureProjectile(Projectile projectile, PooledObject impactEffect, float damageAmount,
            float metresPerSecond, float range, float collisionRadius)
        {
            projectilePrefab = projectile;
            impactEffectPrefab = impactEffect;
            damage = Mathf.Max(0f, damageAmount);
            speed = Mathf.Max(0.1f, metresPerSecond);
            maxRange = Mathf.Max(0.1f, range);
            radius = Mathf.Max(0.01f, collisionRadius);
        }

        public override void Execute(in AbilityContext context)
        {
            if (projectilePrefab == null)
            {
                Debug.LogError($"Spell '{name}' has no projectile prefab assigned.", this);
                return;
            }

            if (!projectilePrefab.TryGetComponent(out PooledObject pooledPrefab))
            {
                Debug.LogError($"Projectile prefab '{projectilePrefab.name}' has no PooledObject component.", this);
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(context.Direction);
            PooledObject instance = Pools.Spawn(pooledPrefab, context.Origin, rotation, maxActiveProjectiles);

            if (instance != null && instance.TryGetComponent(out Projectile projectile))
            {
                projectile.Launch(new ProjectileLaunch(context.Caster, context.Direction, speed, maxRange, radius,
                    damage, hitLayers, impactEffectPrefab));
            }
        }
    }
}
