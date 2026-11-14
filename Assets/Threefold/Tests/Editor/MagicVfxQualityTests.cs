using NUnit.Framework;
using Threefold.CombatFeedback;
using Threefold.VisualStyle;

namespace Threefold.Tests
{
    public sealed class MagicVfxQualityTests
    {
        [Test]
        public void PerformanceKeepsOverHalfTheParticles()
        {
            Assert.AreEqual(0.55f, MagicVfxQuality.BurstShare(GraphicsPresets.Performance, 3), 0.001f);
            Assert.IsFalse(MagicVfxQuality.AllowSecondaries(GraphicsPresets.Performance, 3));
        }

        [Test]
        public void BalancedAndQualityKeepFullBursts()
        {
            Assert.AreEqual(1f, MagicVfxQuality.BurstShare(GraphicsPresets.Balanced, 3), 0.001f);
            Assert.AreEqual(1f, MagicVfxQuality.BurstShare(GraphicsPresets.Quality, 3), 0.001f);
            Assert.IsTrue(MagicVfxQuality.AllowSecondaries(GraphicsPresets.Balanced, 3));
        }

        [Test]
        public void MissingLevelsDefaultToFullShare()
        {
            Assert.AreEqual(1f, MagicVfxQuality.BurstShare(0, 0), 0.001f);
        }
    }
}
