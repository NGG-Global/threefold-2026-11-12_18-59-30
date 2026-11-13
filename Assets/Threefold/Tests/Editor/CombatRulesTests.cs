using NUnit.Framework;
using Threefold.Combat;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The shared combat rules of the disciplines: who an attack hits, how heat builds, what a
    /// guard stops, how a charge scales, and where a drag aims.
    /// </summary>
    public sealed class CombatRulesTests
    {
        private static TargetCandidate At(float x, float z, bool alive = true, bool allied = false, bool self = false, float y = 0f)
        {
            return new TargetCandidate { Position = new Vector3(x, y, z), Alive = alive, Allied = allied, IsSelf = self };
        }

        private static Vector3 Heading(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private static AreaShape Sector(float reach, float arc)
        {
            return new AreaShape(Vector3.zero, Vector3.forward, reach, arc, 1.8f);
        }

        // --------------------------------------------------- target selection

        [Test]
        public void ATargetInFrontWithinReachIsHit()
        {
            var candidates = new[] { At(0f, 2f) };
            var selected = new int[4];
            Assert.AreEqual(1, TargetSelection.Select(Sector(2.4f, 70f), candidates, 1, 1, selected));
            Assert.AreEqual(0, selected[0]);
        }

        [Test]
        public void TargetsOutOfReachBehindOrTooHighAreNotHit()
        {
            var candidates = new[] { At(0f, 3f), At(0f, -1.5f), At(2f, 0.2f), At(0f, 1.5f, y: 2.5f) };
            var selected = new int[4];
            Assert.AreEqual(0, TargetSelection.Select(Sector(2.4f, 70f), candidates, candidates.Length, 4, selected));
        }

        [Test]
        public void AlliesTheDefeatedAndTheAttackerAreNeverHit()
        {
            var candidates = new[] { At(0f, 1f, allied: true), At(0f, 1.2f, alive: false), At(0f, 0.1f, self: true), At(0f, 2f) };
            var selected = new int[4];
            int count = TargetSelection.Select(Sector(2.4f, 120f), candidates, candidates.Length, 4, selected);

            Assert.AreEqual(1, count);
            Assert.AreEqual(3, selected[0]);
        }

        [Test]
        public void ASingleTargetStrikeTakesTheNearest()
        {
            var candidates = new[] { At(0.3f, 2.2f), At(-0.2f, 1.1f), At(0f, 1.8f) };
            var selected = new int[1];
            Assert.AreEqual(1, TargetSelection.Select(Sector(2.4f, 70f), candidates, candidates.Length, 1, selected));
            Assert.AreEqual(1, selected[0]);
        }

        [Test]
        public void AnAreaAttackHitsUpToItsLimitNearestFirst()
        {
            var candidates = new[] { At(0f, 6f), At(0f, 2f), At(1f, 4f), At(-1f, 5f), At(0f, 3f) };
            var selected = new int[8];
            int count = TargetSelection.Select(Sector(8f, 75f), candidates, candidates.Length, 3, selected);

            Assert.AreEqual(3, count);
            Assert.AreEqual(1, selected[0]);
            Assert.AreEqual(4, selected[1]);
            Assert.AreEqual(2, selected[2]);
        }

        [Test]
        public void AFullCircleHitsBehindAsWell()
        {
            var candidates = new[] { At(0f, -2f) };
            var selected = new int[1];
            Assert.AreEqual(1, TargetSelection.Select(Sector(3f, 360f), candidates, 1, 1, selected));
        }

        [Test]
        public void TheResultBufferCapsTheNumberOfTargets()
        {
            var candidates = new[] { At(0f, 1f), At(0f, 2f), At(0f, 3f) };
            var selected = new int[2];
            Assert.AreEqual(2, TargetSelection.Select(Sector(8f, 90f), candidates, 3, 10, selected));
        }

        [Test]
        public void NoCandidatesGivesNoHits()
        {
            Assert.AreEqual(0, TargetSelection.Select(Sector(3f, 90f), new TargetCandidate[0], 0, 4, new int[4]));
        }

        [Test]
        public void ASwingTurnsTowardsTheTargetNearestItsDirection()
        {
            var candidates = new[] { At(2f, 2f), At(0.3f, 2.5f), At(-3f, 0.5f) };
            int target = TargetSelection.FindFacingTarget(Vector3.zero, Vector3.forward, candidates, 3, 4f, 60f);
            Assert.AreEqual(1, target);
        }

        [Test]
        public void ASwingDoesNotTurnTowardsTargetsFarOffToTheSideOrOutOfRange()
        {
            var candidates = new[] { At(-3f, 0.2f), At(0f, 9f), At(0f, 2f, allied: true) };
            Assert.AreEqual(-1, TargetSelection.FindFacingTarget(Vector3.zero, Vector3.forward, candidates, 3, 4f, 60f));
        }

        // ---------------------------------------------------------------- heat

        [Test]
        public void HeatBuildsAndIgnitesAtCapacity()
        {
            var meter = new HeatMeter(new HeatSettings { capacity = 100f });
            Assert.IsFalse(meter.Add(40f));
            Assert.AreEqual(0.4f, meter.Fraction, 0.001f);
            Assert.IsFalse(meter.Add(40f));
            Assert.IsTrue(meter.Add(40f));
            Assert.IsTrue(meter.IsBurning);
        }

        [Test]
        public void HeatFadesAfterAPause()
        {
            var meter = new HeatMeter(new HeatSettings { decayDelay = 1.5f, decayPerSecond = 20f });
            meter.Add(50f);
            meter.Tick(1f);
            Assert.AreEqual(50f, meter.Heat, 0.001f);

            meter.Tick(1f);
            Assert.Less(meter.Heat, 50f);

            meter.Tick(10f);
            Assert.AreEqual(0f, meter.Heat, 0.001f);
        }

        [Test]
        public void ABurnDealsItsDamageInTicksAndThenEnds()
        {
            var settings = new HeatSettings { burnSeconds = 4f, burnDamagePerSecond = 6f, burnTickSeconds = 0.5f };
            var meter = new HeatMeter(settings);
            meter.Add(100f);

            float total = 0f;
            for (int i = 0; i < 100; i++)
            {
                total += meter.Tick(0.1f);
            }

            Assert.AreEqual(24f, total, 0.01f);
            Assert.IsFalse(meter.IsBurning);
            Assert.AreEqual(0f, meter.Heat, 0.001f);
        }

        [Test]
        public void HeatAddedWhileBurningIsIgnored()
        {
            var meter = new HeatMeter(new HeatSettings());
            meter.Add(100f);
            Assert.IsFalse(meter.Add(100f));

            for (int i = 0; i < 60; i++)
            {
                meter.Tick(0.1f);
            }

            Assert.AreEqual(0f, meter.Heat, 0.001f, "The burn must not have been extended.");
        }

        [Test]
        public void AnImmuneTargetNeverHeats()
        {
            var meter = new HeatMeter(new HeatSettings { susceptibility = 0f });
            Assert.IsFalse(meter.CanHeat);
            Assert.IsFalse(meter.Add(500f));
            Assert.AreEqual(0f, meter.Heat);
        }

        [Test]
        public void ASusceptibleTargetIgnitesSooner()
        {
            var straw = new HeatMeter(new HeatSettings { susceptibility = 2f });
            Assert.IsTrue(straw.Add(50f));
        }

        [Test]
        public void ClearingRemovesHeatAndBurn()
        {
            var meter = new HeatMeter(new HeatSettings());
            meter.Add(100f);
            meter.Clear();
            Assert.IsFalse(meter.IsBurning);
            Assert.AreEqual(0f, meter.Tick(1f));
        }

        // --------------------------------------------------------------- block

        [Test]
        public void AGuardCoversHitsFromTheFront()
        {
            // The defender faces +Z; the hit travels towards -Z, so it comes from the front.
            Assert.IsTrue(BlockRules.Covers(Vector3.forward, Vector3.back, 160f));
        }

        [Test]
        public void AGuardDoesNotCoverHitsFromBehindOrFarToTheSide()
        {
            Assert.IsFalse(BlockRules.Covers(Vector3.forward, Vector3.forward, 160f));
            Assert.IsFalse(BlockRules.Covers(Vector3.forward, Vector3.right, 160f));
        }

        [Test]
        public void AGuardCoversTheEdgeOfItsArc()
        {
            // Built by hand: Quaternion.Euler needs the engine, and these tests run without it.
            Vector3 fromSeventyDegrees = -Heading(70f);
            Vector3 fromNinetyDegrees = -Heading(92f);
            Assert.IsTrue(BlockRules.Covers(Vector3.forward, fromSeventyDegrees, 160f));
            Assert.IsFalse(BlockRules.Covers(Vector3.forward, fromNinetyDegrees, 160f));
        }

        [Test]
        public void DamageWithoutADirectionIsNotCovered()
        {
            Assert.IsFalse(BlockRules.Covers(Vector3.forward, Vector3.zero, 160f));
        }

        [Test]
        public void MitigationRemovesTheGivenShare()
        {
            Assert.AreEqual(4f, BlockRules.Mitigate(20f, 0.8f), 0.0001f);
            Assert.AreEqual(0f, BlockRules.Mitigate(20f, 1f), 0.0001f);
            Assert.AreEqual(20f, BlockRules.Mitigate(20f, 0f), 0.0001f);
        }

        // -------------------------------------------------------------- charge

        [Test]
        public void ChargePowerRisesToOneAndIsCapped()
        {
            Assert.AreEqual(0f, ChargeScaling.Power(0f, 1.1f), 0.0001f);
            Assert.AreEqual(0.5f, ChargeScaling.Power(0.55f, 1.1f), 0.0001f);
            Assert.AreEqual(1f, ChargeScaling.Power(5f, 1.1f), 0.0001f);
        }

        [Test]
        public void AChargedValueLiesBetweenItsUnchargedAndFullAmounts()
        {
            Assert.AreEqual(22f, ChargeScaling.Scale(22f, 48f, 0f), 0.0001f);
            Assert.AreEqual(35f, ChargeScaling.Scale(22f, 48f, 0.5f), 0.0001f);
            Assert.AreEqual(48f, ChargeScaling.Scale(22f, 48f, 3f), 0.0001f);
        }

        // ----------------------------------------------------------------- aim

        [Test]
        public void WithoutADragAnAreaAttackGoesStraightAheadOfTheCamera()
        {
            Vector3 direction = ChargedAim.GroundDirection(Vector2.zero, 90f);
            Assert.AreEqual(1f, direction.x, 0.0001f);
            Assert.AreEqual(0f, direction.z, 0.0001f);
        }

        [Test]
        public void ADragAimsRelativeToTheCamera()
        {
            // Camera looks along +Z: dragging left aims along -X, dragging down aims back along -Z.
            Vector3 left = ChargedAim.GroundDirection(new Vector2(-1f, 0f), 0f);
            Vector3 back = ChargedAim.GroundDirection(new Vector2(0f, -0.6f), 0f);
            Assert.AreEqual(-1f, left.x, 0.0001f);
            Assert.AreEqual(-1f, back.z, 0.0001f);

            // Camera looks along +X: dragging up aims along +X.
            Vector3 up = ChargedAim.GroundDirection(new Vector2(0f, 1f), 90f);
            Assert.AreEqual(1f, up.x, 0.0001f);
        }

        [Test]
        public void AGroundDirectionIsFlatAndOfUnitLength()
        {
            Vector3 direction = ChargedAim.GroundDirection(new Vector2(0.3f, 0.2f), 37f);
            Assert.AreEqual(0f, direction.y, 0.0001f);
            Assert.AreEqual(1f, direction.magnitude, 0.0001f);
        }

        [Test]
        public void ACameraAimedShotIsOffsetByAtMostItsLimits()
        {
            Vector2 none = ChargedAim.CameraOffsets(Vector2.zero, 25f, 12f);
            Vector2 full = ChargedAim.CameraOffsets(new Vector2(1f, 0f), 25f, 12f);
            Vector2 beyond = ChargedAim.CameraOffsets(new Vector2(0f, -3f), 25f, 12f);

            Assert.AreEqual(Vector2.zero, none);
            Assert.AreEqual(25f, full.x, 0.0001f);
            Assert.AreEqual(-12f, beyond.y, 0.0001f);
        }

        [Test]
        public void OffsetsTurnAnAimDirection()
        {
            Vector3 turned = ChargedAim.ApplyOffsets(Vector3.forward, 90f, 0f);
            Assert.AreEqual(1f, turned.x, 0.0001f);
            Assert.AreEqual(0f, turned.z, 0.0001f);

            Vector3 raised = ChargedAim.ApplyOffsets(Vector3.forward, 0f, 30f);
            Assert.AreEqual(0.5f, raised.y, 0.0001f);
            Assert.AreEqual(1f, raised.magnitude, 0.0001f);

            Vector3 same = ChargedAim.ApplyOffsets(new Vector3(0.3f, 0.2f, 0.9f).normalized, 0f, 0f);
            Assert.AreEqual(new Vector3(0.3f, 0.2f, 0.9f).normalized.x, same.x, 0.0001f);
        }

        // ------------------------------------------------------- camera assist

        [Test]
        public void CameraAssistDriftsTowardsTheTargetAtALimitedRate()
        {
            float step = CameraAssist.YawStep(0f, 30f, 40f, 0.1f, 4f, 60f);
            Assert.AreEqual(4f, step, 0.0001f);

            float left = CameraAssist.YawStep(0f, -30f, 40f, 0.1f, 4f, 60f);
            Assert.AreEqual(-4f, left, 0.0001f);
        }

        [Test]
        public void CameraAssistNeverSnapsOrOvershoots()
        {
            // 6 degrees off with a 4 degree dead zone: at most 2 degrees, however long the frame.
            float step = CameraAssist.YawStep(0f, 6f, 40f, 5f, 4f, 60f);
            Assert.AreEqual(2f, step, 0.0001f);
        }

        [Test]
        public void CameraAssistLeavesCentredAndFarOffTargetsAlone()
        {
            Assert.AreEqual(0f, CameraAssist.YawStep(0f, 3f, 40f, 0.1f, 4f, 60f));
            Assert.AreEqual(0f, CameraAssist.YawStep(0f, 100f, 40f, 0.1f, 4f, 60f));
            Assert.AreEqual(0f, CameraAssist.YawStep(0f, 30f, 0f, 0.1f, 4f, 60f), "Switched off.");
        }

        [Test]
        public void CameraAssistTakesTheShortWayRound()
        {
            float step = CameraAssist.YawStep(350f, 10f, 40f, 0.1f, 4f, 60f);
            Assert.Greater(step, 0f);
        }

        // -------------------------------------------------------------- damage

        [Test]
        public void StaggerDefaultsToTheDamageAndCanBeSetApart()
        {
            var plain = new DamageInfo(20f, Vector3.zero, Vector3.forward, null);
            var heavy = new DamageInfo(14f, Vector3.zero, Vector3.forward, null, 30f, SpellElement.Force);

            Assert.AreEqual(20f, plain.PoiseDamage);
            Assert.AreEqual(SpellElement.None, plain.Element);
            Assert.AreEqual(30f, heavy.PoiseDamage);
            Assert.AreEqual(SpellElement.Force, heavy.Element);
        }

        [Test]
        public void ChangingTheAmountKeepsStaggerAndElement()
        {
            var hit = new DamageInfo(14f, Vector3.one, Vector3.forward, null, 30f, SpellElement.Fire).WithAmount(5f);
            Assert.AreEqual(5f, hit.Amount);
            Assert.AreEqual(30f, hit.Stagger);
            Assert.AreEqual(SpellElement.Fire, hit.Element);
            Assert.AreEqual(Vector3.one, hit.Point);
        }

        [Test]
        public void ForceIsItsOwnKindOfMagic()
        {
            Assert.IsTrue(SpellElements.Matches(SpellElement.Force, SpellElement.Force));
            Assert.IsFalse(SpellElements.Matches(SpellElement.Arcane | SpellElement.Fire, SpellElement.Force));
        }
    }
}
