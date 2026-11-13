using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// A strike that hits everything within reach in front of the user, or all around it when the
    /// arc is 360 degrees. It resolves at once; any wind-up belongs to whoever uses it, which keeps
    /// the telegraph and the damage rules apart. Used by the Ruins Guardian, and suitable for a
    /// Rogue or Monk melee attack later.
    /// </summary>
    [CreateAssetMenu(menuName = "Threefold/Abilities/Melee Strike", fileName = "NewMeleeStrike")]
    public sealed class MeleeStrikeDefinition : AbilityDefinition
    {
        private const int MaxImpactEffects = 6;
        private const int MaxTargets = 16;

        // Shared by every strike. Strikes resolve one at a time on the main thread.
        private static readonly Collider[] Overlaps = new Collider[32];
        private static readonly Health[] Struck = new Health[MaxTargets];
        private static readonly RaycastHit[] CoverHits = new RaycastHit[16];

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

        public float Damage => damage;
        public float Reach => reach;
        public float ArcDegrees => arcDegrees;
        public PooledObject ImpactEffectPrefab => impactEffectPrefab;

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
            Vector3 offset = point - origin;
            if (Mathf.Abs(offset.y) > verticalReach)
            {
                return false;
            }

            offset.y = 0f;
            if (offset.sqrMagnitude > reach * reach)
            {
                return false;
            }

            if (arcDegrees >= 359.9f || offset.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(forward, offset) <= arcDegrees * 0.5f;
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

            user.TryGetComponent(out Health userHealth);

            // The sphere is raised so that it covers the body height of a target standing on the same ground.
            Vector3 centre = origin + Vector3.up * (verticalReach * 0.5f);
            float radius = Mathf.Sqrt(reach * reach + verticalReach * verticalReach * 0.25f);
            int count = Physics.OverlapSphereNonAlloc(centre, radius, Overlaps, hitLayers, QueryTriggerInteraction.Ignore);
            int struckCount = 0;

            for (int i = 0; i < count && struckCount < MaxTargets; i++)
            {
                Collider other = Overlaps[i];
                Overlaps[i] = null;

                if (other == null || other.transform.IsChildOf(userTransform))
                {
                    continue;
                }

                Health target = other.GetComponentInParent<Health>();
                if (target == null || !target.IsAlive || AlreadyStruck(target, struckCount) ||
                    (userHealth != null && userHealth.IsAlliedWith(target)))
                {
                    continue;
                }

                // Measured to the nearest point of the collider's bounds, so a wide target is hit at its
                // edge. Bounds work for every collider type, including the player's CharacterController.
                Vector3 closest = other.bounds.ClosestPoint(origin);
                Vector3 bodyPoint = new Vector3(closest.x, target.transform.position.y, closest.z);
                if (!IsInsideArea(origin, forward, bodyPoint, reach, arcDegrees, verticalReach))
                {
                    continue;
                }

                // A wall or column between the user and the target takes the blow: cover works.
                if (IsBehindCover(userTransform, userHealth, target, origin + Vector3.up * (verticalReach * 0.5f)))
                {
                    continue;
                }

                Struck[struckCount++] = target;

                Vector3 direction = target.transform.position - origin;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : forward;

                target.ApplyDamage(new DamageInfo(damage * context.DamageScale, target.AimPoint, direction, user));
            }

            for (int i = 0; i < struckCount; i++)
            {
                Struck[i] = null;
            }

            Vector3 landing = arcDegrees >= 359.9f ? origin : origin + forward * (reach * 0.6f);

            if (impactEffectPrefab != null)
            {
                Pools.Spawn(impactEffectPrefab, landing, Quaternion.identity, MaxImpactEffects);
            }

            CombatEvents.RaiseNoise(new NoiseEvent(landing, noiseRadius, user));
        }

        /// <summary>True if something other than the user, the target and the user's allies lies between them.</summary>
        private bool IsBehindCover(Transform userTransform, Health userHealth, Health target, Vector3 from)
        {
            Vector3 to = target.AimPoint;
            Vector3 offset = to - from;
            float length = offset.magnitude;
            if (length < 0.01f)
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(from, offset / length, CoverHits, length, hitLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hit = CoverHits[i].transform;
                if (hit.IsChildOf(userTransform) || hit.IsChildOf(target.transform))
                {
                    continue;
                }

                Health other = hit.GetComponentInParent<Health>();
                if (other != null && (other == target || (userHealth != null && userHealth.IsAlliedWith(other))))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool AlreadyStruck(Health target, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (Struck[i] == target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
