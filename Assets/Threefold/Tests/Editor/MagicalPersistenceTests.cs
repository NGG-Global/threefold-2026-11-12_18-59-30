using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.Persistence;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The three tiers on a magical object: what of its state is saved and comes back, what is
    /// temporary and is reset, how temporary states stack, run out and are cleaned up, and what
    /// happens to them while the object is switched off. The objects are the six examples with
    /// their default values, driven by the rules alone.
    /// </summary>
    public sealed class MagicalPersistenceTests
    {
        private int nextEvent = 1000;

        private static MagicalPropertySet Example(ExampleObject example)
        {
            return new MagicalPropertySet(WorldMagicDefaults.Settings(example)) { KineticAxis = Vector3.forward };
        }

        private MagicEffect Effect(MagicEffectKind kind, float magnitude, float seconds = 0f)
        {
            return new MagicEffect(kind, magnitude, Vector3.forward, seconds, Vector3.zero, null, -1, EffectTargets.All, nextEvent++);
        }

        private static void Run(MagicalPropertySet set, float seconds, float step = 0.1f)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += step)
            {
                set.Tick(step);
                set.ClearPendingEmissions();
            }
        }

        /// <summary>Saves a set the way the component does and loads the record into a new object of the same kind.</summary>
        private static MagicalPropertySet SaveAndLoad(MagicalPropertySet set, ExampleObject example)
        {
            ObjectRecord record = set.Capture(set.Settings.persisted);
            MagicalPropertySet loaded = Example(example);
            loaded.Load(record, loaded.Settings.persisted);
            return loaded;
        }

        // ------------------------------------------------ persistent state comes back

        [Test]
        public void ADestroyedBarrierStaysDestroyed()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            barricade.Apply(Effect(MagicEffectKind.Heat, 100f));
            Assert.AreEqual(ThermalState.Burning, barricade.Thermal.State);

            // While it only burns there is nothing lasting to save: fire is temporary.
            Assert.IsTrue(barricade.Capture(barricade.Settings.persisted).IsEmpty);

            Run(barricade, 6f);
            Assert.IsTrue(barricade.IsDestroyed);
            ObjectRecord record = barricade.Capture(barricade.Settings.persisted);
            Assert.AreEqual(PersistedState.Destruction, record.Saved);

            MagicalPropertySet loaded = SaveAndLoad(barricade, ExampleObject.WoodenBarricade);
            Assert.IsTrue(loaded.IsDestroyed);
            Assert.IsTrue(loaded.IsAtRest);
            Assert.AreEqual(EffectResponse.Spent, loaded.Apply(Effect(MagicEffectKind.Heat, 50f)));
        }

        [Test]
        public void AnOpenedDoorStaysOpenAndItsDamageIsKeptButNotItsCharge()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(90f, door.Structural.Integrity, 0.001f);

            // Charged but not yet open: the charge is temporary and is lost.
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            MagicalPropertySet half = SaveAndLoad(door, ExampleObject.ReinforcedDoor);
            Assert.AreEqual(ArcaneState.Uncharged, half.Arcane.State);
            Assert.AreEqual(90f, half.Structural.Integrity, 0.001f, "The damage is kept.");
            Assert.AreEqual(StructuralState.Weakened, half.Structural.State);

            // Opened: that lasts.
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(ArcaneState.Activated, door.Arcane.State);
            ObjectRecord record = door.Capture(door.Settings.persisted);
            Assert.AreEqual(PersistedState.Damage | PersistedState.Activation, record.Saved);

            MagicalPropertySet open = SaveAndLoad(door, ExampleObject.ReinforcedDoor);
            Assert.AreEqual(ArcaneState.Activated, open.Arcane.State);
            Assert.IsTrue(open.Meets(PropertyCondition.Activated | PropertyCondition.Fractured), "The way is still open.");
            Run(open, 60f);
            Assert.AreEqual(ArcaneState.Activated, open.Arcane.State, "A latch does not run down after loading either.");
        }

        [Test]
        public void APlatformIsFoundWhereItWasLeft()
        {
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            platform.Apply(Effect(MagicEffectKind.Force, 5f));
            Run(platform, 0.3f);
            Assert.AreEqual(KineticState.Moving, platform.Kinetic.State);

            // Saved in the middle of its glide: what is kept is where the glide ends.
            ObjectRecord record = platform.Capture(platform.Settings.persisted);
            Assert.AreEqual(PersistedState.Position, record.Saved);
            Assert.AreEqual(4f, record.OffsetZ, 0.001f);

            MagicalPropertySet loaded = SaveAndLoad(platform, ExampleObject.MovablePlatform);
            Assert.AreEqual(KineticState.Resting, loaded.Kinetic.State);
            Assert.AreEqual(4f, loaded.Kinetic.Offset.z, 0.001f);
            Assert.IsTrue(loaded.IsAtRest);

            // It carries on from there.
            loaded.Apply(Effect(MagicEffectKind.Force, 5f));
            Run(loaded, 3f);
            Assert.AreEqual(6f, loaded.Kinetic.Offset.z, 0.001f);
            Assert.IsTrue(loaded.Meets(PropertyCondition.AtTravelLimit));
        }

        [Test]
        public void ACollapsedStructureStaysDownAndABraceIsNotSaved()
        {
            MagicalPropertySet braced = Example(ExampleObject.UnstableStructure);
            braced.Apply(Effect(MagicEffectKind.Stabilize, 1f, 1.5f));
            Assert.AreEqual(StabilityState.Braced, braced.Stability.State);
            Assert.IsTrue(braced.Capture(braced.Settings.persisted).IsEmpty);
            Assert.AreEqual(StabilityState.Unstable, SaveAndLoad(braced, ExampleObject.UnstableStructure).Stability.State);

            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(StabilityState.Collapsed, structure.Stability.State);

            MagicalPropertySet loaded = SaveAndLoad(structure, ExampleObject.UnstableStructure);
            Assert.AreEqual(StabilityState.Collapsed, loaded.Stability.State);
            Assert.AreEqual(EffectResponse.Resisted, loaded.Apply(Effect(MagicEffectKind.Stabilize, 1f, 1.5f)), "There is no way back up.");
        }

        // -------------------------------------------------- temporary state is reset

        [Test]
        public void HeatChargeAndStressAreResetByAReload()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            stone.Apply(Effect(MagicEffectKind.Heat, 80f));
            Assert.AreEqual(ThermalState.Heated, stone.Thermal.State);
            MagicalPropertySet coldStone = SaveAndLoad(stone, ExampleObject.HeatSensitiveStone);
            Assert.AreEqual(ThermalState.Unheated, coldStone.Thermal.State);
            Assert.AreEqual(0f, coldStone.Thermal.Heat);

            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(ArcaneState.Activated, crystal.Arcane.State);
            Assert.IsTrue(crystal.Capture(crystal.Settings.persisted).IsEmpty, "A timed activation is temporary by nature.");
            Assert.AreEqual(ArcaneState.Uncharged, SaveAndLoad(crystal, ExampleObject.ArcaneCrystal).Arcane.State);

            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            structure.Apply(Effect(MagicEffectKind.Force, 2.5f));
            Assert.Greater(structure.Stability.Stress, 0f);
            MagicalPropertySet rested = SaveAndLoad(structure, ExampleObject.UnstableStructure);
            Assert.AreEqual(0f, rested.Stability.Stress);
            Assert.AreEqual(80f, rested.Structural.Integrity, "This example does not keep partial damage.");
        }

        [Test]
        public void TheSameObjectReloadedKeepsWhatLastsAndDropsWhatDoesNot()
        {
            // A restart: the object itself takes its saved state again.
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.Force, 5f));
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            ObjectRecord record = door.Capture(door.Settings.persisted);

            door.Load(record, door.Settings.persisted);

            Assert.AreEqual(90f, door.Structural.Integrity, 0.001f);
            Assert.AreEqual(ArcaneState.Uncharged, door.Arcane.State);
            Assert.IsTrue(door.IsAtRest);

            // The effects that brought it here can be delivered again afterwards: its memory of events is fresh.
            Assert.AreEqual(EffectResponse.Applied, door.Apply(MagicEffect.Of(MagicEffectKind.ArcaneCharge, 10f, nextEvent - 1)));
        }

        // ------------------------------------------------------------ exceptions

        [Test]
        public void HeatIsKeptOnlyWhereItIsNamedAsAnException()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone);
            settings.persisted |= PersistedState.Heat;
            var furnace = new MagicalPropertySet(settings);
            furnace.Apply(Effect(MagicEffectKind.Heat, 80f));

            ObjectRecord record = furnace.Capture(settings.persisted);
            Assert.AreNotEqual(PersistedState.None, record.Saved & PersistedState.Heat);
            Assert.AreEqual(80f, record.Heat, 0.001f);

            var loaded = new MagicalPropertySet(settings);
            loaded.Load(record, settings.persisted);
            Assert.AreEqual(ThermalState.Heated, loaded.Thermal.State);

            // The same record on an object that no longer names the exception: the heat is not applied.
            var plain = Example(ExampleObject.HeatSensitiveStone);
            plain.Load(record, plain.Settings.persisted);
            Assert.AreEqual(ThermalState.Unheated, plain.Thermal.State);
        }

        [Test]
        public void AFireNeverSurvivesAReloadEvenWithTheHeatException()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);
            settings.persisted |= PersistedState.Heat;
            var barricade = new MagicalPropertySet(settings);
            barricade.Apply(Effect(MagicEffectKind.Heat, 100f));
            Assert.AreEqual(ThermalState.Burning, barricade.Thermal.State);

            var loaded = new MagicalPropertySet(settings);
            loaded.Load(barricade.Capture(settings.persisted), settings.persisted);

            Assert.AreEqual(ThermalState.Unheated, loaded.Thermal.State);
            Assert.IsTrue(loaded.IsAtRest);
        }

        [Test]
        public void ChargeIsKeptOnlyAsAnExceptionAndATimedActivationNeverAsRunning()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal);
            settings.persisted = PersistedState.Charge;
            var crystal = new MagicalPropertySet(settings);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 40f));

            var half = new MagicalPropertySet(settings);
            half.Load(crystal.Capture(settings.persisted), settings.persisted);
            Assert.AreEqual(ArcaneState.Charged, half.Arcane.State);
            Assert.AreEqual(40f, half.Arcane.Charge, 0.001f);

            // Activated on a timer: what comes back is the charge, not the running timer.
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(ArcaneState.Activated, crystal.Arcane.State);
            var full = new MagicalPropertySet(settings);
            full.Load(crystal.Capture(settings.persisted), settings.persisted);
            Assert.AreEqual(ArcaneState.Charged, full.Arcane.State);

            // "While charged" is a state of the charge itself, so it does come back.
            settings.arcaneSettings.activation = ArcaneActivation.WhileCharged;
            var lamp = new MagicalPropertySet(settings);
            lamp.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            var lit = new MagicalPropertySet(settings);
            lit.Load(lamp.Capture(settings.persisted), settings.persisted);
            Assert.AreEqual(ArcaneState.Activated, lit.Arcane.State);
        }

        // ----------------------------------------------------- loading is reliable

        [Test]
        public void LoadingAgainAndAgainGivesTheSameState()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.Force, 5f));
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            ObjectRecord first = door.Capture(door.Settings.persisted);

            MagicalPropertySet current = door;
            for (int round = 0; round < 8; round++)
            {
                current = SaveAndLoad(current, ExampleObject.ReinforcedDoor);
                ObjectRecord again = current.Capture(current.Settings.persisted);
                Assert.IsTrue(first.Matches(again), $"Round {round}: the record drifted.");
            }

            // And loading the same record into the same object repeatedly leaves it unchanged.
            for (int round = 0; round < 8; round++)
            {
                current.Load(first, current.Settings.persisted);
            }

            Assert.IsTrue(first.Matches(current.Capture(current.Settings.persisted)));
        }

        [Test]
        public void LoadingAnnouncesNothingAndSendsNoReaction()
        {
            // The stone's bursting apart shoves what is near: once, when it happens, not when it is loaded.
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            stone.Apply(Effect(MagicEffectKind.Heat, 80f));
            stone.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.IsTrue(stone.IsDestroyed);
            Assert.AreEqual(1, stone.PendingEmissionCount, "Shattering sends its reaction.");
            ObjectRecord record = stone.Capture(stone.Settings.persisted);

            MagicalPropertySet loaded = Example(ExampleObject.HeatSensitiveStone);
            var announced = new List<string>();
            loaded.StateChanged += (kind, from, to) => announced.Add($"{kind}:{from}>{to}");

            loaded.Load(record, loaded.Settings.persisted);
            loaded.Tick(0.1f);

            Assert.IsTrue(loaded.IsDestroyed);
            Assert.IsEmpty(announced, "What already happened is not announced again.");
            Assert.AreEqual(0, loaded.PendingEmissionCount, "And its reaction is not sent again.");

            // The same for something that is still there after loading and goes on being advanced: a
            // structure that had come down does not shove its surroundings a second time.
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(1, structure.PendingEmissionCount);

            MagicalPropertySet down = Example(ExampleObject.UnstableStructure);
            down.StateChanged += (kind, from, to) => announced.Add($"{kind}:{from}>{to}");
            down.Load(structure.Capture(structure.Settings.persisted), down.Settings.persisted);
            for (int i = 0; i < 20; i++)
            {
                down.Tick(0.1f);
            }

            Assert.AreEqual(StabilityState.Collapsed, down.Stability.State);
            Assert.IsEmpty(announced);
            Assert.AreEqual(0, down.PendingEmissionCount);
        }

        [Test]
        public void ARecordThatDoesNotFitDoesNoHarm()
        {
            // A door's record on a platform: nothing in it applies.
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.Force, 5f));
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            platform.Load(door.Capture(door.Settings.persisted), platform.Settings.persisted);
            Assert.AreEqual(Vector3.zero, platform.Kinetic.Offset);
            Assert.IsTrue(platform.IsAtRest);

            // Values outside what the object can hold are brought within it, not believed.
            var wild = new ObjectRecord
            {
                Saved = PersistedState.Position | PersistedState.Damage | PersistedState.Activation, OffsetX = 50f, OffsetZ = 999f,
                Integrity = 99999f, Activated = true,
            };
            platform.Load(wild, platform.Settings.persisted);
            Assert.AreEqual(6f, platform.Kinetic.Offset.z, 0.001f, "Held to the end of its rail.");
            Assert.AreEqual(0f, platform.Kinetic.Offset.x, 0.001f, "And on its rail.");

            MagicalPropertySet target = Example(ExampleObject.ReinforcedDoor);
            wild.Integrity = -5f;
            target.Load(wild, target.Settings.persisted);
            Assert.AreEqual(0f, target.Structural.Integrity);

            wild.Integrity = float.NaN;
            wild.OffsetZ = float.NaN;
            target.Load(wild, target.Settings.persisted);
            Assert.AreEqual(150f, target.Structural.Integrity, "Not a number: the starting value is used.");
            platform.Load(wild, platform.Settings.persisted);
            Assert.AreEqual(Vector3.zero, platform.Kinetic.Offset);
        }

        [Test]
        public void TheRulesBetweenPropertiesHoldForALoadedState()
        {
            // Keeps its damage but not its collapse: the collapse follows from the damage again.
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            settings.persisted = PersistedState.Damage;
            settings.stabilitySettings.collapseForce = 100f;
            settings.stabilitySettings.stressLimit = 0f;
            var structure = new MagicalPropertySet(settings);

            for (int i = 0; i < 3; i++)
            {
                structure.Apply(Effect(MagicEffectKind.Force, 4f));
            }

            Assert.AreEqual(StructuralState.Fractured, structure.Structural.State);
            Assert.AreEqual(StabilityState.Collapsed, structure.Stability.State);

            var loaded = new MagicalPropertySet(settings);
            loaded.Load(structure.Capture(settings.persisted), settings.persisted);

            Assert.AreEqual(StructuralState.Fractured, loaded.Structural.State);
            Assert.AreEqual(StabilityState.Collapsed, loaded.Stability.State);
        }

        [Test]
        public void EachStateHasATierThatFollowsFromWhatIsSaved()
        {
            MagicalPropertySettings barricade = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);
            Assert.AreEqual(PersistenceTier.Persistent, MagicalObjectRules.TierOf(PropertyCondition.Destroyed, barricade));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Burning, barricade));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Heated, barricade));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Weakened, barricade));

            MagicalPropertySettings door = WorldMagicDefaults.Settings(ExampleObject.ReinforcedDoor);
            Assert.AreEqual(PersistenceTier.Persistent, MagicalObjectRules.TierOf(PropertyCondition.Activated, door));
            Assert.AreEqual(PersistenceTier.Persistent, MagicalObjectRules.TierOf(PropertyCondition.Weakened, door));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Charged, door));

            MagicalPropertySettings crystal = WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal);
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Activated, crystal));

            MagicalPropertySettings platform = WorldMagicDefaults.Settings(ExampleObject.MovablePlatform);
            Assert.AreEqual(PersistenceTier.Persistent, MagicalObjectRules.TierOf(PropertyCondition.AtTravelLimit, platform));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Moving, platform));

            MagicalPropertySettings structure = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            Assert.AreEqual(PersistenceTier.Persistent, MagicalObjectRules.TierOf(PropertyCondition.Collapsed, structure));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Braced, structure));

            // The exception moves heat up a tier; the fire itself never.
            barricade.persisted |= PersistedState.Heat;
            Assert.AreEqual(PersistenceTier.Persistent, MagicalObjectRules.TierOf(PropertyCondition.Heated, barricade));
            Assert.AreEqual(PersistenceTier.Temporary, MagicalObjectRules.TierOf(PropertyCondition.Burning, barricade));
        }

        // ------------------------------------------------- stacking and refreshing

        [Test]
        public void ABraceRefreshesExtendsOrIsIgnoredByPolicy()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);

            settings.stabilitySettings.braceStacking = StackPolicy.Refresh;
            var refresh = new MagicalPropertySet(settings);
            refresh.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f));
            Run(refresh, 1f);
            Assert.AreEqual(EffectResponse.Applied, refresh.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f)));
            Assert.AreEqual(2f, refresh.Stability.BraceRemaining, 0.01f, "It starts its time again, no more.");

            settings.stabilitySettings.braceStacking = StackPolicy.Extend;
            var extend = new MagicalPropertySet(settings);
            extend.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f));
            Run(extend, 1f);
            extend.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f));
            Assert.AreEqual(3f, extend.Stability.BraceRemaining, 0.01f, "The new time is added to what was left.");
            for (int i = 0; i < 10; i++)
            {
                extend.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f));
            }

            Assert.AreEqual(6f, extend.Stability.BraceRemaining, 0.01f, "Up to the longest a brace can last.");

            settings.stabilitySettings.braceStacking = StackPolicy.Ignore;
            var ignore = new MagicalPropertySet(settings);
            ignore.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f));
            Run(ignore, 1f);
            Assert.AreEqual(EffectResponse.Resisted, ignore.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f)));
            Assert.AreEqual(1f, ignore.Stability.BraceRemaining, 0.01f, "The first brace runs its course.");
            Run(ignore, 1.1f);
            Assert.AreEqual(EffectResponse.Applied, ignore.Apply(Effect(MagicEffectKind.Stabilize, 1f, 2f)), "Then a new one takes.");
        }

        [Test]
        public void MoreHeatOnAFireFollowsThePolicy()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);
            settings.structural = false;

            // The default: a fire cannot be fed.
            var ignore = new MagicalPropertySet(settings);
            ignore.Apply(Effect(MagicEffectKind.Heat, 100f));
            Run(ignore, 3f);
            Assert.AreEqual(EffectResponse.Resisted, ignore.Apply(Effect(MagicEffectKind.Heat, 100f)));
            Run(ignore, 3.1f);
            Assert.AreEqual(ThermalState.Unheated, ignore.Thermal.State, "Six seconds, as its burn time says.");

            settings.thermalSettings.burnStacking = StackPolicy.Refresh;
            var refresh = new MagicalPropertySet(settings);
            refresh.Apply(Effect(MagicEffectKind.Heat, 100f));
            Run(refresh, 3f);
            Assert.AreEqual(EffectResponse.Applied, refresh.Apply(Effect(MagicEffectKind.Heat, 100f)));
            Run(refresh, 5f);
            Assert.AreEqual(ThermalState.Burning, refresh.Thermal.State, "The burn started its time again.");
            Run(refresh, 1.2f);
            Assert.AreEqual(ThermalState.Unheated, refresh.Thermal.State);

            settings.thermalSettings.burnStacking = StackPolicy.Extend;
            var extend = new MagicalPropertySet(settings);
            extend.Apply(Effect(MagicEffectKind.Heat, 100f));
            for (int i = 0; i < 5; i++)
            {
                extend.Apply(Effect(MagicEffectKind.Heat, 100f));
            }

            Run(extend, 11.5f);
            Assert.AreEqual(ThermalState.Burning, extend.Thermal.State);
            Run(extend, 0.7f);
            Assert.AreEqual(ThermalState.Unheated, extend.Thermal.State, "At most twice the burn time, however much is added.");
        }

        [Test]
        public void MoreChargeOnATimedActivationFollowsThePolicy()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal);

            var ignore = new MagicalPropertySet(settings);
            ignore.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Run(ignore, 5f);
            Assert.AreEqual(EffectResponse.Resisted, ignore.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f)));
            Run(ignore, 5.1f);
            Assert.AreEqual(ArcaneState.Uncharged, ignore.Arcane.State);

            settings.arcaneSettings.activeStacking = StackPolicy.Refresh;
            var refresh = new MagicalPropertySet(settings);
            refresh.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Run(refresh, 5f);
            Assert.AreEqual(EffectResponse.Applied, refresh.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f)));
            Run(refresh, 9f);
            Assert.AreEqual(ArcaneState.Activated, refresh.Arcane.State);
            Run(refresh, 1.2f);
            Assert.AreEqual(ArcaneState.Uncharged, refresh.Arcane.State);

            settings.arcaneSettings.activeStacking = StackPolicy.Extend;
            var extend = new MagicalPropertySet(settings);
            extend.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            for (int i = 0; i < 4; i++)
            {
                extend.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            }

            Run(extend, 19.5f);
            Assert.AreEqual(ArcaneState.Activated, extend.Arcane.State);
            Run(extend, 0.7f);
            Assert.AreEqual(ArcaneState.Uncharged, extend.Arcane.State, "At most twice the active time.");
        }

        // ------------------------------------------------- expiry and cleaning up

        [Test]
        public void ABraceCanBeMadeBreakable()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            settings.stabilitySettings.braceBreakForce = 4.5f;
            var structure = new MagicalPropertySet(settings);
            structure.Apply(Effect(MagicEffectKind.Stabilize, 1f, 5f));

            // Below the breaking force the brace holds.
            structure.Apply(Effect(MagicEffectKind.Force, 4f));
            Assert.AreEqual(StabilityState.Braced, structure.Stability.State);

            // At it, the brace ends before its time. The blow is spent on the brace: the structure still stands.
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(StabilityState.Unstable, structure.Stability.State);
            Assert.AreEqual(0f, structure.Stability.Stress, "The blow that broke the brace did nothing more.");

            // The next one brings it down.
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(StabilityState.Collapsed, structure.Stability.State);
        }

        [Test]
        public void EveryTemporaryStateEndsWithTheObject()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(ArcaneState.Charged, door.Arcane.State);

            for (int i = 0; i < 3; i++)
            {
                door.Apply(Effect(MagicEffectKind.Force, 5f));
            }

            Assert.IsTrue(door.IsDestroyed);
            Assert.AreEqual(ArcaneState.Uncharged, door.Arcane.State, "Its charge went with it.");
            Assert.IsTrue(door.IsAtRest, "Nothing is left that needs time to pass.");

            // A brace on something that is then destroyed: gone as well.
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            settings.stabilitySettings.collapseWhenStructure = StructuralTrigger.Never;
            var structure = new MagicalPropertySet(settings);
            structure.Apply(Effect(MagicEffectKind.Stabilize, 1f, 5f));
            for (int i = 0; i < 4; i++)
            {
                structure.Apply(Effect(MagicEffectKind.Force, 5f));
            }

            Assert.IsTrue(structure.IsDestroyed);
            Assert.AreNotEqual(StabilityState.Braced, structure.Stability.State);
            Assert.IsTrue(structure.IsAtRest);
        }

        [Test]
        public void ClearingTemporaryStateKeepsWhatLasts()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.Force, 5f));
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));

            door.ClearTemporary();

            Assert.AreEqual(90f, door.Structural.Integrity, 0.001f, "Damage is not temporary.");
            Assert.AreEqual(ArcaneState.Uncharged, door.Arcane.State);

            // A latched activation is not temporary either.
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            door.ClearTemporary();
            Assert.AreEqual(ArcaneState.Activated, door.Arcane.State);

            // A glide is completed rather than undone: the object is where it was going.
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            platform.Apply(Effect(MagicEffectKind.Force, 5f));
            platform.ClearTemporary();
            Assert.AreEqual(4f, platform.Kinetic.Offset.z, 0.001f);
            Assert.IsTrue(platform.IsAtRest);
        }

        // ------------------------------------------------ while switched off

        [Test]
        public void TimeAwayIsMadeUpSilently()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            stone.Apply(Effect(MagicEffectKind.Heat, 80f));
            var announced = new List<string>();
            stone.StateChanged += (kind, from, to) => announced.Add($"{kind}:{from}>{to}");

            stone.CatchUp(2f);
            Assert.AreEqual(ThermalState.Heated, stone.Thermal.State, "Two seconds: still within its delay.");

            stone.CatchUp(20f);
            Assert.AreEqual(ThermalState.Unheated, stone.Thermal.State);
            Assert.AreEqual(0f, stone.Thermal.Heat);
            Assert.IsEmpty(announced, "Nobody was there to see it cool.");
            Assert.IsTrue(stone.IsAtRest);
        }

        [Test]
        public void AFireBurnsDownWhileNobodyLooksAndSpreadsNowhere()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            barricade.Apply(Effect(MagicEffectKind.Heat, 100f));
            barricade.ClearPendingEmissions();

            barricade.CatchUp(30f);

            Assert.IsTrue(barricade.IsDestroyed, "The lasting result is the same as if it had been watched.");
            Assert.AreEqual(0, barricade.PendingEmissionCount, "An object that is away sets nothing else alight.");
            Assert.AreEqual(PersistedState.Destruction, barricade.Capture(barricade.Settings.persisted).Saved);
        }

        [Test]
        public void TheResultOfMakingUpTimeDoesNotDependOnHowItIsCutUp()
        {
            MagicalPropertySet once = Example(ExampleObject.HeatSensitiveStone);
            MagicalPropertySet pieces = Example(ExampleObject.HeatSensitiveStone);
            once.Apply(Effect(MagicEffectKind.Heat, 80f));
            pieces.Apply(Effect(MagicEffectKind.Heat, 80f));

            once.CatchUp(4.5f);
            pieces.CatchUp(1.5f);
            pieces.CatchUp(1.5f);
            pieces.CatchUp(1.5f);

            Assert.AreEqual(once.Thermal.Heat, pieces.Thermal.Heat, 0.001f);
            Assert.AreEqual(once.Thermal.State, pieces.Thermal.State);
        }

        [Test]
        public void MakingUpTimeIsBounded()
        {
            // Something that never comes to rest by itself: charge that does not drain.
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal);
            settings.arcaneSettings.decayPerSecond = 0f;
            var crystal = new MagicalPropertySet(settings);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 40f));

            // A very long absence does not mean a very long calculation: after two minutes' worth the
            // temporary state is simply dropped.
            crystal.CatchUp(100000f);

            Assert.AreEqual(ArcaneState.Uncharged, crystal.Arcane.State);
            Assert.IsTrue(crystal.IsAtRest);
        }

        // ------------------------------------------------------ reaction rules

        [Test]
        public void AReactionOnEnteringAStateFiresOncePerEntry()
        {
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 40f));
            Assert.AreEqual(0, crystal.PendingEmissionCount, "Charged is not activated.");

            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(1, crystal.PendingEmissionCount);
            EmissionRequest request = crystal.GetPendingEmission(0);
            Assert.AreEqual(1, request.Depth, "One step from the player's arrow.");
            Assert.AreEqual(nextEvent - 1, request.EventId, "It carries the arrow's identity on.");
            Assert.AreEqual(ReactionPriority.Critical, crystal.GetEmissionRule(request.Rule).priority);
            crystal.ClearPendingEmissions();

            // While it stays activated, nothing more is sent.
            Run(crystal, 5f);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(0, crystal.PendingEmissionCount);

            // It goes dark and is activated again: a new entry, a new reaction.
            Run(crystal, 6f);
            Assert.AreEqual(ArcaneState.Uncharged, crystal.Arcane.State);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(1, crystal.PendingEmissionCount);
        }

        [Test]
        public void ARepeatingReactionRunsAtItsIntervalWhileTheStateLasts()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            barricade.Apply(Effect(MagicEffectKind.Heat, 100f));
            Assert.AreEqual(0, barricade.PendingEmissionCount, "The first comes after one interval, not at once.");
            Assert.IsFalse(barricade.IsAtRest);

            int pulses = 0;
            var ids = new HashSet<int>();
            for (int step = 0; step < 100; step++)
            {
                barricade.Tick(0.1f);
                for (int i = 0; i < barricade.PendingEmissionCount; i++)
                {
                    EmissionRequest request = barricade.GetPendingEmission(i);
                    Assert.AreEqual(1, request.Depth);
                    Assert.IsTrue(ids.Add(request.EventId), "Each pulse is an event of its own.");
                    pulses++;
                }

                barricade.ClearPendingEmissions();
            }

            // It burns down after five seconds: one pulse a second until then, none after.
            Assert.IsTrue(barricade.IsDestroyed);
            Assert.AreEqual(4, pulses);
        }

        [Test]
        public void ALongStepGivesOnePulseNotABurst()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);
            settings.structural = false;
            var fire = new MagicalPropertySet(settings);
            fire.Apply(Effect(MagicEffectKind.Heat, 100f));

            fire.Tick(3.5f);

            Assert.AreEqual(1, fire.PendingEmissionCount);
        }

        [Test]
        public void AReactionIsOneStepFurtherFromThePlayerThanItsCause()
        {
            // Set alight by another object's reaction, two steps from the player.
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            barricade.Apply(new MagicEffect(MagicEffectKind.Heat, 100f, Vector3.forward, 0f, Vector3.zero, null, -1,
                EffectTargets.All, nextEvent++, 2));
            Assert.AreEqual(2, barricade.GetCauseDepth(WorldPropertyKind.Thermal));

            barricade.Tick(1.05f);
            Assert.AreEqual(1, barricade.PendingEmissionCount);
            Assert.AreEqual(3, barricade.GetPendingEmission(0).Depth);
            barricade.ClearPendingEmissions();

            // The player then strikes it. That does not make its fire any closer to the player.
            barricade.Apply(Effect(MagicEffectKind.Force, 2f));
            Assert.AreEqual(0, barricade.GetCauseDepth(WorldPropertyKind.Structural));
            barricade.Tick(1.05f);
            Assert.AreEqual(3, barricade.GetPendingEmission(0).Depth);

            // What the fire does to the structure is as far away as the fire.
            Assert.AreEqual(2, barricade.GetCauseDepth(WorldPropertyKind.Structural));
        }

        [Test]
        public void AnObjectWithARunningReactionIsNotAtRestAndAnIdleOneIs()
        {
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            Assert.IsTrue(structure.IsAtRest, "Unstable, but nothing is changing.");

            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone);
            settings.emissions = new[]
            {
                new ReactionEmission
                {
                    when = PropertyCondition.Heated, timing = EmissionTiming.WhileHolding, interval = 1f,
                    effect = MagicEffectKind.Heat, magnitude = 5f, radius = 2f,
                },
            };
            settings.thermalSettings.decayPerSecond = 0f;
            var furnace = new MagicalPropertySet(settings);
            furnace.Apply(Effect(MagicEffectKind.Heat, 80f));

            Assert.IsFalse(furnace.IsAtRest, "It keeps radiating, so time has to keep passing for it.");
        }

        // ----------------------------------------------------- review follow-ups

        [Test]
        public void RubbleThatWasClearedAwayStaysCleared()
        {
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(StabilityState.Collapsed, structure.Stability.State);

            // The fallen structure is then broken up until nothing is left of it.
            for (int i = 0; i < 4 && !structure.IsDestroyed; i++)
            {
                structure.Apply(Effect(MagicEffectKind.Force, 5f));
            }

            Assert.IsTrue(structure.IsDestroyed);

            MagicalPropertySet loaded = SaveAndLoad(structure, ExampleObject.UnstableStructure);
            Assert.IsTrue(loaded.IsDestroyed, "The way that was cleared is still clear.");
            Assert.AreEqual(StabilityState.Collapsed, loaded.Stability.State);
        }

        [Test]
        public void ALatchHoldsThroughARestartAndGoesOnlyWhenTheStateIsReplaced()
        {
            // The condition holds: active whatever else is true.
            Assert.IsTrue(PropertyStateTrigger.DecideAfterReset(true, false, false, false, false));
            Assert.IsTrue(PropertyStateTrigger.DecideAfterReset(true, false, true, true, false));

            // A restart resets the crystal the trigger watches. A latch that had caught stays caught,
            // as a solved puzzle stays solved; a trigger without a latch follows its object.
            Assert.IsTrue(PropertyStateTrigger.DecideAfterReset(false, true, true, false, false));
            Assert.IsFalse(PropertyStateTrigger.DecideAfterReset(false, true, false, false, false));
            Assert.IsFalse(PropertyStateTrigger.DecideAfterReset(false, false, true, false, false), "A latch that never caught has nothing to hold.");

            // The state itself was replaced (a new game, a checkpoint): the latch holds only if what is
            // remembered for the trigger says so.
            Assert.IsFalse(PropertyStateTrigger.DecideAfterReset(false, true, true, true, false));
            Assert.IsTrue(PropertyStateTrigger.DecideAfterReset(false, true, true, true, true));
            Assert.IsFalse(PropertyStateTrigger.DecideAfterReset(false, true, false, true, true), "Without a latch nothing is held.");
        }

        [Test]
        public void TheUpdateOfAnOlderDefinitionKeepsWhatWasAlreadySetByHand()
        {
            MagicalPropertySettings defaults = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);

            // Untouched: everything the new version introduced is filled in.
            var untouched = new MagicalPropertySettings { thermal = true };
            Assert.IsTrue(WorldMagicDefaults.FillWorldState(untouched, defaults));
            Assert.AreEqual(defaults.persisted, untouched.persisted);
            Assert.AreEqual(1, untouched.emissions.Length);
            Assert.IsTrue(untouched.thermal, "Values that existed before are not touched.");

            // Any one of them already set by hand: all of them are kept.
            var persistedChanged = new MagicalPropertySettings { persisted = PersistedState.Position };
            Assert.IsFalse(WorldMagicDefaults.FillWorldState(persistedChanged, defaults));
            Assert.AreEqual(PersistedState.Position, persistedChanged.persisted);
            Assert.AreEqual(0, persistedChanged.emissions.Length);

            var reactionsAdded = new MagicalPropertySettings { emissions = new[] { new ReactionEmission { magnitude = 3f } } };
            Assert.IsFalse(WorldMagicDefaults.FillWorldState(reactionsAdded, defaults));
            Assert.AreEqual(3f, reactionsAdded.emissions[0].magnitude);
            Assert.AreEqual(WorldMagicDefaults.UnsetPersisted, reactionsAdded.persisted);

            var markedCritical = new MagicalPropertySettings { critical = true };
            Assert.IsFalse(WorldMagicDefaults.FillWorldState(markedCritical, defaults));

            var inactiveChosen = new MagicalPropertySettings { whenInactive = InactiveBehaviour.Reset };
            Assert.IsFalse(WorldMagicDefaults.FillWorldState(inactiveChosen, defaults));
            Assert.AreEqual(InactiveBehaviour.Reset, inactiveChosen.whenInactive);

            Assert.IsFalse(WorldMagicDefaults.FillWorldState(null, defaults));
            Assert.IsFalse(WorldMagicDefaults.FillWorldState(untouched, null));
        }

        [Test]
        public void TheBurnPolicyMeansTheSameToACreatureAsToAnObject()
        {
            var settings = new HeatSettings { capacity = 50f, burnSeconds = 4f, flammable = true };

            // The default: a creature that is alight takes no more heat, exactly as an object.
            var creature = new CreatureEffectModel(settings, 0f, true);
            creature.Apply(Effect(MagicEffectKind.Heat, 100f), out _);
            Assert.IsTrue(creature.Thermal.IsBurning);
            Assert.AreEqual(EffectResponse.Resisted, creature.Apply(Effect(MagicEffectKind.Heat, 100f), out _));

            // Set to renew: the same field now does for a creature what it does for an object.
            settings = new HeatSettings { capacity = 50f, burnSeconds = 4f, flammable = true, burnStacking = StackPolicy.Refresh };
            creature = new CreatureEffectModel(settings, 0f, true);
            var thing = new MagicalPropertySet(new MagicalPropertySettings { thermal = true, thermalSettings = settings });
            creature.Apply(Effect(MagicEffectKind.Heat, 100f), out _);
            thing.Apply(Effect(MagicEffectKind.Heat, 100f));
            creature.Thermal.Tick(3f);
            thing.Tick(3f);

            Assert.AreEqual(EffectResponse.Applied, creature.Apply(Effect(MagicEffectKind.Heat, 100f), out _));
            Assert.IsFalse(creature.IgnitedByLastEffect, "Renewed, not set alight a second time.");
            Assert.AreEqual(EffectResponse.Applied, thing.Apply(Effect(MagicEffectKind.Heat, 100f)));

            creature.Thermal.Tick(3f);
            thing.Tick(3f);
            Assert.IsTrue(creature.Thermal.IsBurning, "Six seconds in, with a burn time of four: it was renewed.");
            Assert.AreEqual(ThermalState.Burning, thing.Thermal.State);
        }
    }
}
