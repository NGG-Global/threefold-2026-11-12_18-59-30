using System;
using Threefold.Combat;
using Threefold.Pooling;
using UnityEngine;
using UnityEngine.AI;

namespace Threefold.Enemies
{
    /// <summary>
    /// An enemy in the scene. It gives the <see cref="EnemyBrain"/> a body: it moves with a
    /// NavMeshAgent, looks for the player at intervals, uses its abilities through an
    /// <see cref="AbilityCaster"/>, and reports damage, defeat and noises to the brain.
    ///
    /// Cost per enemy is kept low for phones: sight is checked a few times a second (less often
    /// when far from the player) with one raycast, new paths are only requested when the
    /// destination has moved, and nothing is allocated during play.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class EnemyController : MonoBehaviour, IEnemyBody, IRestorable
    {
        private const float NavMeshSearchRadius = 2f;
        private const float StandPointSearchRadius = 1.5f;
        private const float RepathThreshold = 0.5f;
        private const float ArrivalTolerance = 0.15f;
        private const float NearDistance = 22f;
        private const float KnockbackSeconds = 0.25f;
        private const float PlacementRetrySeconds = 0.5f;
        private const float StandPointJitter = 20f;
        private const int MaxDefeatEffects = 4;

        private static int nextId = 1;

        [Tooltip("What kind of enemy this is: tuning for senses, movement and attacks.")]
        [SerializeField] private EnemyDefinition definition;

        [Tooltip("Optional. Coordinates attacks and difficulty. Found in the scene if empty.")]
        [SerializeField] private EnemyDirector director;

        [Tooltip("Optional. The route walked while unaware.")]
        [SerializeField] private PatrolRoute patrolRoute;

        [Header("Parts")]
        [SerializeField] private AbilityCaster caster;
        [SerializeField] private TargetAimProvider aim;

        [Tooltip("Optional. Shows attack warnings.")]
        [SerializeField] private EnemyTelegraph telegraph;

        [Tooltip("Where the enemy sees from. Without it, a point 1.6 m above the feet is used.")]
        [SerializeField] private Transform eyes;

        [Tooltip("Switched off when defeated, so shots and the player pass through.")]
        [SerializeField] private Collider[] bodyColliders = new Collider[0];

        [Tooltip("Optional. Effect spawned when defeated.")]
        [SerializeField] private PooledObject defeatEffect;

        [Header("Perception")]
        [Tooltip("What blocks sight.")]
        [SerializeField] private LayerMask sightBlockers = Physics.DefaultRaycastLayers;

        [Tooltip("Seconds between sight checks while the player is near or the enemy is fighting.")]
        [Min(0.05f)]
        [SerializeField] private float perceptionInterval = 0.2f;

        [Tooltip("Seconds between sight checks while the player is far away.")]
        [Min(0.05f)]
        [SerializeField] private float distantPerceptionInterval = 0.6f;

        private readonly RaycastHit[] sightHits = new RaycastHit[8];

        private Health health;
        private NavMeshAgent agent;
        private EnemyBrain brain;
        private EnemyTuning tuning;
        private Health target;
        private AttackTokenPool ownTokens;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private TargetObservation observation;
        private float nextPerceptionTime;
        private float nextPlacementTime;
        private bool hasLook;
        private Vector3 lookPoint;
        private Vector3 knockbackVelocity;
        private float knockbackRemaining;
        private bool restoring;
        private int id;

        /// <summary>Raised when this enemy notices the player by itself. The encounter alerts its group.</summary>
        public event Action<EnemyController> Alerted;

        /// <summary>Raised when this enemy is defeated.</summary>
        public event Action<EnemyController> Defeated;

        /// <summary>Raised when this enemy has been put back at its post for a new attempt.</summary>
        public event Action<EnemyController> Restored;

        /// <summary>Raised after every change of behaviour. Arguments: previous, new.</summary>
        public event Action<EnemyStateId, EnemyStateId> StateChanged;

        public EnemyDefinition Definition => definition;
        public EnemyBrain Brain => brain;
        public Health Health => health != null ? health : health = GetComponent<Health>();
        public PatrolRoute Route => patrolRoute;
        public AbilityCaster Caster => caster;
        public EnemyTelegraph Telegraph => telegraph;
        public EnemyStateId State => brain != null ? brain.State : EnemyStateId.Idle;
        public float StateTime => brain != null ? brain.TimeInState : 0f;
        public bool IsDefeated => Health != null && !Health.IsAlive;

        /// <summary>The attack being performed, or null.</summary>
        public EnemyAttackProfile CurrentAttackProfile
        {
            get
            {
                if (brain == null || tuning == null || tuning.attacks == null)
                {
                    return null;
                }

                int index = brain.CurrentAttack;
                return index >= 0 && index < tuning.attacks.Length ? tuning.attacks[index] : null;
            }
        }

        /// <summary>Connects the enemy's parts. Called by the setup tooling.</summary>
        public void Bind(EnemyDefinition enemyDefinition, AbilityCaster abilityCaster, TargetAimProvider aimProvider,
            EnemyTelegraph attackTelegraph, Transform eyePoint, Collider[] colliders, PooledObject defeatBurst)
        {
            definition = enemyDefinition;
            caster = abilityCaster;
            aim = aimProvider;
            telegraph = attackTelegraph;
            eyes = eyePoint;
            bodyColliders = colliders ?? new Collider[0];
            defeatEffect = defeatBurst;
        }

        /// <summary>Connects the enemy to the scene. Called by the setup tooling.</summary>
        public void BindScene(EnemyDirector sceneDirector, PatrolRoute route)
        {
            director = sceneDirector;
            patrolRoute = route;
        }

        /// <summary>Sets the director only if none is assigned. Called by the setup tooling.</summary>
        public void BindDirectorIfEmpty(EnemyDirector sceneDirector)
        {
            if (director == null)
            {
                director = sceneDirector;
            }
        }

        public bool HasDefinition => definition != null;
        public bool HasCaster => caster != null;
        public bool HasTelegraph => telegraph != null;

        /// <summary>Joins the fight after an ally's alert, after the given delay.</summary>
        public void ReceiveAllyAlert(Vector3 targetPosition, float delaySeconds)
        {
            if (brain != null)
            {
                brain.ReceiveAllyAlert(targetPosition, delaySeconds);
            }
        }

        /// <summary>Applies a difficulty: health, speed, awareness, telegraph length, damage and accuracy.</summary>
        public void ApplyDifficulty(EnemyDifficultySettings settings, bool refillHealth)
        {
            if (definition == null)
            {
                return;
            }

            tuning = definition.Tuning.Scaled(settings);
            brain?.ApplyTuning(tuning);
            Health.SetMaximum(tuning.maxHealth, refillHealth);

            if (caster != null)
            {
                caster.DamageMultiplier = settings != null ? settings.damageMultiplier : 1f;
            }

            if (aim != null)
            {
                float lead = settings != null ? settings.aimLead : 0.5f;
                float spread = settings != null ? settings.aimSpreadDegrees : 2f;
                aim.Configure(lead, spread, FindProjectileSpeed());
            }

            ConfigureAgent();
        }

        /// <summary>Puts the enemy back at its post at full health, for a restart.</summary>
        public void Restore()
        {
            restoring = true;
            Health.Revive();
            restoring = false;
            ResetToHome();
        }

        // --------------------------------------------------------------- Unity

        private void Awake()
        {
            health = GetComponent<Health>();
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
            homePosition = transform.position;
            homeRotation = transform.rotation;
            id = nextId++;
        }

        private void OnEnable()
        {
            health.Damaged += HandleDamaged;
            health.Defeated += HandleDefeated;
            health.Revived += HandleRevived;
            CombatEvents.Noise += HandleNoise;

            if (brain != null && director != null)
            {
                director.Register(this);
            }
        }

        private void OnDisable()
        {
            health.Damaged -= HandleDamaged;
            health.Defeated -= HandleDefeated;
            health.Revived -= HandleRevived;
            CombatEvents.Noise -= HandleNoise;

            if (brain != null)
            {
                brain.Tokens.Release(id);
            }

            if (director != null)
            {
                director.Unregister(this);
            }
        }

        private void Start()
        {
            if (definition == null)
            {
                Debug.LogError($"Enemy '{name}' has no Enemy Definition and will do nothing.", this);
                enabled = false;
                return;
            }

            if (director == null)
            {
                director = FindAnyObjectByType<EnemyDirector>();
            }

            AttackTokenPool tokens;
            EnemyDifficultySettings settings = null;
            if (director != null)
            {
                tokens = director.Tokens;
                settings = director.Settings;
                target = director.Player;
            }
            else
            {
                // Alone in a scene, the enemy limits nobody and looks for the player itself.
                ownTokens = AttackTokenPool.Unlimited();
                tokens = ownTokens;
                target = FindPlayer();
            }

            if (aim != null)
            {
                aim.Target = target;
            }

            ApplyDifficulty(settings, true);

            // The agent starts switched off in the prefab and is switched on here, after every
            // object's OnEnable, when the scene's navigation mesh has certainly been loaded.
            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            TryPlaceOnNavMesh();
            PrepareTelegraph();

            brain = new EnemyBrain(this, tuning, tokens, id);
            brain.Alerted += HandleBrainAlerted;
            brain.StateChanged += HandleBrainStateChanged;

            if (director != null)
            {
                director.Register(this);
            }

            // Spread sight checks across frames so enemies do not all raycast at once.
            nextPerceptionTime = Time.time + UnityEngine.Random.Range(0f, perceptionInterval);
        }

        private void Update()
        {
            if (brain == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (brain.IsDefeated)
            {
                return;
            }

            if (!IsAgentReady && Time.time >= nextPlacementTime)
            {
                TryPlaceOnNavMesh();
            }

            if (Time.time >= nextPerceptionTime)
            {
                Sense();
            }

            ownTokens?.Update(deltaTime);
            brain.HealthFraction = Health.Fraction;
            brain.Tick(deltaTime, observation);

            UpdateKnockback(deltaTime);
            UpdateRotation(deltaTime);
        }

        private void OnDrawGizmosSelected()
        {
            EnemyTuning shown = tuning ?? (definition != null ? definition.Tuning : null);
            if (shown == null)
            {
                return;
            }

            Vector3 position = transform.position;
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(position, shown.sightRange);
            Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(position, shown.closeSenseRange);

            if (shown.leashRadius > 0f)
            {
                Gizmos.color = new Color(0.5f, 0.7f, 1f, 0.4f);
                Gizmos.DrawWireSphere(Application.isPlaying ? homePosition : position, shown.leashRadius);
            }
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            nextId = 1;
        }

        // ---------------------------------------------------------- perception

        private void Sense()
        {
            bool near = target != null && (target.transform.position - transform.position).sqrMagnitude < NearDistance * NearDistance;
            nextPerceptionTime = Time.time + (near || brain.IsEngaged ? perceptionInterval : distantPerceptionInterval);

            if (target == null || !target.IsAlive || !target.isActiveAndEnabled)
            {
                observation = TargetObservation.None;
                return;
            }

            Vector3 targetPosition = target.transform.position;
            float distance = EnemyRules.FlatDistance(transform.position, targetPosition);
            bool visible = false;

            if (distance <= tuning.sightRange)
            {
                Vector3 eye = EyePosition;
                Vector3 aimPoint = target.AimPoint;
                bool noticed = distance <= tuning.closeSenseRange ||
                               EnemyRules.IsInsideViewCone(transform.forward, aimPoint - eye, tuning.sightAngle);
                visible = noticed && HasClearLine(eye, aimPoint);
            }

            observation = new TargetObservation(true, visible, targetPosition);
        }

        private Vector3 EyePosition => eyes != null ? eyes.position : transform.position + Vector3.up * 1.6f;

        /// <summary>True if nothing but this enemy and the target lies between the two points.</summary>
        private bool HasClearLine(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            float length = offset.magnitude;
            if (length < 0.01f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(from, offset / length, sightHits, length, sightBlockers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hit = sightHits[i].transform;
                if (hit.IsChildOf(transform) || (target != null && hit.IsChildOf(target.transform)))
                {
                    continue;
                }

                // Allies do not block: the enemy's own shots and strikes pass over them too.
                Health other = hit.GetComponentInParent<Health>();
                if (other != null && Health.IsAlliedWith(other))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private Health FindPlayer()
        {
            var candidates = Health.Active;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null && candidates[i].Team == CombatTeam.Player)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------ IEnemyBody

        public Vector3 Position => transform.position;

        public Vector3 Forward
        {
            get
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            }
        }

        public Vector3 HomePosition => homePosition;

        public bool IsOnScreen => director == null || director.IsOnScreen(EyePosition);

        public int PatrolPointCount => patrolRoute != null ? patrolRoute.Count : 0;

        public Vector3 GetPatrolPoint(int index)
        {
            return patrolRoute != null ? patrolRoute.GetPoint(index) : homePosition;
        }

        public bool MoveTo(Vector3 destination, bool run)
        {
            if (!IsAgentReady || tuning == null)
            {
                return false;
            }

            agent.speed = run ? tuning.runSpeed : tuning.walkSpeed;

            if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, NavMeshSearchRadius, agent.areaMask))
            {
                return false;
            }

            // Asking for the same place again would throw away the current path for nothing.
            if (agent.hasPath && !agent.isStopped &&
                (hit.position - agent.destination).sqrMagnitude < RepathThreshold * RepathThreshold)
            {
                return true;
            }

            agent.isStopped = false;
            return agent.SetDestination(hit.position);
        }

        public void Stop()
        {
            if (!IsAgentReady)
            {
                return;
            }

            agent.isStopped = true;
            agent.ResetPath();
        }

        public bool HasArrived
        {
            get
            {
                if (!IsAgentReady || agent.isStopped)
                {
                    return true;
                }

                if (agent.pathPending)
                {
                    return false;
                }

                if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    return true;
                }

                return agent.remainingDistance <= agent.stoppingDistance + ArrivalTolerance;
            }
        }

        public void LookAt(Vector3 point)
        {
            hasLook = true;
            lookPoint = point;
        }

        public void ClearLook()
        {
            hasLook = false;
        }

        public bool IsFacing(Vector3 point, float toleranceDegrees)
        {
            return EnemyRules.IsInsideViewCone(transform.forward, point - transform.position, toleranceDegrees * 2f);
        }

        public void Knockback(Vector3 direction, float distance)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f || distance <= 0f)
            {
                return;
            }

