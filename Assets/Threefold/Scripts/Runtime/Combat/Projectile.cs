using Threefold.Pooling;
using Threefold.WorldMagic;
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

        /// <summary>How far away the impact can be heard by enemies, in metres. 0 is silent.</summary>
        public readonly float NoiseRadius;

        /// <summary>Poise damage. Negative uses the damage.</summary>
        public readonly float Stagger;

        /// <summary>The kind of magic carried. None uses the projectile prefab's own kind.</summary>
        public readonly SpellElement Element;

        /// <summary>
        /// The magical effects carried, apart from the damage. If empty, the projectile's kind of magic
        /// decides: an arcane bolt charges, a fire bolt heats, with the damage as the strength.
        /// </summary>
        public readonly EffectPayload Effects;

        /// <summary>The discipline the shot came from, as its selector slot, or -1.</summary>
        public readonly int Discipline;

        /// <summary>Size of the projectile's visuals relative to the prefab. A charged shot is larger.</summary>
        public readonly float VisualScale;

        /// <summary>Whether force can turn the projectile aside. Not designated by default.</summary>
        public readonly ProjectileDeflection Deflection;

        public ProjectileLaunch(GameObject owner, Vector3 direction, float speed, float maxRange, float radius,
            float damage, LayerMask hitLayers, PooledObject impactEffect, float noiseRadius = 0f, float stagger = -1f,
            SpellElement element = SpellElement.None, EffectPayload effects = default, float visualScale = 1f,
            int discipline = -1, ProjectileDeflection deflection = default)
        {
            Deflection = deflection;
            Stagger = stagger;
            Element = element;
            Effects = effects;
            VisualScale = visualScale;
            Discipline = discipline;
            Owner = owner;
            Direction = direction;
            Speed = speed;
            MaxRange = maxRange;
            Radius = radius;
            Damage = damage;
            HitLayers = hitLayers;
            ImpactEffect = impactEffect;
            NoiseRadius = noiseRadius;
        }
    }

    /// <summary>
    /// A pooled projectile that flies in a straight line. Each frame it sweeps a sphere along the
    /// distance it is about to travel, so it cannot pass through thin objects at high speed and
    /// needs no Rigidbody. It never hits the character that launched it, and it passes over that
    /// character's allies, so an enemy's shot does not strike another enemy in its way.
    ///
    /// A projectile designated as deflectable can be turned aside by force (<see cref="DeflectionRules"/>):
    /// by a guard with force it is about to strike, or by an attack with force whose area it is in. It
    /// keeps what it carries and changes owner, a set number of times at most.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledObject))]
    public sealed class Projectile : MonoBehaviour
    {
        private const int MaxImpactEffects = 12;

        [Tooltip("Optional. Cleared on each launch so a reused projectile does not draw a streak across the scene.")]
        [SerializeField] private TrailRenderer trail;

        [Tooltip("The kind of magic this projectile carries. Magical activators respond only to the kinds they accept.")]
        [SerializeField] private SpellElement element = SpellElement.Arcane;

        // Projectiles that force could turn aside just now. Few at a time (motes are pooled, 16 at most).
        private static readonly System.Collections.Generic.List<Projectile> Deflectable =
            new System.Collections.Generic.List<Projectile>(16);

        // How many projectiles are in flight, for the development diagnostics.
        private static int flyingCount;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private readonly Collider[] overlaps = new Collider[8];
        private int deflections;

        private PooledObject pooled;
        private ProjectileLaunch launch;
        private Transform ownerTransform;
        private CombatTeam ownerTeam;
        private float remainingRange;
        private bool flying;
        private Vector3 baseScale;
        private bool baseScaleKnown;
        private int effectEventId;

        /// <summary>The kind of magic this projectile carries.</summary>
        public SpellElement Element
        {
            get => element;
            set => element = value;
        }

        /// <summary>How many projectiles are in flight now, of every owner.</summary>
        public static int FlyingCount => flyingCount;

        /// <summary>How often this projectile has been turned aside since it was launched.</summary>
        public int Deflections => deflections;

        /// <summary>The values it flies with now, after any deflection.</summary>
        public ProjectileLaunch Current => launch;

        /// <summary>
        /// Turns aside every designated projectile in an attack's area whose demand the attack's force
        /// meets, away from the attack's origin (or back at its sender, within the assist angle). A
        /// projectile behind cover is not reached, as a target behind cover is not. Called by
        /// <see cref="AreaStrike"/> for attacks that carry force.
        /// </summary>
        /// <returns>How many were turned.</returns>
        public static int DeflectWithin(in AreaShape shape, float force, GameObject user, int discipline, Vector3 coverOrigin,
            LayerMask coverLayers)
        {
            if (force <= 0f || user == null || Deflectable.Count == 0)
            {
                return 0;
            }

            CombatTeam team = user.TryGetComponent(out Health userHealth) ? userHealth.Team : CombatTeam.Neutral;
            int turned = 0;

            // Backwards, so that entries can be removed: one that is gone, or that a turn has used up.
            for (int i = Deflectable.Count - 1; i >= 0; i--)
            {
                Projectile projectile = Deflectable[i];
                if (projectile == null || !projectile.flying)
                {
                    Deflectable.RemoveAt(i);
                    continue;
                }

                Vector3 position = projectile.transform.position;
                Vector3 flat = new Vector3(position.x, shape.Origin.y, position.z);
                if (Mathf.Abs(position.y - shape.Origin.y) > shape.VerticalReach || !TargetSelection.IsInside(shape, flat) ||
                    AreaStrike.IsBehindCover(user.transform, projectile.transform, coverOrigin, position, coverLayers))
                {
                    continue;
                }

                Vector3 away = flat - shape.Origin;
                away = away.sqrMagnitude > 0.0001f ? away.normalized : shape.Forward;
                var push = new DeflectionPush(user, team, away, force, DeflectionRules.DefaultReturnAssistDegrees, discipline);
                if (projectile.TryTurn(push))
                {
                    turned++;
                }
            }

            return turned;
        }

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

            if (!baseScaleKnown)
            {
                baseScale = transform.localScale;
                baseScaleKnown = true;
            }

            // Set on every launch: a pooled projectile must not keep the size of its previous shot.
            transform.localScale = baseScale * Mathf.Max(0.05f, data.VisualScale);

            launch = data;
            deflections = 0;
            effectEventId = MagicEffectEvents.NextId();
            ownerTransform = data.Owner != null ? data.Owner.transform : null;
            ownerTeam = data.Owner != null && data.Owner.TryGetComponent(out Health ownerHealth)
                ? ownerHealth.Team
                : CombatTeam.Neutral;
            remainingRange = data.MaxRange;
            SetFlying(true);

            if (data.Deflection.IsDesignated && !Deflectable.Contains(this))
            {
                Deflectable.Add(this);
            }

            if (trail != null)
            {
                trail.Clear();
            }

            // A sweep does not report colliders it starts inside, so check for a point-blank target first.
            int count = Physics.OverlapSphereNonAlloc(transform.position, data.Radius, overlaps, data.HitLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!IsIgnored(overlaps[i]))
                {
                    if (!TryDeflectOff(overlaps[i]))
                    {
                        Impact(transform.position, -data.Direction, overlaps[i]);
                    }

                    return;
                }
            }
        }

        private void OnDisable()
        {
            SetFlying(false);
            Deflectable.Remove(this);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Deflectable.Clear();
            flyingCount = 0;
        }

        private void SetFlying(bool value)
        {
            if (flying == value)
            {
                return;
            }

            flying = value;
            flyingCount += value ? 1 : -1;
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

                // Force + projectile = deflection: a guard with force turns it instead of taking the hit.
                if (!TryDeflectOff(hit.collider))
                {
                    Impact(point, normal, hit.collider);
                }

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
                if (hits[i].distance >= nearestDistance || IsIgnored(hits[i].collider))
                {
                    continue;
                }

                nearest = hits[i];
                nearestDistance = hits[i].distance;
            }

            return nearestDistance < float.MaxValue;
        }

        /// <summary>The owner and the owner's allies are passed through.</summary>
        private bool IsIgnored(Collider other)
        {
            if (other == null || (ownerTransform != null && other.transform.IsChildOf(ownerTransform)))
            {
                return true;
            }

            if (ownerTeam == CombatTeam.Neutral)
            {
                return false;
            }

            // Only reached when the sweep touches something, which is rare per frame.
            Health health = other.GetComponentInParent<Health>();
            return health != null && CombatTeams.AreAllied(ownerTeam, health.Team);
        }

        /// <summary>Asks what the projectile is about to strike whether it turns it aside.</summary>
        private bool TryDeflectOff(Collider other)
        {
            if (!launch.Deflection.IsDesignated || other == null)
            {
                return false;
            }

            // Only reached when the projectile strikes something, which is rare per frame.
            IProjectileDeflector deflector = other.GetComponentInParent<IProjectileDeflector>();
            if (deflector == null)
            {
                return false;
            }

            var incoming = new IncomingProjectile(transform.position, launch.Direction, launch.Owner, ownerTeam, launch.Deflection);
            return deflector.TryDeflect(incoming, out DeflectionPush push) && TryTurn(push);
        }

        /// <summary>
        /// Turns the projectile with a push, if the rules allow it. It keeps flying from where it is, with
        /// a new owner, a new direction, a fresh range and a new event identity: what it does next is the
        /// deflector's doing, and a target it reaches is struck once.
        /// </summary>
        private bool TryTurn(in DeflectionPush push)
        {
            if (!flying || !DeflectionRules.CanDeflect(launch.Deflection, deflections, push.Force, ownerTeam, push.Team))
            {
                return false;
            }

            Vector3 position = transform.position;
            Health senderHealth = launch.Owner != null ? launch.Owner.GetComponent<Health>() : null;
            bool hasSender = senderHealth != null && senderHealth.IsAlive;
            Vector3 senderPoint = hasSender ? senderHealth.AimPoint : Vector3.zero;
            Vector3 direction = DeflectionRules.Redirect(push.Direction, position, hasSender, senderPoint, push.ReturnAssistDegrees);

            SpellElement carried = launch.Element != SpellElement.None ? launch.Element : element;
            launch = DeflectionRules.Deflected(launch, push, direction, carried);
            deflections++;
            effectEventId = MagicEffectEvents.NextId();
            ownerTransform = push.Deflector != null ? push.Deflector.transform : null;
            ownerTeam = push.Team;
            remainingRange = launch.MaxRange;
            transform.rotation = Quaternion.LookRotation(direction);

            if (deflections >= launch.Deflection.maxDeflections)
            {
                Deflectable.Remove(this);
            }

            CombatEvents.RaiseStatus(new StatusEvent(push.Deflector, StatusKind.Deflected, position, 0f));
            return true;
        }

        private void Impact(Vector3 point, Vector3 normal, Collider other)
        {
            SetFlying(false);
            Deflectable.Remove(this);

            SpellElement carried = launch.Element != SpellElement.None ? launch.Element : element;

            IDamageable target = other.GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive && launch.Damage > 0f)
            {
                target.ApplyDamage(new DamageInfo(launch.Damage, point, launch.Direction, launch.Owner, launch.Stagger,
                    carried));
            }

            // Effects are separate from the damage: an enemy takes both, a crystal only the effect.
            // Only reached on impact, which is rare per frame, so the lookup costs little.
            IMagicEffectReceiver receiver = other.GetComponentInParent<IMagicEffectReceiver>();
            if (receiver != null)
            {
                EffectPayload effects = launch.Effects.IsEmpty
                    ? EffectPayload.FromElement(carried, launch.Damage)
                    : launch.Effects;
                AreaStrike.Deliver(receiver, effects, launch.Direction, point, launch.Owner, launch.Discipline, effectEventId);
            }

            if (launch.ImpactEffect != null)
            {
                Quaternion rotation = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                Pools.Spawn(launch.ImpactEffect, point, rotation, MaxImpactEffects);
            }

            CombatEvents.RaiseNoise(new NoiseEvent(point, launch.NoiseRadius, launch.Owner));

            pooled.ReturnToPool();
        }

        private void Expire()
        {
            SetFlying(false);
            Deflectable.Remove(this);
            pooled.ReturnToPool();
        }
    }
}
