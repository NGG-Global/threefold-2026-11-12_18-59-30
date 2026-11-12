using NUnit.Framework;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Tests
{
    public sealed class HealthAndManaTests
    {
        private const float Tolerance = 0.0001f;

        private GameObject host;
        private Health health;
        private Mana mana;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("HealthAndManaTests");
            health = host.AddComponent<Health>();
            mana = host.AddComponent<Mana>();
            health.Configure(100f, Vector3.up);
            mana.Configure(100f, 10f, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        private static DamageInfo Damage(float amount)
        {
            return new DamageInfo(amount, Vector3.zero, Vector3.forward, null);
        }

        [Test]
        public void DamageReducesHealth()
        {
            health.ApplyDamage(Damage(30f));

            Assert.AreEqual(70f, health.Current, Tolerance);
            Assert.IsTrue(health.IsAlive);
        }

        [Test]
        public void DamagedEventReportsTheAmountActuallyRemoved()
        {
            float reported = -1f;
            health.Damaged += info => reported = info.Amount;
            health.ApplyDamage(Damage(90f));

            health.ApplyDamage(Damage(50f));

            Assert.AreEqual(10f, reported, Tolerance);
        }

        [Test]
        public void ReachingZeroDefeatsExactlyOnce()
        {
            int defeats = 0;
            health.Defeated += () => defeats++;

            health.ApplyDamage(Damage(100f));
            health.ApplyDamage(Damage(100f));

            Assert.IsFalse(health.IsAlive);
            Assert.AreEqual(1, defeats);
        }

        [Test]
        public void InvulnerableHealthIgnoresDamage()
        {
            health.Invulnerable = true;

            health.ApplyDamage(Damage(60f));

            Assert.AreEqual(100f, health.Current, Tolerance);
        }

        [Test]
        public void HealDoesNotExceedMaximumAndDoesNotReviveTheDefeated()
        {
            health.ApplyDamage(Damage(20f));
            Assert.AreEqual(20f, health.Heal(50f), Tolerance);

            health.ApplyDamage(Damage(100f));
            Assert.AreEqual(0f, health.Heal(50f), Tolerance);
            Assert.IsFalse(health.IsAlive);
        }

        [Test]
        public void ReviveRestoresFullHealth()
        {
            bool revived = false;
            health.Revived += () => revived = true;
            health.ApplyDamage(Damage(100f));

            health.Revive();

            Assert.IsTrue(health.IsAlive);
            Assert.IsTrue(revived);
            Assert.AreEqual(100f, health.Current, Tolerance);
        }

        [Test]
        public void DamageIsAnnouncedThroughCombatEvents()
        {
            DamageEvent received = default;
            int count = 0;
            void Handler(DamageEvent damage)
            {
                received = damage;
                count++;
            }

            CombatEvents.DamageDealt += Handler;
            try
            {
                health.ApplyDamage(Damage(100f));
            }
            finally
            {
                CombatEvents.DamageDealt -= Handler;
            }

            Assert.AreEqual(1, count);
            Assert.AreSame(host, received.Target);
            Assert.AreEqual(100f, received.Amount, Tolerance);
            Assert.IsTrue(received.Defeated);
        }

        [Test]
        public void SpendingManaReducesIt()
        {
            Assert.IsTrue(mana.TrySpend(30f));
            Assert.AreEqual(70f, mana.Current, Tolerance);
        }

        [Test]
        public void SpendingMoreManaThanAvailableIsRefusedAndReported()
        {
            float refused = -1f;
            mana.SpendRefused += amount => refused = amount;
            mana.TrySpend(95f);

            Assert.IsFalse(mana.TrySpend(12f));
            Assert.AreEqual(12f, refused, Tolerance);
            Assert.AreEqual(5f, mana.Current, Tolerance);
        }

        [Test]
        public void ManaDoesNotRegenerateDuringTheDelay()
        {
            mana.TrySpend(50f);

            mana.Tick(0.5f);

            Assert.AreEqual(50f, mana.Current, Tolerance);
        }

        [Test]
        public void ManaRegeneratesAfterTheDelay()
        {
            mana.TrySpend(50f);
            mana.Tick(1.01f);

            mana.Tick(2f);

            Assert.AreEqual(70f, mana.Current, Tolerance);
        }

        [Test]
        public void ManaRegenerationStopsAtTheMaximum()
        {
            mana.TrySpend(5f);
            mana.Tick(1.01f);

            mana.Tick(60f);

            Assert.AreEqual(100f, mana.Current, Tolerance);
        }
    }
}
