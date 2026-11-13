using System;
using NUnit.Framework;
using Threefold.Enemies;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The enemy's behaviour, run through the real brain and state machine with a fake body and
    /// the default Ruins Guardian and Arcane Wisp tuning. Time advances in steps of 50 ms. These
    /// tests need no Unity engine.
    /// </summary>
    public sealed class EnemyBrainTests
    {
        private const float Step = 0.05f;

        private FakeEnemyBody body;
        private AttackTokenPool tokens;
        private EnemyBrain brain;
        private int alertedCount;

        private void Create(EnemyTuning tuning, AttackTokenPool pool = null)
        {
            body = new FakeEnemyBody { Position = Vector3.zero, HomePosition = Vector3.zero, Forward = Vector3.forward };
            tokens = pool ?? new AttackTokenPool(2, 1, 0f, 0f);
            brain = new EnemyBrain(body, tuning, tokens, 1);
            alertedCount = 0;
            brain.Alerted += _ => alertedCount++;
        }

        private static TargetObservation Seen(Vector3 position)
        {
            return new TargetObservation(true, true, position);
        }

        private static TargetObservation Hidden(Vector3 position)
        {
            return new TargetObservation(true, false, position);
        }

        private void Run(float seconds, TargetObservation observation)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += Step)
            {
                brain.Tick(Step, observation);
                tokens.Update(Step);
            }
        }

        /// <summary>Runs until the condition holds and returns the time it took. Fails if it never does.</summary>
        private float RunUntil(Func<bool> condition, float maxSeconds, TargetObservation observation)
        {
            float elapsed = 0f;
            while (!condition())
            {
                Assert.Less(elapsed, maxSeconds, $"Condition not reached within {maxSeconds} s; state is {brain.State}.");
                brain.Tick(Step, observation);
                tokens.Update(Step);
                elapsed += Step;
            }

            return elapsed;
        }

        /// <summary>A Guardian that has noticed a player standing 2 m in front of it, and is now chasing.</summary>
        private void EngageGuardianAtCloseRange(Vector3 player)
        {
            Create(EnemyArchetypes.RuinsGuardian());
            RunUntil(() => brain.State == EnemyStateId.Chase, 2f, Seen(player));
        }

        // -------------------------------------------------------------- calm

        [Test]
        public void StartsIdleAndPatrolsItsRoute()
        {
            Create(EnemyArchetypes.RuinsGuardian());
            body.Route.Add(Vector3.zero);
            body.Route.Add(new Vector3(10f, 0f, 0f));

            Assert.AreEqual(EnemyStateId.Idle, brain.State);

            RunUntil(() => brain.State == EnemyStateId.Patrol, 3f, TargetObservation.None);
            RunUntil(() => body.Destination == new Vector3(10f, 0f, 0f), 6f, TargetObservation.None);
            Assert.IsFalse(body.LastMoveRan, "Patrolling is done at walking pace.");
        }

        [Test]
        public void AGlimpseAtTheEdgeOfSightMakesItSuspiciousNotHostile()
        {
            Create(EnemyArchetypes.RuinsGuardian());
            var player = new Vector3(0f, 0f, 12f);

            Run(0.6f, Seen(player));

            Assert.AreEqual(EnemyStateId.Investigate, brain.State);
            Assert.Less(brain.Awareness.Value, 1f);
            Assert.AreEqual(player, body.Destination);
            Assert.AreEqual(0, alertedCount, "A suspicion is not yet an alert.");
        }

        [Test]
        public void ContinuedSightTurnsSuspicionIntoAnAlert()
        {
            Create(EnemyArchetypes.RuinsGuardian());

            RunUntil(() => brain.State == EnemyStateId.Alerted, 3f, Seen(new Vector3(0f, 0f, 12f)));
            Assert.AreEqual(1, alertedCount);
        }

        [Test]
        public void APlayerRightNextToItIsNoticedAtOnceAndAfterAPauseChased()
        {
            Create(EnemyArchetypes.RuinsGuardian());
            var player = new Vector3(0f, 0f, 2f);

            Run(Step, Seen(player));
            Assert.AreEqual(EnemyStateId.Alerted, brain.State);

            // The reaction pause is the player's warning.
            float reaction = RunUntil(() => brain.State == EnemyStateId.Chase, 2f, Seen(player));
            Assert.Greater(reaction, 0.5f);
        }

        [Test]
        public void ANoiseWithinHearingIsInvestigated()
        {
            Create(EnemyArchetypes.RuinsGuardian());

            brain.HearNoise(new Vector3(20f, 0f, 0f), 30f);
            Run(Step, TargetObservation.None);
            Assert.AreEqual(EnemyStateId.Idle, brain.State, "Beyond its hearing range.");

            brain.HearNoise(new Vector3(8f, 0f, 0f), 10f);
            Run(Step, TargetObservation.None);
            Assert.AreEqual(EnemyStateId.Investigate, brain.State);
            Assert.AreEqual(new Vector3(8f, 0f, 0f), body.Destination);
        }

        [Test]
        public void InvestigationSearchesThenReturnsHome()
        {
            Create(EnemyArchetypes.RuinsGuardian());
            brain.HearNoise(new Vector3(8f, 0f, 0f), 10f);
            Run(Step, TargetObservation.None);

            body.ArriveAtDestination();
            float search = RunUntil(() => brain.State == EnemyStateId.Return, 10f, TargetObservation.None);
            Assert.GreaterOrEqual(search, brain.Tuning.searchSeconds - Step);
            Assert.AreEqual(Vector3.zero, body.Destination);

            body.ArriveAtDestination();
            Run(Step, TargetObservation.None);
            Assert.AreEqual(EnemyStateId.Idle, brain.State);
        }

        [Test]
        public void AnAllyAlertIsJoinedAfterItsDelayAndNotPassedOn()
        {
            Create(EnemyArchetypes.RuinsGuardian());

            brain.ReceiveAllyAlert(new Vector3(5f, 0f, 5f), 0.5f);
            Run(0.4f, TargetObservation.None);
            Assert.AreEqual(EnemyStateId.Idle, brain.State);

            Run(0.2f, TargetObservation.None);
            Assert.AreEqual(EnemyStateId.Alerted, brain.State);
            Assert.IsTrue(brain.AlertedByAlly);
            Assert.AreEqual(0, alertedCount, "Passing an ally's alert on again would make the group alert itself in a loop.");
        }

        [Test]
        public void BeingHitWhileUnawareTurnsItTowardsTheAttacker()
        {
            Create(EnemyArchetypes.RuinsGuardian());
            var attacker = new Vector3(0f, 0f, -6f);

            brain.TakeDamage(10f, Vector3.forward, true, attacker);

            Assert.AreEqual(EnemyStateId.Alerted, brain.State);
            Assert.AreEqual(attacker, brain.LastKnownPosition);
            Assert.AreEqual(1, alertedCount);
        }

        // ------------------------------------------------------------- combat

        [Test]
        public void AMeleeAttackIsTelegraphedLockedAndThenLands()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);

            RunUntil(() => brain.State == EnemyStateId.Attack, 1f, Seen(player));
            Assert.AreEqual(1, tokens.HolderCount, "The attack holds a token.");
            Assert.AreEqual("begin 1", body.Events[0], "The Slam, the second attack in the list, is used at full health.");

            float windup = brain.Tuning.attacks[1].windupSeconds;
            Run(windup * 0.4f, Seen(player));
            Assert.AreEqual(0, body.PerformCount, "Nothing lands during the wind-up.");
            CollectionAssert.DoesNotContain(body.Events, "lock 1", "It still follows the player early in the wind-up.");

            Run(windup * 0.6f + Step, Seen(player));
            CollectionAssert.Contains(body.Events, "lock 1");
            Assert.AreEqual(1, body.PerformCount);

            RunUntil(() => brain.State == EnemyStateId.Chase, 2f, Seen(player));
            Assert.AreEqual(0, tokens.HolderCount, "The token is handed back after the recovery.");
            Assert.Greater(brain.Cooldowns[1], 0f);
        }

        [Test]
        public void TheShockwaveIsOnlyUsedOnceTheGuardianIsWorn()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);

            brain.HealthFraction = 0.5f;
            RunUntil(() => brain.State == EnemyStateId.Attack, 1f, Seen(player));
            Assert.AreEqual("begin 0", body.Events[0]);
        }

        [Test]
        public void TwoGuardiansNeverAttackAtTheSameTime()
        {
            var player = new Vector3(0f, 0f, 2f);
            var pool = new AttackTokenPool(2, 1, 0f, 0f);
            var bodyA = new FakeEnemyBody { Position = new Vector3(-1f, 0f, 0f), HomePosition = new Vector3(-1f, 0f, 0f) };
            var bodyB = new FakeEnemyBody { Position = new Vector3(1f, 0f, 0f), HomePosition = new Vector3(1f, 0f, 0f) };
            var a = new EnemyBrain(bodyA, EnemyArchetypes.RuinsGuardian(), pool, 1);
            var b = new EnemyBrain(bodyB, EnemyArchetypes.RuinsGuardian(), pool, 2);

            for (float t = 0f; t < 12f; t += Step)
            {
                a.Tick(Step, Seen(player));
                b.Tick(Step, Seen(player));
                pool.Update(Step);

                bool both = a.State == EnemyStateId.Attack && b.State == EnemyStateId.Attack;
                Assert.IsFalse(both, $"Both guardians were attacking at {t:0.00} s.");
            }

            Assert.Greater(bodyA.PerformCount, 0, "The first guardian attacked.");
            Assert.Greater(bodyB.PerformCount, 0, "The second guardian took its turn.");
            Assert.Greater(bodyA.StandPointRequests + bodyB.StandPointRequests, 0,
                "The guardian without a token waited at a distance.");
        }

        [Test]
        public void BreakingPoiseDuringTheWindupCancelsTheAttack()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);
            RunUntil(() => brain.State == EnemyStateId.Attack, 1f, Seen(player));

            brain.TakeDamage(60f, Vector3.forward, true, player);

            Assert.AreEqual(EnemyStateId.Stagger, brain.State);
            Assert.AreEqual(1, body.CancelCount);
            Assert.AreEqual(0, body.PerformCount);
            Assert.AreEqual(0, tokens.HolderCount, "An interrupted attack gives its token back.");
            Assert.AreEqual(1, body.KnockbackCount);
            Assert.Greater(brain.Cooldowns[1], 0f, "An interrupted attack is not retried at once.");
        }

        [Test]
        public void AStaggeredGuardianCannotBeStaggeredAgainStraightAway()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);

            brain.TakeDamage(60f, Vector3.forward, true, player);
            Assert.AreEqual(EnemyStateId.Stagger, brain.State);

            RunUntil(() => brain.State != EnemyStateId.Stagger, 2f, Seen(player));
            brain.TakeDamage(60f, Vector3.forward, true, player);
            Assert.AreNotEqual(EnemyStateId.Stagger, brain.State);
        }

        [Test]
        public void ALostPlayerIsSearchedForWhereLastSeen()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);

            var hidingPlace = new Vector3(4f, 0f, 6f);
            RunUntil(() => brain.State == EnemyStateId.Investigate, 10f, Hidden(hidingPlace));

            Assert.AreEqual(hidingPlace, brain.InvestigatePoint,
                "Just after losing sight it still knew where the player went.");
            Assert.Less(brain.Awareness.Value, 1f);
        }

        [Test]
        public void ChasingBeyondTheLeashGivesUpAndIgnoresThePlayerUntilHome()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);

            body.Position = new Vector3(0f, 0f, 23f);
            var farPlayer = new Vector3(0f, 0f, 30f);
            Run(Step, Seen(farPlayer));

            Assert.AreEqual(EnemyStateId.Return, brain.State);
            Assert.IsTrue(brain.IgnoresTargetUntilHome);

            body.Position = new Vector3(0f, 0f, 20f);
            Run(1f, Seen(new Vector3(0f, 0f, 21f)));
            Assert.AreEqual(EnemyStateId.Return, brain.State, "It does not turn back while walking home.");

            body.ArriveAtDestination();
            Run(Step, TargetObservation.None);
            Assert.AreEqual(EnemyStateId.Idle, brain.State);
            Assert.IsFalse(brain.IgnoresTargetUntilHome);
        }

        [Test]
        public void ADefeatedPlayerIsNoLongerChased()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);

            Run(Step, TargetObservation.None);

            Assert.AreEqual(EnemyStateId.Return, brain.State);
            Assert.AreEqual(Vector3.zero, body.Destination);
        }

        [Test]
        public void DefeatDuringAnAttackStopsEverythingAndFreesTheToken()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);
            RunUntil(() => brain.State == EnemyStateId.Attack, 1f, Seen(player));

            brain.Defeat();

            Assert.AreEqual(EnemyStateId.Dead, brain.State);
            Assert.IsTrue(body.Defeated);
            Assert.AreEqual(1, body.CancelCount);
            Assert.AreEqual(0, tokens.HolderCount);

            brain.TakeDamage(100f, Vector3.forward, true, player);
            Run(1f, Seen(player));
            Assert.AreEqual(EnemyStateId.Dead, brain.State);
            Assert.AreEqual(0, body.PerformCount);
        }

        [Test]
        public void ResetForgetsEverything()
        {
            var player = new Vector3(0f, 0f, 2f);
            EngageGuardianAtCloseRange(player);
            RunUntil(() => brain.State == EnemyStateId.Attack, 1f, Seen(player));
            brain.Defeat();

            brain.ResetToHome();

            Assert.AreEqual(EnemyStateId.Idle, brain.State);
            Assert.AreEqual(0f, brain.Awareness.Value);
            Assert.IsFalse(brain.HasLastKnownPosition);
            Assert.AreEqual(0, tokens.HolderCount);
            foreach (float cooldown in brain.Cooldowns)
            {
                Assert.AreEqual(0f, cooldown);
            }
        }

        // ------------------------------------------------------------- ranged

        [Test]
        public void AWispBacksAwayFromAPlayerWhoClosesIn()
        {
            Create(EnemyArchetypes.ArcaneWisp());
            var player = new Vector3(0f, 0f, 2.8f);

            RunUntil(() => brain.State == EnemyStateId.Chase, 2f, Seen(player));
            Run(Step, Seen(player));

            Assert.AreEqual(6f, body.LastStandMin);
            Assert.AreEqual(11f, body.LastStandMax);
            Assert.IsTrue(body.LastStandNeedsSight, "A place to shoot from needs a clear view.");
            Assert.IsTrue(body.LastMoveRan, "Retreating is urgent.");
            Assert.Greater(EnemyRules.FlatDistance(body.Destination.Value, player), 6f);
            Assert.AreEqual(EnemyStateId.Chase, brain.State, "It retreats instead of firing point-blank.");
        }

        [Test]
        public void ACorneredWispFiresUpClose()
        {
            Create(EnemyArchetypes.ArcaneWisp());
            body.StandPointsAvailable = false;
            var player = new Vector3(0f, 0f, 2.8f);

            RunUntil(() => brain.State == EnemyStateId.Attack, 3f, Seen(player));
        }

        [Test]
        public void AWispShotChargesThenLocksThenFires()
        {
            Create(EnemyArchetypes.ArcaneWisp());
            var player = new Vector3(0f, 0f, 8f);

            RunUntil(() => brain.State == EnemyStateId.Attack, 3f, Seen(player));
            RunUntil(() => body.PerformCount == 1, 2f, Seen(player));
            RunUntil(() => brain.State == EnemyStateId.Chase, 2f, Seen(player));

            CollectionAssert.AreEqual(new[] { "begin 0", "lock 0", "perform 0", "end 0" }, body.Events);
        }

        [Test]
        public void EveryHitStaggersAWisp()
        {
            Create(EnemyArchetypes.ArcaneWisp());
            brain.TakeDamage(20f, Vector3.forward, true, new Vector3(0f, 0f, -8f));
            Assert.AreEqual(EnemyStateId.Stagger, brain.State);
        }
    }
}
