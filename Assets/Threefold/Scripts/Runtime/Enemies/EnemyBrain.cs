using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// The decision making of one enemy. It keeps what the enemy knows (awareness, where the
    /// target was last seen, cooldowns, poise), runs the state machine, and reacts to events that
    /// can interrupt anything: damage, defeat, noises and alerts from allies.
    ///
    /// It is plain C#. It never touches Unity objects; it acts through <see cref="IEnemyBody"/>
    /// and learns about the world through <see cref="TargetObservation"/>. The Unity component
    /// <see cref="EnemyController"/> supplies both, and the tests supply fakes, which is how the
    /// whole behaviour is tested without the engine.
    /// </summary>
    public sealed class EnemyBrain
    {
        private const float LongAgo = 1000000f;

        /// <summary>How recent a sighting must be for awareness below full to count as suspicious.</summary>
        private const float SuspicionMemorySeconds = 0.5f;

        /// <summary>Where an attacker is assumed to be when damage arrives without a known source, in metres.</summary>
        private const float UnknownAttackerDistance = 6f;

        private readonly EnemyStateMachine machine = new EnemyStateMachine();
        private readonly System.Random random;
        private float[] cooldowns = new float[0];

        private bool noisePending;
        private Vector3 noisePosition;
        private bool allyAlertPending;
        private float allyAlertRemaining;
        private Vector3 allyAlertPosition;

        public EnemyBrain(IEnemyBody body, EnemyTuning tuning, AttackTokenPool tokens, int id, int seed = 0)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            Tokens = tokens ?? AttackTokenPool.Unlimited();
            Id = id;
            random = new System.Random(seed != 0 ? seed : id * 7919 + 17);
            ApplyTuning(tuning ?? new EnemyTuning());

            machine.Add(new IdleState());
            machine.Add(new PatrolState());
            machine.Add(new AlertedState());
            machine.Add(new InvestigateState());
            machine.Add(new ChaseState());
            machine.Add(new AttackState());
            machine.Add(new StaggerState());
            machine.Add(new ReturnState());
            machine.Add(new DeadState());
            machine.Changed += HandleStateChanged;
            machine.ChangeTo(EnemyStateId.Idle, this);
        }

        public IEnemyBody Body { get; }
        public EnemyTuning Tuning { get; private set; }
        public AttackTokenPool Tokens { get; private set; }

        /// <summary>Identifies this enemy to the attack token pool. Unique within a scene.</summary>
        public int Id { get; }

        public EnemyStateId State => machine.CurrentId;
        public float TimeInState => machine.TimeInState;

        /// <summary>Seconds of thinking since the brain was created. The brain's own clock.</summary>
        public float Now { get; private set; }

        public AwarenessMeter Awareness { get; } = new AwarenessMeter();
        public PoiseMeter Poise { get; } = new PoiseMeter();

        /// <summary>The most recent observation of the target.</summary>
        public TargetObservation Target { get; private set; }

        public bool HasLastKnownPosition { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public float TimeSinceTargetSeen { get; private set; } = LongAgo;

        /// <summary>The enemy's health as a share of its maximum. Set by the body each update.</summary>
        public float HealthFraction { get; set; } = 1f;

        /// <summary>Set after giving up a chase at the leash: the target is ignored until home is reached.</summary>
        public bool IgnoresTargetUntilHome { get; internal set; }

        /// <summary>True when the current alert came from an ally, which is then not passed on again.</summary>
        public bool AlertedByAlly { get; internal set; }

        public Vector3 InvestigatePoint { get; internal set; }

        /// <summary>The direction of the hit that caused the current stagger.</summary>
        public Vector3 StaggerDirection { get; private set; }

        /// <summary>Index of the attack chosen or in progress, or -1.</summary>
        public int CurrentAttack { get; internal set; } = -1;

        /// <summary>Index into the patrol route of the point being walked to.</summary>
        public int PatrolIndex { get; internal set; }

        public IReadOnlyList<float> Cooldowns => cooldowns;

        /// <summary>True while fighting: from noticing the player until the player is lost.</summary>
        public bool IsEngaged
        {
            get
            {
                EnemyStateId state = machine.CurrentId;
                return state == EnemyStateId.Alerted || state == EnemyStateId.Chase || state == EnemyStateId.Attack ||
                       state == EnemyStateId.Stagger;
            }
        }

        public bool IsDefeated => machine.CurrentId == EnemyStateId.Dead;

        /// <summary>True if the target was glimpsed recently but awareness is not yet full.</summary>
        public bool IsSuspicious => HasLastKnownPosition && Awareness.Value >= Tuning.suspicionThreshold &&
                                    TimeSinceTargetSeen <= SuspicionMemorySeconds;

        internal float ReactionSeconds { get; set; }

        /// <summary>Raised after every state change. Arguments: previous state, new state.</summary>
        public event Action<EnemyStateId, EnemyStateId> StateChanged;

        /// <summary>
        /// Raised when this enemy notices the player by itself (not through an ally). The encounter
        /// uses it to alert the enemy's group.
        /// </summary>
        public event Action<EnemyBrain> Alerted;

        /// <summary>Replaces the tuning, for example when the difficulty changes. Cooldowns are kept.</summary>
        public void ApplyTuning(EnemyTuning tuning)
        {
            Tuning = tuning ?? new EnemyTuning();

            int attackCount = Tuning.attacks != null ? Tuning.attacks.Length : 0;
            if (cooldowns.Length != attackCount)
            {
                var resized = new float[attackCount];
                Array.Copy(cooldowns, resized, Math.Min(cooldowns.Length, attackCount));
                cooldowns = resized;
            }

            Poise.Configure(Tuning.poiseThreshold, Tuning.poiseRecoveryPerSecond, Tuning.staggerImmunitySeconds);
        }

        /// <summary>Moves the enemy to another token pool, giving back anything held in the old one.</summary>
        public void SetTokenPool(AttackTokenPool pool)
        {
            Tokens.Release(Id);
            Tokens = pool ?? AttackTokenPool.Unlimited();
        }

        /// <summary>Thinks for one update.</summary>
        public void Tick(float deltaTime, in TargetObservation observation)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Now += deltaTime;

            if (IsDefeated)
            {
                Target = observation;
                machine.Tick(this, deltaTime);
                return;
            }

            Perceive(deltaTime, observation);
            Poise.Tick(deltaTime);

            for (int i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] > 0f)
                {
                    cooldowns[i] -= deltaTime;
                }
            }

            if (allyAlertPending)
            {
                allyAlertRemaining -= deltaTime;
                if (allyAlertRemaining <= 0f)
                {
                    allyAlertPending = false;
                    ApplyAllyAlert();
                }
            }

            machine.Tick(this, deltaTime);
        }

        /// <summary>Damage was taken. Makes the enemy fully aware and may stagger it.</summary>
        /// <param name="amount">Damage actually applied.</param>
        /// <param name="direction">Direction the damage travelled in.</param>
        /// <param name="sourceKnown">Whether the attacker's position is known.</param>
        /// <param name="sourcePosition">The attacker's position, if known.</param>
        public void TakeDamage(float amount, Vector3 direction, bool sourceKnown, Vector3 sourcePosition)
        {
            if (IsDefeated)
            {
                return;
            }

            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : -Body.Forward;

            if (sourceKnown)
            {
                RememberTarget(sourcePosition);
            }
            else if (!HasLastKnownPosition)
            {
                RememberTarget(Body.Position - direction * UnknownAttackerDistance);
            }

            IgnoresTargetUntilHome = false;
            noisePending = false;
            allyAlertPending = false;

            bool wasEngaged = IsEngaged;
            Awareness.Fill();

            if (Poise.Absorb(amount))
            {
                StaggerDirection = direction;
                if (!wasEngaged)
                {
                    AlertedByAlly = false;
                    Alerted?.Invoke(this);
                }

                machine.ChangeTo(EnemyStateId.Stagger, this);
                return;
            }

            if (!wasEngaged)
            {
                // Being hit is unmistakable, so the reaction is quicker than to a sighting.
                ReactionSeconds = Tuning.alertReactionSeconds * 0.5f;
                AlertedByAlly = false;
                machine.ChangeTo(EnemyStateId.Alerted, this);
            }
        }

        /// <summary>The enemy has been defeated.</summary>
        public void Defeat()
        {
            if (IsDefeated)
            {
                return;
            }

            noisePending = false;
            allyAlertPending = false;
            machine.ChangeTo(EnemyStateId.Dead, this);
        }

        /// <summary>A noise was made. An enemy that is not fighting comes to look if it can hear it.</summary>
        /// <param name="radius">How far the noise carries, in metres.</param>
        public void HearNoise(Vector3 position, float radius)
        {
            if (IsDefeated || IsEngaged || IgnoresTargetUntilHome)
            {
                return;
            }

            float distance = EnemyRules.FlatDistance(Body.Position, position);
            if (distance > radius || distance > Tuning.hearingRange)
            {
                return;
            }

            noisePending = true;
            noisePosition = position;
        }

        /// <summary>An ally has spotted the target. After the delay this enemy joins the fight.</summary>
        public void ReceiveAllyAlert(Vector3 targetPosition, float delaySeconds)
        {
            if (IsDefeated || IsEngaged)
            {
                return;
            }

            allyAlertPending = true;
            allyAlertRemaining = Mathf.Max(0f, delaySeconds);
            allyAlertPosition = targetPosition;
        }

        /// <summary>Forgets everything and stands idle, for a restart. The body is placed by its owner.</summary>
        public void ResetToHome()
        {
            Tokens.Release(Id);
            ForgetTarget();
            Poise.Reset();
            Array.Clear(cooldowns, 0, cooldowns.Length);
            noisePending = false;
            allyAlertPending = false;
            IgnoresTargetUntilHome = false;
            AlertedByAlly = false;
            CurrentAttack = -1;
            PatrolIndex = 0;
            HealthFraction = 1f;
            machine.ChangeTo(EnemyStateId.Idle, this);
        }

        // ---------------------------------------------------------- for states

        internal void ChangeState(EnemyStateId id)
        {
            machine.ChangeTo(id, this);
        }

        internal float NextRandom(float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        internal void SetCooldown(int index, float seconds)
        {
            if (index >= 0 && index < cooldowns.Length)
            {
                cooldowns[index] = Mathf.Max(cooldowns[index], seconds);
            }
        }

        internal void RaiseAlerted()
        {
            Alerted?.Invoke(this);
        }

        internal void ForgetTarget()
        {
            Awareness.Clear();
            HasLastKnownPosition = false;
            TimeSinceTargetSeen = LongAgo;
        }

        /// <summary>
        /// Shared by the calm states (idle, patrol, return): reacts to full awareness, a noise, or,
        /// if allowed, suspicion. Returns true if the state was changed.
        /// </summary>
        internal bool ReactToThreats(bool allowSuspicion)
        {
            if (IgnoresTargetUntilHome)
            {
                return false;
            }

            if (Awareness.IsFull && HasLastKnownPosition)
            {
                ReactionSeconds = Tuning.alertReactionSeconds;
                AlertedByAlly = false;
                machine.ChangeTo(EnemyStateId.Alerted, this);
                return true;
            }

            if (noisePending)
            {
                noisePending = false;
                InvestigatePoint = noisePosition;
                Awareness.RaiseTo(Tuning.suspicionThreshold);
                machine.ChangeTo(EnemyStateId.Investigate, this);
                return true;
            }

            if (allowSuspicion && IsSuspicious)
            {
                InvestigatePoint = LastKnownPosition;
                machine.ChangeTo(EnemyStateId.Investigate, this);
                return true;
            }

            return false;
        }

        // -------------------------------------------------------------- private

        private void Perceive(float deltaTime, in TargetObservation observation)
        {
            Target = observation;

            if (observation.Visible && !IgnoresTargetUntilHome)
            {
                RememberTarget(observation.Position);
                Awareness.Observe(deltaTime, EnemyRules.FlatDistance(Body.Position, observation.Position), Tuning);
                return;
            }

            TimeSinceTargetSeen = Mathf.Min(LongAgo, TimeSinceTargetSeen + deltaTime);

            // Just after losing sight, a fighting enemy still knows where the target went, as a
            // person would. Without this, enemies stop dead at every corner.
            if (IsEngaged && observation.Exists && TimeSinceTargetSeen <= Tuning.trackingGraceSeconds)
            {
                LastKnownPosition = observation.Position;
            }

            if (!IsEngaged)
            {
                Awareness.Decay(deltaTime, Tuning.awarenessDecay);
            }
        }

        private void RememberTarget(Vector3 position)
        {
            LastKnownPosition = position;
            HasLastKnownPosition = true;
            TimeSinceTargetSeen = 0f;
        }

        private void ApplyAllyAlert()
        {
            if (IsDefeated || IsEngaged)
            {
                return;
            }

            RememberTarget(allyAlertPosition);
            IgnoresTargetUntilHome = false;
            Awareness.Fill();
            AlertedByAlly = true;
            ReactionSeconds = Tuning.alertReactionSeconds;
            machine.ChangeTo(EnemyStateId.Alerted, this);
        }

        private void HandleStateChanged(EnemyStateId previous, EnemyStateId next)
        {
            StateChanged?.Invoke(previous, next);
        }
    }
}
