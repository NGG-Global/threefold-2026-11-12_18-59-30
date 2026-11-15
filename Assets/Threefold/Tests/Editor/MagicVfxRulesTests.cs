using System.Collections.Generic;
using NUnit.Framework;
using Threefold.CombatFeedback;
using Threefold.Magic;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The plain rules behind the magical combat effects: the geometry of waves, ribbons and inflows, the quality
    /// levels and intensity, the Android budget, which arrival a magical object shows, and the starting timing of
    /// each discipline. Run outside Unity by Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class MagicVfxRulesTests
    {
        private const float Tolerance = 0.0001f;

        // --------------------------------------------------------------- geometry

        [Test]
        public void TurningForwardByNinetyDegreesPointsRight()
        {
            Vector3 right = MagicVfxGeometry.Turn(new Vector3(0f, 0f, 1f), 90f);
            Assert.AreEqual(1f, right.x, Tolerance);
            Assert.AreEqual(0f, right.z, Tolerance);
            Assert.AreEqual(0f, right.y, Tolerance);
        }

        [Test]
        public void AnArcPointIsAtItsRadiusAndItsMiddleIsStraightAhead()
        {
            Vector3 origin = new Vector3(2f, 1f, -3f);
            Vector3 forward = new Vector3(1f, 0f, 0f);
            Vector3 middle = MagicVfxGeometry.ArcPoint(origin, forward, 120f, 4f, 0.5f);
            Assert.AreEqual(6f, middle.x, Tolerance);
            Assert.AreEqual(-3f, middle.z, Tolerance);

            for (float across = 0f; across <= 1f; across += 0.125f)
            {
                Vector3 point = MagicVfxGeometry.ArcPoint(origin, forward, 120f, 4f, across);
                Assert.AreEqual(4f, (point - origin).magnitude, 0.001f);
                float angle = Vector3.Angle(forward, point - origin);
                Assert.LessOrEqual(angle, 60.01f, "inside the arc");
            }

            Vector3 edge = MagicVfxGeometry.ArcPoint(origin, forward, 120f, 4f, 1f);
            Assert.AreEqual(60f, Vector3.Angle(forward, edge - origin), 0.01f);
        }

        [Test]
        public void AWaveFrontLeavesQuicklyAndEndsAtItsReach()
        {
            Assert.AreEqual(0.4f, MagicVfxGeometry.FrontRadius(0.4f, 6f, 0f), Tolerance);
            Assert.AreEqual(6f, MagicVfxGeometry.FrontRadius(0.4f, 6f, 1f), Tolerance);
            Assert.AreEqual(6f, MagicVfxGeometry.FrontRadius(0.4f, 6f, 3f), Tolerance, "never past its reach");

            float last = 0f;
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float radius = MagicVfxGeometry.FrontRadius(0.4f, 6f, t);
                Assert.GreaterOrEqual(radius, last);
                last = radius;
            }

            Assert.Greater(MagicVfxGeometry.FrontRadius(0f, 1f, 0.5f), 0.5f, "more than half way at half time");
        }

        [Test]
        public void ARibbonSweepsAcrossItsArcEitherWay()
        {
            Assert.AreEqual(0f, MagicVfxGeometry.SweepAcross(0f, false), Tolerance);
            Assert.AreEqual(1f, MagicVfxGeometry.SweepAcross(1f, false), Tolerance);
            Assert.AreEqual(1f, MagicVfxGeometry.SweepAcross(0f, true), Tolerance);
            Assert.AreEqual(0f, MagicVfxGeometry.SweepAcross(1f, true), Tolerance);
            Assert.AreEqual(0.5f, MagicVfxGeometry.SweepAcross(0.5f, true), Tolerance);
        }

        [Test]
        public void ScriptedEmittersEmitTheSameNumberPerSecondAtAnyFrameRate()
        {
            foreach (float fps in new[] { 15f, 30f, 60f, 120f })
            {
                float carry = 0f;
                int total = 0;
                int frames = Mathf.RoundToInt(fps);
                for (int i = 0; i < frames; i++)
                {
                    total += MagicVfxGeometry.ParticlesThisFrame(70f, 1f / fps, ref carry);
                }

                Assert.AreEqual(70, total, 1, $"{fps} frames per second");
            }
        }

        [Test]
        public void ShellPointsLieOnTheShell()
        {
            Vector3 centre = new Vector3(1f, 2f, 3f);
            for (int i = 0; i < 12; i++)
            {
                Assert.AreEqual(0.8f, (MagicVfxGeometry.ShellPoint(centre, 0.8f, i, 12) - centre).magnitude, 0.001f);
            }
        }

        [Test]
        public void AnInflowIsLongEnoughToSeeButNotLongerThanItsLimit()
        {
            Vector3 centre = Vector3.zero;

            // Struck at the surface: pulled back along the way the magic came.
            Vector3 near = MagicVfxGeometry.InflowStart(new Vector3(0f, 0f, -0.2f), centre, new Vector3(0f, 0f, 1f), 0.6f, 2.5f);
            Assert.AreEqual(0.6f, (near - centre).magnitude, 0.001f);
            Assert.Less(near.z, 0f, "on the side it came from");

            // Struck from far away: shortened.
            Vector3 far = MagicVfxGeometry.InflowStart(new Vector3(0f, 0f, -10f), centre, Vector3.zero, 0.6f, 2.5f);
            Assert.AreEqual(2.5f, (far - centre).magnitude, 0.001f);

            // In between: where it struck.
            Vector3 mid = new Vector3(1f, 0f, 0f);
            Assert.AreEqual(mid, MagicVfxGeometry.InflowStart(mid, centre, Vector3.zero, 0.6f, 2.5f));
        }

        // ---------------------------------------------------------------- quality

        [Test]
        public void PerformanceDrawsFewerParticlesShorterTrailsAndNoSecondaries()
        {
            MagicVfxLevel performance = MagicVfxQuality.For(GraphicsPresets.Performance, 3, 1f);
            MagicVfxLevel balanced = MagicVfxQuality.For(GraphicsPresets.Balanced, 3, 1f);
            MagicVfxLevel quality = MagicVfxQuality.For(GraphicsPresets.Quality, 3, 1f);

            Assert.AreEqual(0.55f, performance.BurstShare, Tolerance);
            Assert.Less(performance.TrailTimeScale, 1f);
            Assert.IsFalse(performance.Secondaries);
            Assert.Less(performance.EmitterRateScale, 1f);

            Assert.AreEqual(1f, balanced.BurstShare, Tolerance);
            Assert.AreEqual(1f, balanced.TrailTimeScale, Tolerance);
            Assert.IsTrue(balanced.Secondaries);
            Assert.AreEqual(balanced.BurstShare, quality.BurstShare, Tolerance);
        }

        [Test]
        public void IntensityReducesEverythingButNeverBelowItsFloor()
        {
            MagicVfxLevel full = MagicVfxQuality.For(GraphicsPresets.Balanced, 3, 1f);
            MagicVfxLevel half = MagicVfxQuality.For(GraphicsPresets.Balanced, 3, 0.5f);
            MagicVfxLevel none = MagicVfxQuality.For(GraphicsPresets.Balanced, 3, 0f);

            Assert.Less(half.BurstShare, full.BurstShare);
            Assert.Less(half.TrailTimeScale, full.TrailTimeScale);
            Assert.AreEqual(MagicVfxQuality.MinIntensity, none.BurstShare, Tolerance, "clamped to the floor");
            Assert.Greater(none.TrailTimeScale, 0f);
            Assert.AreEqual(1f, MagicVfxQuality.ClampIntensity(float.NaN), Tolerance);
            Assert.AreEqual(1f, MagicVfxQuality.ClampIntensity(3f), Tolerance);
        }

        [Test]
        public void WithoutGraphicsLevelsEffectsAreDrawnAsAuthored()
        {
            MagicVfxLevel level = MagicVfxQuality.For(0, 0, 1f);
            Assert.AreEqual(1f, level.BurstShare, Tolerance);
            Assert.IsTrue(level.Secondaries);
        }

        // ----------------------------------------------------------------- budget

        private static MagicVfxSummary Clean(MagicVfxRole role)
        {
            return new MagicVfxSummary { Name = "Effect", Role = role, ParticleSystems = 2, MaxParticles = 20, Seconds = 0.5f, Pooled = true };
        }

        [Test]
        public void AnEffectWithinItsBudgetPasses()
        {
            var problems = new List<string>();
            foreach (MagicVfxRole role in System.Enum.GetValues(typeof(MagicVfxRole)))
            {
                Assert.AreEqual(0, MagicVfxBudget.Check(Clean(role), problems), role.ToString());
            }

            Assert.IsEmpty(problems);
        }

        [Test]
        public void EveryBudgetRuleIsReported()
        {
            MagicVfxSummary heavy = Clean(MagicVfxRole.Burst);
            heavy.Pooled = false;
            heavy.Lights = 1;
            heavy.Collisions = 2;
            heavy.AudioSources = 1;
            heavy.MissingMaterials = 1;
            heavy.BuiltInShaders = 3;
            heavy.SubEmitters = 1;
            heavy.ParticleSystems = 9;
            heavy.MaxParticles = 9000;
            heavy.TrailRenderers = 3;
            heavy.Seconds = 5f;

            var problems = new List<string>();
            Assert.AreEqual(11, MagicVfxBudget.Check(heavy, problems));
        }

        [Test]
        public void HeldEffectsAreNotLimitedInTime()
        {
            var problems = new List<string>();
            MagicVfxSummary barrier = Clean(MagicVfxRole.Barrier);
            barrier.Seconds = 30f;
            MagicVfxSummary trail = Clean(MagicVfxRole.Attached);
            trail.Seconds = 30f;
            Assert.AreEqual(0, MagicVfxBudget.Check(barrier, problems) + MagicVfxBudget.Check(trail, problems));
        }

        [Test]
        public void TheUnchangedPackPrefabsBreakTheBudget()
        {
            // The stock Free Slash slash, as counted in Docs/MagicVfx/PackInventory.md: 4 systems, 4000 particles,
            // a point light, collision. It must not be used without adaptation.
            var stock = new MagicVfxSummary
            {
                Name = "Slash Fire VFX", Role = MagicVfxRole.Burst, ParticleSystems = 4, MaxParticles = 4000, Lights = 2,
                Collisions = 1, Seconds = 1f, Pooled = false,
            };

            var problems = new List<string>();
            Assert.GreaterOrEqual(MagicVfxBudget.Check(stock, problems), 4);
        }

        // ------------------------------------------------------------------ world

        [Test]
        public void EachKindOfMagicArrivesInItsOwnWay()
        {
            Assert.AreEqual(WorldArrivalLook.ChargeInflow, WorldReactionVfx.Choose(MagicEffectKind.ArcaneCharge, EffectResponse.Applied));
            Assert.AreEqual(WorldArrivalLook.Heat, WorldReactionVfx.Choose(MagicEffectKind.Heat, EffectResponse.Applied));
            Assert.AreEqual(WorldArrivalLook.Force, WorldReactionVfx.Choose(MagicEffectKind.Force, EffectResponse.Applied));
            Assert.AreEqual(WorldArrivalLook.Brace, WorldReactionVfx.Choose(MagicEffectKind.Stabilize, EffectResponse.Applied));
        }

        [Test]
        public void MagicThatDidNotTakeShowsNoArrival()
        {
            foreach (EffectResponse response in new[]
                     {
                         EffectResponse.NoProperty, EffectResponse.Filtered, EffectResponse.Duplicate, EffectResponse.Resisted,
                         EffectResponse.Spent,
                     })
            {
                foreach (MagicEffectKind kind in System.Enum.GetValues(typeof(MagicEffectKind)))
                {
                    Assert.AreEqual(WorldArrivalLook.None, WorldReactionVfx.Choose(kind, response), $"{kind} {response}");
                }
            }
        }

        [Test]
        public void OnlyThePlayersOwnAttackShowsAnArrival()
        {
            Assert.IsTrue(WorldReactionVfx.ShowsArrival(0));
            Assert.IsFalse(WorldReactionVfx.ShowsArrival(1));
            Assert.IsFalse(WorldReactionVfx.ShowsArrival(3));
        }

        // ---------------------------------------------------------------- timing

        [Test]
        public void EachDisciplineHasItsOwnBodyTiming()
        {
            BodyEffectTiming arcane = MagicVfxDefaults.For(MagicDiscipline.Arcane);
            BodyEffectTiming flame = MagicVfxDefaults.For(MagicDiscipline.Flame);
            BodyEffectTiming aegis = MagicVfxDefaults.For(MagicDiscipline.Aegis);

            Assert.IsTrue(arcane.configured && flame.configured && aegis.configured);

            Assert.AreEqual(BodyEffectMoment.Effect, arcane.trailFrom, "Arcane: a precise trail from the effect moment");
            Assert.AreEqual(ChargePlacement.Effector, arcane.chargePlacement, "gathers in the casting hand");

            Assert.AreEqual(BodyEffectMoment.Windup, flame.trailFrom, "Flame: fire through the whole wind-up");
            Assert.Less(flame.trailStartIntensity, 1f, "intensifies towards contact");
            Assert.IsTrue(flame.lingerAfterStrike, "embers after the strike");
            Assert.AreEqual(ChargePlacement.BothForearms, flame.chargePlacement, "heat along the arms");

            Assert.IsTrue(aegis.gatherBeforeStrike, "Aegis: force gathers at the palm");
            Assert.AreEqual(BodyEffectMoment.None, aegis.swingFrom, "no slash");
            Assert.AreEqual(ChargePlacement.Chest, aegis.chargePlacement, "pressed inwards in front of the chest");

            foreach (BodyEffectTiming timing in new[] { arcane, flame, aegis })
            {
                Assert.Less(timing.chargeRadiusAtFull, timing.chargeRadiusAtStart, "every charge concentrates");
                Assert.LessOrEqual(timing.cancelDrainSeconds, timing.drainSeconds, "a cancellation leaves less behind");
            }
        }

        [Test]
        public void AnAssetUsesItsOwnTimingOnlyOnceConfigured()
        {
            var unset = new BodyEffectTiming();
            Assert.AreNotSame(unset, MagicVfxDefaults.Resolve(unset, MagicDiscipline.Flame), "an unconfigured timing is not used");
            Assert.AreEqual(BodyEffectMoment.Windup, MagicVfxDefaults.Resolve(unset, MagicDiscipline.Flame).trailFrom);

            BodyEffectTiming own = MagicVfxDefaults.For(MagicDiscipline.Flame);
            own.trailFrom = BodyEffectMoment.HitWindow;
            Assert.AreSame(own, MagicVfxDefaults.Resolve(own, MagicDiscipline.Flame));
            Assert.AreEqual(BodyEffectMoment.Windup, MagicVfxDefaults.Resolve(null, MagicDiscipline.Flame).trailFrom);
        }
    }
}
