using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.Magic;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The checks the editor's validation is built on, and the one-time update of discipline values
    /// generated before the effects on the world existed. Both are plain C#, so they run without Unity.
    /// </summary>
    public sealed class MagicalObjectRulesTests
    {
        private static List<string> Problems(MagicalPropertySettings settings)
        {
            var problems = new List<string>();
            int count = MagicalObjectRules.Validate(settings, problems);
            Assert.AreEqual(problems.Count, count, "The count returned matches the problems listed.");
            return problems;
        }

        private static void AssertOneProblem(MagicalPropertySettings settings, string expectedStart)
        {
            List<string> problems = Problems(settings);
            Assert.AreEqual(1, problems.Count, string.Join(" | ", problems));
            StringAssert.StartsWith(expectedStart, problems[0]);
        }

        // ---------------------------------------------------------- validation

        [Test]
        public void EveryExampleDefinitionIsValid()
        {
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                List<string> problems = Problems(WorldMagicDefaults.Settings(example));
                Assert.AreEqual(0, problems.Count, $"{example}: {string.Join(" | ", problems)}");
                Assert.IsNotEmpty(WorldMagicDefaults.Name(example));
                Assert.IsNotEmpty(WorldMagicDefaults.Description(example));
            }
        }

        [Test]
        public void MissingSettingsAndNoPropertyAreReported()
        {
            AssertOneProblem(null, "has no property settings");
            AssertOneProblem(new MagicalPropertySettings(), "has no property switched on");
        }

        [Test]
        public void AStructureNothingCanDamageIsReported()
        {
            var settings = new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings { forceDamage = 0f },
            };

            AssertOneProblem(settings, "structural: nothing can damage it");

            // Fire counts as a way to damage it, but only on something that can burn.
            settings.structuralSettings.burnDamagePerSecond = 5f;
            List<string> problems = Problems(settings);
            Assert.AreEqual(2, problems.Count, string.Join(" | ", problems));

            settings.thermal = true;
            settings.thermalSettings = new HeatSettings { flammable = true };
            Assert.AreEqual(0, Problems(settings).Count);
        }

        [Test]
        public void BrittlenessWhenHeatedNeedsSomethingThatHeats()
        {
            var settings = new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings { heatedBrittleness = 6f },
            };

            AssertOneProblem(settings, "structural: brittleness when heated is set");

            settings.thermal = true;
            settings.thermalSettings = new HeatSettings { flammable = false };
            Assert.AreEqual(0, Problems(settings).Count);

            // A thermal property that cannot heat does not help.
            settings.thermalSettings.susceptibility = 0f;
            Assert.AreEqual(2, Problems(settings).Count);
        }

        [Test]
        public void FireDamageNeedsSomethingThatBurns()
        {
            var settings = new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { flammable = false },
                structural = true,
                structuralSettings = new StructuralSettings { burnDamagePerSecond = 10f },
            };

            AssertOneProblem(settings, "structural: fire damage is set");
        }

        [Test]
        public void TheFractureThresholdMustLieBelowTheWeakenedOne()
        {
            var settings = new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings { weakenedFraction = 0.3f, fracturedFraction = 0.3f },
            };

            AssertOneProblem(settings, "structural: the fractured threshold");
        }

        [Test]
        public void APlatformThatCannotMoveIsReported()
        {
            var settings = new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings { minTravel = 2f, maxTravel = 2f },
            };

            AssertOneProblem(settings, "kinetic: there is no room to travel");

            settings.kineticSettings = new KineticSettings { resistance = 1f };
            AssertOneProblem(settings, "kinetic: resistance is 1");

            settings.kineticSettings = new KineticSettings { localAxis = Vector3.zero };
            AssertOneProblem(settings, "kinetic: the axis has no direction");

            settings.kineticSettings = new KineticSettings { localAxis = Vector3.up };
            AssertOneProblem(settings, "kinetic: the axis points up or down");

            settings.kineticSettings = new KineticSettings { mode = KineticMode.GroundPlane, maxRange = 0f };
            AssertOneProblem(settings, "kinetic: the range");

            // On the ground plane the axis and its limits are not used, so they are not checked.
            settings.kineticSettings = new KineticSettings
            {
                mode = KineticMode.GroundPlane, maxRange = 4f, localAxis = Vector3.zero, minTravel = 0f, maxTravel = 0f,
            };
            Assert.AreEqual(0, Problems(settings).Count);
        }

        [Test]
        public void ACrystalThatCannotChargeOrActivateIsReported()
        {
            var settings = new MagicalPropertySettings { arcane = true, arcaneSettings = new ArcaneSettings { susceptibility = 0f } };
            AssertOneProblem(settings, "arcane: susceptibility is 0");

            settings.arcaneSettings = new ArcaneSettings { activationThreshold = 0f };
            AssertOneProblem(settings, "arcane: the activation threshold");

            settings.arcaneSettings = new ArcaneSettings { activation = ArcaneActivation.Timed, activeSeconds = 0f };
            AssertOneProblem(settings, "arcane: a timed activation");
        }

        [Test]
        public void StabilityThatNeverDoesAnythingIsReported()
        {
            var settings = new MagicalPropertySettings
            {
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = false, unstableWhenStructure = StructuralTrigger.Never,
                    collapseWhenStructure = StructuralTrigger.Never,
                },
            };

            AssertOneProblem(settings, "stability: it starts stable and nothing can unsettle it");

            // Following a structure the object does not have is a different mistake, reported as such.
            settings.stabilitySettings = new StabilitySettings
            {
                startsUnstable = true, unstableWhenStructure = StructuralTrigger.Weakened,
                collapseWhenStructure = StructuralTrigger.Never,
            };
            AssertOneProblem(settings, "stability: it follows the state of a structure");

            // With a structure to follow, a stable start is fine.
            settings.stabilitySettings.startsUnstable = false;
            settings.structural = true;
            Assert.AreEqual(0, Problems(settings).Count);
        }

        // ------------------------------------------- answers and reachable states

        [Test]
        public void TheRulesAgreeWithTheLiveObjectAboutWhatItAnswers()
        {
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                MagicalPropertySettings settings = WorldMagicDefaults.Settings((ExampleObject)i);
                var set = new MagicalPropertySet(settings);
                for (int kind = 0; kind < 4; kind++)
                {
                    Assert.AreEqual(set.Answers((MagicEffectKind)kind), MagicalObjectRules.Answers(settings, (MagicEffectKind)kind),
                        $"{(ExampleObject)i} / {(MagicEffectKind)kind}");
                }
            }

            Assert.IsFalse(MagicalObjectRules.Answers(null, MagicEffectKind.Heat));
        }

        [Test]
        public void NoExampleAnswersEveryKindOfEffect()
        {
            // The brief: not every object responds to every discipline.
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                MagicalPropertySettings settings = WorldMagicDefaults.Settings((ExampleObject)i);
                int answered = 0;
                for (int kind = 0; kind < 4; kind++)
                {
                    answered += MagicalObjectRules.Answers(settings, (MagicEffectKind)kind) ? 1 : 0;
                }

                Assert.Greater(answered, 0, ((ExampleObject)i).ToString());
                Assert.Less(answered, 4, ((ExampleObject)i).ToString());
            }
        }

        [Test]
        public void ReachableStatesFollowFromTheProperties()
        {
            PropertyCondition crystal = MagicalObjectRules.Reachable(WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));
            Assert.AreEqual(PropertyCondition.Charged | PropertyCondition.Activated, crystal);

            PropertyCondition stone = MagicalObjectRules.Reachable(WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone));
            Assert.AreNotEqual(PropertyCondition.None, stone & PropertyCondition.Heated);
            Assert.AreEqual(PropertyCondition.None, stone & PropertyCondition.Burning, "The stone never burns.");
            Assert.AreNotEqual(PropertyCondition.None, stone & PropertyCondition.Destroyed);

            PropertyCondition barricade = MagicalObjectRules.Reachable(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            Assert.AreNotEqual(PropertyCondition.None, barricade & PropertyCondition.Burning);
            Assert.AreEqual(PropertyCondition.None, barricade & (PropertyCondition.Charged | PropertyCondition.Moving));

            Assert.AreEqual(PropertyCondition.None, MagicalObjectRules.Reachable(null));
        }

        [Test]
        public void AnObjectNeverMeetsAConditionOutsideItsReachableSet()
        {
            // Every example is put through everything the disciplines can do to it. Whatever state it
            // ends up in, it must be one the rules said it could reach: the validation relies on that.
            PropertyCondition[] all =
            {
                PropertyCondition.Heated, PropertyCondition.Burning, PropertyCondition.Weakened, PropertyCondition.Fractured,
                PropertyCondition.Destroyed, PropertyCondition.Charged, PropertyCondition.Activated, PropertyCondition.Moving,
                PropertyCondition.AtTravelLimit, PropertyCondition.Unstable, PropertyCondition.Braced, PropertyCondition.Collapsed,
            };

            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                MagicalPropertySettings settings = WorldMagicDefaults.Settings((ExampleObject)i);
                PropertyCondition reachable = MagicalObjectRules.Reachable(settings);
                var set = new MagicalPropertySet(settings) { KineticAxis = Vector3.forward };
                int eventId = 1;

                for (int round = 0; round < 40; round++)
                {
                    var kind = (MagicEffectKind)(round % 4);
                    set.Apply(new MagicEffect(kind, 30f + round * 5f, Vector3.forward, 1.5f, Vector3.zero, null, -1,
                        EffectTargets.All, eventId++));
                    set.Tick(0.25f);

                    foreach (PropertyCondition condition in all)
                    {
                        if (set.Meets(condition))
                        {
                            Assert.AreNotEqual(PropertyCondition.None, reachable & condition,
                                $"{(ExampleObject)i} met {condition} in round {round}, which the rules call unreachable.");
                        }
                    }
                }
            }
        }

        // ------------------------------------ discipline values from before LM-03

        /// <summary>A discipline's values as an asset generated before the world effects existed would hold them.</summary>
        private static void OldValues(MagicDiscipline discipline, out MeleeComboProfile melee, out GuardEffectProfile guard,
            out ProjectileProfile projectile)
        {
            melee = DisciplineDefaults.Melee(discipline);
            foreach (MeleeStepProfile step in melee.steps)
            {
                step.force = 0f;
            }

            guard = new GuardEffectProfile();
            projectile = DisciplineDefaults.Charged(discipline).Projectile;
            if (projectile != null)
            {
                projectile.arcaneChargeMin = 0f;
                projectile.arcaneChargeMax = 0f;
            }
        }

        [Test]
        public void OldAegisValuesGainForceAndTheBrace()
        {
            OldValues(MagicDiscipline.Aegis, out MeleeComboProfile melee, out GuardEffectProfile guard, out ProjectileProfile projectile);
            Assert.IsNull(projectile, "Aegis has no projectile attack.");
            Assert.IsFalse(guard.HasEffect);

            Assert.IsTrue(DisciplineDefaults.FillWorldEffects(MagicDiscipline.Aegis, melee, guard, projectile));

            MeleeComboProfile defaults = DisciplineDefaults.Melee(MagicDiscipline.Aegis);
            for (int i = 0; i < defaults.StepCount; i++)
            {
                Assert.AreEqual(defaults.steps[i].force, melee.steps[i].force, 0.0001f, $"Step {i}");
                Assert.Greater(melee.steps[i].force, 0f, $"Step {i}");
            }

            GuardEffectProfile guardDefaults = DisciplineDefaults.Guard(MagicDiscipline.Aegis);
            Assert.IsTrue(guard.HasEffect);
            Assert.AreEqual(guardDefaults.stabilize, guard.stabilize, 0.0001f);
            Assert.AreEqual(guardDefaults.radius, guard.radius, 0.0001f);
            Assert.AreEqual(guardDefaults.pulseSeconds, guard.pulseSeconds, 0.0001f);

            // A second pass finds nothing left to do.
            Assert.IsFalse(DisciplineDefaults.FillWorldEffects(MagicDiscipline.Aegis, melee, guard, projectile));
        }

        [Test]
        public void OldArcaneValuesGainTheArrowsCharge()
        {
            OldValues(MagicDiscipline.Arcane, out MeleeComboProfile melee, out GuardEffectProfile guard, out ProjectileProfile projectile);

            Assert.IsTrue(DisciplineDefaults.FillWorldEffects(MagicDiscipline.Arcane, melee, guard, projectile));

            ProjectileProfile defaults = DisciplineDefaults.Charged(MagicDiscipline.Arcane).Projectile;
            Assert.AreEqual(defaults.arcaneChargeMin, projectile.arcaneChargeMin, 0.0001f);
            Assert.AreEqual(defaults.arcaneChargeMax, projectile.arcaneChargeMax, 0.0001f);
            Assert.Greater(projectile.arcaneChargeMax, projectile.arcaneChargeMin);
            Assert.IsFalse(guard.HasEffect, "An Arcane guard does not brace.");

            foreach (MeleeStepProfile step in melee.steps)
            {
                Assert.AreEqual(0f, step.force, "Arcane swings carry no force.");
            }
        }

        [Test]
        public void OldFlameValuesNeedNothing()
        {
            OldValues(MagicDiscipline.Flame, out MeleeComboProfile melee, out GuardEffectProfile guard, out ProjectileProfile projectile);
            Assert.IsFalse(DisciplineDefaults.FillWorldEffects(MagicDiscipline.Flame, melee, guard, projectile));
            Assert.IsFalse(guard.HasEffect);
        }

        [Test]
        public void ValuesTunedSinceAreKept()
        {
            OldValues(MagicDiscipline.Aegis, out MeleeComboProfile melee, out GuardEffectProfile guard, out _);
            melee.steps[0].force = 2.5f;
            guard.stabilize = 3f;
            guard.radius = 9f;

            DisciplineDefaults.FillWorldEffects(MagicDiscipline.Aegis, melee, guard, null);

            Assert.AreEqual(2.5f, melee.steps[0].force, 0.0001f, "A force set by hand is kept.");
            Assert.AreEqual(3f, guard.stabilize, 0.0001f, "A brace set by hand is kept.");
            Assert.AreEqual(9f, guard.radius, 0.0001f);

            OldValues(MagicDiscipline.Arcane, out melee, out guard, out ProjectileProfile projectile);
            projectile.arcaneChargeMax = 55f;
            DisciplineDefaults.FillWorldEffects(MagicDiscipline.Arcane, melee, guard, projectile);
            Assert.AreEqual(0f, projectile.arcaneChargeMin, 0.0001f, "A charge set by hand is kept as it is.");
            Assert.AreEqual(55f, projectile.arcaneChargeMax, 0.0001f);
        }

        [Test]
        public void ACombThatWasShortenedIsFilledOnlyWhereItHasSwings()
        {
            OldValues(MagicDiscipline.Aegis, out MeleeComboProfile melee, out GuardEffectProfile guard, out _);
            melee.steps = new[] { melee.steps[0] };

            Assert.DoesNotThrow(() => DisciplineDefaults.FillWorldEffects(MagicDiscipline.Aegis, melee, guard, null));
            Assert.Greater(melee.steps[0].force, 0f);

            // Nothing to fill and nothing to break when the values are missing altogether.
            Assert.IsFalse(DisciplineDefaults.FillWorldEffects(MagicDiscipline.Flame, null, null, null));
        }

        [Test]
        public void SomethingFlammableWithNoTimeToBurnIsReported()
        {
            var settings = new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { capacity = 50f, flammable = true, burnSeconds = 0f },
            };

            AssertOneProblem(settings, "thermal: it is flammable but its burn time is 0");
            Assert.AreEqual(PropertyCondition.Heated, MagicalObjectRules.Reachable(settings), "It can be heated but never burns.");

            // The rule matches what the object really does: at full heat it goes cold instead of burning.
            var set = new MagicalPropertySet(settings);
            set.Apply(MagicEffect.Of(MagicEffectKind.Heat, 500f, 1));
            Assert.IsFalse(set.Meets(PropertyCondition.Burning));

            // Fire as the only way to damage a structure is then no way at all.
            settings.structural = true;
            settings.structuralSettings = new StructuralSettings { forceDamage = 0f, burnDamagePerSecond = 10f };
            List<string> problems = Problems(settings);
            Assert.AreEqual(3, problems.Count, string.Join(" | ", problems));
            StringAssert.StartsWith("structural: nothing can damage it", problems[1]);
            StringAssert.StartsWith("structural: fire damage is set", problems[2]);
        }

        [Test]
        public void SomethingThatCannotHeatReachesNoThermalState()
        {
            var settings = new MagicalPropertySettings
            {
                thermal = true, thermalSettings = new HeatSettings { susceptibility = 0f, flammable = true },
            };

            Assert.AreEqual(PropertyCondition.None, MagicalObjectRules.Reachable(settings));
        }
    }
}
