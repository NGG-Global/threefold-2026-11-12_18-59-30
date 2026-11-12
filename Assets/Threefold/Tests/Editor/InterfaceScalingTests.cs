using NUnit.Framework;
using Threefold.Interface;
using UnityEngine;

namespace Threefold.Tests
{
    public sealed class InterfaceScalingTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void FullScreenSafeAreaFillsTheScreen()
        {
            SafeAreaFitter.CalculateAnchors(new Rect(0f, 0f, 2400f, 1080f), 2400, 1080,
                out Vector2 anchorMin, out Vector2 anchorMax);

            Assert.AreEqual(Vector2.zero, anchorMin);
            Assert.AreEqual(Vector2.one, anchorMax);
        }

        [Test]
        public void NotchOnTheLeftInsetsTheLeftEdgeOnly()
        {
            SafeAreaFitter.CalculateAnchors(new Rect(120f, 0f, 2280f, 1080f), 2400, 1080,
                out Vector2 anchorMin, out Vector2 anchorMax);

            Assert.AreEqual(0.05f, anchorMin.x, Tolerance);
            Assert.AreEqual(0f, anchorMin.y, Tolerance);
            Assert.AreEqual(1f, anchorMax.x, Tolerance);
            Assert.AreEqual(1f, anchorMax.y, Tolerance);
        }

        [Test]
        public void InsetsOnAllSidesAreApplied()
        {
            SafeAreaFitter.CalculateAnchors(new Rect(100f, 50f, 1800f, 900f), 2000, 1000,
                out Vector2 anchorMin, out Vector2 anchorMax);

            Assert.AreEqual(0.05f, anchorMin.x, Tolerance);
            Assert.AreEqual(0.05f, anchorMin.y, Tolerance);
            Assert.AreEqual(0.95f, anchorMax.x, Tolerance);
            Assert.AreEqual(0.95f, anchorMax.y, Tolerance);
        }

        [Test]
        public void UnusableSafeAreaFallsBackToTheFullScreen()
        {
            SafeAreaFitter.CalculateAnchors(new Rect(0f, 0f, 0f, 0f), 0, 0,
                out Vector2 anchorMin, out Vector2 anchorMax);

            Assert.AreEqual(Vector2.zero, anchorMin);
            Assert.AreEqual(Vector2.one, anchorMax);
        }

        [Test]
        public void ScaleFollowsPixelDensity()
        {
            float scale = DensityCanvasScaler.CalculateScale(320f, 1440, 1f, 340f, 420f);

            Assert.AreEqual(2f, scale, Tolerance);
        }

        [Test]
        public void ScaleIsLimitedSoTheCanvasKeepsItsMinimumHeight()
        {
            // 480 dpi would give 3, but a 720 pixel high screen must keep 340 canvas units.
            float scale = DensityCanvasScaler.CalculateScale(480f, 720, 1f, 340f, 420f);

            Assert.AreEqual(720f / 340f, scale, Tolerance);
        }

        [Test]
        public void UnknownDensityUsesTheFallbackCanvasHeight()
        {
            float scale = DensityCanvasScaler.CalculateScale(0f, 840, 1f, 340f, 420f);

            Assert.AreEqual(2f, scale, Tolerance);
        }

        [Test]
        public void MultiplierScalesTheResult()
        {
            float scale = DensityCanvasScaler.CalculateScale(160f, 1080, 1.5f, 340f, 420f);

            Assert.AreEqual(1.5f, scale, Tolerance);
        }

        [Test]
        public void ScaleIsAlwaysPositive()
        {
            float scale = DensityCanvasScaler.CalculateScale(0f, 0, 1f, 340f, 420f);

            Assert.Greater(scale, 0f);
        }
    }
}
