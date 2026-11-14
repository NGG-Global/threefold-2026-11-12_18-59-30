using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The magical properties one at a time and together: how each answers effects, where its
    /// thresholds lie, what it refuses, what happens when the same event arrives twice, and what is
    /// left when an object is destroyed.
    /// </summary>
    public sealed class WorldPropertyTests
    {
        private readonly List<string> transitions = new List<string>();

        private MagicalPropertySet Create(MagicalPropertySettings settings)
        {
            transitions.Clear();
            var set = new MagicalPropertySet(settings);
            set.StateChanged += (kind, from, to) => transitions.Add($"{kind}:{from}>{to}");
            return set;
        }

        private static MagicEffect Heat(float amount, int eventId = 0) => MagicEffect.Of(MagicEffectKind.Heat, amount, eventId);

        private static MagicEffect Charge(float amount, int eventId = 0) =>
            MagicEffect.Of(MagicEffectKind.ArcaneCharge, amount, eventId);

        private static MagicEffect Force(float amount, Vector3 direction, int eventId = 0)
        {
            return new MagicEffect(MagicEffectKind.Force, amount, direction, 0f, Vector3.zero, null, -1, EffectTargets.All, eventId);
        }

        private static MagicEffect Brace(float strength, float seconds, int eventId = 0)
        {
            return new MagicEffect(MagicEffectKind.Stabilize, strength, Vector3.zero, seconds, Vector3.zero, null, -1,
                EffectTargets.Objects, eventId);
        }

        private static void Run(MagicalPropertySet set, float seconds, float step = 0.1f)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += step)
            {
                set.Tick(step);
            }
        }

        // ------------------------------------------------------------- thermal

        [Test]
        public void HeatMovesAnObjectFromUnheatedToHeatedToBurning()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { capacity = 100f, heatedFraction = 0.5f, flammable = true },
            });

            Assert.AreEqual(ThermalState.Unheated, set.Thermal.State);
            Assert.AreEqual(EffectResponse.Applied, set.Apply(Heat(40f)));
            Assert.AreEqual(ThermalState.Unheated, set.Thermal.State, "Below the heated threshold.");

            set.Apply(Heat(20f));
            Assert.AreEqual(ThermalState.Heated, set.Thermal.State);

            set.Apply(Heat(40f));
            Assert.AreEqual(ThermalState.Burning, set.Thermal.State);
            CollectionAssert.AreEqual(new[] { "Thermal:0>1", "Thermal:1>2" }, transitions);
        }

        [Test]
        public void AHeatedObjectCoolsBackToUnheated()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { capacity = 100f, heatedFraction = 0.5f, decayDelay = 1f, decayPerSecond = 20f },
            });

            set.Apply(Heat(70f));
            Assert.AreEqual(ThermalState.Heated, set.Thermal.State);
            Assert.IsFalse(set.IsAtRest);

            Run(set, 6f);
            Assert.AreEqual(ThermalState.Unheated, set.Thermal.State);
            Assert.IsTrue(set.IsAtRest, "A cold object needs no more updates.");
            CollectionAssert.AreEqual(new[] { "Thermal:0>1", "Thermal:1>0" }, transitions);
        }

        [Test]
        public void AnObjectThatCannotBurnOnlyBecomesHeated()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { capacity = 100f, heatedFraction = 0.6f, flammable = false },
            });

            set.Apply(Heat(500f));
            Assert.AreEqual(ThermalState.Heated, set.Thermal.State);
            Assert.IsFalse(set.Thermal.IsBurning);
            Assert.AreEqual(100f, set.Thermal.Heat, 0.001f);
        }

        [Test]
        public void ABurnEndsByItselfAndTheObjectStartsFromCold()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { capacity = 50f, burnSeconds = 3f },
            });

            set.Apply(Heat(60f));
            Assert.AreEqual(ThermalState.Burning, set.Thermal.State);
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Heat(60f)), "Heat added while burning is refused.");

            Run(set, 4f);
            Assert.AreEqual(ThermalState.Unheated, set.Thermal.State);
        }

        // ---------------------------------------------------------- structural

        [Test]
        public void ForceWearsAStructureDownThroughItsStates()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 100f, weakenedFraction = 0.6f, fracturedFraction = 0.25f, forceThreshold = 1f, forceDamage = 15f,
                },
            });

            // Each force of 3 is 2 above the threshold: 30 integrity.
            set.Apply(Force(3f, Vector3.forward));
            Assert.AreEqual(StructuralState.Intact, set.Structural.State);
            set.Apply(Force(3f, Vector3.forward));
            Assert.AreEqual(StructuralState.Weakened, set.Structural.State);
            set.Apply(Force(3f, Vector3.forward));
            Assert.AreEqual(StructuralState.Fractured, set.Structural.State);
            set.Apply(Force(3f, Vector3.forward));
            Assert.AreEqual(StructuralState.Destroyed, set.Structural.State);
            CollectionAssert.AreEqual(new[] { "Structural:0>1", "Structural:1>2", "Structural:2>3" }, transitions);
        }

        [Test]
        public void AForceAtOrBelowTheThresholdDoesNoHarm()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings { integrity = 100f, forceThreshold = 3.5f, forceDamage = 40f },
            });

            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Force(3.5f, Vector3.forward)));
            Assert.AreEqual(100f, set.Structural.Integrity);

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Force(3.6f, Vector3.forward)));
            Assert.Less(set.Structural.Integrity, 100f);
        }

        [Test]
        public void ABrittleStructureBreaksSoonerThanAToughOne()
        {
            var tough = new StructuralProperty(new StructuralSettings { forceThreshold = 0f, forceDamage = 10f, brittleness = 0.5f });
            var brittle = new StructuralProperty(new StructuralSettings { forceThreshold = 0f, forceDamage = 10f, brittleness = 3f });

            Assert.AreEqual(10f, tough.ApplyForce(2f, false), 0.001f);
            Assert.AreEqual(60f, brittle.ApplyForce(2f, false), 0.001f);
        }

        [Test]
        public void TheStateThresholdsAreExact()
        {
            var structure = new StructuralProperty(new StructuralSettings { integrity = 100f, weakenedFraction = 0.6f, fracturedFraction = 0.25f });
            structure.Damage(39.9f);
            Assert.AreEqual(StructuralState.Intact, structure.State);
            structure.Damage(0.1f);
            Assert.AreEqual(StructuralState.Weakened, structure.State, "Exactly at 60%.");
            structure.Damage(35f);
            Assert.AreEqual(StructuralState.Fractured, structure.State, "Exactly at 25%.");
            structure.Damage(24.9f);
            Assert.AreEqual(StructuralState.Fractured, structure.State);
            structure.Damage(0.1f);
            Assert.AreEqual(StructuralState.Destroyed, structure.State);
        }

        // -------------------------------------------------------------- arcane

        [Test]
        public void ChargeMovesAnObjectFromUnchargedToChargedToActivated()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings { activationThreshold = 100f, activation = ArcaneActivation.Latch },
            });

            set.Apply(Charge(40f));
            Assert.AreEqual(ArcaneState.Charged, set.Arcane.State);
            set.Apply(Charge(40f));
            Assert.AreEqual(ArcaneState.Charged, set.Arcane.State);
            set.Apply(Charge(40f));
            Assert.AreEqual(ArcaneState.Activated, set.Arcane.State);
            CollectionAssert.AreEqual(new[] { "Arcane:0>1", "Arcane:1>2" }, transitions);
        }

        [Test]
        public void ChargeDrainsAwayIfItIsNotToppedUp()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings { activationThreshold = 100f, decayDelay = 1f, decayPerSecond = 20f },
            });

            set.Apply(Charge(60f));
            Run(set, 0.9f);
            Assert.AreEqual(60f, set.Arcane.Charge, 0.001f, "Within the delay.");

            Run(set, 5f);
            Assert.AreEqual(ArcaneState.Uncharged, set.Arcane.State);
            Assert.IsTrue(set.IsAtRest);
        }

        [Test]
        public void ALatchedActivationStaysForGood()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings { activationThreshold = 100f, activation = ArcaneActivation.Latch },
            });

            set.Apply(Charge(100f));
            Run(set, 60f);
            Assert.AreEqual(ArcaneState.Activated, set.Arcane.State);
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Charge(50f)), "It is already switched on.");
        }

        [Test]
        public void ATimedActivationGoesDarkAgain()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings { activationThreshold = 100f, activation = ArcaneActivation.Timed, activeSeconds = 5f },
            });

            set.Apply(Charge(100f));
            Run(set, 4f);
            Assert.AreEqual(ArcaneState.Activated, set.Arcane.State);
            Run(set, 2f);
            Assert.AreEqual(ArcaneState.Uncharged, set.Arcane.State);
            CollectionAssert.AreEqual(new[] { "Arcane:0>2", "Arcane:2>0" }, transitions);
        }

        [Test]
        public void AnActivationThatLastsWhileChargedCanBeToppedUp()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                arcane = true,
                arcaneSettings = new ArcaneSettings
                {
                    activationThreshold = 100f, activation = ArcaneActivation.WhileCharged, decayDelay = 0f, decayPerSecond = 25f,
                },
            });

            set.Apply(Charge(100f));
            Run(set, 3f);
            Assert.AreEqual(ArcaneState.Activated, set.Arcane.State);
            Assert.AreEqual(EffectResponse.Applied, set.Apply(Charge(100f)));
            Run(set, 3f);
            Assert.AreEqual(ArcaneState.Activated, set.Arcane.State, "Topped up before it drained.");
            Run(set, 2f);
            Assert.AreEqual(ArcaneState.Uncharged, set.Arcane.State);
        }

        // ------------------------------------------------------------- kinetic

        private static MagicalPropertySettings Platform()
        {
            return new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings
                {
                    resistance = 0.2f, minimumForce = 1.5f, mode = KineticMode.Axis, minTravel = 0f, maxTravel = 6f,
                    maxStepPerPush = 4f, moveSpeed = 4f,
                },
            };
        }

        [Test]
        public void AForceMovesAnObjectAlongItsAxisByWhatItsResistanceLeaves()
        {
            MagicalPropertySet set = Create(Platform());
            set.KineticAxis = Vector3.forward;

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Force(2.5f, Vector3.forward)));
            Assert.AreEqual(KineticState.Moving, set.Kinetic.State);

            Run(set, 2f);
            Assert.AreEqual(KineticState.Resting, set.Kinetic.State);
            Assert.AreEqual(2f, set.Kinetic.Offset.z, 0.001f, "2.5 less 20%.");
            CollectionAssert.AreEqual(new[] { "Kinetic:0>1", "Kinetic:1>0" }, transitions);
        }

        [Test]
        public void TheGlideIsGradualNotInstant()
        {
            MagicalPropertySet set = Create(Platform());
            set.Apply(Force(5f, Vector3.forward));
            set.Tick(0.25f);
            Assert.AreEqual(1f, set.Kinetic.Offset.z, 0.001f, "4 m/s for a quarter of a second.");
        }

        [Test]
        public void AnObjectStopsAtTheEndOfItsTravel()
        {
            MagicalPropertySet set = Create(Platform());
            for (int i = 0; i < 4; i++)
            {
                set.Apply(Force(5f, Vector3.forward));
                Run(set, 2f);
            }

            Assert.AreEqual(6f, set.Kinetic.Offset.z, 0.001f);
            Assert.IsTrue(set.Meets(PropertyCondition.AtTravelLimit));
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Force(5f, Vector3.forward)), "It cannot go further.");
        }

        [Test]
        public void APushAcrossTheAxisOrTooWeakDoesNotMoveIt()
        {
            MagicalPropertySet set = Create(Platform());
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Force(5f, Vector3.right)));
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Force(1f, Vector3.forward)));
            Assert.AreEqual(Vector3.zero, set.Kinetic.TargetOffset);
        }

        [Test]
        public void AnObjectCanBePushedBackTheWayItCameButNotPastItsStart()
        {
            MagicalPropertySet set = Create(Platform());
            set.Apply(Force(5f, Vector3.forward));
            Run(set, 2f);
            set.Apply(Force(5f, Vector3.back));
            Run(set, 2f);
            Assert.AreEqual(0f, set.Kinetic.Offset.z, 0.001f);

            set.Apply(Force(5f, Vector3.back));
            Run(set, 2f);
            Assert.AreEqual(0f, set.Kinetic.Offset.z, 0.001f, "The travel starts at 0.");
        }

        [Test]
        public void AnObjectOnTheGroundMovesInAnyDirectionWithinItsRange()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings
                {
                    resistance = 0f, minimumForce = 0f, mode = KineticMode.GroundPlane, maxRange = 3f, maxStepPerPush = 10f,
                },
            });

            set.Apply(Force(2f, Vector3.right));
            Run(set, 2f);
            Assert.AreEqual(2f, set.Kinetic.Offset.x, 0.001f);

            set.Apply(Force(8f, Vector3.right));
            Run(set, 3f);
            Assert.AreEqual(3f, set.Kinetic.Offset.magnitude, 0.001f, "Held within its range.");
        }

        [Test]
        public void AnObstacleShortensTheGlide()
        {
            var kinetic = new KineticProperty(Platform().kineticSettings);
            kinetic.Push(Vector3.forward, 5f, Vector3.forward);
            kinetic.LimitGlide(1.5f);

            for (int i = 0; i < 40; i++)
            {
                kinetic.Tick(0.1f);
            }

            Assert.AreEqual(1.5f, kinetic.Offset.z, 0.001f);
        }

        // ----------------------------------------------------------- stability

        private static MagicalPropertySettings Unstable()
        {
            return new MagicalPropertySettings
            {
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 3.5f, stressLimit = 5f, stressRecoveryPerSecond = 0.5f,
                    braceThreshold = 0.5f, braceSeconds = 2f, maxBraceSeconds = 6f,
                },
            };
        }

        [Test]
        public void AStrongPushBringsAnUnstableObjectDown()
        {
            MagicalPropertySet set = Create(Unstable());
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State);

            set.Apply(Force(3.5f, Vector3.forward));
            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State);
            CollectionAssert.AreEqual(new[] { "Stability:1>3" }, transitions);
        }

        [Test]
        public void LighterForceAddsUpUntilTheObjectFalls()
        {
            MagicalPropertySet set = Create(Unstable());
            set.Apply(Force(2f, Vector3.forward));
            set.Apply(Force(2f, Vector3.forward));
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State);
            set.Apply(Force(2f, Vector3.forward));
            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State, "6 stress against a limit of 5.");
        }

        [Test]
        public void StressFadesIfTheObjectIsLeftAlone()
        {
            MagicalPropertySet set = Create(Unstable());
            set.Apply(Force(2f, Vector3.forward));
            set.Apply(Force(2f, Vector3.forward));
            Run(set, 10f);
            set.Apply(Force(2f, Vector3.forward));
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State);
        }

        [Test]
        public void ABraceHoldsAnUnstableObjectAgainstAnyForce()
        {
            MagicalPropertySet set = Create(Unstable());
            Assert.AreEqual(EffectResponse.Applied, set.Apply(Brace(1f, 2f)));
            Assert.AreEqual(StabilityState.Braced, set.Stability.State);

            set.Apply(Force(10f, Vector3.forward));
            Assert.AreEqual(StabilityState.Braced, set.Stability.State);

            Run(set, 2.5f);
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State, "The brace is temporary.");
            CollectionAssert.AreEqual(new[] { "Stability:1>2", "Stability:2>1" }, transitions);

            set.Apply(Force(10f, Vector3.forward));
            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State);
        }

        [Test]
        public void AWeakBraceIsRefusedAndABraceCannotBeHeldForEver()
        {
            MagicalPropertySet set = Create(Unstable());
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Brace(0.2f, 2f)));
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State);

            // Renewing a brace refreshes it; it does not add up.
            for (int i = 0; i < 20; i++)
            {
                set.Apply(Brace(1f, 1.5f));
            }

            Assert.AreEqual(1.5f, set.Stability.BraceRemaining, 0.001f);

            set.Apply(Brace(1f, 50f));
            Assert.AreEqual(6f, set.Stability.BraceRemaining, 0.001f, "Capped at the longest brace.");
        }

        [Test]
        public void ABraceWithoutADurationUsesTheObjectsOwn()
        {
            MagicalPropertySet set = Create(Unstable());
            set.Apply(Brace(1f, 0f));
            Assert.AreEqual(2f, set.Stability.BraceRemaining, 0.001f);
        }

        [Test]
        public void AStableObjectDoesNotCollapseUnderForce()
        {
            MagicalPropertySettings settings = Unstable();
            settings.stabilitySettings.startsUnstable = false;
            MagicalPropertySet set = Create(settings);

            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Force(50f, Vector3.forward)));
            Assert.AreEqual(StabilityState.Stable, set.Stability.State);
        }

        // -------------------------------------------------- several properties

        [Test]
        public void FireConsumesTheStructureOfABurningObject()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            set.Apply(Heat(100f));
            Assert.AreEqual(ThermalState.Burning, set.Thermal.State);

            Run(set, 2.5f);
            Assert.AreEqual(StructuralState.Weakened, set.Structural.State);
            Run(set, 4f);
            Assert.AreEqual(StructuralState.Destroyed, set.Structural.State);
        }

        [Test]
        public void HeatMakesSensitiveStoneBrittleAndCoolingMakesItToughAgain()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone));

            set.Apply(Force(5f, Vector3.forward));
            Assert.AreEqual(StructuralState.Intact, set.Structural.State, "Cold, it shrugs off a full pulse.");
            float coldLoss = 100f - set.Structural.Integrity;

            set.Apply(Heat(80f));
            Assert.AreEqual(ThermalState.Heated, set.Thermal.State);
            Assert.IsFalse(set.Thermal.IsBurning, "Stone does not burn.");

            Run(set, 12f);
            Assert.AreEqual(ThermalState.Unheated, set.Thermal.State);
            float before = set.Structural.Integrity;
            set.Apply(Force(5f, Vector3.forward));
            Assert.AreEqual(coldLoss, before - set.Structural.Integrity, 0.001f, "Cooled, it is as tough as before.");
        }

        [Test]
        public void ABracedObjectDoesNotMove()
        {
            MagicalPropertySettings settings = Unstable();
            settings.kinetic = true;
            settings.kineticSettings = new KineticSettings { resistance = 0f, minimumForce = 0f, mode = KineticMode.GroundPlane };
            settings.stabilitySettings.collapseForce = 100f;
            settings.stabilitySettings.stressLimit = 0f;
            MagicalPropertySet set = Create(settings);

            set.Apply(Brace(1f, 2f));
            set.Apply(Force(2f, Vector3.forward));
            Assert.AreEqual(Vector3.zero, set.Kinetic.TargetOffset, "Resistance to displacement while braced.");

            Run(set, 3f);
            set.Apply(Force(2f, Vector3.forward));
            Assert.AreEqual(2f, set.Kinetic.TargetOffset.z, 0.001f);
        }

        [Test]
        public void DamageUnsettlesAStableObjectAndAFailedStructureBringsItDown()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings
                {
                    integrity = 100f, weakenedFraction = 0.6f, fracturedFraction = 0.25f, forceThreshold = 0f, forceDamage = 10f,
                },
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = false, collapseForce = 100f, stressLimit = 0f,
                    unstableWhenStructure = StructuralTrigger.Weakened, collapseWhenStructure = StructuralTrigger.Fractured,
                },
            });

            set.Apply(Force(4f, Vector3.forward));
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State, "Weakened: it becomes unstable.");

            set.Apply(Force(4f, Vector3.forward));
            Assert.AreEqual(StructuralState.Fractured, set.Structural.State);
            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State, "Fractured: it comes down.");
        }

        [Test]
        public void ABraceDelaysTheCollapseOfAFailedStructureUntilItEnds()
        {
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings { integrity = 100f, forceThreshold = 0f, forceDamage = 40f },
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 100f, stressLimit = 0f, collapseWhenStructure = StructuralTrigger.Fractured,
                },
            });

            set.Apply(Brace(1f, 2f));
            set.Apply(Force(2f, Vector3.forward));
            Assert.AreEqual(StructuralState.Fractured, set.Structural.State);
            Assert.AreEqual(StabilityState.Braced, set.Stability.State, "Held up while the brace lasts.");

            Run(set, 2.5f);
            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State);
        }

        [Test]
        public void EachPropertyOfAnObjectKeepsItsOwnState()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.ReinforcedDoor));

            set.Apply(Charge(60f));
            set.Apply(Force(5f, Vector3.forward));

            Assert.AreEqual(ArcaneState.Charged, set.Arcane.State);
            Assert.AreEqual(StructuralState.Weakened, set.Structural.State);
            Assert.AreEqual(2, set.Settings.PropertyCount);
            Assert.IsTrue(set.Meets(PropertyCondition.Charged | PropertyCondition.Destroyed));
            Assert.IsFalse(set.Meets(PropertyCondition.Activated | PropertyCondition.Fractured));
        }

        // -------------------------------------------------- incompatible effects

        [Test]
        public void AnEffectNoPropertyAnswersIsRefusedAndChangesNothing()
        {
            MagicalPropertySet crystal = Create(WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));
            Assert.AreEqual(EffectResponse.NoProperty, crystal.Apply(Heat(500f)));
            Assert.AreEqual(EffectResponse.NoProperty, crystal.Apply(Force(50f, Vector3.forward)));
            Assert.AreEqual(EffectResponse.NoProperty, crystal.Apply(Brace(5f, 5f)));
            Assert.AreEqual(ArcaneState.Uncharged, crystal.Arcane.State);
            Assert.IsEmpty(transitions);

            MagicalPropertySet platform = Create(WorldMagicDefaults.Settings(ExampleObject.MovablePlatform));
            Assert.AreEqual(EffectResponse.NoProperty, platform.Apply(Heat(500f)));
            Assert.AreEqual(EffectResponse.NoProperty, platform.Apply(Charge(500f)));

            MagicalPropertySet barricade = Create(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            Assert.AreEqual(EffectResponse.NoProperty, barricade.Apply(Charge(500f)));
        }

        [Test]
        public void AnObjectAnswersOnlyTheEffectsItsPropertiesCover()
        {
            foreach (ExampleObject example in System.Enum.GetValues(typeof(ExampleObject)))
            {
                MagicalPropertySettings settings = WorldMagicDefaults.Settings(example);
                var set = new MagicalPropertySet(settings);

                Assert.AreEqual(settings.thermal, set.Answers(MagicEffectKind.Heat), example.ToString());
                Assert.AreEqual(settings.arcane, set.Answers(MagicEffectKind.ArcaneCharge), example.ToString());
                Assert.AreEqual(settings.stability, set.Answers(MagicEffectKind.Stabilize), example.ToString());
                Assert.AreEqual(settings.structural || settings.kinetic || settings.stability,
                    set.Answers(MagicEffectKind.Force), example.ToString());
                Assert.Less(settings.PropertyCount, 5, "No example responds to everything.");
                Assert.Greater(settings.PropertyCount, 0);
            }
        }

        [Test]
        public void AnEffectMeantForCreaturesDoesNotTouchAnObject()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            var onlyCreatures = new MagicEffect(MagicEffectKind.Heat, 100f, Vector3.zero, 0f, Vector3.zero, null, 1,
                EffectTargets.Creatures, 0);

            Assert.AreEqual(EffectResponse.Filtered, set.Apply(onlyCreatures));
            Assert.AreEqual(0f, set.Thermal.Heat);
        }

        [Test]
        public void AnEffectWithNoStrengthDoesNothing()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Charge(0f)));
        }

        // ------------------------------------------------------ repeated effects

        [Test]
        public void TheSameEventActsOnAnObjectOnlyOnce()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Charge(40f, 7)));
            Assert.AreEqual(EffectResponse.Duplicate, set.Apply(Charge(40f, 7)));
            Assert.AreEqual(EffectResponse.Duplicate, set.Apply(Charge(40f, 7)));
            Assert.AreEqual(40f, set.Arcane.Charge, 0.001f);

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Charge(40f, 8)), "A new event is a new effect.");
            Assert.AreEqual(80f, set.Arcane.Charge, 0.001f);
        }

        [Test]
        public void DifferentEffectsOfOneEventAllArrive()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            Assert.AreEqual(EffectResponse.Applied, set.Apply(Heat(20f, 3)));
            Assert.AreEqual(EffectResponse.Applied, set.Apply(Force(4f, Vector3.forward, 3)));
            Assert.Greater(set.Thermal.Heat, 0f);
            Assert.Less(set.Structural.Integrity, 60f);
        }

        [Test]
        public void EffectsWithoutAnIdentityAreNeverTreatedAsRepeats()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));
            set.Apply(Charge(30f));
            set.Apply(Charge(30f));
            Assert.AreEqual(60f, set.Arcane.Charge, 0.001f);
        }

        [Test]
        public void TheEventLogForgetsOldEventsButNotRecentOnes()
        {
            var log = new EffectEventLog();
            Assert.IsFalse(log.CheckAndRecord(1, MagicEffectKind.Heat));
            Assert.IsTrue(log.CheckAndRecord(1, MagicEffectKind.Heat));
            Assert.IsFalse(log.CheckAndRecord(1, MagicEffectKind.Force), "Another kind of the same event.");

            for (int id = 2; id < 12; id++)
            {
                log.CheckAndRecord(id, MagicEffectKind.Heat);
            }

            Assert.IsTrue(log.CheckAndRecord(11, MagicEffectKind.Heat));
            Assert.IsFalse(log.CheckAndRecord(1, MagicEffectKind.Heat), "Long past: it has been forgotten.");
        }

        [Test]
        public void EventIdentitiesAreNeverZeroAndNeverRepeatInARow()
        {
            int a = MagicEffectEvents.NextId();
            int b = MagicEffectEvents.NextId();
            Assert.AreNotEqual(0, a);
            Assert.AreNotEqual(0, b);
            Assert.AreNotEqual(a, b);
        }

        // -------------------------------------------- destruction and cleanup

        [Test]
        public void ADestroyedObjectTakesNoMoreEffectsAndStopsBurning()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            set.Apply(Heat(100f));
            Run(set, 8f);

            Assert.IsTrue(set.IsDestroyed);
            Assert.IsFalse(set.Thermal.IsBurning, "Nothing is left alight.");
            Assert.IsTrue(set.IsAtRest, "A destroyed object needs no more updates.");
            Assert.AreEqual(EffectResponse.Spent, set.Apply(Heat(100f)));
            Assert.AreEqual(EffectResponse.Spent, set.Apply(Force(5f, Vector3.forward)));
            Assert.IsTrue(set.Meets(PropertyCondition.Destroyed));
            Assert.IsFalse(set.Meets(PropertyCondition.Burning));

            int count = transitions.Count;
            Run(set, 5f);
            Assert.AreEqual(count, transitions.Count, "Nothing more is announced.");
        }

        [Test]
        public void DestructionIsAnnouncedExactlyOnce()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
            set.Apply(Force(50f, Vector3.forward));
            set.Apply(Force(50f, Vector3.forward));

            Assert.AreEqual(1, transitions.FindAll(t => t.EndsWith(">3") && t.StartsWith("Structural")).Count);
        }

        [Test]
        public void RestoringPutsEveryPropertyBackWithoutAnnouncingIt()
        {
            MagicalPropertySet set = Create(WorldMagicDefaults.Settings(ExampleObject.ReinforcedDoor));
            set.Apply(Charge(200f, 5));
            set.Apply(Force(5f, Vector3.forward, 5));
            set.Apply(Force(5f, Vector3.forward, 6));
            transitions.Clear();

            set.Restore();

            Assert.AreEqual(ArcaneState.Uncharged, set.Arcane.State);
            Assert.AreEqual(StructuralState.Intact, set.Structural.State);
            Assert.IsEmpty(transitions);
            Assert.AreEqual(EffectResponse.Applied, set.Apply(Charge(10f, 5)), "The event log was cleared as well.");
        }

        [Test]
        public void ACollapsedObjectStaysDown()
        {
            MagicalPropertySet set = Create(Unstable());
            set.Apply(Force(10f, Vector3.forward));
            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Brace(5f, 5f)), "It cannot be braced back up.");
            Run(set, 20f);
            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State);
            Assert.IsTrue(set.IsAtRest);
        }

        // -------------------------------------------------------- determinism

        [Test]
        public void TheSameEffectsInTheSameOrderGiveTheSameResult()
        {
            string RunOnce()
            {
                var log = new List<string>();
                var set = new MagicalPropertySet(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
                set.StateChanged += (kind, from, to) => log.Add($"{kind}:{from}>{to}");
                set.Apply(Heat(30f, 1));
                set.Tick(0.5f);
                set.Apply(Force(3f, Vector3.forward, 2));
                set.Apply(Heat(40f, 3));
                for (int i = 0; i < 80; i++)
                {
                    set.Tick(0.1f);
                }

                return string.Join(",", log) + $"|{set.Structural.Integrity:0.000}|{set.Thermal.Heat:0.000}";
            }

            Assert.AreEqual(RunOnce(), RunOnce());
        }

        [Test]
        public void ChangesAreAnnouncedInAFixedOrder()
        {
            // One force that both cracks the structure and, through it, brings the object down.
            MagicalPropertySet set = Create(new MagicalPropertySettings
            {
                structural = true,
                structuralSettings = new StructuralSettings { integrity = 100f, forceThreshold = 0f, forceDamage = 40f },
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 100f, stressLimit = 0f, collapseWhenStructure = StructuralTrigger.Fractured,
                },
            });

            set.Apply(Force(2f, Vector3.forward));
            CollectionAssert.AreEqual(new[] { "Structural:0>2", "Stability:1>3" }, transitions);
        }

        // ------------------------------------------- sliding under other states

        /// <summary>Something that slides freely along one axis and stands only just.</summary>
        private static MagicalPropertySettings SlidingAndUnstable(float stressLimit = 0f)
        {
            return new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings
                {
                    resistance = 0f, minimumForce = 0f, mode = KineticMode.Axis, localAxis = Vector3.forward,
                    minTravel = 0f, maxTravel = 10f, maxStepPerPush = 10f, moveSpeed = 4f,
                },
                stability = true,
                stabilitySettings = new StabilitySettings
                {
                    startsUnstable = true, collapseForce = 3f, stressLimit = stressLimit,
                    unstableWhenStructure = StructuralTrigger.Never, collapseWhenStructure = StructuralTrigger.Never,
                    braceThreshold = 0.5f, braceSeconds = 2f, maxBraceSeconds = 6f,
                },
            };
        }

        [Test]
        public void AForceThatBringsAnObjectDownDoesNotAlsoSlideIt()
        {
            MagicalPropertySet set = Create(SlidingAndUnstable());
            set.KineticAxis = Vector3.forward;

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Force(4f, Vector3.forward)));

            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State);
            Assert.AreEqual(Vector3.zero, set.Kinetic.TargetOffset, "The blow that topples it does not push it.");
            Assert.AreEqual(KineticState.Resting, set.Kinetic.State);
            Assert.IsFalse(set.Meets(PropertyCondition.Moving));
            Assert.IsTrue(set.IsAtRest, "Nothing is left changing, so the object can sleep.");
            CollectionAssert.AreEqual(new[] { "Stability:1>3" }, transitions);
        }

        [Test]
        public void ACollapseStopsAGlideThatIsUnderWay()
        {
            MagicalPropertySet set = Create(SlidingAndUnstable(3.5f));
            set.KineticAxis = Vector3.forward;

            set.Apply(Force(2f, Vector3.forward, 1));
            Run(set, 0.2f);
            Assert.AreEqual(KineticState.Moving, set.Kinetic.State);
            float reached = set.Kinetic.Offset.z;

            // The second push takes the stress over the limit: it brings the object down instead of moving it on.
            set.Apply(Force(2f, Vector3.forward, 2));

            Assert.AreEqual(StabilityState.Collapsed, set.Stability.State);
            Assert.AreEqual(KineticState.Resting, set.Kinetic.State);
            Assert.IsTrue(set.IsAtRest);

            Run(set, 2f);
            Assert.AreEqual(reached, set.Kinetic.Offset.z, 0.0001f, "It lies where it fell.");
        }

        [Test]
        public void ABraceStopsAGlideThatIsUnderWay()
        {
            MagicalPropertySet set = Create(SlidingAndUnstable());
            set.KineticAxis = Vector3.forward;

            set.Apply(Force(2f, Vector3.forward, 1));
            Run(set, 0.2f);
            float reached = set.Kinetic.Offset.z;
            Assert.Greater(reached, 0f);
            Assert.Less(reached, 2f);

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Brace(1f, 1.5f, 2)));

            Assert.AreEqual(StabilityState.Braced, set.Stability.State);
            Assert.AreEqual(KineticState.Resting, set.Kinetic.State, "A braced object holds its place at once.");
            CollectionAssert.Contains(transitions, "Kinetic:1>0");

            Run(set, 1f);
            Assert.AreEqual(reached, set.Kinetic.Offset.z, 0.0001f);

            // When the brace has run out it stays where it was stopped; the old glide does not resume.
            Run(set, 2f);
            Assert.AreEqual(StabilityState.Unstable, set.Stability.State);
            Assert.AreEqual(reached, set.Kinetic.Offset.z, 0.0001f);
        }

        [Test]
        public void AForceThatDestroysAnObjectDoesNotSlideIt()
        {
            MagicalPropertySettings settings = Platform();
            settings.structural = true;
            settings.structuralSettings = new StructuralSettings { integrity = 50f, forceThreshold = 0f, forceDamage = 100f };
            MagicalPropertySet set = Create(settings);
            set.KineticAxis = Vector3.forward;

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Force(5f, Vector3.forward)));

            Assert.IsTrue(set.IsDestroyed);
            Assert.AreEqual(Vector3.zero, set.Kinetic.TargetOffset);
            Assert.IsFalse(set.Meets(PropertyCondition.Moving), "What is destroyed is not left 'moving'.");
            Assert.IsTrue(set.IsAtRest);
        }

        [Test]
        public void APushThatIsBlockedOutrightChangesNothingAndAnnouncesNothing()
        {
            MagicalPropertySet set = Create(Platform());
            set.KineticAxis = Vector3.forward;
            set.GlideLimit = (from, to) => 0f;

            Assert.AreEqual(EffectResponse.Resisted, set.Apply(Force(5f, Vector3.forward)));

            Assert.AreEqual(KineticState.Resting, set.Kinetic.State);
            Assert.AreEqual(Vector3.zero, set.Kinetic.TargetOffset);
            Assert.IsEmpty(transitions, "Nothing moved, so nothing is announced.");
            Assert.IsTrue(set.IsAtRest);
        }

        [Test]
        public void APushStopsWhereTheWayIsBlocked()
        {
            MagicalPropertySet set = Create(Platform());
            set.KineticAxis = Vector3.forward;

            Vector3 askedFrom = Vector3.one;
            Vector3 askedTo = Vector3.one;
            int asked = 0;
            set.GlideLimit = (from, to) =>
            {
                asked++;
                askedFrom = from;
                askedTo = to;
                return 1.5f;
            };

            Assert.AreEqual(EffectResponse.Applied, set.Apply(Force(5f, Vector3.forward)));

            Assert.AreEqual(1, asked, "The way is checked once per push.");
            Assert.AreEqual(Vector3.zero, askedFrom);
            Assert.AreEqual(4f, askedTo.z, 0.0001f, "It is asked about the glide the push wanted.");
            Assert.AreEqual(1.5f, set.Kinetic.TargetOffset.z, 0.0001f);
            CollectionAssert.AreEqual(new[] { "Kinetic:0>1" }, transitions);

            Run(set, 3f);
            Assert.AreEqual(1.5f, set.Kinetic.Offset.z, 0.0001f);
            Assert.AreEqual(KineticState.Resting, set.Kinetic.State);

            // A force too weak to move it does not ask at all.
            set.Apply(Force(0.5f, Vector3.forward));
            Assert.AreEqual(1, asked);
        }
    }
}