            knockbackVelocity = direction.normalized * (distance / KnockbackSeconds);
            knockbackRemaining = KnockbackSeconds;
        }

        public bool TryFindStandPoint(Vector3 targetPosition, float minDistance, float maxDistance, bool needsSight,
            out Vector3 point)
        {
            point = default;
            if (!IsAgentReady)
            {
                return false;
            }

            float preferred = EnemyRules.PreferredDistance(minDistance, maxDistance);
            Vector3 eyeOffset = EyePosition - transform.position;
            Vector3 aimPoint = target != null ? target.AimPoint : targetPosition + Vector3.up;

            // A little randomness, so a ranged enemy drifts between shots instead of standing still.
            float jitter = UnityEngine.Random.Range(-StandPointJitter, StandPointJitter);

            for (int i = 0; i < EnemyRules.StandPointCandidateCount; i++)
            {
                float angle = EnemyRules.GetStandPointAngle(i) + jitter;
                Vector3 candidate = EnemyRules.PointAround(targetPosition, transform.position, preferred, angle);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, StandPointSearchRadius, agent.areaMask))
                {
                    continue;
                }

                float distance = EnemyRules.FlatDistance(hit.position, targetPosition);
                if (distance < minDistance - 0.5f || distance > maxDistance + 0.5f)
                {
                    continue;
                }

                if (needsSight && !HasClearLine(hit.position + eyeOffset, aimPoint))
                {
                    continue;
                }

                point = hit.position;
                return true;
            }

            return false;
        }

        public void BeginAttack(int attackIndex, float windupSeconds)
        {
            EnemyAttackProfile profile = GetAttack(attackIndex);
            if (aim != null)
            {
                aim.Unlock();
                aim.Target = target;
            }

            if (telegraph != null && profile != null)
            {
                telegraph.Begin(attackIndex, profile.kind, windupSeconds);
            }
        }

        public void LockAttack(int attackIndex)
        {
            if (aim != null)
            {
                aim.Lock(CastOrigin);
            }

            if (telegraph != null)
            {
                telegraph.Lock();
            }
        }

        public bool PerformAttack(int attackIndex)
        {
            EnemyAttackProfile profile = GetAttack(attackIndex);
            if (profile == null || caster == null)
            {
                return false;
            }

            if (telegraph != null)
            {
                telegraph.Strike();
            }

            return caster.TryCast(profile.abilitySlot) == CastResult.Success;
        }

        public void EndAttack(int attackIndex, bool interrupted)
        {
            if (aim != null)
            {
                aim.Unlock();
            }

            if (telegraph != null)
            {
                telegraph.End(interrupted);
            }
        }

        public void OnDefeated()
        {
            knockbackRemaining = 0f;
            hasLook = false;

            // A defeated enemy must stop blocking paths, avoidance and shots.
            if (agent != null && agent.enabled)
            {
                agent.enabled = false;
            }

            SetCollidersEnabled(false);

            if (telegraph != null)
            {
                telegraph.End(true);
            }

            if (defeatEffect != null)
            {
                Pools.Spawn(defeatEffect, Health.AimPoint, Quaternion.identity, MaxDefeatEffects);
            }

            Defeated?.Invoke(this);
        }

        // -------------------------------------------------------------- events

        private void HandleDamaged(DamageInfo damage)
        {
            if (brain == null)
            {
                return;
            }

            bool sourceKnown = damage.Source != null;
            Vector3 sourcePosition = sourceKnown ? damage.Source.transform.position : Vector3.zero;
            brain.TakeDamage(damage.Amount, damage.Direction, sourceKnown, sourcePosition);
        }

        private void HandleDefeated()
        {
            brain?.Defeat();
        }

        private void HandleRevived()
        {
            if (!restoring)
            {
                ResetToHome();
            }
        }

        private void HandleNoise(NoiseEvent noise)
        {
            if (brain == null || brain.IsDefeated || noise.Radius <= 0f)
            {
                return;
            }

            if ((noise.Position - transform.position).sqrMagnitude > noise.Radius * noise.Radius)
            {
                return;
            }

            // Noises made by this enemy or its allies are expected, not suspicious.
            if (noise.Source != null)
            {
                if (noise.Source == gameObject ||
                    (noise.Source.TryGetComponent(out Health sourceHealth) && Health.IsAlliedWith(sourceHealth)))
                {
                    return;
                }
            }

            brain.HearNoise(noise.Position, noise.Radius);
        }

        private void HandleBrainAlerted(EnemyBrain alertedBrain)
        {
            Alerted?.Invoke(this);
        }

        private void HandleBrainStateChanged(EnemyStateId previous, EnemyStateId next)
        {
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>Where the enemy believes the player to be. Used to alert its group.</summary>
        public Vector3 LastKnownTargetPosition => brain != null && brain.HasLastKnownPosition
            ? brain.LastKnownPosition
            : (target != null ? target.transform.position : transform.position);

        // ------------------------------------------------------------- private

        private bool IsAgentReady => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

        private Vector3 CastOrigin => caster != null && caster.CastPoint != null ? caster.CastPoint.position : EyePosition;

        private EnemyAttackProfile GetAttack(int index)
        {
            return tuning != null && tuning.attacks != null && index >= 0 && index < tuning.attacks.Length
                ? tuning.attacks[index]
                : null;
        }

        private void ConfigureAgent()
        {
            if (agent == null || tuning == null)
            {
                return;
            }

            agent.speed = tuning.walkSpeed;

            // Melee enemies stop well inside their reach; ranged enemies go where they are sent.
            float stopping = 0.3f;
            if (tuning.role == EnemyRole.Melee && tuning.attacks != null)
            {
                float reach = float.MaxValue;
                foreach (EnemyAttackProfile attack in tuning.attacks)
                {
                    if (attack != null && attack.usableBelowHealthFraction >= 1f)
                    {
                        reach = Mathf.Min(reach, attack.maxRange);
                    }
                }

                stopping = reach < float.MaxValue ? reach * 0.7f : 1.5f;
            }

            agent.stoppingDistance = stopping;
        }

        private float FindProjectileSpeed()
        {
            if (caster == null)
            {
                return 14f;
            }

            foreach (AbilityDefinition ability in caster.Abilities)
            {
                if (ability is ProjectileSpellDefinition spell)
                {
                    return spell.Speed;
                }
            }

            return 14f;
        }

        /// <summary>Gives the telegraph the shape of each melee attack, read from the ability assets.</summary>
        private void PrepareTelegraph()
        {
            if (telegraph == null || caster == null || tuning == null || tuning.attacks == null)
            {
                return;
            }

            for (int i = 0; i < tuning.attacks.Length; i++)
            {
                EnemyAttackProfile attack = tuning.attacks[i];
                if (attack != null && caster.GetAbility(attack.abilitySlot) is MeleeStrikeDefinition strike)
                {
                    telegraph.PrepareArea(i, strike.Reach, strike.ArcDegrees);
                }
            }
        }

        private void TryPlaceOnNavMesh()
        {
            nextPlacementTime = Time.time + PlacementRetrySeconds;

            if (agent == null || !agent.isActiveAndEnabled || agent.isOnNavMesh)
            {
                return;
            }

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, NavMeshSearchRadius, agent.areaMask))
            {
                agent.Warp(hit.position);
            }
        }

        private void ResetToHome()
        {
            knockbackRemaining = 0f;
            hasLook = false;
            SetCollidersEnabled(true);

            if (agent != null && !agent.enabled)
            {
                agent.enabled = true;
            }

            if (IsAgentReady)
            {
                agent.Warp(homePosition);
            }
            else
            {
                transform.position = homePosition;
                TryPlaceOnNavMesh();
            }

            transform.rotation = homeRotation;
            nextPerceptionTime = Time.time + UnityEngine.Random.Range(0f, perceptionInterval);
            observation = TargetObservation.None;

            if (aim != null)
            {
                aim.Unlock();
            }

            if (telegraph != null)
            {
                telegraph.End(true);
            }

            brain?.ResetToHome();
            Restored?.Invoke(this);
        }

        private void SetCollidersEnabled(bool on)
        {
            for (int i = 0; i < bodyColliders.Length; i++)
            {
                if (bodyColliders[i] != null)
                {
                    bodyColliders[i].enabled = on;
                }
            }
        }

        private void UpdateKnockback(float deltaTime)
        {
            if (knockbackRemaining <= 0f)
            {
                return;
            }

            knockbackRemaining -= deltaTime;
            if (IsAgentReady)
            {
                // Move keeps the enemy on the navigation mesh, so a push never shoves it into a wall.
                float fade = Mathf.Clamp01(knockbackRemaining / KnockbackSeconds);
                agent.Move(knockbackVelocity * (deltaTime * (0.5f + fade)));
            }
        }

        private void UpdateRotation(float deltaTime)
        {
            Vector3 desired;
            if (hasLook)
            {
                desired = lookPoint - transform.position;
            }
            else if (IsAgentReady && !agent.isStopped && agent.desiredVelocity.sqrMagnitude > 0.04f)
            {
                desired = agent.desiredVelocity;
            }
            else
            {
                return;
            }

            desired.y = 0f;
            if (desired.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion goal = Quaternion.LookRotation(desired);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, tuning.turnSpeed * deltaTime);
        }
    }
}
