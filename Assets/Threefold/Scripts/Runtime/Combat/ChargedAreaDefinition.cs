using System.Collections.Generic;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
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
                Effects = area.Effects(power),
                Discipline = context.Discipline,
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
