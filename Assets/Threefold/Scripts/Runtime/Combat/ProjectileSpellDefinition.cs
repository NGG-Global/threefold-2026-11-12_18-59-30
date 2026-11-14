using Threefold.Pooling;
using Threefold.WorldMagic;
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

        [Tooltip("How far away an impact can be heard by enemies, in metres. 0 is silent.")]
        [Min(0f)]
        [SerializeField] private float impactNoiseRadius = 9f;

        [Header("Magic")]
        [Tooltip("The magical effects each projectile carries, apart from its damage. Empty: its kind of magic decides " +
                 "(an arcane shot charges, a fire shot heats), with the damage as the strength.")]
        [SerializeField] private EffectPayload effects;

        [Tooltip("Whether force can turn the projectiles aside (the Aegis guard and Aegis attacks), and how easily.")]
        [SerializeField] private ProjectileDeflection deflection;

        // Whether the deflection was ever set by the setup tooling, so it is filled once and then left to tuning.
        [SerializeField, HideInInspector] private bool deflectionConfigured;

        public Projectile ProjectilePrefab => projectilePrefab;
        public PooledObject ImpactEffectPrefab => impactEffectPrefab;
        public float Damage => damage;
        public float Speed => speed;
        public float MaxRange => maxRange;
        public float Radius => radius;
        public float ImpactNoiseRadius => impactNoiseRadius;
        public EffectPayload Effects => effects;
        public ProjectileDeflection Deflection => deflection;
        public bool DeflectionConfigured => deflectionConfigured;

        /// <summary>Sets the effects each projectile carries. Called by the setup tooling.</summary>
        public void ConfigureEffects(EffectPayload payload)
        {
            effects = payload;
        }

        /// <summary>Sets whether and how force turns the projectiles aside. Called by the setup tooling, once.</summary>
        public void ConfigureDeflection(ProjectileDeflection value)
        {
            deflection = value;
            deflectionConfigured = true;
        }

        /// <summary>Sets how many projectiles may exist at once and how far impacts are heard. Called by the setup tooling.</summary>
        public void ConfigureLimits(int maxProjectiles, float noiseRadius)
        {
            maxActiveProjectiles = Mathf.Max(1, maxProjectiles);
            impactNoiseRadius = Mathf.Max(0f, noiseRadius);
        }

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
                    damage * context.DamageScale, hitLayers, impactEffectPrefab, impactNoiseRadius, effects: effects,
                    deflection: deflection));
            }
        }
    }
}
