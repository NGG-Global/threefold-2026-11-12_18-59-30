using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>Values a projectile is launched with.</summary>
    public readonly struct ProjectileLaunch
    {
        public readonly GameObject Owner;
        public readonly Vector3 Direction;
        public readonly float Speed;
        public readonly float MaxRange;
        public readonly float Radius;
        public readonly float Damage;
        public readonly LayerMask HitLayers;
        public readonly PooledObject ImpactEffect;

        public ProjectileLaunch(GameObject owner, Vector3 direction, float speed, float maxRange, float radius,
            float damage, LayerMask hitLayers, PooledObject impactEffect)
        {
            Owner = owner;
            Direction = direction;
            Speed = speed;
            MaxRange = maxRange;
            Radius = radius;
            Damage = damage;
            HitLayers = hitLayers;
            ImpactEffect = impactEffect;
        }
    }

    /// <summary>
    /// A pooled projectile that flies in a straight line. Each frame it sweeps a sphere along the
    /// distance it is about to travel, so it cannot pass through thin objects at high speed and
    /// needs no Rigidbody. It never hits the character that launched it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledObject))]
    public sealed class Projectile : MonoBehaviour
    {
        private const int MaxImpactEffects = 12;

        [Tooltip("Optional. Cleared on each launch so a reused projectile does not draw a streak across the scene.")]
        [SerializeField] private TrailRenderer trail;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private readonly Collider[] overlaps = new Collider[8];

        private PooledObject pooled;
        private ProjectileLaunch launch;
        private Transform ownerTransform;
        private float remainingRange;
        private bool flying;

        /// <summary>Connects the trail. Called by the setup tooling.</summary>
        public void Bind(TrailRenderer trailRenderer)
        {
            trail = trailRenderer;
        }

        /// <summary>Starts the projectile from its current position.</summary>
        public void Launch(in ProjectileLaunch data)
        {
            if (pooled == null)
            {
                pooled = GetComponent<PooledObject>();
            }

            launch = data;
            ownerTransform = data.Owner != null ? data.Owner.transform : null;
            remainingRange = data.MaxRange;
            flying = true;

            if (trail != null)
            {
                trail.Clear();
            }

            // A sweep does not report colliders it starts inside, so check for a point-blank target first.
            int count = Physics.OverlapSphereNonAlloc(transform.position, data.Radius, overlaps, data.HitLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!IsOwner(overlaps[i]))
                {
                    Impact(transform.position, -data.Direction, overlaps[i]);
                    return;
                }
            }
        }

        private void OnDisable()
        {
            flying = false;
        }

        private void Update()
        {
            if (!flying)
            {
                return;
            }

            float step = Mathf.Min(launch.Speed * Time.deltaTime, remainingRange);
            if (step <= 0f)
            {
                Expire();
                return;
            }

            Vector3 position = transform.position;

            if (TryFindHit(position, step, out RaycastHit hit))
            {
                Vector3 point = hit.distance > 0f ? hit.point : position;
                Vector3 normal = hit.distance > 0f ? hit.normal : -launch.Direction;
                transform.position = position + launch.Direction * hit.distance;
                Impact(point, normal, hit.collider);
                return;
            }

            transform.position = position + launch.Direction * step;
            remainingRange -= step;

            if (remainingRange <= 0f)
            {
                Expire();
            }
        }

        private bool TryFindHit(Vector3 position, float distance, out RaycastHit nearest)
        {
            nearest = default;
            float nearestDistance = float.MaxValue;

            int count = Physics.SphereCastNonAlloc(position, launch.Radius, launch.Direction, hits, distance,
                launch.HitLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (IsOwner(hits[i].collider) || hits[i].distance >= nearestDistance)
                {
                    continue;
                }

                nearest = hits[i];
                nearestDistance = hits[i].distance;
            }

            return nearestDistance < float.MaxValue;
        }

        private bool IsOwner(Collider other)
        {
            return other == null || (ownerTransform != null && other.transform.IsChildOf(ownerTransform));
        }

        private void Impact(Vector3 point, Vector3 normal, Collider other)
        {
            flying = false;

            IDamageable target = other.GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive && launch.Damage > 0f)
            {
                target.ApplyDamage(new DamageInfo(launch.Damage, point, launch.Direction, launch.Owner));
            }

            if (launch.ImpactEffect != null)
            {
                Quaternion rotation = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                Pools.Spawn(launch.ImpactEffect, point, rotation, MaxImpactEffects);
            }

            pooled.ReturnToPool();
        }

        private void Expire()
        {
            flying = false;
            pooled.ReturnToPool();
        }
    }
}
