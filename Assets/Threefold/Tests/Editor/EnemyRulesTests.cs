using NUnit.Framework;
using Threefold.Combat;
using Threefold.Enemies;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>Senses, positioning geometry, attack choice, meters, difficulty and teams. No Unity engine needed.</summary>
    public sealed class EnemyRulesTests
    {
        private const float Tolerance = 0.001f;

        // -------------------------------------------------------------- senses

        [Test]
        public void ViewConeSeesAheadButNotBehind()
        {
            Assert.IsTrue(EnemyRules.IsInsideViewCone(Vector3.forward, new Vector3(1f, 0f, 2f), 120f));
            Assert.IsFalse(EnemyRules.IsInsideViewCone(Vector3.forward, new Vector3(2f, 0f, 0.5f), 120f));
            Assert.IsFalse(EnemyRules.IsInsideViewCone(Vector3.forward, Vector3.back, 120f));
            Assert.IsTrue(EnemyRules.IsInsideViewCone(Vector3.forward, Vector3.back, 360f));
        }

        [Test]
        public void HeightDifferenceDoesNotNarrowTheViewCone()
        {
            Assert.IsTrue(EnemyRules.IsInsideViewCone(Vector3.forward, new Vector3(0f, 5f, 1f), 60f));
        }

        [Test]
        public void AwarenessRisesFasterUpCloseAndInstantlyWithinTheCloseSense()
        {
            float close = EnemyRules.AwarenessGainRate(2f, 2.5f, 13f, 2.5f, 0.5f);
            float near = EnemyRules.AwarenessGainRate(4f, 2.5f, 13f, 2.5f, 0.5f);
            float far = EnemyRules.AwarenessGainRate(12f, 2.5f, 13f, 2.5f, 0.5f);

            Assert.IsTrue(float.IsPositiveInfinity(close));
            Assert.Greater(near, far);
            Assert.AreEqual(0.5f, EnemyRules.AwarenessGainRate(20f, 2.5f, 13f, 2.5f, 0.5f), Tolerance);
        }

        [Test]
        public void AwarenessFillsDecaysAndStaysWithinRange()
        {
            var meter = new AwarenessMeter();
            EnemyTuning tuning = EnemyArchetypes.RuinsGuardian();

            meter.Observe(10f, 12f, tuning);
            Assert.AreEqual(1f, meter.Value);
            Assert.IsTrue(meter.IsFull);

            meter.Decay(100f, 0.2f);
            Assert.AreEqual(0f, meter.Value);

            meter.RaiseTo(0.4f);
            meter.RaiseTo(0.2f);
            Assert.AreEqual(0.4f, meter.Value, Tolerance, "RaiseTo never lowers awareness.");
        }

        // ---------------------------------------------------------------- poise

        [Test]
        public void PoiseBreaksWhenDamageAddsUpQuickly()
        {
            var poise = new PoiseMeter();
            poise.Configure(55f, 18f, 2.5f);

            Assert.IsFalse(poise.Absorb(20f));
            Assert.IsFalse(poise.Absorb(20f));
            Assert.IsTrue(poise.Absorb(20f), "Three bolts in quick succession stagger a Guardian.");
        }

        [Test]
        public void PoiseRecoversBetweenSlowHits()
        {
            var poise = new PoiseMeter();
            poise.Configure(55f, 18f, 2.5f);

            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(poise.Absorb(20f));
                poise.Tick(1.5f);
            }
        }

        [Test]
        public void AfterAStaggerPoiseCannotBreakAgainForAWhile()
        {
            var poise = new PoiseMeter();
            poise.Configure(1f, 0f, 1f);

            Assert.IsTrue(poise.Absorb(20f));
            Assert.IsFalse(poise.Absorb(20f));
            poise.Tick(1.1f);
            Assert.IsTrue(poise.Absorb(20f));
        }

        [Test]
        public void ZeroThresholdMeansNeverStaggered()
        {
            var poise = new PoiseMeter();
            poise.Configure(0f, 0f, 0f);
            Assert.IsFalse(poise.Absorb(1000f));
        }

        // ------------------------------------------------------------- geometry

        [Test]
        public void RotatingClockwiseAQuarterTurnPointsRight()
        {
            Vector3 right = EnemyRules.RotateAroundUp(Vector3.forward, 90f);
            Assert.AreEqual(1f, right.x, Tolerance);
            Assert.AreEqual(0f, right.z, Tolerance);
        }

        [Test]
        public void PointAroundKeepsTheDistanceAndPrefersTheEnemysSide()
        {
            var target = new Vector3(2f, 0f, 3f);
            var enemy = new Vector3(2f, 0f, -5f);

            Vector3 same = EnemyRules.PointAround(target, enemy, 6f, 0f);
            Assert.AreEqual(6f, EnemyRules.FlatDistance(target, same), Tolerance);
            Assert.Less(same.z, target.z, "Angle 0 stays on the enemy's side.");

            Vector3 turned = EnemyRules.PointAround(target, enemy, 6f, 70f);
            Assert.AreEqual(6f, EnemyRules.FlatDistance(target, turned), Tolerance);
        }

        [Test]
        public void StandPointCandidatesStartOnTheEnemysOwnSide()
        {
            Assert.AreEqual(0f, EnemyRules.GetStandPointAngle(0));
            Assert.Greater(EnemyRules.StandPointCandidateCount, 6);
        }

        [Test]
        public void TheChaseIsAbandonedOnlyWhenBothAreBeyondTheLeash()
        {
            Vector3 home = Vector3.zero;
            Assert.IsFalse(EnemyRules.ShouldGiveUpChase(home, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 30f), 22f));
            Assert.IsFalse(EnemyRules.ShouldGiveUpChase(home, new Vector3(0f, 0f, 23f), new Vector3(0f, 0f, 15f), 22f));
            Assert.IsTrue(EnemyRules.ShouldGiveUpChase(home, new Vector3(0f, 0f, 23f), new Vector3(0f, 0f, 30f), 22f));
            Assert.IsFalse(EnemyRules.ShouldGiveUpChase(home, new Vector3(0f, 0f, 90f), new Vector3(0f, 0f, 99f), 0f));
        }

        // -------------------------------------------------------------- attacks

        [Test]
        public void TheFirstEligibleAttackInTheListIsChosen()
        {
            EnemyAttackProfile[] attacks = EnemyArchetypes.RuinsGuardian().attacks;
            var cooldowns = new float[attacks.Length];

            Assert.AreEqual(1, EnemyRules.ChooseAttack(attacks, cooldowns, 2f, 1f, true), "At full health only the Slam.");
            Assert.AreEqual(0, EnemyRules.ChooseAttack(attacks, cooldowns, 2f, 0.5f, true), "Worn down, the Shockwave comes first.");
            Assert.AreEqual(0, EnemyRules.ChooseAttack(attacks, cooldowns, 2.8f, 0.5f, true), "The Shockwave reaches further.");
            Assert.AreEqual(-1, EnemyRules.ChooseAttack(attacks, cooldowns, 5f, 0.5f, true), "Out of reach.");
            Assert.AreEqual(-1, EnemyRules.ChooseAttack(attacks, cooldowns, 2f, 1f, false), "Out of sight.");

            cooldowns[0] = 3f;
            Assert.AreEqual(1, EnemyRules.ChooseAttack(attacks, cooldowns, 2f, 0.5f, true), "On cooldown, the next one is used.");

            cooldowns[1] = 1f;
            Assert.AreEqual(-1, EnemyRules.ChooseAttack(attacks, cooldowns, 2f, 0.5f, true));
            Assert.AreEqual(-1, EnemyRules.FirstReadyAttack(attacks, cooldowns, 0.5f));
        }

        [Test]
        public void TheStruckAreaFollowsReachArcAndHeight()
        {
            Vector3 origin = Vector3.zero;

            Assert.IsTrue(MeleeStrikeDefinition.IsInsideArea(origin, Vector3.forward, new Vector3(0f, 0f, 2f), 2.6f, 110f, 1.8f));
            Assert.IsTrue(MeleeStrikeDefinition.IsInsideArea(origin, Vector3.forward, new Vector3(1.5f, 0f, 1.5f), 2.6f, 110f, 1.8f));
            Assert.IsFalse(MeleeStrikeDefinition.IsInsideArea(origin, Vector3.forward, new Vector3(0f, 0f, -2f), 2.6f, 110f, 1.8f),
                "Behind the Guardian is safe from the Slam.");
            Assert.IsFalse(MeleeStrikeDefinition.IsInsideArea(origin, Vector3.forward, new Vector3(0f, 0f, 3f), 2.6f, 110f, 1.8f),
                "Beyond its reach.");
            Assert.IsFalse(MeleeStrikeDefinition.IsInsideArea(origin, Vector3.forward, new Vector3(0f, 2.5f, 1f), 2.6f, 110f, 1.8f),
                "On a ledge above it.");
            Assert.IsTrue(MeleeStrikeDefinition.IsInsideArea(origin, Vector3.forward, new Vector3(0f, 0f, -2f), 3.6f, 360f, 1.8f),
                "The Shockwave reaches all around.");
        }

        // ----------------------------------------------------------- difficulty

        [Test]
        public void ScalingMakesACopyAndLeavesTheOriginalAlone()
        {
            EnemyTuning original = EnemyArchetypes.RuinsGuardian();
            float windup = original.attacks[1].windupSeconds;

            EnemyTuning hard = original.Scaled(EnemyDifficultySettings.Hard());

            Assert.AreEqual(windup, original.attacks[1].windupSeconds);
            Assert.AreNotSame(original.attacks[1], hard.attacks[1]);
            Assert.Less(hard.attacks[1].windupSeconds, windup);
            Assert.Greater(hard.maxHealth, original.maxHealth);
            Assert.Greater(hard.awarenessGainNear, original.awarenessGainNear);
        }

        [Test]
        public void EasyGivesMoreTimeToReactThanHard()
        {
            EnemyTuning wisp = EnemyArchetypes.ArcaneWisp();
            EnemyTuning easy = wisp.Scaled(EnemyDifficultySettings.Easy());
            EnemyTuning hard = wisp.Scaled(EnemyDifficultySettings.Hard());

            Assert.Greater(easy.attacks[0].windupSeconds, hard.attacks[0].windupSeconds);
            Assert.Greater(easy.attacks[0].cooldownSeconds, hard.attacks[0].cooldownSeconds);
            Assert.Less(EnemyDifficultySettings.Easy().maxSimultaneousAttackers,
                EnemyDifficultySettings.Hard().maxSimultaneousAttackers);
        }

        [Test]
        public void EveryTelegraphLeavesTimeToReactEvenOnHard()
        {
            EnemyDifficultySettings hard = EnemyDifficultySettings.Hard();
            foreach (EnemyTuning tuning in new[] { EnemyArchetypes.RuinsGuardian(), EnemyArchetypes.ArcaneWisp() })
            {
                foreach (EnemyAttackProfile attack in tuning.Scaled(hard).attacks)
                {
                    Assert.GreaterOrEqual(attack.windupSeconds, 0.6f, $"'{attack.name}' must warn at least 0.6 s ahead.");

                    // The part after the lock is the dodge window.
                    float dodgeWindow = attack.windupSeconds * (1f - attack.trackFraction);
                    Assert.GreaterOrEqual(dodgeWindow, 0.19f, $"'{attack.name}' must lock early enough to be dodged.");
                }
            }
        }

        [Test]
        public void TheGuardianIsSlowerThanTheMageAndTheWispFasterOnFoot()
        {
            const float mageWalk = 4f;
            const float mageSprint = 7f;
            Assert.Less(EnemyArchetypes.RuinsGuardian().runSpeed, mageWalk, "The Guardian can be outwalked.");
            Assert.Less(EnemyArchetypes.ArcaneWisp().runSpeed, mageSprint, "A sprinting Mage can catch a Wisp.");
        }

        // ----------------------------------------------------------------- teams

        [Test]
        public void OnlyTheSameSideIsAllied()
        {
            Assert.IsTrue(CombatTeams.AreAllied(CombatTeam.Enemy, CombatTeam.Enemy));
            Assert.IsFalse(CombatTeams.AreAllied(CombatTeam.Enemy, CombatTeam.Player));
            Assert.IsFalse(CombatTeams.AreAllied(CombatTeam.Neutral, CombatTeam.Neutral), "Neutral is nobody's ally.");
        }
    }
}
