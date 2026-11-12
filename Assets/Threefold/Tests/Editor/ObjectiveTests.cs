using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.Objectives;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The objective and restart rules. Edit Mode does not run component callbacks, so the tests
    /// call <see cref="DefeatTargetsObjective.Refresh"/> where a defeat would trigger it in play.
    /// </summary>
    public sealed class ObjectiveTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private DefeatTargetsObjective objective;
        private Health[] targets;

        [SetUp]
        public void SetUp()
        {
            targets = new Health[3];
            for (int i = 0; i < targets.Length; i++)
            {
                var targetObject = new GameObject("Target " + i);
                created.Add(targetObject);
                targets[i] = targetObject.AddComponent<Health>();
                targets[i].Configure(50f, Vector3.up);
            }

            var host = new GameObject("Objective");
            created.Add(host);
            objective = host.AddComponent<DefeatTargetsObjective>();
            objective.SetTargets(targets);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in created)
            {
                Object.DestroyImmediate(item);
            }

            created.Clear();
        }

        private void Defeat(int index)
        {
            targets[index].ApplyDamage(new DamageInfo(1000f, Vector3.zero, Vector3.forward, null));
            objective.Refresh();
        }

        [Test]
        public void StartsWithNothingDefeated()
        {
            Assert.AreEqual(3, objective.TotalCount);
            Assert.AreEqual(0, objective.DefeatedCount);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void ProgressIsCountedAndAnnounced()
        {
            int announcedDefeated = -1;
            int announcedTotal = -1;
            objective.Progressed += (defeated, total) =>
            {
                announcedDefeated = defeated;
                announcedTotal = total;
            };

            Defeat(1);

            Assert.AreEqual(1, objective.DefeatedCount);
            Assert.AreEqual(1, announcedDefeated);
            Assert.AreEqual(3, announcedTotal);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void DamageThatDoesNotDefeatIsNotProgress()
        {
            targets[0].ApplyDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, null));
            objective.Refresh();

            Assert.AreEqual(0, objective.DefeatedCount);
        }

        [Test]
        public void CompletesExactlyOnceWhenAllTargetsAreDefeated()
        {
            int completions = 0;
            objective.Completed += () => completions++;

            Defeat(0);
            Defeat(1);
            Assert.IsFalse(objective.IsComplete);

            Defeat(2);
            objective.Refresh();

            Assert.IsTrue(objective.IsComplete);
            Assert.AreEqual(1, completions);
        }

        [Test]
        public void ObjectiveWithoutTargetsIsNeverComplete()
        {
            objective.SetTargets(new Health[0]);

            Assert.AreEqual(0, objective.TotalCount);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void SameTargetIsListedOnlyOnce()
        {
            objective.SetTargets(new[] { targets[0], targets[0], null, targets[1] });

            Assert.AreEqual(2, objective.TotalCount);
        }

        [Test]
        public void ResetAfterRevivingTargetsStartsANewAttempt()
        {
            bool resetAnnounced = false;
            objective.ProgressReset += () => resetAnnounced = true;
            Defeat(0);
            Defeat(1);
            Defeat(2);

            foreach (Health target in targets)
            {
                target.Revive();
            }

            objective.ResetProgress();

            Assert.IsTrue(resetAnnounced);
            Assert.IsFalse(objective.IsComplete);
            Assert.AreEqual(0, objective.DefeatedCount);
            Assert.AreEqual(0f, objective.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void SessionRestartRevivesTargetsRefillsThePlayerAndClearsTheObjective()
        {
            var player = new GameObject("Player");
            created.Add(player);
            Health playerHealth = player.AddComponent<Health>();
            Mana playerMana = player.AddComponent<Mana>();
            playerHealth.Configure(100f, Vector3.up);
            playerMana.Configure(100f, 0f, 0f);
            playerHealth.ApplyDamage(new DamageInfo(40f, Vector3.zero, Vector3.forward, null));
            playerMana.TrySpend(70f);

            PrototypeSession session = objective.gameObject.AddComponent<PrototypeSession>();
            session.Bind(objective, playerHealth, playerMana, null, null);

            bool restarted = false;
            session.Restarted += () => restarted = true;
            Defeat(0);
            Defeat(1);
            Defeat(2);
            Assert.IsTrue(objective.IsComplete);

            session.Restart();

            Assert.IsTrue(restarted);
            Assert.IsFalse(objective.IsComplete);
            Assert.AreEqual(0, objective.DefeatedCount);
            foreach (Health target in targets)
            {
                Assert.IsTrue(target.IsAlive);
                Assert.AreEqual(50f, target.Current, 0.0001f);
            }

            Assert.AreEqual(100f, playerHealth.Current, 0.0001f);
            Assert.AreEqual(100f, playerMana.Current, 0.0001f);
        }

        [Test]
        public void DummyThatDoesNotRespawnStaysDownUntilRestored()
        {
            var dummyObject = new GameObject("Construct");
            created.Add(dummyObject);
            Health health = dummyObject.AddComponent<Health>();
            TrainingDummy dummy = dummyObject.AddComponent<TrainingDummy>();
            health.Configure(30f, Vector3.up);
            dummy.Respawns = false;

            health.ApplyDamage(new DamageInfo(30f, Vector3.zero, Vector3.forward, null));
            Assert.IsFalse(health.IsAlive);

            dummy.Restore();

            Assert.IsTrue(health.IsAlive);
            Assert.AreEqual(30f, health.Current, 0.0001f);
        }
    }
}
