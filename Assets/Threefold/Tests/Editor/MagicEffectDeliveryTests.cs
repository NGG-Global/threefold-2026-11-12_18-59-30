using NUnit.Framework;
using Threefold.Combat;
using Threefold.Enemies;
using Threefold.Magic;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// What the three disciplines' attacks carry, what the six example objects do with it, and that
    /// an enemy and an object answer the same effect by the same rules. The attacks use the
    /// disciplines' default numbers and the objects their default definitions, so these tests show
    /// that each example really responds the way it is meant to.
    /// </summary>
    public sealed class MagicEffectDeliveryTests
    {
        private int nextEvent = 100;

        private static MagicalPropertySet Example(ExampleObject example)
        {
            return new MagicalPropertySet(WorldMagicDefaults.Settings(example)) { KineticAxis = Vector3.forward };
        }

        /// <summary>Delivers a payload the way an attack does: each effect it carries, in order, under one event.</summary>
        private EffectResponse[] Deliver(MagicalPropertySet target, EffectPayload payload, int discipline)
        {
            int eventId = nextEvent++;
            var responses = new EffectResponse[4];
            for (int kind = 0; kind < 4; kind++)
            {
                float magnitude = payload.Get((MagicEffectKind)kind);
                responses[kind] = magnitude > 0f
                    ? target.Apply(new MagicEffect((MagicEffectKind)kind, magnitude, Vector3.forward, payload.stabilizeSeconds,
                        Vector3.zero, null, discipline, payload.targets, eventId))
                    : EffectResponse.NoProperty;
            }

            return responses;
        }

        private static void Run(MagicalPropertySet set, float seconds)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += 0.1f)
            {
                set.Tick(0.1f);
            }
        }

        private static EffectPayload Melee(MagicDiscipline discipline, int step) => DisciplineDefaults.Melee(discipline).steps[step].Effects;

        private static EffectPayload Arrow(float power) => DisciplineDefaults.Charged(MagicDiscipline.Arcane).Projectile.Effects(power);

        private static EffectPayload Wave(float power) => DisciplineDefaults.Charged(MagicDiscipline.Flame).Area.Effects(power);

        private static EffectPayload Pulse(float power) => DisciplineDefaults.Charged(MagicDiscipline.Aegis).Area.Effects(power);

        private static EffectPayload Guard(MagicDiscipline discipline) => DisciplineDefaults.Guard(discipline).Effects;

        // --------------------------------------------- what each attack carries

        [Test]
        public void TheArcaneArrowCarriesChargeAndNothingElse()
        {
            EffectPayload uncharged = Arrow(0f);
            EffectPayload full = Arrow(1f);

            Assert.Greater(uncharged.arcaneCharge, 0f);
            Assert.Greater(full.arcaneCharge, uncharged.arcaneCharge, "More charge from a longer hold.");
            Assert.AreEqual(0f, full.heat);
            Assert.AreEqual(0f, full.force);
            Assert.AreEqual(0f, full.stabilize);
        }

        [Test]
        public void ArcaneMeleeCarriesNoEffect()
        {
            foreach (MeleeStepProfile step in DisciplineDefaults.Melee(MagicDiscipline.Arcane).steps)
            {
                Assert.IsTrue(step.Effects.IsEmpty, step.name);
            }
        }

        [Test]
        public void FlameMeleeAndTheFlameWaveCarryHeatAndNothingElse()
        {
            foreach (MeleeStepProfile step in DisciplineDefaults.Melee(MagicDiscipline.Flame).steps)
            {
                Assert.Greater(step.Effects.heat, 0f, step.name);
                Assert.AreEqual(0f, step.Effects.force, step.name);
                Assert.AreEqual(0f, step.Effects.arcaneCharge, step.name);
            }

            Assert.Greater(Wave(0f).heat, 0f);
            Assert.Greater(Wave(1f).heat, Wave(0f).heat);
            Assert.AreEqual(0f, Wave(1f).force);
            Assert.AreEqual(0f, Wave(1f).arcaneCharge);
        }

        [Test]
        public void AegisMeleeAndTheForcePulseCarryForceAndNothingElse()
        {
            foreach (MeleeStepProfile step in DisciplineDefaults.Melee(MagicDiscipline.Aegis).steps)
            {
                Assert.Greater(step.Effects.force, 0f, step.name);
                Assert.AreEqual(0f, step.Effects.heat, step.name);
            }

            Assert.Greater(Pulse(0f).force, 0f);
            Assert.Greater(Pulse(1f).force, Pulse(0f).force);
            Assert.AreEqual(0f, Pulse(1f).heat);
            Assert.Greater(Pulse(0f).force, Melee(MagicDiscipline.Aegis, 1).force, "The pulse is far stronger than a strike.");
        }

        [Test]
        public void OnlyTheAegisGuardStabilisesAndOnlyObjects()
        {
            Assert.IsTrue(DisciplineDefaults.Guard(MagicDiscipline.Aegis).HasEffect);
            Assert.IsFalse(DisciplineDefaults.Guard(MagicDiscipline.Arcane).HasEffect);
            Assert.IsFalse(DisciplineDefaults.Guard(MagicDiscipline.Flame).HasEffect);

            EffectPayload guard = Guard(MagicDiscipline.Aegis);
            Assert.Greater(guard.stabilize, 0f);
            Assert.Greater(guard.stabilizeSeconds, DisciplineDefaults.Guard(MagicDiscipline.Aegis).pulseSeconds,
                "Each brace outlasts the gap to the next, so the hold is unbroken.");
            Assert.AreEqual(EffectTargets.Objects, guard.targets);
            Assert.AreEqual(0f, guard.force);
        }

        [Test]
        public void ASpellThatOnlyNamesItsElementCarriesTheMatchingEffect()
        {
            Assert.AreEqual(20f, EffectPayload.FromElement(SpellElement.Arcane, 20f).arcaneCharge);
            Assert.AreEqual(20f, EffectPayload.FromElement(SpellElement.Fire, 20f).heat);
            Assert.AreEqual(20f, EffectPayload.FromElement(SpellElement.Force, 20f).force);
            Assert.IsTrue(EffectPayload.FromElement(SpellElement.None, 20f).IsEmpty);
            Assert.IsTrue(EffectPayload.FromElement(SpellElement.Arcane, 0f).IsEmpty);
        }

        [Test]
        public void AnEffectStandsForItsKindOfMagicToPuzzleActivators()
        {
            Assert.AreEqual(SpellElement.Arcane, SpellActivator.ElementOf(MagicEffectKind.ArcaneCharge));
            Assert.AreEqual(SpellElement.Fire, SpellActivator.ElementOf(MagicEffectKind.Heat));
            Assert.AreEqual(SpellElement.Force, SpellActivator.ElementOf(MagicEffectKind.Force));
            Assert.AreEqual(SpellElement.None, SpellActivator.ElementOf(MagicEffectKind.Stabilize));
        }

        // ------------------------------------------------------ arcane crystal

        [Test]
        public void AFullArcaneArrowActivatesTheCrystalAndItGoesDarkAgainLater()
        {
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            Deliver(crystal, Arrow(1f), 0);
            Assert.AreEqual(ArcaneState.Activated, crystal.Arcane.State);

            Run(crystal, 11f);
            Assert.AreEqual(ArcaneState.Uncharged, crystal.Arcane.State);
        }

        [Test]
        public void UnchargedArrowsChargeTheCrystalStepByStep()
        {
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            Deliver(crystal, Arrow(0f), 0);
            Assert.AreEqual(ArcaneState.Charged, crystal.Arcane.State);
            Deliver(crystal, Arrow(0f), 0);
            Assert.AreEqual(ArcaneState.Charged, crystal.Arcane.State);
            Deliver(crystal, Arrow(0f), 0);
            Assert.AreEqual(ArcaneState.Activated, crystal.Arcane.State);
        }

        [Test]
        public void TheCrystalIgnoresFlameAndAegis()
        {
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            Assert.AreEqual(EffectResponse.NoProperty, Deliver(crystal, Wave(1f), 1)[(int)MagicEffectKind.Heat]);
            Assert.AreEqual(EffectResponse.NoProperty, Deliver(crystal, Pulse(1f), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(EffectResponse.NoProperty, Deliver(crystal, Guard(MagicDiscipline.Aegis), 2)[(int)MagicEffectKind.Stabilize]);
            Assert.AreEqual(ArcaneState.Uncharged, crystal.Arcane.State);
        }

        // ---------------------------------------------------- wooden barricade

        [Test]
        public void AFlameComboSetsTheBarricadeAlightAndItBurnsDown()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);

            Deliver(barricade, Melee(MagicDiscipline.Flame, 0), 1);
            Assert.AreEqual(ThermalState.Unheated, barricade.Thermal.State);
            Deliver(barricade, Melee(MagicDiscipline.Flame, 1), 1);
            Assert.AreEqual(ThermalState.Heated, barricade.Thermal.State);
            Deliver(barricade, Melee(MagicDiscipline.Flame, 2), 1);
            Assert.AreEqual(ThermalState.Burning, barricade.Thermal.State);

            Run(barricade, 7f);
            Assert.AreEqual(StructuralState.Destroyed, barricade.Structural.State);
        }

        [Test]
        public void AChargedFlameWaveIgnitesTheBarricadeAtOnceAndAnUnchargedOneOnlyHeatsIt()
        {
            MagicalPropertySet weak = Example(ExampleObject.WoodenBarricade);
            Deliver(weak, Wave(0f), 1);
            Assert.AreEqual(ThermalState.Heated, weak.Thermal.State);

            MagicalPropertySet strong = Example(ExampleObject.WoodenBarricade);
            Deliver(strong, Wave(1f), 1);
            Assert.AreEqual(ThermalState.Burning, strong.Thermal.State);
        }

        [Test]
        public void TheBarricadeShrugsOffStrikesButTwoFullPulsesBreakIt()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);

            Assert.AreEqual(EffectResponse.Resisted, Deliver(barricade, Melee(MagicDiscipline.Aegis, 0), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(EffectResponse.Resisted, Deliver(barricade, Melee(MagicDiscipline.Aegis, 1), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(60f, barricade.Structural.Integrity);

            Deliver(barricade, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Weakened, barricade.Structural.State);
            Deliver(barricade, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Destroyed, barricade.Structural.State);
        }

        [Test]
        public void TheBarricadeIgnoresArcaneCharge()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            Assert.AreEqual(EffectResponse.NoProperty, Deliver(barricade, Arrow(1f), 0)[(int)MagicEffectKind.ArcaneCharge]);
        }

        // ------------------------------------------------- heat-sensitive stone

        [Test]
        public void TheStoneBarrierNeedsHeatAndThenForce()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);

            // Force alone: ten full pulses barely mark it.
            for (int i = 0; i < 10; i++)
            {
                Deliver(stone, Pulse(1f), 2);
            }

            Assert.AreEqual(StructuralState.Intact, stone.Structural.State);

            // Heat alone: it glows but does not break or burn.
            Deliver(stone, Wave(1f), 1);
            Assert.AreEqual(ThermalState.Heated, stone.Thermal.State);
            Assert.AreEqual(StructuralState.Intact, stone.Structural.State);

            // Heat, then force while it is still hot: it shatters.
            Run(stone, 1f);
            Deliver(stone, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Destroyed, stone.Structural.State);
        }

        [Test]
        public void AFullFlameComboHeatsTheStoneToo()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            MeleeComboProfile combo = DisciplineDefaults.Melee(MagicDiscipline.Flame);

            // Two swings are not enough; the third, landing while the stone is still warm, is.
            for (int step = 0; step < combo.StepCount; step++)
            {
                Assert.AreEqual(ThermalState.Unheated, stone.Thermal.State, $"Before swing {step + 1}.");
                Deliver(stone, Melee(MagicDiscipline.Flame, step), 1);
                Run(stone, combo.steps[step].TotalSeconds);
            }

            Assert.AreEqual(ThermalState.Heated, stone.Thermal.State);
            Deliver(stone, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Destroyed, stone.Structural.State);
        }

        [Test]
        public void TheStoneCoolsIfThePlayerWaitsTooLong()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            Deliver(stone, Wave(1f), 1);

            // Still hot four seconds after a full wave, cold again after six.
            Run(stone, 4f);
            Assert.AreEqual(ThermalState.Heated, stone.Thermal.State);
            Run(stone, 2f);
            Assert.AreEqual(ThermalState.Unheated, stone.Thermal.State);

            Run(stone, 6f);
            Assert.AreEqual(ThermalState.Unheated, stone.Thermal.State);

            Deliver(stone, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Intact, stone.Structural.State);
        }

        [Test]
        public void AnUnchargedWaveDoesNotHeatTheStoneEnough()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            Deliver(stone, Wave(0f), 1);
            Assert.AreEqual(ThermalState.Unheated, stone.Thermal.State);
        }

        // ----------------------------------------------------- movable platform

        [Test]
        public void TheForcePulseSlidesThePlatformFurtherWithMoreCharge()
        {
            MagicalPropertySet weak = Example(ExampleObject.MovablePlatform);
            Deliver(weak, Pulse(0f), 2);
            Run(weak, 3f);

            MagicalPropertySet strong = Example(ExampleObject.MovablePlatform);
            Deliver(strong, Pulse(1f), 2);
            Run(strong, 3f);

            Assert.AreEqual(2f, weak.Kinetic.Offset.z, 0.001f);
            Assert.AreEqual(4f, strong.Kinetic.Offset.z, 0.001f);
        }

        [Test]
        public void AegisStrikesAreTooLightToMoveThePlatform()
        {
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            Assert.AreEqual(EffectResponse.Resisted, Deliver(platform, Melee(MagicDiscipline.Aegis, 0), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(EffectResponse.Resisted, Deliver(platform, Melee(MagicDiscipline.Aegis, 1), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(Vector3.zero, platform.Kinetic.TargetOffset);
        }

        [Test]
        public void ThePlatformIgnoresHeatAndCharge()
        {
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            Assert.AreEqual(EffectResponse.NoProperty, Deliver(platform, Wave(1f), 1)[(int)MagicEffectKind.Heat]);
            Assert.AreEqual(EffectResponse.NoProperty, Deliver(platform, Arrow(1f), 0)[(int)MagicEffectKind.ArcaneCharge]);
        }

        // ---------------------------------------------------- unstable structure

        [Test]
        public void AFullForcePulseBringsTheUnstableStructureDown()
        {
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            Deliver(structure, Pulse(1f), 2);
            Assert.AreEqual(StabilityState.Collapsed, structure.Stability.State);
        }

        [Test]
        public void TwoUnchargedPulsesBringItDownButOneDoesNot()
        {
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            Deliver(structure, Pulse(0f), 2);
            Assert.AreEqual(StabilityState.Unstable, structure.Stability.State);
            Deliver(structure, Pulse(0f), 2);
            Assert.AreEqual(StabilityState.Collapsed, structure.Stability.State);
        }

        [Test]
        public void AnAegisGuardBracesTheStructureAgainstThePulse()
        {
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);

            Assert.AreEqual(EffectResponse.Applied, Deliver(structure, Guard(MagicDiscipline.Aegis), 2)[(int)MagicEffectKind.Stabilize]);
            Assert.AreEqual(StabilityState.Braced, structure.Stability.State);

            Deliver(structure, Pulse(1f), 2);
            Assert.AreEqual(StabilityState.Braced, structure.Stability.State);
        }

        [Test]
        public void HoldingTheGuardKeepsTheStructureBracedAndLettingGoEndsIt()
        {
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            GuardEffectProfile guard = DisciplineDefaults.Guard(MagicDiscipline.Aegis);

            // Ten seconds of holding block: a brace every pulse interval.
            float sincePulse = guard.pulseSeconds;
            for (float t = 0f; t < 10f; t += 0.1f)
            {
                sincePulse += 0.1f;
                if (sincePulse >= guard.pulseSeconds)
                {
                    sincePulse = 0f;
                    Deliver(structure, guard.Effects, 2);
                }

                structure.Tick(0.1f);
                Assert.AreEqual(StabilityState.Braced, structure.Stability.State, $"At {t:0.0} s.");
            }

            Run(structure, guard.stabilizeSeconds + 0.2f);
            Assert.AreEqual(StabilityState.Unstable, structure.Stability.State);
        }

        // ------------------------------------------------------ reinforced door

        [Test]
        public void FireAndLightForceDoNothingToTheReinforcedDoor()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);

            Assert.AreEqual(EffectResponse.NoProperty, Deliver(door, Wave(1f), 1)[(int)MagicEffectKind.Heat]);
            Assert.AreEqual(EffectResponse.Resisted, Deliver(door, Pulse(0f), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(EffectResponse.Resisted, Deliver(door, Melee(MagicDiscipline.Aegis, 1), 2)[(int)MagicEffectKind.Force]);
            Assert.AreEqual(150f, door.Structural.Integrity);
        }

        [Test]
        public void TwoFullPulsesBreachTheDoor()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            Deliver(door, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Weakened, door.Structural.State);
            Assert.IsFalse(door.Meets(PropertyCondition.Activated | PropertyCondition.Fractured));

            Deliver(door, Pulse(1f), 2);
            Assert.AreEqual(StructuralState.Fractured, door.Structural.State);
            Assert.IsTrue(door.Meets(PropertyCondition.Activated | PropertyCondition.Fractured), "Breached: the way is open.");
        }

        [Test]
        public void TwoFullArrowsOpenTheDoorsArcaneLockForGood()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            Deliver(door, Arrow(1f), 0);
            Assert.AreEqual(ArcaneState.Charged, door.Arcane.State);
            Deliver(door, Arrow(1f), 0);
            Assert.AreEqual(ArcaneState.Activated, door.Arcane.State);
            Assert.AreEqual(StructuralState.Intact, door.Structural.State, "Opened without a scratch.");

            Run(door, 60f);
            Assert.IsTrue(door.Meets(PropertyCondition.Activated | PropertyCondition.Fractured));
        }

        // ------------------------------------------- enemies and environment

        [Test]
        public void HeatMeansTheSameToACreatureAsToAnObject()
        {
            var settings = new HeatSettings { susceptibility = 1.3f, capacity = 100f, heatedFraction = 0.5f };
            var creature = new CreatureEffectModel(settings, 0f, true);
            var thing = new MagicalPropertySet(new MagicalPropertySettings { thermal = true, thermalSettings = settings });

            foreach (float heat in new[] { 18f, 18f, 30f, 45f })
            {
                int eventId = nextEvent++;
                var effect = new MagicEffect(MagicEffectKind.Heat, heat, Vector3.zero, 0f, Vector3.zero, null, 1,
                    EffectTargets.All, eventId);
                creature.Apply(effect, out _);
                thing.Apply(effect);

                Assert.AreEqual(thing.Thermal.State, creature.Thermal.State);
                Assert.AreEqual(thing.Thermal.Heat, creature.Thermal.Heat, 0.0001f);
            }

            Assert.AreEqual(ThermalState.Burning, creature.Thermal.State);
        }

        [Test]
        public void ForceMovesACreatureAsFarAsAnObjectWithTheSameResistance()
        {
            var creature = new CreatureEffectModel(new HeatSettings(), 0.6f, true);
            var thing = new MagicalPropertySet(new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings
                {
                    resistance = 0.6f, minimumForce = 0f, mode = KineticMode.GroundPlane, maxRange = 50f, maxStepPerPush = 50f,
                },
            });

            var effect = new MagicEffect(MagicEffectKind.Force, 5f, Vector3.forward, 0f, Vector3.zero, null, 2,
                EffectTargets.All, nextEvent++);

            creature.Apply(effect, out float creatureDistance);
            thing.Apply(effect);

            Assert.AreEqual(2f, creatureDistance, 0.0001f);
            Assert.AreEqual(creatureDistance, thing.Kinetic.TargetOffset.magnitude, 0.0001f);
        }

        [Test]
        public void TheGuardianIsPushedLessAndHeatsSlowerThanTheWisp()
        {
            EnemyTuning guardianTuning = EnemyArchetypes.RuinsGuardian();
            EnemyTuning wispTuning = EnemyArchetypes.ArcaneWisp();
            var guardian = new CreatureEffectModel(guardianTuning.heat, guardianTuning.pushResistance, true);
            var wisp = new CreatureEffectModel(wispTuning.heat, wispTuning.pushResistance, true);

            EffectPayload pulse = Pulse(1f);
            var force = new MagicEffect(MagicEffectKind.Force, pulse.force, Vector3.forward, 0f, Vector3.zero, null, 2,
                pulse.targets, nextEvent++);
            guardian.Apply(force, out float guardianPush);
            wisp.Apply(force, out float wispPush);
            Assert.AreEqual(2f, guardianPush, 0.001f);
            Assert.AreEqual(5f, wispPush, 0.001f);

            EffectPayload wave = Wave(1f);
            var heat = new MagicEffect(MagicEffectKind.Heat, wave.heat, Vector3.zero, 0f, Vector3.zero, null, 1,
                wave.targets, nextEvent++);
            guardian.Apply(heat, out _);
            wisp.Apply(heat, out _);
            Assert.IsFalse(guardian.Thermal.IsBurning, "Stone heats slowly.");
            Assert.IsTrue(wisp.IgnitedByLastEffect, "One full wave sets a Wisp alight.");
        }

        [Test]
        public void ACreatureRefusesChargeAndBracingJustAsAnObjectWithoutThoseProperties()
        {
            var creature = new CreatureEffectModel(new HeatSettings(), 0f, true);
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);

            MagicEffect charge = MagicEffect.Of(MagicEffectKind.ArcaneCharge, 100f, nextEvent++);
            MagicEffect brace = MagicEffect.Of(MagicEffectKind.Stabilize, 1f, nextEvent++);

            Assert.AreEqual(EffectResponse.NoProperty, creature.Apply(charge, out _));
            Assert.AreEqual(EffectResponse.NoProperty, barricade.Apply(charge));
            Assert.AreEqual(EffectResponse.NoProperty, creature.Apply(brace, out _));
            Assert.AreEqual(EffectResponse.NoProperty, barricade.Apply(brace));
        }

        [Test]
        public void ACreatureTakesTheSameEventOnlyOnceToo()
        {
            var creature = new CreatureEffectModel(new HeatSettings(), 0f, true);
            MagicEffect heat = MagicEffect.Of(MagicEffectKind.Heat, 30f, nextEvent++);

            Assert.AreEqual(EffectResponse.Applied, creature.Apply(heat, out _));
            Assert.AreEqual(EffectResponse.Duplicate, creature.Apply(heat, out _));
            Assert.AreEqual(30f, creature.Thermal.Heat, 0.001f);
        }

        [Test]
        public void AnEffectMeantForObjectsDoesNotTouchACreature()
        {
            var creature = new CreatureEffectModel(new HeatSettings(), 0f, true);
            EffectPayload guard = Guard(MagicDiscipline.Aegis);
            var brace = new MagicEffect(MagicEffectKind.Stabilize, guard.stabilize, Vector3.zero, guard.stabilizeSeconds,
                Vector3.zero, null, 2, guard.targets, nextEvent++);

            Assert.AreEqual(EffectResponse.Filtered, creature.Apply(brace, out _));
        }

        [Test]
        public void ADummyOnAPostTakesHeatButIsNotMoved()
        {
            var dummy = new CreatureEffectModel(new HeatSettings { susceptibility = 1.4f }, 1f, false);

            Assert.AreEqual(EffectResponse.NoProperty, dummy.Apply(MagicEffect.Of(MagicEffectKind.Force, 5f, nextEvent++), out float push));
            Assert.AreEqual(0f, push);
            Assert.AreEqual(EffectResponse.Applied, dummy.Apply(MagicEffect.Of(MagicEffectKind.Heat, 30f, nextEvent++), out _));
            Assert.AreEqual(42f, dummy.Thermal.Heat, 0.001f);
        }

        [Test]
        public void AnImmuneCreatureAndAnImmuneObjectBothResistHeat()
        {
            var immune = new HeatSettings { susceptibility = 0f };
            var creature = new CreatureEffectModel(immune, 0f, true);
            var thing = new MagicalPropertySet(new MagicalPropertySettings { thermal = true, thermalSettings = immune });
            MagicEffect heat = MagicEffect.Of(MagicEffectKind.Heat, 500f, nextEvent++);

            Assert.AreEqual(EffectResponse.Resisted, creature.Apply(heat, out _));
            Assert.AreEqual(EffectResponse.Resisted, thing.Apply(heat));
        }

        [Test]
        public void TheSameArithmeticMovesEverything()
        {
            Assert.AreEqual(0f, KineticRules.Displacement(1f, 0.2f, 1.5f), "Below the minimum.");
            Assert.AreEqual(4f, KineticRules.Displacement(5f, 0.2f, 1.5f), 0.0001f);
            Assert.AreEqual(0f, KineticRules.Displacement(5f, 1f), "Fully resisted.");
            Assert.AreEqual(5f, KineticRules.Displacement(5f, 0f), 0.0001f);
            Assert.AreEqual(0f, KineticRules.Displacement(-3f, 0f));
        }
    }
}
