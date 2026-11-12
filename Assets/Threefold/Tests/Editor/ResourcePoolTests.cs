using NUnit.Framework;
using Threefold.Combat;

namespace Threefold.Tests
{
    public sealed class ResourcePoolTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void StartsFull()
        {
            var pool = new ResourcePool(80f);

            Assert.AreEqual(80f, pool.Current, Tolerance);
            Assert.AreEqual(1f, pool.Fraction, Tolerance);
            Assert.IsTrue(pool.IsFull);
        }

        [Test]
        public void RemoveNeverGoesBelowZeroAndReportsWhatWasRemoved()
        {
            var pool = new ResourcePool(50f);

            float removed = pool.Remove(70f);

            Assert.AreEqual(50f, removed, Tolerance);
            Assert.AreEqual(0f, pool.Current, Tolerance);
            Assert.IsTrue(pool.IsEmpty);
        }

        [Test]
        public void AddNeverExceedsMaximumAndReportsWhatWasAdded()
        {
            var pool = new ResourcePool(50f);
            pool.Remove(10f);

            float added = pool.Add(25f);

            Assert.AreEqual(10f, added, Tolerance);
            Assert.AreEqual(50f, pool.Current, Tolerance);
        }

        [Test]
        public void NegativeAmountsChangeNothing()
        {
            var pool = new ResourcePool(50f);
            pool.Remove(20f);

            Assert.AreEqual(0f, pool.Remove(-5f), Tolerance);
            Assert.AreEqual(0f, pool.Add(-5f), Tolerance);
            Assert.AreEqual(30f, pool.Current, Tolerance);
        }

        [Test]
        public void TrySpendSucceedsWhenAffordable()
        {
            var pool = new ResourcePool(100f);

            Assert.IsTrue(pool.TrySpend(40f));
            Assert.AreEqual(60f, pool.Current, Tolerance);
        }

        [Test]
        public void TrySpendTakesNothingWhenNotAffordable()
        {
            var pool = new ResourcePool(100f);
            pool.Remove(95f);

            Assert.IsFalse(pool.TrySpend(12f));
            Assert.AreEqual(5f, pool.Current, Tolerance);
        }

        [Test]
        public void TrySpendOfExactlyTheRemainderSucceeds()
        {
            var pool = new ResourcePool(36f);
            pool.TrySpend(12f);
            pool.TrySpend(12f);

            Assert.IsTrue(pool.TrySpend(12f));
            Assert.AreEqual(0f, pool.Current, Tolerance);
        }

        [Test]
        public void LoweringTheMaximumCapsTheCurrentValue()
        {
            var pool = new ResourcePool(100f);

            pool.SetMaximum(40f, false);

            Assert.AreEqual(40f, pool.Current, Tolerance);
        }
    }
}
