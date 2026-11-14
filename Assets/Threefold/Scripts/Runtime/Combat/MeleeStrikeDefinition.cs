using Threefold.Pooling;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// A strike that hits everything within reach in front of the user, or all around it when the
    /// arc is 360 degrees. It resolves at once through <see cref="AreaStrike"/>; any wind-up belongs
    /// to whoever uses it, which keeps the telegraph and the damage rules apart. Used by the Ruins
    /// Guardian. The Mage's own melee is defined by its discipline and resolved the same way.
    /// </summary>
    [CreateAssetMenu(menuName = "Threefold/Abilities/Melee Strike", fileName = "NewMeleeStrike")]
    public sealed class MeleeStrikeDefinition : AbilityDefinition
    {
        private const int MaxImpactEffects = 6;
        private const int MaxTargets = 16;

        [Header("Strike")]
        [Min(0f)]
        [SerializeField] private float damage = 25f;

        [Tooltip("How far the strike reaches from the user's position, in metres.")]
        [Min(0.1f)]
        [SerializeField] private float reach = 2.6f;

        [Tooltip("Width of the struck area in degrees, centred on the user's facing. 360 hits all around.")]
        [Range(10f, 360f)]
        [SerializeField] private float arcDegrees = 110f;

        [Tooltip("Targets more than this far above or below the user's feet are not hit, in metres.")]
        [Min(0.1f)]
        [SerializeField] private float verticalReach = 1.8f;

        [Tooltip("Layers the strike can hit.")]
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Optional. Effect spawned where the strike lands.")]
        [SerializeField] private PooledObject impactEffectPrefab;

        [Tooltip("How far away the strike can be heard by enemies, in metres.")]
        [Min(0f)]
        [SerializeField] private float noiseRadius = 12f;

        [Header("Magic")]
        [Tooltip("The magical effects the strike carries apart from its damage, such as force for the objects it lands " +
                 "on. Which kinds of target take them is part of the payload.")]
        [SerializeField] private EffectPayload effects;

        // Whether the effects were ever set by the setup tooling, so they are filled once and then left to tuning.
        [SerializeField, HideInInspector] private bool effectsConfigured;

        public float Damage => damage;
        public float Reach => reach;
        public float ArcDegrees => arcDegrees;
        public PooledObject ImpactEffectPrefab => impactEffectPrefab;
        public EffectPayload Effects => effects;
        public bool EffectsConfigured => effectsConfigured;

        /// <summary>Sets the effects the strike carries. Called by the setup tooling, once.</summary>
        public void ConfigureEffects(EffectPayload payload)
        {
            effects = payload;
            effectsConfigured = true;
        }

        /// <summary>Sets the strike values. Called by the setup tooling.</summary>
        public void ConfigureStrike(float damageAmount, float reachMetres, float arc, PooledObject impactEffect)
        {
            damage = Mathf.Max(0f, damageAmount);
            reach = Mathf.Max(0.1f, reachMetres);
            arcDegrees = Mathf.Clamp(arc, 10f, 360f);
            impactEffectPrefab = impactEffect;
        }

        /// <summary>
        /// True when a point lies inside the struck area of a user standing at <paramref name="origin"/>
        /// and facing <paramref name="forward"/>. Pure geometry, shared with the telegraph and the tests.
        /// </summary>
        public static bool IsInsideArea(Vector3 origin, Vector3 forward, Vector3 point, float reach, float arcDegrees,
            float verticalReach)
        {
            return TargetSelection.IsInside(new AreaShape(origin, forward, reach, arcDegrees, verticalReach), point);
        }

        public override void Execute(in AbilityContext context)
        {
            GameObject user = context.Caster;
            if (user == null)
            {
                return;
            }

            // The strike follows the user's body, not the aim: a dodged telegraph stays dodged.
            Transform userTransform = user.transform;
            Vector3 origin = userTransform.position;
            Vector3 forward = userTransform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            var request = new AreaStrikeRequest
            {
                User = user,
                Shape = new AreaShape(origin, forward, reach, arcDegrees, verticalReach),
                Damage = damage * context.DamageScale,
                Stagger = -1f,
                Element = SpellElement.None,
                MaxTargets = MaxTargets,
                Effects = effects,
                Discipline = -1,
                HitLayers = hitLayers,
                CoverCheckHeight = verticalReach * 0.5f,
            };
            AreaStrike.Resolve(request, null);

            Vector3 landing = arcDegrees >= 359.9f ? origin : origin + forward * (reach * 0.6f);

            if (impactEffectPrefab != null)
            {
                Pools.Spawn(impactEffectPrefab, landing, Quaternion.identity, MaxImpactEffects);
            }

            CombatEvents.RaiseNoise(new NoiseEvent(landing, noiseRadius, user));
        }
    }
}
