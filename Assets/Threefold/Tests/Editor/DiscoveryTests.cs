using System;
using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.Discovery;
using Threefold.Magic;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// How the world hints at its magic and answers what the player tries: which objects resonate
    /// with which discipline, that nothing resonates falsely, what each kind of attempt is answered
    /// with, and that the options and the quality level change how rich an answer is but never
    /// which answer it is. The objects are the six examples and the disciplines their defaults.
    /// </summary>
    public sealed class DiscoveryTests
    {
        private static readonly ExampleObject[] Examples =
        {
            ExampleObject.ArcaneCrystal, ExampleObject.WoodenBarricade, ExampleObject.HeatSensitiveStone,
            ExampleObject.MovablePlatform, ExampleObject.UnstableStructure, ExampleObject.ReinforcedDoor,
        };

        private static readonly MagicDiscipline[] Disciplines = { MagicDiscipline.Arcane, MagicDiscipline.Flame, MagicDiscipline.Aegis };

        private static readonly FeedbackCategory[] Categories =
        {
            FeedbackCategory.Incompatible, FeedbackCategory.Insufficient, FeedbackCategory.Progress,
            FeedbackCategory.Transition, FeedbackCategory.Completed, FeedbackCategory.Blocked,
        };

        private int nextEvent = 9000;

        private static MagicalPropertySet Example(ExampleObject example)
        {
            return new MagicalPropertySet(WorldMagicDefaults.Settings(example)) { KineticAxis = Vector3.forward };
        }

        private MagicEffect Effect(MagicEffectKind kind, float magnitude, Vector3? direction = null)
        {
            return new MagicEffect(kind, magnitude, direction ?? Vector3.forward, 1.5f, Vector3.zero, null, 0, EffectTargets.All, nextEvent++);
        }

        /// <summary>Applies an effect and classifies the answer the way the component does.</summary>
        private FeedbackResult Try(MagicalPropertySet set, MagicEffectKind kind, float magnitude, Vector3? direction = null)
        {
            bool significant = false;
            Action<WorldPropertyKind, int, int> watch = (property, from, to) =>
                significant |= ExperimentFeedback.IsSignificant(property, from, to);
            set.StateChanged += watch;
            MagicEffect effect = Effect(kind, magnitude, direction);
            EffectResponse response = set.Apply(effect);
            set.StateChanged -= watch;
            set.ClearPendingEmissions();
            return ExperimentFeedback.Classify(set, effect, response, significant);
        }

        private static void Run(MagicalPropertySet set, float seconds)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += 0.1f)
            {
                set.Tick(0.1f);
                set.ClearPendingEmissions();
            }
        }

        // ------------------------------------------- what the disciplines offer

        [Test]
        public void EachDisciplineOffersTheEffectsItsAttacksReallyCarry()
        {
            Assert.AreEqual(EffectKinds.ArcaneCharge, Compatibility.OfDefaultDiscipline(MagicDiscipline.Arcane));
            Assert.AreEqual(EffectKinds.Heat, Compatibility.OfDefaultDiscipline(MagicDiscipline.Flame));
            Assert.AreEqual(EffectKinds.Force | EffectKinds.Stabilize, Compatibility.OfDefaultDiscipline(MagicDiscipline.Aegis));

            // It follows the values, not the name: an Arcane swing retuned to carry heat is offered as heat.
            MeleeComboProfile melee = DisciplineDefaults.Melee(MagicDiscipline.Arcane);
            melee.steps[0].heat = 5f;
            EffectKinds retuned = Compatibility.OfDiscipline(melee, DisciplineDefaults.Charged(MagicDiscipline.Arcane).Projectile.Effects(1f),
                DisciplineDefaults.Guard(MagicDiscipline.Arcane));
            Assert.AreEqual(EffectKinds.ArcaneCharge | EffectKinds.Heat, retuned);

            Assert.AreEqual(EffectKinds.None, Compatibility.OfDiscipline(null, default, null));
        }

        // -------------------------------------------------- level 2: resonance

        [Test]
        public void AnObjectResonatesWithTheDisciplinesThatCouldChangeIt()
        {
            var expected = new Dictionary<ExampleObject, bool[]>
            {
                // Arcane, Flame, Aegis
                { ExampleObject.ArcaneCrystal, new[] { true, false, false } },
                { ExampleObject.WoodenBarricade, new[] { false, true, true } },
                { ExampleObject.HeatSensitiveStone, new[] { false, true, true } },
                { ExampleObject.MovablePlatform, new[] { false, false, true } },
                { ExampleObject.UnstableStructure, new[] { false, false, true } },
                { ExampleObject.ReinforcedDoor, new[] { true, false, true } },
            };

            foreach (ExampleObject example in Examples)
            {
                MagicalPropertySet set = Example(example);
                for (int i = 0; i < Disciplines.Length; i++)
                {
                    bool resonates = Compatibility.Matching(set, Compatibility.OfDefaultDiscipline(Disciplines[i])) != EffectKinds.None;
                    Assert.AreEqual(expected[example][i], resonates, $"{example} with {Disciplines[i]}.");
                }
            }
        }

        [Test]
        public void NoObjectResonatesWithEveryDiscipline()
        {
            foreach (ExampleObject example in Examples)
            {
                MagicalPropertySet set = Example(example);
                int count = 0;
                foreach (MagicDiscipline discipline in Disciplines)
                {
                    count += Compatibility.Matching(set, Compatibility.OfDefaultDiscipline(discipline)) != EffectKinds.None ? 1 : 0;
                }

                Assert.Greater(count, 0, $"{example} answers something.");
                Assert.Less(count, 3, $"{example} does not answer everything.");
            }
        }

        [Test]
        public void ResonanceIsNeverShownForSomethingThatCannotWork()
        {
            var resonance = new ResonanceSettings();
            var strong = new DiscoverySettings { clueIntensity = 2f, strongResonance = true };

            // Not compatible: nothing, however close, however well aimed, whatever the options.
            Assert.AreEqual(0f, ResonanceRules.Strength(false, 0.5f, true, true, resonance, strong));
            Assert.AreEqual(0f, ResonanceRules.Strength(false, 0.5f, false, false, resonance, new DiscoverySettings()));

            // And "could respond" is never claimed for an effect that then does nothing, in any state
            // the examples can be brought into: this is what makes resonance trustworthy.
            foreach (ExampleObject example in Examples)
            {
                foreach (MagicalPropertySet set in StatesOf(example))
                {
                    for (int kind = 0; kind < 4; kind++)
                    {
                        var effectKind = (MagicEffectKind)kind;
                        if (set.CouldRespondTo(effectKind))
                        {
                            continue;
                        }

                        // The strongest possible attempt, from either side.
                        EffectResponse forward = set.Apply(Effect(effectKind, 1000f, Vector3.forward));
                        EffectResponse back = set.Apply(Effect(effectKind, 1000f, Vector3.back));
                        Assert.AreNotEqual(EffectResponse.Applied, forward, $"{example} / {effectKind}: claimed impossible, yet it worked.");
                        Assert.AreNotEqual(EffectResponse.Applied, back, $"{example} / {effectKind}: claimed impossible, yet it worked.");
                    }
                }
            }
        }

        [Test]
        public void WhereResonanceIsShownAStrongEnoughAttemptDoesWork()
        {
            foreach (ExampleObject example in Examples)
            {
                foreach (MagicalPropertySet set in StatesOf(example))
                {
                    for (int kind = 0; kind < 4; kind++)
                    {
                        var effectKind = (MagicEffectKind)kind;
                        if (!set.CouldRespondTo(effectKind))
                        {
                            continue;
                        }

                        bool worked = set.Apply(Effect(effectKind, 1000f, Vector3.forward)) == EffectResponse.Applied ||
                                      set.Apply(Effect(effectKind, 1000f, Vector3.back)) == EffectResponse.Applied;
                        Assert.IsTrue(worked, $"{example} / {effectKind}: resonance promised a possibility that does not exist.");
                    }
                }
            }
        }

        /// <summary>Each example fresh, and in the states the disciplines can bring it into.</summary>
        private IEnumerable<MagicalPropertySet> StatesOf(ExampleObject example)
        {
            yield return Example(example);

            // Warmed, charged, shoved and braced a little.
            MagicalPropertySet touched = Example(example);
            touched.Apply(Effect(MagicEffectKind.Heat, 30f));
            touched.Apply(Effect(MagicEffectKind.ArcaneCharge, 40f));
            touched.Apply(Effect(MagicEffectKind.Force, 2f));
            touched.Apply(Effect(MagicEffectKind.Stabilize, 1f));
            touched.ClearPendingEmissions();
            yield return touched;

            // Taken as far as each effect goes.
            MagicalPropertySet spent = Example(example);
            spent.Apply(Effect(MagicEffectKind.ArcaneCharge, 1000f));
            spent.Apply(Effect(MagicEffectKind.Heat, 1000f));
            spent.ClearPendingEmissions();
            yield return spent;

            MagicalPropertySet pushed = Example(example);
            pushed.Apply(Effect(MagicEffectKind.Force, 5f));
            Run(pushed, 3f);
            pushed.Apply(Effect(MagicEffectKind.Force, 5f));
            Run(pushed, 3f);
            yield return pushed;

            MagicalPropertySet broken = Example(example);
            for (int i = 0; i < 6; i++)
            {
                broken.Apply(Effect(MagicEffectKind.Force, 50f));
            }

            broken.ClearPendingEmissions();
            yield return broken;
        }

        [Test]
        public void ResonanceFollowsTheStateOfTheObject()
        {
            EffectKinds arcane = Compatibility.OfDefaultDiscipline(MagicDiscipline.Arcane);
            EffectKinds flame = Compatibility.OfDefaultDiscipline(MagicDiscipline.Flame);
            EffectKinds aegis = Compatibility.OfDefaultDiscipline(MagicDiscipline.Aegis);

            // A door whose lock is open has nothing more to take from Arcane.
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            Assert.AreEqual(EffectKinds.ArcaneCharge, Compatibility.Matching(door, arcane));
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 200f));
            Assert.AreEqual(EffectKinds.None, Compatibility.Matching(door, arcane));

            // Something alight takes no more heat; when the fire is out, it would again.
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);
            settings.structural = false;
            var fire = new MagicalPropertySet(settings);
            fire.Apply(Effect(MagicEffectKind.Heat, 100f));
            Assert.AreEqual(EffectKinds.None, Compatibility.Matching(fire, flame));
            Run(fire, 7f);
            Assert.AreEqual(EffectKinds.Heat, Compatibility.Matching(fire, flame));

            // A structure that has come down can no longer be braced; what is destroyed answers nothing.
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            Assert.AreEqual(EffectKinds.Force | EffectKinds.Stabilize, Compatibility.Matching(structure, aegis));
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(EffectKinds.Force, Compatibility.Matching(structure, aegis), "Its rubble can still be broken up.");
            for (int i = 0; i < 5; i++)
            {
                structure.Apply(Effect(MagicEffectKind.Force, 50f));
            }

            Assert.IsTrue(structure.IsDestroyed);
            Assert.AreEqual(EffectKinds.None, Compatibility.Matching(structure, EffectKinds.All));
        }

        [Test]
        public void ResonanceIsFaintWhenNearStrongerWhenAimedAndStrongestWhenPreparing()
        {
            var resonance = new ResonanceSettings();
            var settings = new DiscoverySettings();

            float near = ResonanceRules.Strength(true, 2f, false, false, resonance, settings);
            float nearAndPreparing = ResonanceRules.Strength(true, 2f, false, true, resonance, settings);
            float aimed = ResonanceRules.Strength(true, 2f, true, false, resonance, settings);
            float prepared = ResonanceRules.Strength(true, 2f, true, true, resonance, settings);

            Assert.Greater(near, 0f);
            Assert.Less(near, aimed);
            Assert.Less(aimed, prepared);
            Assert.AreEqual(near, nearAndPreparing, "Charging without aiming at it says nothing about it.");

            // It fades with distance and stops at the radius, also when aimed at.
            Assert.Less(ResonanceRules.Strength(true, 7f, false, false, resonance, settings), near);
            Assert.AreEqual(0f, ResonanceRules.Strength(true, resonance.radius + 0.1f, true, true, resonance, settings));

            // It swells and fades rather than switching.
            float shown = 0f;
            shown = ResonanceRules.Approach(shown, 1f, 0.5f, 0.1f);
            Assert.AreEqual(0.2f, shown, 0.0001f);
            for (int i = 0; i < 10; i++)
            {
                shown = ResonanceRules.Approach(shown, 1f, 0.5f, 0.1f);
            }

            Assert.AreEqual(1f, shown, 0.0001f);
        }

        // -------------------------------------- level 3: what an attempt came to

        [Test]
        public void TheWrongKindOfMagicIsAnsweredAsIncompatible()
        {
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(Example(ExampleObject.ArcaneCrystal), MagicEffectKind.Heat, 80f).Category);
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(Example(ExampleObject.ArcaneCrystal), MagicEffectKind.Force, 5f).Category);
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(Example(ExampleObject.MovablePlatform), MagicEffectKind.ArcaneCharge, 100f).Category);
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(Example(ExampleObject.WoodenBarricade), MagicEffectKind.Stabilize, 1f).Category);
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(Example(ExampleObject.ReinforcedDoor), MagicEffectKind.Heat, 80f).Category);
        }

        [Test]
        public void TheRightKindButTooLittleOfItIsAnsweredAsInsufficient()
        {
            // The door strains under an uncharged pulse and does not give.
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            FeedbackResult result = Try(door, MagicEffectKind.Force, 2.5f);
            Assert.AreEqual(FeedbackCategory.Insufficient, result.Category);
            Assert.AreEqual(150f, door.Structural.Integrity, "Nothing was displaced or damaged.");

            // An Aegis strike is too light for the platform.
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            Assert.AreEqual(FeedbackCategory.Insufficient, Try(platform, MagicEffectKind.Force, 1f).Category);
            Assert.AreEqual(Vector3.zero, platform.Kinetic.TargetOffset);

            // A brace too weak to hold.
            Assert.AreEqual(FeedbackCategory.Insufficient, Try(Example(ExampleObject.UnstableStructure), MagicEffectKind.Stabilize, 0.2f).Category);

            // The same attempts with enough behind them are answered differently.
            Assert.AreNotEqual(FeedbackCategory.Insufficient, Try(door, MagicEffectKind.Force, 5f).Category);
            Assert.AreNotEqual(FeedbackCategory.Insufficient, Try(platform, MagicEffectKind.Force, 5f).Category);
        }

        [Test]
        public void AnEffectThatTakesWithoutCrossingAThresholdIsProgressAndGrowsTowardsIt()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            FeedbackResult first = Try(barricade, MagicEffectKind.Heat, 10f);
            FeedbackResult second = Try(barricade, MagicEffectKind.Heat, 5f);

            Assert.AreEqual(FeedbackCategory.Progress, first.Category);
            Assert.AreEqual(FeedbackCategory.Progress, second.Category);
            Assert.Greater(second.Progress, first.Progress, "The answer fills out as the threshold comes nearer.");
            Assert.Less(second.Progress, 1f);

            // A first trace of charge is progress too, not yet "something gave".
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            Assert.AreEqual(FeedbackCategory.Progress, Try(crystal, MagicEffectKind.ArcaneCharge, 40f).Category);
            Assert.AreEqual(FeedbackCategory.Progress, Try(crystal, MagicEffectKind.ArcaneCharge, 40f).Category);

            // Damage that does not yet change the door's state.
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            FeedbackResult dent = Try(door, MagicEffectKind.Force, 4f);
            Assert.AreEqual(FeedbackCategory.Progress, dent.Category);
            Assert.Greater(dent.Progress, 0f);
        }

        [Test]
        public void CrossingAThresholdIsATransitionAndReachingTheEndIsCompletion()
        {
            // Heated, then alight.
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            Assert.AreEqual(FeedbackCategory.Transition, Try(barricade, MagicEffectKind.Heat, 30f).Category, "Heated.");
            Assert.AreEqual(FeedbackCategory.Transition, Try(barricade, MagicEffectKind.Heat, 30f).Category, "Burning.");

            // The crystal activates for a time: a transition. The door's lock opens for good: completion.
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            Assert.AreEqual(FeedbackCategory.Transition, Try(crystal, MagicEffectKind.ArcaneCharge, 100f).Category);
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            Assert.AreEqual(FeedbackCategory.Progress, Try(door, MagicEffectKind.ArcaneCharge, 100f).Category);
            Assert.AreEqual(FeedbackCategory.Completed, Try(door, MagicEffectKind.ArcaneCharge, 100f).Category);

            // The platform gives way and slides.
            Assert.AreEqual(FeedbackCategory.Transition, Try(Example(ExampleObject.MovablePlatform), MagicEffectKind.Force, 5f).Category);

            // The structure is braced, and later comes down.
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            Assert.AreEqual(FeedbackCategory.Transition, Try(structure, MagicEffectKind.Stabilize, 1f).Category);
            Run(structure, 2f);
            Assert.AreEqual(FeedbackCategory.Completed, Try(structure, MagicEffectKind.Force, 5f).Category);

            // The heated stone shatters.
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            Assert.AreEqual(FeedbackCategory.Transition, Try(stone, MagicEffectKind.Heat, 80f).Category);
            Assert.AreEqual(FeedbackCategory.Completed, Try(stone, MagicEffectKind.Force, 5f).Category);
        }

        [Test]
        public void TheRightKindOnSomethingHeldForNowIsAnsweredAsBlocked()
        {
            // Braced: a push that would otherwise have unsettled it does nothing until the brace ends.
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            structure.Apply(Effect(MagicEffectKind.Stabilize, 1f));
            Assert.AreEqual(FeedbackCategory.Blocked, Try(structure, MagicEffectKind.Force, 0.9f).Category);
            Assert.AreEqual(0f, structure.Stability.Stress);

            // Something that slides and can be braced: braced, the strongest push does not move it.
            var slider = new MagicalPropertySet(new MagicalPropertySettings
            {
                kinetic = true,
                kineticSettings = new KineticSettings { resistance = 0f, minimumForce = 0f, minTravel = 0f, maxTravel = 10f },
                stability = true,
                stabilitySettings = new StabilitySettings { startsUnstable = true, collapseForce = 100f, stressLimit = 0f },
            }) { KineticAxis = Vector3.forward };
            slider.Apply(Effect(MagicEffectKind.Stabilize, 1f));
            Assert.AreEqual(FeedbackCategory.Blocked, Try(slider, MagicEffectKind.Force, 5f).Category);
            Assert.AreEqual(Vector3.zero, slider.Kinetic.TargetOffset);

            // Already alight.
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            barricade.Apply(Effect(MagicEffectKind.Heat, 100f));
            Assert.AreEqual(FeedbackCategory.Blocked, Try(barricade, MagicEffectKind.Heat, 50f).Category);

            // The crystal while it is activated.
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            crystal.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            Assert.AreEqual(FeedbackCategory.Blocked, Try(crystal, MagicEffectKind.ArcaneCharge, 100f).Category);

            // The platform against its far stop.
            MagicalPropertySet platform = Example(ExampleObject.MovablePlatform);
            platform.Apply(Effect(MagicEffectKind.Force, 5f));
            Run(platform, 3f);
            platform.Apply(Effect(MagicEffectKind.Force, 5f));
            Run(platform, 3f);
            Assert.IsTrue(platform.Meets(PropertyCondition.AtTravelLimit));
            Assert.AreEqual(FeedbackCategory.Blocked, Try(platform, MagicEffectKind.Force, 5f).Category);

            // The other way it moves again: blocked was for now, not for good.
            Assert.AreEqual(FeedbackCategory.Transition, Try(platform, MagicEffectKind.Force, 5f, Vector3.back).Category);
        }

        [Test]
        public void WhatIsFinishedSaysSoRatherThanRefusing()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 200f));
            Assert.AreEqual(FeedbackCategory.Completed, Try(door, MagicEffectKind.ArcaneCharge, 100f).Category);

            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            structure.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(FeedbackCategory.Completed, Try(structure, MagicEffectKind.Stabilize, 1f).Category, "Too late to brace.");

            // What is gone answers nothing at all.
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            stone.Apply(Effect(MagicEffectKind.Heat, 80f));
            stone.Apply(Effect(MagicEffectKind.Force, 5f));
            Assert.AreEqual(FeedbackCategory.None, Try(stone, MagicEffectKind.Force, 5f).Category);
        }

        [Test]
        public void AnObjectWithSeveralPropertiesAnswersEachKindOnItsOwnTerms()
        {
            // The door: a lock that charges and a structure that only heavy force harms.
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            Assert.AreEqual(FeedbackCategory.Progress, Try(door, MagicEffectKind.ArcaneCharge, 60f).Category);
            Assert.AreEqual(FeedbackCategory.Insufficient, Try(door, MagicEffectKind.Force, 2f).Category);
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(door, MagicEffectKind.Heat, 50f).Category);
            Assert.AreEqual(FeedbackCategory.Incompatible, Try(door, MagicEffectKind.Stabilize, 1f).Category);

            // The progress shown for one kind is that kind's, not the other's.
            FeedbackResult charge = Try(door, MagicEffectKind.ArcaneCharge, 30f);
            Assert.AreEqual(0.6f, charge.Progress, 0.001f);
            Assert.AreEqual(0f, ExperimentFeedback.ProgressOf(door, MagicEffectKind.Force), "Its structure is untouched.");

            // The stone: heat prepares it, force finishes it; each is answered for what it did.
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            Assert.AreEqual(FeedbackCategory.Progress, Try(stone, MagicEffectKind.Force, 5f).Category, "Cold: barely marked.");
            Assert.AreEqual(FeedbackCategory.Transition, Try(stone, MagicEffectKind.Heat, 80f).Category);
            Assert.AreEqual(FeedbackCategory.Completed, Try(stone, MagicEffectKind.Force, 5f).Category);

            // The unstable structure: a brace and a blow act on the same property with opposite results.
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            Assert.AreEqual(FeedbackCategory.Progress, Try(structure, MagicEffectKind.Force, 2f).Category, "Stress builds.");
            Assert.AreEqual(FeedbackCategory.Transition, Try(structure, MagicEffectKind.Stabilize, 1f).Category);
            Assert.AreEqual(FeedbackCategory.Blocked, Try(structure, MagicEffectKind.Force, 0.9f).Category, "The brace holds it.");

            // A brace keeps it standing; it does not armour it. A heavy blow still cracks the stone,
            // and that is what is answered: the structural property, on its own terms.
            Assert.AreEqual(FeedbackCategory.Transition, Try(structure, MagicEffectKind.Force, 5f).Category);
            Assert.AreEqual(StabilityState.Braced, structure.Stability.State);
            Assert.AreEqual(StructuralState.Weakened, structure.Structural.State);
        }

        [Test]
        public void OnlyReachingAThresholdCountsAsSomethingGiving()
        {
            Assert.IsFalse(ExperimentFeedback.IsSignificant(WorldPropertyKind.Arcane, (int)ArcaneState.Uncharged, (int)ArcaneState.Charged));
            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Arcane, (int)ArcaneState.Charged, (int)ArcaneState.Activated));
            Assert.IsFalse(ExperimentFeedback.IsSignificant(WorldPropertyKind.Arcane, (int)ArcaneState.Activated, (int)ArcaneState.Uncharged));

            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Thermal, (int)ThermalState.Unheated, (int)ThermalState.Heated));
            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Thermal, (int)ThermalState.Heated, (int)ThermalState.Burning));
            Assert.IsFalse(ExperimentFeedback.IsSignificant(WorldPropertyKind.Thermal, (int)ThermalState.Heated, (int)ThermalState.Unheated), "Cooling.");

            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Structural, (int)StructuralState.Intact, (int)StructuralState.Weakened));
            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Kinetic, (int)KineticState.Resting, (int)KineticState.Moving));
            Assert.IsFalse(ExperimentFeedback.IsSignificant(WorldPropertyKind.Kinetic, (int)KineticState.Moving, (int)KineticState.Resting), "Coming to rest.");

            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Stability, (int)StabilityState.Unstable, (int)StabilityState.Braced));
            Assert.IsFalse(ExperimentFeedback.IsSignificant(WorldPropertyKind.Stability, (int)StabilityState.Braced, (int)StabilityState.Unstable), "A brace running out.");
            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Stability, (int)StabilityState.Stable, (int)StabilityState.Unstable));
            Assert.IsTrue(ExperimentFeedback.IsSignificant(WorldPropertyKind.Stability, (int)StabilityState.Unstable, (int)StabilityState.Collapsed));
        }

        // --------------------------------------------- not too much, not too often

        [Test]
        public void WhatOtherObjectsSetOffIsShownOnlyWhenSomethingChanged()
        {
            var incompatible = new FeedbackResult(FeedbackCategory.Incompatible, MagicEffectKind.Heat, 0f);
            var progress = new FeedbackResult(FeedbackCategory.Progress, MagicEffectKind.Heat, 0.3f);
            var transition = new FeedbackResult(FeedbackCategory.Transition, MagicEffectKind.Heat, 1f);
            var nothing = new FeedbackResult(FeedbackCategory.None, MagicEffectKind.Heat, 0f);

            // The player's own attempt: always answered.
            Assert.IsTrue(ExperimentFeedback.ShouldPresent(incompatible, 0));
            Assert.IsTrue(ExperimentFeedback.ShouldPresent(progress, 0));

            // A neighbour's fire reaching something once a second: silent until something gives.
            Assert.IsFalse(ExperimentFeedback.ShouldPresent(incompatible, 1));
            Assert.IsFalse(ExperimentFeedback.ShouldPresent(progress, 2));
            Assert.IsTrue(ExperimentFeedback.ShouldPresent(transition, 2));
            Assert.IsFalse(ExperimentFeedback.ShouldPresent(nothing, 0));
        }

        [Test]
        public void TheSameSmallAnswerIsNotRepeatedWithoutPause()
        {
            var state = new FeedbackGateState();
            const EffectKinds Aimed = EffectKinds.Heat | EffectKinds.ArcaneCharge | EffectKinds.Force;
            var blocked = new FeedbackResult(FeedbackCategory.Blocked, MagicEffectKind.Force, 0f);
            var insufficient = new FeedbackResult(FeedbackCategory.Insufficient, MagicEffectKind.Force, 0f);
            var transition = new FeedbackResult(FeedbackCategory.Transition, MagicEffectKind.Force, 1f, true);

            Assert.IsTrue(FeedbackGate.Allow(ref state, blocked, 10f, 1.5f, Aimed));
            Assert.IsFalse(FeedbackGate.Allow(ref state, blocked, 10.4f, 1.5f, Aimed), "The same again, too soon.");
            Assert.IsFalse(FeedbackGate.Allow(ref state, blocked, 11.4f, 1.5f, Aimed));
            Assert.IsTrue(FeedbackGate.Allow(ref state, blocked, 11.6f, 1.5f, Aimed), "After the pause it is shown again.");

            // A different answer is news and is shown at once; a change of state always is.
            Assert.IsTrue(FeedbackGate.Allow(ref state, insufficient, 11.7f, 1.5f, Aimed));
            Assert.IsTrue(FeedbackGate.Allow(ref state, transition, 11.8f, 1.5f, Aimed));
            Assert.IsTrue(FeedbackGate.Allow(ref state, transition, 11.9f, 1.5f, Aimed));

            // The Aegis guard reaches everything nearby several times a second. Things it means nothing
            // to stay still: "incompatible" is for attacks that were aimed.
            var guardOnWall = new FeedbackResult(FeedbackCategory.Incompatible, MagicEffectKind.Stabilize, 0f);
            var fresh = new FeedbackGateState();
            Assert.IsFalse(FeedbackGate.Allow(ref fresh, guardOnWall, 20f, 1.5f, Aimed));
            var heatOnCrystal = new FeedbackResult(FeedbackCategory.Incompatible, MagicEffectKind.Heat, 0f);
            Assert.IsTrue(FeedbackGate.Allow(ref fresh, heatOnCrystal, 20f, 1.5f, Aimed));
        }

        // ------------------------------------------- how an answer is presented

        [Test]
        public void EveryCategoryHasAMovementOfItsOwnSoNoneDependsOnColour()
        {
            var settings = new DiscoverySettings();
            var motions = new HashSet<CueMotion>();
            var sounds = new HashSet<DiscoverySound>();

            foreach (FeedbackCategory category in Categories)
            {
                FeedbackCue cue = FeedbackPresentation.Plan(new FeedbackResult(category, MagicEffectKind.Force, 0.5f), settings,
                    DiscoveryQuality.Low);
                Assert.AreNotEqual(CueMotion.None, cue.Motion, category.ToString());
                Assert.IsTrue(motions.Add(cue.Motion), $"{category} shares its movement with another category.");
                Assert.IsTrue(sounds.Add(cue.Sound), $"{category} shares its sound with another category.");
                Assert.GreaterOrEqual(cue.Amplitude, FeedbackPresentation.MinimumAmplitude);
                Assert.Greater(cue.Seconds, 0f);

                // The movement really moves something at some point of its course.
                float largest = 0f;
                for (int i = 1; i < 20; i++)
                {
                    MotionSample sample = FeedbackPresentation.Sample(cue.Motion, i / 20f, cue.Amplitude);
                    largest = Mathf.Max(largest, Mathf.Abs(sample.Along) + Mathf.Abs(sample.Across) + Mathf.Abs(sample.Scale));
                }

                Assert.Greater(largest, 0.003f, $"{category}: the movement is too small to see.");

                // And it ends where it began: nothing is left displaced.
                MotionSample end = FeedbackPresentation.Sample(cue.Motion, 1f, cue.Amplitude);
                Assert.AreEqual(0f, end.Along, 0.0005f, category.ToString());
                Assert.AreEqual(0f, end.Across, 0.0005f, category.ToString());
                Assert.AreEqual(0f, end.Scale, 0.0005f, category.ToString());
            }

            Assert.AreEqual(CueMotion.None, FeedbackPresentation.Plan(new FeedbackResult(FeedbackCategory.None, MagicEffectKind.Heat, 0f),
                settings, DiscoveryQuality.High).Motion);
        }

        [Test]
        public void TheAnswerIsTheSameOnEveryQualityLevelOnlyItsParticlesDiffer()
        {
            var settings = new DiscoverySettings();
            foreach (FeedbackCategory category in Categories)
            {
                var result = new FeedbackResult(category, MagicEffectKind.Heat, 0.4f);
                FeedbackCue low = FeedbackPresentation.Plan(result, settings, DiscoveryQuality.Low);
                FeedbackCue medium = FeedbackPresentation.Plan(result, settings, DiscoveryQuality.Medium);
                FeedbackCue high = FeedbackPresentation.Plan(result, settings, DiscoveryQuality.High);

                foreach (FeedbackCue cue in new[] { low, medium })
                {
                    Assert.AreEqual(high.Motion, cue.Motion, category.ToString());
                    Assert.AreEqual(high.Amplitude, cue.Amplitude, category.ToString());
                    Assert.AreEqual(high.Seconds, cue.Seconds, category.ToString());
                    Assert.AreEqual(high.Accent, cue.Accent, category.ToString());
                    Assert.AreEqual(high.Sound, cue.Sound, category.ToString());
                    Assert.AreEqual(high.Haptic, cue.Haptic, category.ToString());
                }

                Assert.LessOrEqual(low.Particles, medium.Particles, category.ToString());
                Assert.LessOrEqual(medium.Particles, high.Particles, category.ToString());
            }

            // The largest answer on the richest level is still restrained, and smaller on the weakest.
            var finished = new FeedbackResult(FeedbackCategory.Completed, MagicEffectKind.Force, 1f, true);
            Assert.LessOrEqual(FeedbackPresentation.Plan(finished, settings, DiscoveryQuality.High).Particles, 12);
            Assert.Less(FeedbackPresentation.Plan(finished, settings, DiscoveryQuality.Low).Particles,
                FeedbackPresentation.Plan(finished, settings, DiscoveryQuality.High).Particles);

            // The game's own quality setting is what counts: its lowest level is Low, its highest High.
            Assert.AreEqual(DiscoveryQuality.Low, DiscoveryQualityRules.FromLevel(0, 3));
            Assert.AreEqual(DiscoveryQuality.Medium, DiscoveryQualityRules.FromLevel(1, 3));
            Assert.AreEqual(DiscoveryQuality.High, DiscoveryQualityRules.FromLevel(2, 3));
            Assert.AreEqual(DiscoveryQuality.Low, DiscoveryQualityRules.FromLevel(0, 2));
            Assert.AreEqual(DiscoveryQuality.High, DiscoveryQualityRules.FromLevel(1, 2));
            Assert.AreEqual(DiscoveryQuality.Medium, DiscoveryQualityRules.FromLevel(0, 1), "With nothing to choose between, the middle.");
            Assert.AreEqual(DiscoveryQuality.Medium, DiscoveryQualityRules.FromLevel(0, 0));
        }

        [Test]
        public void TheOptionsChangeHowMuchIsShownNotWhatIsShown()
        {
            var plain = new DiscoverySettings();
            var result = new FeedbackResult(FeedbackCategory.Insufficient, MagicEffectKind.Force, 0f);
            FeedbackCue normal = FeedbackPresentation.Plan(result, plain, DiscoveryQuality.High);

            // Larger movement on request.
            FeedbackCue emphasised = FeedbackPresentation.Plan(result, new DiscoverySettings { emphasizeMotion = true }, DiscoveryQuality.High);
            Assert.Greater(emphasised.Amplitude, normal.Amplitude);
            Assert.AreEqual(normal.Motion, emphasised.Motion);
            Assert.LessOrEqual(emphasised.Amplitude, 1f);

            // Fewer particles on request: none for small answers, a few for the large ones.
            var reduced = new DiscoverySettings { reducedParticles = true };
            foreach (FeedbackCategory category in Categories)
            {
                bool significant = category == FeedbackCategory.Transition || category == FeedbackCategory.Completed;
                FeedbackCue cue = FeedbackPresentation.Plan(new FeedbackResult(category, MagicEffectKind.Force, 0.5f, significant), reduced,
                    DiscoveryQuality.High);
                Assert.LessOrEqual(cue.Particles, significant ? 3 : 0, category.ToString());
                Assert.AreNotEqual(CueMotion.None, cue.Motion, "Without particles the movement still says it.");
            }

            // Clues switched off: no resonance and no ambient clue, but an attempt is still answered.
            var off = new DiscoverySettings { clueIntensity = 0f };
            Assert.AreEqual(0f, ResonanceRules.Strength(true, 1f, true, true, new ResonanceSettings(), off));
            Assert.AreEqual(0f, AttentionRules.ClueStrength(0.5f, 1f, off));
            FeedbackCue stillAnswered = FeedbackPresentation.Plan(result, off, DiscoveryQuality.Low);
            Assert.AreEqual(CueMotion.Strain, stillAnswered.Motion);
            Assert.GreaterOrEqual(stillAnswered.Amplitude, FeedbackPresentation.MinimumAmplitude);

            // Stronger clues and stronger resonance on request, never beyond full.
            var strong = new DiscoverySettings { clueIntensity = 2f, strongResonance = true };
            float usual = ResonanceRules.Strength(true, 3f, true, false, new ResonanceSettings(), plain);
            float stronger = ResonanceRules.Strength(true, 3f, true, false, new ResonanceSettings(), strong);
            Assert.Greater(stronger, usual);
            Assert.LessOrEqual(stronger, 1f);
            Assert.AreEqual(1f, AttentionRules.ClueStrength(0.8f, 2f, strong));

            // Changing a copy of the options does not change the original.
            DiscoverySettings copy = plain.Clone();
            copy.clueIntensity = 0.3f;
            Assert.AreEqual(1f, plain.clueIntensity);
        }

        [Test]
        public void NoAnswerRevealsANumber()
        {
            // What the player is given is a category and a strength of presentation. Two very
            // different amounts of the same insufficient force are answered identically: how much
            // more is needed is for the player to find out.
            MagicalPropertySet first = Example(ExampleObject.ReinforcedDoor);
            MagicalPropertySet second = Example(ExampleObject.ReinforcedDoor);
            var settings = new DiscoverySettings();

            FeedbackCue weak = FeedbackPresentation.Plan(Try(first, MagicEffectKind.Force, 0.5f), settings, DiscoveryQuality.High);
            FeedbackCue nearly = FeedbackPresentation.Plan(Try(second, MagicEffectKind.Force, 3.4f), settings, DiscoveryQuality.High);

            Assert.AreEqual(weak.Motion, nearly.Motion);
            Assert.AreEqual(weak.Amplitude, nearly.Amplitude);
            Assert.AreEqual(weak.Accent, nearly.Accent);
            Assert.AreEqual(weak.Particles, nearly.Particles);
        }

        // --------------------------------------------- level 1 and its emphasis

        [Test]
        public void TheCluesAnObjectCarriesFollowFromItsProperties()
        {
            Assert.AreEqual(ClueKinds.ArcanePulse, Compatibility.CluesFor(WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal)));
            Assert.AreEqual(ClueKinds.Flammable, Compatibility.CluesFor(WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade)));
            Assert.AreEqual(ClueKinds.HeatMarks, Compatibility.CluesFor(WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone)));
            Assert.AreEqual(ClueKinds.Track, Compatibility.CluesFor(WorldMagicDefaults.Settings(ExampleObject.MovablePlatform)));
            Assert.AreEqual(ClueKinds.Unsteady, Compatibility.CluesFor(WorldMagicDefaults.Settings(ExampleObject.UnstableStructure)));
            Assert.AreEqual(ClueKinds.ArcanePulse, Compatibility.CluesFor(WorldMagicDefaults.Settings(ExampleObject.ReinforcedDoor)));

            Assert.AreEqual(ClueKinds.None, Compatibility.CluesFor(new MagicalPropertySettings()));
            Assert.AreEqual(ClueKinds.None, Compatibility.CluesFor(null));

            // Something inert carries no clue for a property it does not really have.
            MagicalPropertySettings inert = WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal);
            inert.arcaneSettings.susceptibility = 0f;
            Assert.AreEqual(ClueKinds.None, Compatibility.CluesFor(inert));
        }

        [Test]
        public void ADefaultClueNeverSuggestsADisciplineTheObjectDoesNotAnswer()
        {
            // Only what takes arcane charge pulses in the arcane glow; any other clue is a trace of a neutral tint.
            foreach (ExampleObject example in Examples)
            {
                ClueKinds kinds = Compatibility.CluesFor(WorldMagicDefaults.Settings(example));
                bool arcane = (kinds & ClueKinds.ArcanePulse) != 0;
                Assert.AreEqual(arcane ? ClueDefaults.ArcaneGlow : ClueDefaults.Neutral, ClueDefaults.Accent(kinds), example.ToString());
                Assert.AreEqual(arcane ? ClueDefaults.PulseStrength : ClueDefaults.TraceStrength, ClueDefaults.IdleStrength(kinds),
                    example.ToString());
            }

            Assert.AreEqual(0f, ClueDefaults.IdleStrength(ClueKinds.None), "No property, no clue.");

            // A crate that only burns, as Make Selection Magical would set it up: before, it pulsed like a crystal.
            Assert.AreEqual(ClueDefaults.Neutral, ClueDefaults.Accent(ClueKinds.Flammable));

            // The neutral tint is far from every discipline's colour, so it reads as none of them.
            for (int slot = 0; slot < DisciplineCatalog.Count; slot++)
            {
                Color discipline = DisciplineCatalog.Get(slot).Color;
                float distance = Vector3.Distance(new Vector3(ClueDefaults.Neutral.r, ClueDefaults.Neutral.g, ClueDefaults.Neutral.b),
                    new Vector3(discipline.r, discipline.g, discipline.b));
                Assert.Greater(distance, 0.3f, DisciplineCatalog.Get(slot).Name);
            }
        }

        [Test]
        public void AStateBurstPlaysFewerParticlesOnLowerLevelsAndWithReducedParticlesButIsNeverLeftOut()
        {
            var plain = new DiscoverySettings();
            var reduced = new DiscoverySettings { reducedParticles = true };

            Assert.AreEqual(1f, FeedbackPresentation.StateBurstShare(DiscoveryQuality.High, plain));
            Assert.Less(FeedbackPresentation.StateBurstShare(DiscoveryQuality.Low, plain),
                FeedbackPresentation.StateBurstShare(DiscoveryQuality.Medium, plain));
            Assert.Less(FeedbackPresentation.StateBurstShare(DiscoveryQuality.Medium, plain),
                FeedbackPresentation.StateBurstShare(DiscoveryQuality.High, plain));

            foreach (DiscoveryQuality quality in new[] { DiscoveryQuality.Low, DiscoveryQuality.Medium, DiscoveryQuality.High })
            {
                float share = FeedbackPresentation.StateBurstShare(quality, reduced);
                Assert.Greater(share, 0f, "The burst marks a change of state; it is never left out.");
                Assert.LessOrEqual(share, FeedbackPresentation.StateBurstShare(quality, plain));
                Assert.LessOrEqual(share, FeedbackPresentation.ReducedBurstShare);
            }

            // Before, these values were worked out but the objects' bursts ignored them and always played
            // twelve. A burst of twelve now plays three with reduced particles, as an answer does at most,
            // and half on the lowest level, as the answers do.
            Assert.AreEqual(3, Mathf.RoundToInt(12 * FeedbackPresentation.StateBurstShare(DiscoveryQuality.High, reduced)));
            Assert.AreEqual(6, Mathf.RoundToInt(12 * FeedbackPresentation.StateBurstShare(DiscoveryQuality.Low, plain)));
            Assert.AreEqual(FeedbackPresentation.QualityShare(DiscoveryQuality.Low),
                FeedbackPresentation.StateBurstShare(DiscoveryQuality.Low, plain), "One share per level for answers and bursts.");
        }

        [Test]
        public void AFadingResonanceKeepsTheColourOfTheDisciplineThatCausedIt()
        {
            MagicalPropertySet crystal = Example(ExampleObject.ArcaneCrystal);
            Color arcane = DisciplineCatalog.Get((int)MagicDiscipline.Arcane).Color;
            Color flame = DisciplineCatalog.Get((int)MagicDiscipline.Flame).Color;
            var settings = new ResonanceSettings();
            var options = new DiscoverySettings();

            // Aimed at with Arcane: it resonates in Arcane's colour.
            bool arcaneFits = Compatibility.Matching(crystal, Compatibility.OfDefaultDiscipline(MagicDiscipline.Arcane)) != EffectKinds.None;
            float target = ResonanceRules.Strength(arcaneFits, 3f, true, false, settings, options);
            Assert.Greater(target, 0f);
            Color shown = ResonanceRules.ColourToShow(default, arcane, target);
            Assert.AreEqual(arcane, shown);

            // Switched to Flame, which the crystal does not answer. The resonance fades, and it fades in
            // Arcane's colour: before, it took Flame's orange for the moment of the fade.
            bool flameFits = Compatibility.Matching(crystal, Compatibility.OfDefaultDiscipline(MagicDiscipline.Flame)) != EffectKinds.None;
            Assert.IsFalse(flameFits);
            target = ResonanceRules.Strength(flameFits, 3f, true, false, settings, options);
            Assert.AreEqual(0f, target);
            Assert.AreEqual(arcane, ResonanceRules.ColourToShow(shown, flame, target));
            Assert.AreNotEqual(flame, ResonanceRules.ColourToShow(shown, flame, target));
        }

        [Test]
        public void AnObjectThatNoPushCanMoveDoesNotResonateWithForce()
        {
            // A lift: along an axis that points up, a horizontal push has no share.
            MagicalPropertySet lift = Example(ExampleObject.MovablePlatform);
            lift.KineticAxis = Vector3.up;
            Assert.IsFalse(lift.CouldRespondTo(MagicEffectKind.Force), "Before, it resonated with Aegis and never moved.");
            lift.Apply(Effect(MagicEffectKind.Force, 1000f));
            Run(lift, 3f);
            Assert.AreEqual(0f, lift.Kinetic.Offset.magnitude, 0.0001f, "And indeed the strongest push does nothing.");

            // No room to travel.
            MagicalPropertySettings stuck = WorldMagicDefaults.Settings(ExampleObject.MovablePlatform);
            stuck.kineticSettings.minTravel = 0f;
            stuck.kineticSettings.maxTravel = 0f;
            var block = new MagicalPropertySet(stuck) { KineticAxis = Vector3.forward };
            Assert.IsFalse(block.CouldRespondTo(MagicEffectKind.Force));

            // A slanted axis still has a horizontal share: it resonates, and a push does move it.
            MagicalPropertySet ramp = Example(ExampleObject.MovablePlatform);
            ramp.KineticAxis = new Vector3(0f, 1f, 1f);
            Assert.IsTrue(ramp.CouldRespondTo(MagicEffectKind.Force));
            ramp.Apply(Effect(MagicEffectKind.Force, 1000f));
            Run(ramp, 3f);
            Assert.Greater(ramp.Kinetic.Offset.magnitude, 0f);
        }

        [Test]
        public void LingeringStrengthensCluesOnlyAfterAWhileAndNotWithoutPause()
        {
            var settings = new AttentionSettings { delaySeconds = 10f, rampSeconds = 4f, holdSeconds = 3f, restSeconds = 8f, maxBoost = 1f };
            var state = new AttentionState();
            float boost = 0f;

            float Step(float seconds, bool near = true, bool inCombat = false, bool attempted = false, bool progressed = false,
                bool resolved = false)
            {
                for (float t = 0f; t < seconds - 0.0001f; t += 0.1f)
                {
                    boost = AttentionRules.Advance(ref state, near, resolved, inCombat, attempted, progressed, settings, 0.1f);
                    attempted = false;
                    progressed = false;
                }

                return boost;
            }

            Assert.AreEqual(0f, Step(9.5f), "Nothing before the delay: looking around is not being stuck.");
            Assert.Greater(Step(2.5f), 0f, "Then it begins to rise,");
            Assert.Less(boost, 1f, "gradually.");
            Assert.AreEqual(1f, Step(3f), 0.05f, "It reaches its full strength");
            Assert.AreEqual(1f, Step(1.5f), 0.05f, "and holds.");
            Assert.AreEqual(0f, Step(5f), 0.05f, "Then it rests: it does not go on without pause.");
            Assert.AreEqual(0f, Step(2.5f), 0.05f);
            Assert.Greater(Step(3f), 0f, "After the rest it rises again.");
            Assert.LessOrEqual(boost, settings.maxBoost);
        }

        [Test]
        public void TryingSomethingFightingOrSucceedingQuietensTheEmphasis()
        {
            var settings = new AttentionSettings { delaySeconds = 5f, rampSeconds = 2f, holdSeconds = 60f, restSeconds = 5f };

            AttentionState Lingered()
            {
                var state = new AttentionState();
                for (int i = 0; i < 100; i++)
                {
                    AttentionRules.Advance(ref state, true, false, false, false, false, settings, 0.1f);
                }

                Assert.AreEqual(1f, state.Emphasis, 0.01f);
                return state;
            }

            // An attempt, even one that does nothing: the clock starts again and the emphasis fades. Experimenting is never nagged.
            AttentionState attempt = Lingered();
            AttentionRules.Advance(ref attempt, true, false, false, true, false, settings, 0.1f);
            Assert.Less(attempt.Lingered, 1f);
            for (int i = 0; i < 20; i++)
            {
                AttentionRules.Advance(ref attempt, true, false, false, false, false, settings, 0.1f);
            }

            Assert.AreEqual(0f, attempt.Emphasis, 0.01f);

            // Progress: gone at once.
            AttentionState progress = Lingered();
            Assert.AreEqual(0f, AttentionRules.Advance(ref progress, true, false, false, true, true, settings, 0.1f));
            Assert.AreEqual(0f, progress.Lingered);

            // Combat: it fades, and the time spent fighting nearby does not count as lingering.
            AttentionState combat = Lingered();
            float lingered = combat.Lingered;
            for (int i = 0; i < 30; i++)
            {
                AttentionRules.Advance(ref combat, true, false, true, false, false, settings, 0.1f);
            }

            Assert.AreEqual(0f, combat.Emphasis, 0.01f);
            Assert.AreEqual(lingered, combat.Lingered, 0.001f);

            // Walking away: it fades and the lingering is slowly forgotten.
            AttentionState away = Lingered();
            for (int i = 0; i < 30; i++)
            {
                AttentionRules.Advance(ref away, false, false, false, false, false, settings, 0.1f);
            }

            Assert.AreEqual(0f, away.Emphasis, 0.01f);
            Assert.Less(away.Lingered, lingered);

            // Resolved, or switched off: nothing, at once.
            AttentionState resolved = Lingered();
            Assert.AreEqual(0f, AttentionRules.Advance(ref resolved, true, true, false, false, false, settings, 0.1f));
            AttentionState disabled = Lingered();
            Assert.AreEqual(0f, AttentionRules.Advance(ref disabled, true, false, false, false, false, new AttentionSettings { enabled = false }, 0.1f));
        }

        [Test]
        public void EmphasisOnlyMultipliesWhatTheObjectAlreadyShows()
        {
            var settings = new DiscoverySettings();

            // An object with no clue gains none from emphasis: there is nothing to make stronger, and
            // nothing new appears that could name a discipline.
            Assert.AreEqual(0f, AttentionRules.ClueStrength(0f, 1f, settings));
            Assert.AreEqual(0.6f, AttentionRules.ClueStrength(0.3f, 1f, settings), 0.0001f);
            Assert.AreEqual(0.3f, AttentionRules.ClueStrength(0.3f, 0f, settings), 0.0001f);
            Assert.AreEqual(1f, AttentionRules.ClueStrength(0.9f, 2f, settings), "Never beyond full.");
        }

        // ---------------------------------------------------- review follow-ups

        [Test]
        public void AnAttemptOnSomethingAlreadyFinishedIsAnsweredQuietlyAndNotOverAndOver()
        {
            var settings = new DiscoverySettings();

            // The arrow that opens the lock, and a later arrow at the open lock: both "completed".
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            door.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f));
            FeedbackResult opening = Try(door, MagicEffectKind.ArcaneCharge, 100f);
            FeedbackResult again = Try(door, MagicEffectKind.ArcaneCharge, 100f);
            Assert.AreEqual(FeedbackCategory.Completed, opening.Category);
            Assert.AreEqual(FeedbackCategory.Completed, again.Category);
            Assert.IsTrue(opening.Changed);
            Assert.IsFalse(again.Changed, "The second changed nothing.");

            // The same movement, so it reads as the same thing, but small: no fanfare for nothing.
            FeedbackCue loud = FeedbackPresentation.Plan(opening, settings, DiscoveryQuality.High);
            FeedbackCue quiet = FeedbackPresentation.Plan(again, settings, DiscoveryQuality.High);
            Assert.AreEqual(loud.Motion, quiet.Motion);
            Assert.Less(quiet.Amplitude, loud.Amplitude * 0.5f);
            Assert.Less(quiet.Haptic, loud.Haptic * 0.5f);
            Assert.AreEqual(0, quiet.Particles);

            // And it is not repeated without pause, where a real change always is shown.
            var state = new FeedbackGateState();
            Assert.IsTrue(FeedbackGate.Allow(ref state, opening, 1f, 1.5f, EffectKinds.All));
            Assert.IsFalse(FeedbackGate.Allow(ref state, again, 1.2f, 1.5f, EffectKinds.All), "The opening is still being shown.");
            Assert.IsTrue(FeedbackGate.Allow(ref state, again, 2.6f, 1.5f, EffectKinds.All));
            Assert.IsFalse(FeedbackGate.Allow(ref state, again, 3.0f, 1.5f, EffectKinds.All));
            Assert.IsFalse(FeedbackGate.Allow(ref state, again, 4.0f, 1.5f, EffectKinds.All));
            Assert.IsTrue(FeedbackGate.Allow(ref state, again, 4.2f, 1.5f, EffectKinds.All));

            // Something really finishing is shown at once, however recently anything else was.
            Assert.IsTrue(FeedbackGate.Allow(ref state, opening, 4.3f, 1.5f, EffectKinds.All));
        }

        [Test]
        public void AHeldGuardDoesNotMakeThingsAnswerOverAndOver()
        {
            const EffectKinds Aimed = EffectKinds.Heat | EffectKinds.ArcaneCharge | EffectKinds.Force;

            // On a structure it braces: the bracing is answered once; keeping it up is not an answer at all.
            MagicalPropertySet structure = Example(ExampleObject.UnstableStructure);
            FeedbackResult first = Try(structure, MagicEffectKind.Stabilize, 1f);
            Assert.AreEqual(FeedbackCategory.Transition, first.Category);
            Assert.IsTrue(first.Changed);
            for (int pulse = 0; pulse < 5; pulse++)
            {
                Run(structure, 0.4f);
                Assert.AreEqual(FeedbackCategory.None, Try(structure, MagicEffectKind.Stabilize, 1f).Category, $"Pulse {pulse}.");
            }

            // On a structure that has come down: too late, and said to nobody. The guard is not aimed at anything.
            MagicalPropertySet rubble = Example(ExampleObject.UnstableStructure);
            rubble.Apply(Effect(MagicEffectKind.Force, 5f));
            FeedbackResult tooLate = Try(rubble, MagicEffectKind.Stabilize, 1f);
            Assert.AreEqual(FeedbackCategory.Completed, tooLate.Category);
            Assert.IsFalse(tooLate.Changed);
            var state = new FeedbackGateState();
            Assert.IsFalse(FeedbackGate.Allow(ref state, tooLate, 1f, 1.5f, Aimed));

            // The first bracing itself is shown although the guard is not aimed: something changed.
            Assert.IsTrue(FeedbackGate.Allow(ref state, first, 1f, 1.5f, Aimed));

            // A brace too weak to hold, from a guard: also said to nobody.
            FeedbackResult weak = Try(Example(ExampleObject.UnstableStructure), MagicEffectKind.Stabilize, 0.2f);
            Assert.AreEqual(FeedbackCategory.Insufficient, weak.Category);
            Assert.IsFalse(FeedbackGate.Allow(ref state, weak, 5f, 1.5f, Aimed));
        }

        [Test]
        public void WhatForceCouldDoIsJudgedOnTheObjectAsItIsNow()
        {
            // Stone that only breaks when hot: cold, force can do nothing to it, so it must not resonate with force.
            var settings = new MagicalPropertySettings
            {
                thermal = true,
                thermalSettings = new HeatSettings { capacity = 100f, heatedFraction = 0.5f, flammable = false, decayPerSecond = 0f },
                structural = true,
                structuralSettings = new StructuralSettings { integrity = 100f, forceThreshold = 1f, forceDamage = 10f, brittleness = 0f, heatedBrittleness = 8f },
            };
            var stone = new MagicalPropertySet(settings);

            Assert.IsFalse(stone.CouldRespondTo(MagicEffectKind.Force));
            Assert.AreNotEqual(EffectResponse.Applied, stone.Apply(Effect(MagicEffectKind.Force, 1000f)));

            stone.Apply(Effect(MagicEffectKind.Heat, 80f));
            Assert.IsTrue(stone.CouldRespondTo(MagicEffectKind.Force), "Hot, it can be broken.");
            Assert.AreEqual(EffectResponse.Applied, stone.Apply(Effect(MagicEffectKind.Force, 5f)));

            // A brace that enough force can break is something force can change.
            MagicalPropertySettings braced = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            braced.structural = false;
            var firm = new MagicalPropertySet(braced);
            firm.Apply(Effect(MagicEffectKind.Stabilize, 1f));
            Assert.IsFalse(firm.CouldRespondTo(MagicEffectKind.Force), "A brace that holds against anything.");

            braced.stabilitySettings.braceBreakForce = 4f;
            var breakable = new MagicalPropertySet(braced);
            breakable.Apply(Effect(MagicEffectKind.Stabilize, 1f));
            Assert.IsTrue(breakable.CouldRespondTo(MagicEffectKind.Force));
            Assert.AreEqual(EffectResponse.Applied, breakable.Apply(Effect(MagicEffectKind.Force, 5f)));
        }

        [Test]
        public void SwitchingCluesOffAlsoSwitchesOffTheWiderSway()
        {
            Assert.AreEqual(2f, AttentionRules.SwayScale(1f, new DiscoverySettings()), 0.0001f);
            Assert.AreEqual(1f, AttentionRules.SwayScale(1f, new DiscoverySettings { clueIntensity = 0f }), 0.0001f);
            Assert.AreEqual(1.5f, AttentionRules.SwayScale(1f, new DiscoverySettings { clueIntensity = 0.5f }), 0.0001f);
            Assert.AreEqual(2f, AttentionRules.SwayScale(1f, new DiscoverySettings { clueIntensity = 2f }), 0.0001f, "Never more than the emphasis itself.");
            Assert.AreEqual(1f, AttentionRules.SwayScale(0f, new DiscoverySettings()), 0.0001f);
        }

        // ---------------------------------------------------------- performance

        [Test]
        public void HoweverManyObjectsStandAroundOnlyTheNearestFewAreAttendedTo()
        {
            const int Count = 600;
            var names = new string[Count];
            var distances = new float[Count];
            var random = new System.Random(7);
            for (int i = 0; i < Count; i++)
            {
                names[i] = "object " + i;
                distances[i] = (float)random.NextDouble() * 400f + 1f;
            }

            // Three that are certainly the nearest, placed out of order.
            distances[411] = 0.25f;
            distances[17] = 0.5f;
            distances[590] = 0.75f;

            // Looked through a few per frame, as the director does: the cost per frame is the batch,
            // whatever the size of the level.
            var nearest = new NearestFew<string>(16);
            int frames = 0;
            for (int index = 0; index < Count;)
            {
                for (int batch = 0; batch < 64 && index < Count; batch++, index++)
                {
                    nearest.Offer(names[index], distances[index], 8);
                }

                frames++;
                Assert.LessOrEqual(nearest.Count, 8, "Never more than are attended to.");
            }

            Assert.AreEqual(10, frames, "Six hundred objects, sixty-four a frame.");
            Assert.AreEqual(8, nearest.Count);
            Assert.AreEqual("object 411", nearest[0]);
            Assert.AreEqual("object 17", nearest[1]);
            Assert.AreEqual("object 590", nearest[2]);
            for (int i = 1; i < nearest.Count; i++)
            {
                Assert.LessOrEqual(nearest.DistanceAt(i - 1), nearest.DistanceAt(i), "Nearest first.");
            }

            // Nothing nearer was left out.
            var sorted = (float[])distances.Clone();
            Array.Sort(sorted);
            Assert.AreEqual(sorted[7], nearest.DistanceAt(7));
        }

        [Test]
        public void TheNearestFewAreKeptOnceEachAndInTheOrderFound()
        {
            var nearest = new NearestFew<string>(4);

            // Equal distances keep the order they were offered in.
            nearest.Offer("a", 5f);
            nearest.Offer("b", 5f);
            nearest.Offer("c", 1f);
            Assert.AreEqual(3, nearest.Count);
            CollectionAssert.AreEqual(new[] { "c", "a", "b" }, new[] { nearest[0], nearest[1], nearest[2] });

            // The list of objects can change while it is being looked through, so one may be offered
            // twice in the same look. It is kept once.
            nearest.Offer("a", 0.5f);
            nearest.Offer("c", 9f);
            Assert.AreEqual(3, nearest.Count);
            Assert.IsTrue(nearest.Contains("a"));
            Assert.IsFalse(nearest.Contains("z"));

            // A full set lets go of its farthest for a nearer one, and ignores a farther one.
            nearest.Offer("d", 6f);
            nearest.Offer("e", 7f);
            Assert.AreEqual(4, nearest.Count);
            Assert.IsFalse(nearest.Contains("e"));
            nearest.Offer("f", 2f);
            CollectionAssert.AreEqual(new[] { "c", "f", "a", "b" }, new[] { nearest[0], nearest[1], nearest[2], nearest[3] });
            Assert.IsFalse(nearest.Contains("d"));

            nearest.Offer(null, 0f);
            Assert.AreEqual(4, nearest.Count);

            nearest.Clear();
            Assert.AreEqual(0, nearest.Count);
            Assert.IsFalse(nearest.Contains("c"));
        }

        [Test]
        public void TheRulesThatRunOftenAllocateNothing()
        {
            MagicalPropertySet door = Example(ExampleObject.ReinforcedDoor);
            var resonance = new ResonanceSettings();
            var settings = new DiscoverySettings();
            var attention = new AttentionSettings();
            var attentionState = new AttentionState();
            var gate = new FeedbackGateState();
            var nearest = new NearestFew<MagicalPropertySet>(8);
            EffectKinds aegis = Compatibility.OfDefaultDiscipline(MagicDiscipline.Aegis);
            MagicEffect effect = Effect(MagicEffectKind.Force, 2f);
            float sink = 0f;

            // Once beforehand, so that anything set up on first use is not counted.
            Compatibility.Matching(door, aegis);
            nearest.Offer(door, 1f, 8);
            nearest.Clear();

            AllocationMeter.Begin();
            for (int frame = 0; frame < 500; frame++)
            {
                nearest.Offer(door, frame, 8);
                nearest.Offer(door, 1f, 8);
                if (frame % 16 == 0)
                {
                    nearest.Clear();
                }

                sink += AttentionRules.SwayScale(0.5f, settings);
                bool compatible = Compatibility.Matching(door, aegis) != EffectKinds.None;
                sink += ResonanceRules.Strength(compatible, 3f, true, false, resonance, settings);
                sink += AttentionRules.Advance(ref attentionState, true, false, false, false, false, attention, 0.016f);
                FeedbackResult result = ExperimentFeedback.Classify(door, effect, EffectResponse.Resisted, false);
                if (FeedbackGate.Allow(ref gate, result, frame * 0.016f, 1.5f, EffectKinds.All))
                {
                    FeedbackCue cue = FeedbackPresentation.Plan(result, settings, DiscoveryQuality.Low);
                    sink += FeedbackPresentation.Sample(cue.Motion, 0.5f, cue.Amplitude).Along;
                }

                sink += FeedbackPresentation.Breath(frame * 0.016f, 0.5f, true);
            }

            long allocated = AllocationMeter.End();
            Assert.AreEqual(0L, allocated, "The per-frame rules must not allocate.");
            Assert.Greater(sink, 0f);
        }
    }
}
