using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>Stands at its post. Starts patrolling after a pause if it has a route.</summary>
    internal sealed class IdleState : EnemyState
    {
        public override EnemyStateId Id => EnemyStateId.Idle;

        public override void Enter(EnemyBrain brain)
        {
            brain.Body.Stop();
            brain.Body.ClearLook();
            brain.AlertedByAlly = false;
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            if (brain.ReactToThreats(true))
            {
                return;
            }

            if (brain.Body.PatrolPointCount >= 2 && brain.TimeInState >= brain.Tuning.patrolPauseSeconds)
            {
                brain.ChangeState(EnemyStateId.Patrol);
            }
        }
    }

    /// <summary>Walks from patrol point to patrol point, pausing at each, in a loop.</summary>
    internal sealed class PatrolState : EnemyState
    {
        private bool waiting;
        private float waitRemaining;

        public override EnemyStateId Id => EnemyStateId.Patrol;

        public override void Enter(EnemyBrain brain)
        {
            waiting = false;
            brain.Body.ClearLook();
            GoToCurrentPoint(brain);
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            if (brain.ReactToThreats(true))
            {
                return;
            }

            int count = brain.Body.PatrolPointCount;
            if (count < 2)
            {
                brain.ChangeState(EnemyStateId.Idle);
                return;
            }

            if (waiting)
            {
                waitRemaining -= deltaTime;
                if (waitRemaining <= 0f)
                {
                    brain.PatrolIndex = (brain.PatrolIndex + 1) % count;
                    waiting = false;
                    GoToCurrentPoint(brain);
                }

                return;
            }

            if (brain.Body.HasArrived)
            {
                Pause(brain);
            }
        }

        private void GoToCurrentPoint(EnemyBrain brain)
        {
            int count = brain.Body.PatrolPointCount;
            if (count == 0)
            {
                return;
            }

            brain.PatrolIndex %= count;

            // An unreachable point is skipped after the usual pause.
            if (!brain.Body.MoveTo(brain.Body.GetPatrolPoint(brain.PatrolIndex), false))
            {
                Pause(brain);
            }
        }

        private void Pause(EnemyBrain brain)
        {
            waiting = true;
            waitRemaining = brain.Tuning.patrolPauseSeconds;
            brain.Body.Stop();
        }
    }

    /// <summary>
    /// Has just noticed the player: stops, turns towards them, and pauses before moving in. The
    /// pause and the enemy's visible reaction are the player's warning that a fight is starting.
    /// </summary>
    internal sealed class AlertedState : EnemyState
    {
        private float duration;

        public override EnemyStateId Id => EnemyStateId.Alerted;

        public override void Enter(EnemyBrain brain)
        {
            brain.Body.Stop();
            brain.Body.LookAt(brain.LastKnownPosition);
            brain.IgnoresTargetUntilHome = false;

            // Slight variation, so a group alerted together does not move as one.
            duration = brain.ReactionSeconds * brain.NextRandom(0.85f, 1.15f);

            if (!brain.AlertedByAlly)
            {
                brain.RaiseAlerted();
            }
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            brain.Body.LookAt(brain.LastKnownPosition);

            if (brain.TimeInState >= duration)
            {
                brain.ChangeState(EnemyStateId.Chase);
            }
        }
    }

    /// <summary>
    /// Goes to a point of interest (a glimpse of the player or a noise), then looks around for a
    /// while. Seeing the player properly turns it into a fight; finding nothing sends it home.
    /// </summary>
    internal sealed class InvestigateState : EnemyState
    {
        private const float GlanceAngle = 70f;
        private const float GlanceSeconds = 1.3f;

        /// <summary>Within this distance of the point the enemy starts looking around, even if another enemy stands on it.</summary>
        private const float CloseEnough = 1.5f;

        /// <summary>Allowed walking time, as a multiple of the straight-line time, plus a margin in seconds.</summary>
        private const float TravelAllowance = 2f;
        private const float TravelMargin = 2f;

        private bool searching;
        private float travelStart;
        private float travelLimit;
        private float searchStart;
        private float nextGlance;
        private float glanceSide;
        private Vector3 searchFacing;

        public override EnemyStateId Id => EnemyStateId.Investigate;

        public override void Enter(EnemyBrain brain)
        {
            searching = false;
            brain.Body.ClearLook();
            GoToPoint(brain);
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            // Full awareness becomes a fight; a new noise restarts the investigation there.
            if (brain.ReactToThreats(false))
            {
                return;
            }

            // Still catching glimpses: follow them.
            if (brain.IsSuspicious && EnemyRules.FlatDistance(brain.InvestigatePoint, brain.LastKnownPosition) > 1f)
            {
                brain.InvestigatePoint = brain.LastKnownPosition;
                searching = false;
                brain.Body.ClearLook();
                GoToPoint(brain);
                return;
            }

            if (!searching)
            {
                bool close = EnemyRules.FlatDistance(brain.Body.Position, brain.InvestigatePoint) <= CloseEnough;
                bool tooLong = brain.TimeInState - travelStart > travelLimit;
                if (brain.Body.HasArrived || close || tooLong)
                {
                    StartSearch(brain);
                }

                return;
            }

            if (brain.TimeInState - searchStart >= brain.Tuning.searchSeconds)
            {
                brain.ChangeState(EnemyStateId.Return);
                return;
            }

            if (brain.TimeInState >= nextGlance)
            {
                glanceSide = -glanceSide;
                Vector3 direction = EnemyRules.RotateAroundUp(searchFacing, GlanceAngle * glanceSide);
                brain.Body.LookAt(brain.Body.Position + direction * 4f);
                nextGlance = brain.TimeInState + GlanceSeconds;
            }
        }

        private void GoToPoint(EnemyBrain brain)
        {
            float distance = EnemyRules.FlatDistance(brain.Body.Position, brain.InvestigatePoint);
            travelStart = brain.TimeInState;
            travelLimit = distance / Mathf.Max(0.1f, brain.Tuning.walkSpeed) * TravelAllowance + TravelMargin;

            if (!brain.Body.MoveTo(brain.InvestigatePoint, false))
            {
                StartSearch(brain);
            }
        }

        private void StartSearch(EnemyBrain brain)
        {
            searching = true;
            searchStart = brain.TimeInState;
            brain.Body.Stop();

            searchFacing = brain.Body.Forward;
            searchFacing.y = 0f;
            searchFacing = searchFacing.sqrMagnitude > 0.0001f ? searchFacing.normalized : Vector3.forward;

            glanceSide = 1f;
            nextGlance = brain.TimeInState + GlanceSeconds * 0.5f;
        }
    }

    /// <summary>
    /// Engaged with the player. Decides when to attack (an attack in range, off cooldown, with a
    /// token from the director) and where to be meanwhile:
    ///
    ///   melee enemies close in when they may attack, and otherwise wait in a ring around the
    ///   player, facing them, so several melee enemies take turns instead of crowding in;
    ///   ranged enemies keep within their preferred distance band with a clear view, back away
    ///   when the player closes in, and drift to new positions between shots.
    ///
    /// Losing the player for too long turns into a search; chasing beyond the leash, a return.
    /// A ranged enemy does not shoot from well inside its preferred distance: it backs away first,
    /// and only fires up close once it has found nowhere to back away to.
    /// </summary>
    internal sealed class ChaseState : EnemyState
    {
        /// <summary>Share of its preferred minimum distance inside which a ranged enemy retreats instead of firing.</summary>
        private const float RetreatShare = 0.75f;

        private const float RepathDistance = 0.75f;
        private const float RepathSeconds = 0.3f;
        private const float UrgentRepositionSeconds = 0.4f;
        private const float FaceTargetDistance = 7f;

        private float nextReposition;
        private float nextUrgentReposition;
        private float nextRepath;
        private Vector3 lastDestination;
        private bool hasDestination;
        private bool cornered;

        public override EnemyStateId Id => EnemyStateId.Chase;

        public override void Enter(EnemyBrain brain)
        {
            cornered = false;
            nextReposition = 0f;
            nextUrgentReposition = 0f;
            nextRepath = 0f;
            hasDestination = false;
            brain.CurrentAttack = -1;
        }

        public override void Exit(EnemyBrain brain)
        {
            brain.Tokens.Withdraw(brain.Id);
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            IEnemyBody body = brain.Body;
            EnemyTuning tuning = brain.Tuning;

            if (!brain.Target.Exists)
            {
                // The player has been defeated or is gone.
                brain.Tokens.Release(brain.Id);
                brain.ForgetTarget();
                brain.ChangeState(EnemyStateId.Return);
                return;
            }

            if (brain.TimeSinceTargetSeen > tuning.loseTargetSeconds)
            {
                brain.Tokens.Release(brain.Id);
                brain.InvestigatePoint = brain.LastKnownPosition;
                brain.Awareness.Clear();
                brain.Awareness.RaiseTo(tuning.suspicionThreshold);
                brain.ChangeState(EnemyStateId.Investigate);
                return;
            }

            Vector3 target = brain.LastKnownPosition;

            if (EnemyRules.ShouldGiveUpChase(body.HomePosition, body.Position, target, tuning.leashRadius))
            {
                brain.Tokens.Release(brain.Id);
                brain.ForgetTarget();
                brain.IgnoresTargetUntilHome = true;
                brain.ChangeState(EnemyStateId.Return);
                return;
            }

            float distance = EnemyRules.FlatDistance(body.Position, target);
            bool visible = brain.Target.Visible;

            bool retreating = tuning.role == EnemyRole.Ranged && !cornered &&
                              distance < tuning.preferredMinDistance * RetreatShare;

            int attack = retreating
                ? -1
                : EnemyRules.ChooseAttack(tuning.attacks, brain.Cooldowns, distance, brain.HealthFraction, visible);
            if (attack >= 0)
            {
                EnemyAttackProfile profile = tuning.attacks[attack];
                brain.Tokens.Request(brain.Id, profile.kind, body.IsOnScreen);
                body.LookAt(target);

                if (brain.Tokens.Holds(brain.Id) && body.IsFacing(target, profile.maxStartAngle))
                {
                    brain.CurrentAttack = attack;
                    brain.ChangeState(EnemyStateId.Attack);
                    return;
                }
            }
            else
            {
                // The chance has passed (out of range, out of sight): let another enemy have the turn.
                brain.Tokens.Release(brain.Id);
            }

            if (tuning.role == EnemyRole.Ranged)
            {
                PositionAtRange(brain, target, distance, visible);
            }
            else
            {
                PositionForMelee(brain, target, distance, attack >= 0);
            }
        }

        private void PositionForMelee(EnemyBrain brain, Vector3 target, float distance, bool attackInRange)
        {
            IEnemyBody body = brain.Body;
            EnemyTuning tuning = brain.Tuning;

            int ready = EnemyRules.FirstReadyAttack(tuning.attacks, brain.Cooldowns, brain.HealthFraction);
            bool mayAttack = ready >= 0 &&
                             (brain.Tokens.Holds(brain.Id) || brain.Tokens.HasFreeSlot(tuning.attacks[ready].kind));

            if (mayAttack)
            {
                if (!attackInRange)
                {
                    MoveTowards(brain, target, true);
                }
                else
                {
                    body.Stop();
                    hasDestination = false;
                }

                if (distance <= FaceTargetDistance)
                {
                    body.LookAt(target);
                }
                else if (!attackInRange)
                {
                    body.ClearLook();
                }

                return;
            }

            // Another enemy is attacking, or nothing is ready: wait at a distance, facing the player.
            body.LookAt(target);

            bool tooClose = distance < tuning.waitRingDistance * 0.7f;
            bool due = brain.Now >= nextReposition || (tooClose && brain.Now >= nextUrgentReposition);
            if (!due)
            {
                return;
            }

            nextReposition = brain.Now + tuning.repositionSeconds * brain.NextRandom(0.8f, 1.2f);
            nextUrgentReposition = brain.Now + UrgentRepositionSeconds;

            float ring = tuning.waitRingDistance;
            if (body.TryFindStandPoint(target, ring * 0.85f, ring * 1.15f, false, out Vector3 point))
            {
                body.MoveTo(point, distance > ring + 3f);
                lastDestination = point;
                hasDestination = true;
            }
            else
            {
                body.Stop();
                hasDestination = false;
            }
        }

        private void PositionAtRange(EnemyBrain brain, Vector3 target, float distance, bool visible)
        {
            IEnemyBody body = brain.Body;
            EnemyTuning tuning = brain.Tuning;
            float minimum = tuning.preferredMinDistance;
            float maximum = Mathf.Max(minimum, tuning.preferredMaxDistance);

            body.LookAt(target);

            bool tooClose = distance < minimum;
            bool tooFar = distance > maximum;
            bool urgent = tooClose || tooFar || !visible;
            bool due = brain.Now >= nextReposition || (urgent && brain.Now >= nextUrgentReposition);
            if (!due)
            {
                return;
            }

            nextReposition = brain.Now + tuning.repositionSeconds * brain.NextRandom(0.8f, 1.2f);
            nextUrgentReposition = brain.Now + UrgentRepositionSeconds;

            bool found = body.TryFindStandPoint(target, minimum, maximum, true, out Vector3 point);
            cornered = tooClose && !found;

            if (found)
            {
                body.MoveTo(point, urgent);
                lastDestination = point;
                hasDestination = true;
            }
            else if (!visible || tooFar)
            {
                // Nowhere good to stand: close in until the player can be seen again.
                MoveTowards(brain, target, true);
            }
        }

        private void MoveTowards(EnemyBrain brain, Vector3 point, bool run)
        {
            bool moved = hasDestination && EnemyRules.FlatDistance(point, lastDestination) > RepathDistance;
            if (hasDestination && !moved)
            {
                return;
            }

            if (hasDestination && brain.Now < nextRepath)
            {
                return;
            }

            brain.Body.MoveTo(point, run);
            lastDestination = point;
            hasDestination = true;
            nextRepath = brain.Now + RepathSeconds;
        }
    }

    /// <summary>
    /// One attack: wind-up with its telegraph, the hit, then recovery. During the first part of
    /// the wind-up the enemy follows the target; then the attack locks and can be dodged. The
    /// recovery is the player's opening. Interrupting the wind-up cancels the attack.
    /// </summary>
    internal sealed class AttackState : EnemyState
    {
        /// <summary>Share of the cooldown applied when an attack is interrupted before it lands.</summary>
        private const float InterruptedCooldownShare = 0.5f;

        private EnemyAttackProfile profile;
        private int index;
        private bool locked;
        private bool struck;

        public override EnemyStateId Id => EnemyStateId.Attack;

        public override void Enter(EnemyBrain brain)
        {
            index = brain.CurrentAttack;
            EnemyAttackProfile[] attacks = brain.Tuning.attacks;
            profile = attacks != null && index >= 0 && index < attacks.Length ? attacks[index] : null;
            locked = false;
            struck = false;

            if (profile == null)
            {
                index = -1;
                return;
            }

            brain.Body.Stop();
            brain.Body.LookAt(brain.LastKnownPosition);
            brain.Body.BeginAttack(index, profile.windupSeconds);

            if (profile.trackFraction <= 0f)
            {
                Lock(brain);
            }
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            if (profile == null)
            {
                brain.ChangeState(EnemyStateId.Chase);
                return;
            }

            if (!struck)
            {
                if (!locked)
                {
                    brain.Body.LookAt(brain.LastKnownPosition);
                    if (brain.TimeInState >= profile.windupSeconds * profile.trackFraction)
                    {
                        Lock(brain);
                    }
                }

                if (brain.TimeInState >= profile.windupSeconds)
                {
                    struck = true;
                    brain.Body.PerformAttack(index);
                    brain.SetCooldown(index, profile.cooldownSeconds);
                    brain.Body.EndAttack(index, false);
                }

                return;
            }

            if (brain.TimeInState >= profile.windupSeconds + profile.recoverySeconds)
            {
                brain.ChangeState(EnemyStateId.Chase);
            }
        }

        public override void Exit(EnemyBrain brain)
        {
            if (profile != null && !struck)
            {
                brain.Body.EndAttack(index, true);
                brain.SetCooldown(index, profile.cooldownSeconds * InterruptedCooldownShare);
            }

            brain.Tokens.Release(brain.Id);
            brain.CurrentAttack = -1;
            profile = null;
        }

        private void Lock(EnemyBrain brain)
        {
            locked = true;
            brain.Body.LockAttack(index);
            brain.Body.ClearLook();
        }
    }

    /// <summary>Knocked back and briefly helpless. Any attack in progress has been cancelled.</summary>
    internal sealed class StaggerState : EnemyState
    {
        public override EnemyStateId Id => EnemyStateId.Stagger;

        public override void Enter(EnemyBrain brain)
        {
            brain.Tokens.Release(brain.Id);
            brain.Body.Stop();
            brain.Body.ClearLook();
            brain.Body.Knockback(brain.StaggerDirection, brain.Tuning.knockbackDistance);
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            if (brain.TimeInState >= brain.Tuning.staggerSeconds)
            {
                brain.ChangeState(brain.HasLastKnownPosition ? EnemyStateId.Chase : EnemyStateId.Idle);
            }
        }
    }

    /// <summary>Walks back to its post. After giving up at the leash, it ignores the player until home.</summary>
    internal sealed class ReturnState : EnemyState
    {
        public override EnemyStateId Id => EnemyStateId.Return;

        public override void Enter(EnemyBrain brain)
        {
            brain.Tokens.Release(brain.Id);
            brain.AlertedByAlly = false;
            brain.Body.ClearLook();
            brain.Body.MoveTo(brain.Body.HomePosition, false);
        }

        public override void Tick(EnemyBrain brain, float deltaTime)
        {
            if (brain.ReactToThreats(true))
            {
                return;
            }

            if (brain.Body.HasArrived)
            {
                brain.IgnoresTargetUntilHome = false;
                brain.ChangeState(EnemyStateId.Idle);
            }
        }
    }

    /// <summary>Defeated. Nothing happens until the enemy is restored.</summary>
    internal sealed class DeadState : EnemyState
    {
        public override EnemyStateId Id => EnemyStateId.Dead;

        public override void Enter(EnemyBrain brain)
        {
            brain.Tokens.Release(brain.Id);
            brain.Body.Stop();
            brain.Body.ClearLook();
            brain.Body.OnDefeated();
        }
    }
}
