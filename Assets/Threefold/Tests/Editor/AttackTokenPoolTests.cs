using NUnit.Framework;
using Threefold.Enemies;

namespace Threefold.Tests
{
    /// <summary>The rules that keep a fight readable: how many attack at once, and how far apart.</summary>
    public sealed class AttackTokenPoolTests
    {
        private const float Step = 0.1f;

        [Test]
        public void GrantsAtMostOneTokenPerUpdate()
        {
            var pool = new AttackTokenPool(2, 2, 0f, 0f);
            pool.Request(1, AttackKind.Ranged, true);
            pool.Request(2, AttackKind.Ranged, true);

            pool.Update(Step);
            Assert.AreEqual(1, pool.HolderCount);

            pool.Update(Step);
            Assert.AreEqual(2, pool.HolderCount);
        }

        [Test]
        public void NoMoreThanTheMaximumAttackAtOnce()
        {
            var pool = new AttackTokenPool(2, 2, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                pool.Request(1, AttackKind.Ranged, true);
                pool.Request(2, AttackKind.Ranged, true);
                pool.Request(3, AttackKind.Ranged, true);
                pool.Update(Step);
            }

            Assert.AreEqual(2, pool.HolderCount);
            Assert.IsFalse(pool.Holds(3));

            pool.Release(1);
            pool.Request(3, AttackKind.Ranged, true);
            pool.Update(Step);
            Assert.IsTrue(pool.Holds(3), "A released token goes to the enemy that was waiting.");
        }

        [Test]
        public void OnlyOneMeleeAttackAtATimeWhileRangedMayJoin()
        {
            var pool = new AttackTokenPool(3, 1, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                pool.Request(1, AttackKind.Melee, true);
                pool.Request(2, AttackKind.Melee, true);
                pool.Request(3, AttackKind.Ranged, true);
                pool.Update(Step);
            }

            Assert.AreEqual(1, pool.MeleeHolderCount);
            Assert.AreEqual(2, pool.HolderCount);
            Assert.IsTrue(pool.Holds(3));
            Assert.IsFalse(pool.HasFreeSlot(AttackKind.Melee));
            Assert.IsTrue(pool.HasFreeSlot(AttackKind.Ranged));
        }

        [Test]
        public void AttacksStartAtLeastTheMinimumGapApart()
        {
            var pool = new AttackTokenPool(3, 3, 0.5f, 0f);
            pool.Request(1, AttackKind.Ranged, true);
            pool.Request(2, AttackKind.Ranged, true);
            pool.Update(Step);
            Assert.AreEqual(1, pool.HolderCount);

            for (int i = 0; i < 4; i++)
            {
                pool.Request(2, AttackKind.Ranged, true);
                pool.Update(Step);
            }

            Assert.AreEqual(1, pool.HolderCount, "0.4 s after the first start, the second must still wait.");

            pool.Request(2, AttackKind.Ranged, true);
            pool.Update(Step + 0.01f);
            Assert.AreEqual(2, pool.HolderCount);
        }

        [Test]
        public void AnEnemyOffScreenWaitsLonger()
        {
            var pool = new AttackTokenPool(2, 2, 0f, 1.5f);
            for (int i = 0; i < 14; i++)
            {
                pool.Request(1, AttackKind.Ranged, false);
                pool.Update(Step);
            }

            Assert.IsFalse(pool.Holds(1), "Off screen for 1.4 s.");

            for (int i = 0; i < 3; i++)
            {
                pool.Request(1, AttackKind.Ranged, false);
                pool.Update(Step);
            }

            Assert.IsTrue(pool.Holds(1));
        }

        [Test]
        public void TheLongestWaitingEnemyIsServedFirst()
        {
            var pool = new AttackTokenPool(1, 1, 0f, 0f);
            pool.Request(9, AttackKind.Melee, true);
            pool.Update(Step);
            Assert.IsTrue(pool.Holds(9));

            pool.Request(1, AttackKind.Melee, true);
            pool.Update(Step);
            pool.Request(1, AttackKind.Melee, true);
            pool.Request(2, AttackKind.Melee, true);
            pool.Update(Step);

            pool.Release(9);
            pool.Request(2, AttackKind.Melee, true);
            pool.Request(1, AttackKind.Melee, true);
            pool.Update(Step);

            Assert.IsTrue(pool.Holds(1));
            Assert.IsFalse(pool.Holds(2));
        }

        [Test]
        public void RequestsThatAreNotRepeatedExpire()
        {
            var pool = new AttackTokenPool(1, 1, 0f, 0f);
            pool.Request(9, AttackKind.Melee, true);
            pool.Update(Step);

            pool.Request(1, AttackKind.Melee, true);
            pool.Update(0.6f);

            Assert.AreEqual(0, pool.PendingCount);
        }

        [Test]
        public void ReleasingWithoutATokenIsHarmlessAndClearForgetsEverything()
        {
            var pool = new AttackTokenPool(2, 1, 0f, 0f);
            pool.Release(5);

            pool.Request(1, AttackKind.Melee, true);
            pool.Update(Step);
            pool.Request(2, AttackKind.Ranged, true);

            pool.Clear();

            Assert.AreEqual(0, pool.HolderCount);
            Assert.AreEqual(0, pool.PendingCount);
        }

        [Test]
        public void AnUnlimitedPoolServesEveryone()
        {
            AttackTokenPool pool = AttackTokenPool.Unlimited();
            for (int i = 0; i < 6; i++)
            {
                for (int id = 1; id <= 5; id++)
                {
                    pool.Request(id, AttackKind.Melee, false);
                }

                pool.Update(Step);
            }

            Assert.AreEqual(5, pool.HolderCount);
        }
    }
}
