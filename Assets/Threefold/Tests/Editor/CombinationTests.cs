using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.Enemies;
using Threefold.Magic;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Sprint LM-06: the disciplines combine through properties, never through recipes. These tests
    /// drive the real rules with the disciplines' default attacks, the enemies' default values and the
    /// examples' default definitions, through the five scenarios of the brief (A to E) and the checks
    /// it lists: valid and invalid combinations, different orders, temporary states running out,
    /// enemies and the world acting on each other, chain limits, attribution, duplicates,
    /// responsiveness and balance.
    ///
    /// Where a scene would be needed, a small stand-in does what the component does (a creature that
    /// carries its model's answers out to its brain; a world that sends reactions to what is in reach),
    /// as the chain-reaction tests of LM-04 do. The rules, the queue and the numbers are the real ones.
    /// </summary>
    public sealed class CombinationTests
    {
        private const int Arcane = (int)MagicDiscipline.Arcane;
        private const int Flame = (int)MagicDiscipline.Flame;
        private const int Aegis = (int)MagicDiscipline.Aegis;
        private const float Step = 0.1f;

        private int nextEvent = 90000;

        // ------------------------------------------------------------- helpers

        private static EffectPayload Melee(MagicDiscipline discipline, int step) => DisciplineDefaults.Melee(discipline).steps[step].Effects;
        private static EffectPayload Arrow(float power) => DisciplineDefaults.Charged(MagicDiscipline.Arcane).Projectile.Effects(power);
        private static EffectPayload Wave(float power) => DisciplineDefaults.Charged(MagicDiscipline.Flame).Area.Effects(power);
        private static EffectPayload Pulse(float power) => DisciplineDefaults.Charged(MagicDiscipline.Aegis).Area.Effects(power);
        private static EffectPayload AegisGuard() => DisciplineDefaults.Guard(MagicDiscipline.Aegis).Effects;

        private static MagicalPropertySet Example(ExampleObject example)
        {
            return new MagicalPropertySet(WorldMagicDefaults.Settings(example)) { KineticAxis = Vector3.forward };
        }

        private static MagicalPropertySet Example(CombinationExample example)
        {
            return new MagicalPropertySet(CombinationDefaults.Settings(example)) { KineticAxis = Vector3.forward };
        }

        private static void Run(MagicalPropertySet set, float seconds)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += Step)
            {
                set.Tick(Step);
            }
        }

        /// <summary>Delivers a payload the way every attack does, through the one delivery routine, under one new event.</summary>
        private bool Hit(IMagicEffectReceiver target, EffectPayload payload, int discipline, int depth = 0)
        {
            if (depth == 0)
            {
                return AreaStrike.Deliver(target, payload, Vector3.forward, Vector3.zero, null, discipline, nextEvent++);
            }

            // A reaction: what an object sends on, one step or more from what the player did.
            bool applied = false;
            for (int kind = 0; kind < 4; kind++)
            {
                float magnitude = payload.Get((MagicEffectKind)kind);
                if (magnitude > 0f)
                {
                    applied |= target.ReceiveEffect(new MagicEffect((MagicEffectKind)kind, magnitude, Vector3.forward,
                        payload.stabilizeSeconds, Vector3.zero, null, -1, EffectTargets.All, nextEvent++, depth)) == EffectResponse.Applied;
                }
            }

            return applied;
        }

        private bool Hit(MagicalPropertySet target, EffectPayload payload, int discipline, int depth = 0)
        {
            return Hit(new SetReceiver(target), payload, discipline, depth);
        }

        private MagicEffect Effect(MagicEffectKind kind, float magnitude, int depth = 0, int eventId = -1)
        {
            return new MagicEffect(kind, magnitude, Vector3.forward, 0f, Vector3.zero, null, -1, EffectTargets.All,
                eventId >= 0 ? eventId : nextEvent++, depth);
        }

        private sealed class SetReceiver : IMagicEffectReceiver
        {
            public readonly MagicalPropertySet Set;

            public SetReceiver(MagicalPropertySet set)
            {
                Set = set;
            }

            public EffectResponse ReceiveEffect(in MagicEffect effect) => Set.Apply(effect);
        }

        /// <summary>
        /// A creature as <see cref="EnemyController"/> carries its model out: damage through its
        /// defences into health and the brain's poise; effects into the model; a stagger the model
        /// calls for into the brain, where the stagger immunity applies.
        /// </summary>
        private sealed class Creature : IMagicEffectReceiver
        {
            public readonly EnemyTuning Tuning;
            public readonly CreatureEffectModel Model;
            public readonly FakeEnemyBody Body;
            public readonly EnemyBrain Brain;
            public readonly List<string> Announced = new List<string>();
            public float Health;
            public int Impacts;

            public Creature(EnemyTuning tuning)
            {
                Tuning = tuning;
                Health = tuning.maxHealth;
                Model = new CreatureEffectModel(tuning.heat, tuning.pushResistance, true, tuning.staggerForce, tuning.armour,
                    tuning.ward);
                Model.StateChanged += (kind, from, to) => Announced.Add($"{kind}:{from}>{to}");
                Body = new FakeEnemyBody { Position = Vector3.zero, HomePosition = Vector3.zero, Forward = Vector3.forward };
                Brain = new EnemyBrain(Body, tuning, new AttackTokenPool(2, 1, 0f, 0f), 1);
            }

            /// <summary>A hit with a direction; returns the damage that got through.</summary>
            public float Damage(float amount, float stagger = -1f, bool hasDirection = true)
            {
                float taken = amount * Model.DamageMultiplier(hasDirection);
                Health -= taken;
                Brain.TakeDamage(taken, stagger >= 0f ? stagger : taken, Vector3.forward, true, Vector3.back * 3f);
                return taken;
            }

            public EffectResponse ReceiveEffect(in MagicEffect effect)
            {
                EffectResponse response = Model.Apply(effect, out _);
                if (Model.StaggeredByLastEffect && Brain.TakeImpact(effect.Direction))
                {
                    Impacts++;
                }

                return response;
            }

            public void Tick(float seconds)
            {
                for (float t = 0f; t < seconds - 0.0001f; t += Step)
                {
                    Model.Thermal.Tick(Step);
                    Model.Tick(Step);
                    Brain.Tick(Step, new TargetObservation(true, true, Vector3.back * 3f));
                }
            }
        }

        // ---------------------------------------------- A: heat + force = fracture

        [Test]
        public void A_ColdForceBarelyMarksTheStoneBarrier_HeatedTheSameForceShattersIt()
        {
            MagicalPropertySet cold = Example(ExampleObject.HeatSensitiveStone);
            Hit(cold, Pulse(1f), Aegis);
            Assert.Greater(cold.Structural.Fraction, 0.95f, "Cold, a full Force Pulse barely marks it.");

            MagicalPropertySet heated = Example(ExampleObject.HeatSensitiveStone);
            Hit(heated, Wave(1f), Flame);
            Assert.AreEqual(ThermalState.Heated, heated.Thermal.State);
            Hit(heated, Pulse(1f), Aegis);
            Assert.AreEqual(StructuralState.Destroyed, heated.Structural.State, "Heated, the same pulse shatters it.");
        }

        [Test]
        public void A_HeatFromAnySourceGivesTheSameFracture()
        {
            // Four ways to the same world state: heated stone. Equivalent states must give the same outcome.
            var outcomes = new List<float>();

            MagicalPropertySet byCombo = Example(ExampleObject.HeatSensitiveStone);
            for (int i = 0; i < 3; i++) Hit(byCombo, Melee(MagicDiscipline.Flame, i), Flame);
            MagicalPropertySet byWave = Example(ExampleObject.HeatSensitiveStone);
            Hit(byWave, Wave(1f), Flame);
            MagicalPropertySet byFire = Example(ExampleObject.HeatSensitiveStone);
            for (int i = 0; i < 3; i++) Hit(byFire, new EffectPayload { heat = 25f, targets = EffectTargets.All }, -1, depth: 1);
            MagicalPropertySet byMotes = Example(ExampleObject.HeatSensitiveStone);
            for (int i = 0; i < 2; i++) Hit(byMotes, EnemyArchetypes.CinderMoteEffects, Aegis);

            foreach (MagicalPropertySet stone in new[] { byCombo, byWave, byFire, byMotes })
            {
                Assert.AreEqual(ThermalState.Heated, stone.Thermal.State, "Every source heated it.");
                Hit(stone, Pulse(1f), Aegis);
                outcomes.Add(stone.Structural.Integrity);
            }

            Assert.AreEqual(0f, outcomes[0], "A full pulse shatters heated stone.");
            CollectionAssert.AreEqual(new[] { outcomes[0], outcomes[0], outcomes[0], outcomes[0] }, outcomes,
                "Flame strikes, a wave, a nearby fire and turned fiery motes all lead to the same fracture.");
        }

        [Test]
        public void A_TheBlowMustFollowTheHeat_OnceCooledItIsBarelyMarkedAgain()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            Hit(stone, Wave(1f), Flame);
            Run(stone, 10f);
            Assert.AreEqual(ThermalState.Unheated, stone.Thermal.State, "The heat is temporary: it has cooled.");

            Hit(stone, Pulse(1f), Aegis);
            Assert.Greater(stone.Structural.Fraction, 0.9f, "Cold again, the pulse barely marks it.");
        }

        [Test]
        public void A_ForceBeforeHeatIsNotWasted_AndTheOrderDoesNotChangeTheOutcome()
        {
            MagicalPropertySet heatFirst = Example(ExampleObject.HeatSensitiveStone);
            Hit(heatFirst, Wave(1f), Flame);
            Hit(heatFirst, Pulse(1f), Aegis);

            MagicalPropertySet forceFirst = Example(ExampleObject.HeatSensitiveStone);
            Hit(forceFirst, Pulse(1f), Aegis);
            float afterColdBlow = forceFirst.Structural.Integrity;
            Hit(forceFirst, Wave(1f), Flame);
            Hit(forceFirst, Pulse(1f), Aegis);

            Assert.Less(afterColdBlow, 100f, "The cold blow did a little.");
            Assert.AreEqual(StructuralState.Destroyed, heatFirst.Structural.State);
            Assert.AreEqual(StructuralState.Destroyed, forceFirst.Structural.State,
                "No special case for the order: once the stone is heated, the blow breaks it.");
        }

        [Test]
        public void A_HeatAloneNeverBreaksIt_AndChargeAndBracingMeanNothingToIt()
        {
            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            for (int i = 0; i < 5; i++) Hit(stone, Wave(1f), Flame);
            Run(stone, 3f);

            Assert.AreEqual(StructuralState.Intact, stone.Structural.State, "Stone does not burn: heat alone does not break it.");
            Assert.AreEqual(EffectResponse.NoProperty, stone.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f)));
            Assert.AreEqual(EffectResponse.NoProperty, stone.Apply(Effect(MagicEffectKind.Stabilize, 1f)));
        }

        // ------------------------------------ B: arcane + mechanism = activation

        [Test]
        public void B_UnlitTheLockHoldsTheBlock_ForceIsResistedAndForceDoesNotResonate()
        {
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Assert.IsTrue(slide.IsKineticLocked);
            Assert.IsFalse(slide.CouldRespondTo(MagicEffectKind.Force), "Discovery must not promise movement while it is locked.");
            Assert.IsTrue(slide.CouldRespondTo(MagicEffectKind.ArcaneCharge), "The lock is what answers first.");

            Assert.AreEqual(EffectResponse.Resisted, slide.Apply(new MagicEffect(MagicEffectKind.Force, 5f, Vector3.forward, 0f,
                Vector3.zero, null, Aegis, EffectTargets.All, nextEvent++)));
            Run(slide, 2f);
            Assert.AreEqual(Vector3.zero, slide.Kinetic.Offset, "Not a centimetre.");
        }

        [Test]
        public void B_ChargeLightsTheLock_ThenForceSlidesTheBlock()
        {
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Hit(slide, Arrow(1f), Arcane);

            Assert.AreEqual(ArcaneState.Activated, slide.Arcane.State, "A full Arcane Arrow lights the lock.");
            Assert.IsFalse(slide.IsKineticLocked);
            Assert.IsTrue(slide.CouldRespondTo(MagicEffectKind.Force), "Now force would move it, and discovery may say so.");

            Assert.IsTrue(Hit(slide, Pulse(1f), Aegis));
            Run(slide, 3f);
            Assert.AreEqual(4f, slide.Kinetic.Travel(Vector3.forward), 0.01f, "A full pulse: four metres.");
            Assert.AreEqual(KineticState.Resting, slide.Kinetic.State);

            Hit(slide, Melee(MagicDiscipline.Aegis, 1), Aegis);
            Run(slide, 1f);
            Assert.AreEqual(4f, slide.Kinetic.Travel(Vector3.forward), 0.01f, "Aegis strikes are below its minimum force.");

            Hit(slide, Pulse(0f), Aegis);
            Run(slide, 2f);
            Assert.AreEqual(5f, slide.Kinetic.Travel(Vector3.forward), 0.01f, "It stops at the end of its rail.");
            Assert.IsTrue(slide.Meets(PropertyCondition.AtTravelLimit));
        }

        [Test]
        public void B_TwoUnchargedArrowsLightItOnlyIfTheyFollowEachOther()
        {
            MagicalPropertySet quick = Example(CombinationExample.RuneLockedSlide);
            Hit(quick, Arrow(0f), Arcane);
            Run(quick, 0.5f);
            Hit(quick, Arrow(0f), Arcane);
            Assert.AreEqual(ArcaneState.Activated, quick.Arcane.State);

            MagicalPropertySet slow = Example(CombinationExample.RuneLockedSlide);
            Hit(slow, Arrow(0f), Arcane);
            Run(slow, 6f);
            Hit(slow, Arrow(0f), Arcane);
            Assert.AreEqual(ArcaneState.Charged, slow.Arcane.State, "The first charge drained away: charge is temporary.");
        }

        [Test]
        public void B_WhenTheLockGoesDarkTheBlockStaysWhereItWasLeftAndIsHeldAgain()
        {
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Hit(slide, Arrow(1f), Arcane);
            Hit(slide, Pulse(0f), Aegis);
            Run(slide, 11f);

            Assert.AreNotEqual(ArcaneState.Activated, slide.Arcane.State, "The lock is lit for ten seconds.");
            Assert.AreEqual(2f, slide.Kinetic.Travel(Vector3.forward), 0.01f, "Where it was slid, it stays.");
            Assert.IsTrue(slide.IsKineticLocked);
            Assert.AreEqual(EffectResponse.Resisted, slide.Apply(Effect(MagicEffectKind.Force, 5f)));
        }

        [Test]
        public void B_IfTheLockGoesDarkDuringAGlideTheBlockStopsWhereItIs()
        {
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Hit(slide, Arrow(1f), Arcane);
            Run(slide, 9.5f);
            Hit(slide, Pulse(1f), Aegis);
            Run(slide, 3f);

            float travel = slide.Kinetic.Travel(Vector3.forward);
            Assert.Greater(travel, 0.5f, "It moved while the lock was lit.");
            Assert.Less(travel, 4f, "It stopped when the lock went dark, not at the end of its glide.");
            Assert.AreEqual(KineticState.Resting, slide.Kinetic.State);
            Assert.IsTrue(slide.IsAtRest, "Nothing is left running.");
        }

        [Test]
        public void B_ChargeFromALinkedCrystalCountsLikeAnyOther()
        {
            // An alternative approach: the crystal's reaction (75 charge, one step from the player) and one
            // uncharged arrow light the lock, as two arrows would.
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Hit(slide, new EffectPayload { arcaneCharge = 75f, targets = EffectTargets.Objects }, -1, depth: 1);
            Assert.AreEqual(ArcaneState.Charged, slide.Arcane.State);
            Hit(slide, Arrow(0f), Arcane);
            Assert.AreEqual(ArcaneState.Activated, slide.Arcane.State);
        }

        [Test]
        public void B_MoreChargeWhileLitStartsItsTimeAgain()
        {
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Hit(slide, Arrow(1f), Arcane);
            Run(slide, 8f);
            Hit(slide, Arrow(0f), Arcane);
            Run(slide, 8f);
            Assert.AreEqual(ArcaneState.Activated, slide.Arcane.State, "Kept lit by a new arrow: repeated experimentation pays.");
        }

        [Test]
        public void B_WhereTheBlockWasSlidIsSavedAndComesBackSilentlyAndLocked()
        {
            MagicalPropertySet slide = Example(CombinationExample.RuneLockedSlide);
            Hit(slide, Arrow(1f), Arcane);
            Hit(slide, Pulse(1f), Aegis);
            Run(slide, 3f);

            Persistence.ObjectRecord record = slide.Capture(slide.Settings.persisted);
            MagicalPropertySet loaded = Example(CombinationExample.RuneLockedSlide);
            var announced = new List<WorldPropertyKind>();
            loaded.StateChanged += (kind, from, to) => announced.Add(kind);
            loaded.Load(record, loaded.Settings.persisted);

            Assert.AreEqual(4f, loaded.Kinetic.Travel(Vector3.forward), 0.01f);
            Assert.IsTrue(loaded.IsKineticLocked, "The lock's activation was temporary: it comes back dark.");
            CollectionAssert.IsEmpty(announced, "Loading is silent.");
        }

        [Test]
        public void B_ALockOnTheMovementItselfOrOnAStateTheObjectCannotReachIsReported()
        {
            var problems = new List<string>();
            Assert.AreEqual(0, MagicalObjectRules.Validate(CombinationDefaults.Settings(CombinationExample.RuneLockedSlide), problems),
                string.Join("\n", problems));

            MagicalPropertySettings circular = CombinationDefaults.Settings(CombinationExample.RuneLockedSlide);
            circular.kineticSettings.movableOnlyWhen = PropertyCondition.Moving;
            problems.Clear();
            MagicalObjectRules.Validate(circular, problems);
            Assert.IsTrue(problems.Exists(p => p.Contains("movement itself")), string.Join("\n", problems));

            MagicalPropertySettings unreachable = CombinationDefaults.Settings(CombinationExample.RuneLockedSlide);
            unreachable.arcane = false;
            problems.Clear();
            MagicalObjectRules.Validate(unreachable, problems);
            Assert.IsTrue(problems.Exists(p => p.Contains("never reach")), string.Join("\n", problems));
        }

        // ------------------------------------ C: force + projectile = deflection

        private static ProjectileLaunch CinderMote()
        {
            return new ProjectileLaunch(null, Vector3.back, EnemyArchetypes.CinderMoteSpeed, EnemyArchetypes.CinderMoteRange,
                EnemyArchetypes.CinderMoteRadius, EnemyArchetypes.CinderMoteDamage, default, null, 6f, -1f, SpellElement.Fire,
                EnemyArchetypes.CinderMoteEffects, 1f, -1, EnemyArchetypes.MoteDeflection);
        }

        private static DeflectionPush AegisGuardPush(Vector3 aim)
        {
            GuardEffectProfile guard = DisciplineDefaults.Guard(MagicDiscipline.Aegis);
            return new DeflectionPush(null, CombatTeam.Player, aim, guard.deflectForce, guard.deflectAssistDegrees, Aegis);
        }

        [Test]
        public void C_OnlyTheAegisGuardHasForceToTurnAMote()
        {
            ProjectileDeflection mote = EnemyArchetypes.MoteDeflection;
            foreach (MagicDiscipline discipline in new[] { MagicDiscipline.Arcane, MagicDiscipline.Flame, MagicDiscipline.Aegis })
            {
                float force = DisciplineDefaults.Guard(discipline).deflectForce;
                bool turns = DeflectionRules.CanDeflect(mote, 0, force, CombatTeam.Enemy, CombatTeam.Player);
                Assert.AreEqual(discipline == MagicDiscipline.Aegis, turns, discipline.ToString());
            }

            // Force carried by attacks turns it too: the same rule, whatever applies the force.
            Assert.IsTrue(DeflectionRules.CanDeflect(mote, 0, Melee(MagicDiscipline.Aegis, 0).force, CombatTeam.Enemy, CombatTeam.Player));
            Assert.IsTrue(DeflectionRules.CanDeflect(mote, 0, Pulse(0f).force, CombatTeam.Enemy, CombatTeam.Player));
            Assert.IsFalse(DeflectionRules.CanDeflect(mote, 0, Melee(MagicDiscipline.Flame, 0).force, CombatTeam.Enemy, CombatTeam.Player),
                "Flame carries no force.");
        }

        [Test]
        public void C_TheGuardCatchesOnlyWhatComesAtItsFront()
        {
            float arc = 160f;
            Assert.IsTrue(DeflectionRules.GuardCatches(Vector3.forward, Vector3.back, arc), "Head-on.");
            Assert.IsTrue(DeflectionRules.GuardCatches(Vector3.forward, new Vector3(0.5f, 0f, -1f), arc), "From the front quarter.");
            Assert.IsFalse(DeflectionRules.GuardCatches(Vector3.forward, Vector3.forward, arc), "From behind.");
        }

        [Test]
        public void C_ATurnedMoteKeepsWhatItCarriesAndChangesOwnerAndCredit()
        {
            ProjectileLaunch mote = CinderMote();
            ProjectileLaunch turned = DeflectionRules.Deflected(mote, AegisGuardPush(Vector3.forward), Vector3.forward, SpellElement.Fire);

            Assert.AreEqual(mote.Damage, turned.Damage, "Its damage is kept.");
            Assert.AreEqual(SpellElement.Fire, turned.Element, "Its kind of magic is kept.");
            Assert.AreEqual(EnemyArchetypes.CinderMoteEffects.heat, turned.Effects.heat, "Its heat is kept.");
            Assert.AreEqual(mote.Speed * EnemyArchetypes.MoteDeflection.speedMultiplier, turned.Speed, 0.001f);
            Assert.AreEqual(Vector3.forward, turned.Direction);
            Assert.AreEqual(Aegis, turned.Discipline, "What it does now is credited to the deflector's discipline.");
            Assert.AreEqual(mote.MaxRange, turned.MaxRange, "Its range starts again but is no longer than before.");

            // An arcane mote that only implies its effect keeps it exactly, written out.
            var arcane = new ProjectileLaunch(null, Vector3.back, 13f, 22f, 0.22f, 12f, default, null);
            ProjectileLaunch arcaneTurned = DeflectionRules.Deflected(arcane, AegisGuardPush(Vector3.forward), Vector3.forward,
                SpellElement.Arcane);
            Assert.AreEqual(12f, arcaneTurned.Effects.arcaneCharge);
        }

        [Test]
        public void C_AMoteIsTurnedOnceAtMostNeverByItsOwnSideAndNeverByTooLittleForce()
        {
            ProjectileDeflection mote = EnemyArchetypes.MoteDeflection;
            Assert.IsFalse(DeflectionRules.CanDeflect(mote, 1, 5f, CombatTeam.Player, CombatTeam.Enemy),
                "A turned shot cannot be sent back again: no ping-pong.");
            Assert.IsFalse(DeflectionRules.CanDeflect(mote, 0, 5f, CombatTeam.Enemy, CombatTeam.Enemy),
                "A side never turns its own shots.");
            Assert.IsFalse(DeflectionRules.CanDeflect(mote, 0, 0.4f, CombatTeam.Enemy, CombatTeam.Player));
            Assert.IsFalse(DeflectionRules.CanDeflect(default, 0, 5f, CombatTeam.Enemy, CombatTeam.Player),
                "Only designated projectiles: the Mage's own arrow is not one.");
        }

        [Test]
        public void C_ATurnedShotGoesBackAtItsSenderOnlyNearTheLineOfThePush()
        {
            Vector3 from = Vector3.zero;
            Vector3 sender = new Vector3(1f, 0f, 10f);
            Vector3 near = DeflectionRules.Redirect(Vector3.forward, from, true, sender, 25f);
            Assert.AreEqual(sender.normalized.x, near.x, 0.001f, "Close to the line: it goes back at the sender.");

            Vector3 away = DeflectionRules.Redirect(Vector3.right, from, true, sender, 25f);
            Assert.AreEqual(Vector3.right, away, "Elsewhere: where the player aims, at a barricade for instance.");

            // A sender on a ledge above: the angle is measured along the ground, so it is still found.
            Vector3 onLedge = new Vector3(0.5f, 4.5f, 8f);
            Vector3 up = DeflectionRules.Redirect(Vector3.forward, from, true, onLedge, 25f);
            Assert.AreEqual(onLedge.normalized.y, up.y, 0.001f, "Back up at the Wisp on its ledge.");
        }

        [Test]
        public void C_AFieryMoteTurnedIntoKindlingSetsItAlightAndTheFireReachesTheBarricadeBehind()
        {
            var world = new World();
            Thing kindling = world.Add("kindling", CombinationDefaults.Settings(CombinationExample.DryKindling), new Vector3(0f, 0f, 0f));
            Thing barricade = world.Add("barricade", WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade), new Vector3(0f, 0f, 1.5f));

            ProjectileLaunch turned = DeflectionRules.Deflected(CinderMote(), AegisGuardPush(Vector3.forward), Vector3.forward,
                SpellElement.Fire);
            AreaStrike.Deliver(kindling, turned.Effects, turned.Direction, Vector3.zero, null, turned.Discipline, nextEvent++);

            Assert.AreEqual(ThermalState.Burning, kindling.Set.Thermal.State, "One turned fiery mote sets kindling alight.");
            Assert.AreEqual(0f, barricade.Set.Thermal.Heat, "The barricade was not struck.");

            world.Run(3f);
            Assert.AreEqual(ThermalState.Burning, barricade.Set.Thermal.State, "Its fire set the barricade beside it alight. " +
                string.Join(", ", world.Log));

            world.Run(10f);
            Assert.IsTrue(kindling.Set.IsDestroyed, "The kindling burnt away.");
            Assert.IsTrue(barricade.Set.IsDestroyed, "And so did the barricade.");
        }

        [Test]
        public void C_TheSameMoteDirectlyOnTheBarricadeOnlyHeatsIt_ACompatibleObjectIsNeeded()
        {
            MagicalPropertySet barricade = Example(ExampleObject.WoodenBarricade);
            Hit(barricade, EnemyArchetypes.CinderMoteEffects, Aegis);
            Assert.AreEqual(ThermalState.Heated, barricade.Thermal.State, "One mote heats the barricade.");

            Hit(barricade, EnemyArchetypes.CinderMoteEffects, Aegis);
            Assert.AreEqual(ThermalState.Burning, barricade.Thermal.State, "Two in quick succession set it alight.");
        }

        [Test]
        public void C_AnArcaneMoteTurnedIntoKindlingDoesNothingToIt()
        {
            MagicalPropertySet kindling = Example(CombinationExample.DryKindling);
            ProjectileLaunch arcane = DeflectionRules.Deflected(new ProjectileLaunch(null, Vector3.back, 13f, 22f, 0.22f, 12f, default, null),
                AegisGuardPush(Vector3.forward), Vector3.forward, SpellElement.Arcane);

            Assert.IsFalse(Hit(kindling, arcane.Effects, Aegis), "Arcane charge means nothing to brush.");
            Assert.AreEqual(ThermalState.Unheated, kindling.Thermal.State);
        }

        [Test]
        public void C_ATurnedMoteHurtsAndHeatsTheEnemiesItIsSentAt()
        {
            var wisp = new Creature(EnemyArchetypes.ArcaneWisp());
            ProjectileLaunch turned = DeflectionRules.Deflected(CinderMote(), AegisGuardPush(Vector3.forward), Vector3.forward,
                SpellElement.Fire);

            Assert.IsFalse(CombatTeams.AreAllied(CombatTeam.Player, CombatTeam.Enemy), "Now the player's shot: it strikes enemies.");
            Assert.IsTrue(CombatTeams.AreAllied(CombatTeam.Player, CombatTeam.Player), "And passes over the player who turned it.");

            float taken = wisp.Damage(turned.Damage);
            AreaStrike.Deliver(wisp, turned.Effects, turned.Direction, Vector3.zero, null, turned.Discipline, nextEvent++);
            Assert.Greater(taken, 0f);
            Assert.Greater(wisp.Model.Thermal.Heat, 0f, "Its heat goes with it.");

            var cinder = new Creature(EnemyArchetypes.CinderWisp());
            AreaStrike.Deliver(cinder, turned.Effects, turned.Direction, Vector3.zero, null, turned.Discipline, nextEvent++);
            Assert.AreEqual(0f, cinder.Model.Thermal.Heat, "A Cinder Wisp is a creature of fire: its own heat means nothing to it.");
        }

        // --------------------------------- D: stabilise + instability = bracing

        /// <summary>Holds the Aegis guard for a time, one pulse per interval, as the combat model issues them.</summary>
        private void HoldGuard(MagicalPropertySet target, float seconds)
        {
            float interval = DisciplineDefaults.Guard(MagicDiscipline.Aegis).pulseSeconds;
            for (float t = 0f; t < seconds - 0.0001f; t += interval)
            {
                Hit(target, AegisGuard(), Aegis);
                Run(target, interval);
            }
        }

        [Test]
        public void D_UnbracedTheWalkwayGivesWayUnderWeightBeforeAnyoneCouldCross()
        {
            MagicalPropertySet walkway = Example(CombinationExample.CrumblingWalkway);
            walkway.SetOccupants(1);
            Assert.IsTrue(walkway.Meets(PropertyCondition.Loaded), "The strain is a condition that can be shown.");
            Assert.IsFalse(walkway.IsAtRest, "Weight needs time to pass.");

            float time = 0f;
            while (walkway.Stability.State != StabilityState.Collapsed && time < 5f)
            {
                walkway.Tick(0.05f);
                time += 0.05f;
            }

            const float SprintSpeed = 7f;
            Assert.AreEqual(StabilityState.Collapsed, walkway.Stability.State);
            Assert.Less(time, CombinationDefaults.WalkwayLength / SprintSpeed, "Even a sprint does not get across in time.");
        }

        [Test]
        public void D_HoldingTheGuardBuildsABraceThatCarriesAPlayerAcross()
        {
            MagicalPropertySet walkway = Example(CombinationExample.CrumblingWalkway);
            HoldGuard(walkway, 2.4f);
            Assert.AreEqual(StabilityState.Braced, walkway.Stability.State);
            Assert.Greater(walkway.Stability.BraceRemaining, 6f, "Each pulse added to the brace (Extend), up to eight seconds.");

            // Released, then walked across at walking pace.
            walkway.SetOccupants(1);
            const float WalkSpeed = 4f;
            Run(walkway, CombinationDefaults.WalkwayLength / WalkSpeed);
            walkway.SetOccupants(0);

            Assert.AreEqual(StabilityState.Braced, walkway.Stability.State, "Braced, it bore the weight.");
            Assert.AreEqual(0f, walkway.Stability.Stress, "And took no stress at all.");
        }

        [Test]
        public void D_WhenTheBraceRunsOutUnderWeightItGivesWay()
        {
            MagicalPropertySet walkway = Example(CombinationExample.CrumblingWalkway);
            Hit(walkway, AegisGuard(), Aegis);
            walkway.SetOccupants(1);
            Run(walkway, 1.4f);
            Assert.AreEqual(StabilityState.Braced, walkway.Stability.State);

            Run(walkway, 1.5f);
            Assert.AreEqual(StabilityState.Collapsed, walkway.Stability.State, "The brace is temporary: weight wins when it ends.");
        }

        [Test]
        public void D_KeepingTheGuardUpWhileWalkingKeepsItBraced()
        {
            MagicalPropertySet walkway = Example(CombinationExample.CrumblingWalkway);
            Hit(walkway, AegisGuard(), Aegis);
            walkway.SetOccupants(1);
            HoldGuard(walkway, 12f);
            Assert.AreEqual(StabilityState.Braced, walkway.Stability.State);
        }

        [Test]
        public void D_BraceFirstOrStepOnFirst_TheBraceStopsTheStrainEitherWay()
        {
            MagicalPropertySet stepFirst = Example(CombinationExample.CrumblingWalkway);
            stepFirst.SetOccupants(1);
            Run(stepFirst, 0.3f);
            float strained = stepFirst.Stability.Stress;
            Hit(stepFirst, AegisGuard(), Aegis);
            Run(stepFirst, 1f);

            Assert.Greater(strained, 0f);
            Assert.AreEqual(StabilityState.Braced, stepFirst.Stability.State);
            Assert.AreEqual(strained, stepFirst.Stability.Stress, 0.0001f, "No stress is added while braced.");
        }

        [Test]
        public void D_StepOffBeforeTheLimitAndTheStrainFades()
        {
            MagicalPropertySet walkway = Example(CombinationExample.CrumblingWalkway);
            walkway.SetOccupants(1);
            Run(walkway, 0.5f);
            walkway.SetOccupants(0);
            Assert.IsFalse(walkway.Meets(PropertyCondition.Loaded));
            Run(walkway, 3f);

            Assert.AreEqual(StabilityState.Unstable, walkway.Stability.State);
            Assert.AreEqual(0f, walkway.Stability.Stress, "Repeated attempts start fresh.");
            Assert.IsTrue(walkway.IsAtRest);
        }

        [Test]
        public void D_ABracedWalkwayHoldsAgainstABlow_AnUnbracedOneDoesNot()
        {
            MagicalPropertySet braced = Example(CombinationExample.CrumblingWalkway);
            Hit(braced, AegisGuard(), Aegis);
            Assert.IsFalse(Hit(braced, Pulse(1f), Aegis));
            Assert.AreEqual(StabilityState.Braced, braced.Stability.State);

            MagicalPropertySet loose = Example(CombinationExample.CrumblingWalkway);
            Hit(loose, Pulse(1f), Aegis);
            Assert.AreEqual(StabilityState.Collapsed, loose.Stability.State);
        }

        [Test]
        public void D_WeightMeansNothingToWhatIsNotUnstable()
        {
            Assert.IsFalse(Example(ExampleObject.HeatSensitiveStone).SetOccupants(1), "No stability property.");

            MagicalPropertySet unstable = Example(ExampleObject.UnstableStructure);
            unstable.SetOccupants(1);
            Run(unstable, 10f);
            Assert.AreEqual(StabilityState.Unstable, unstable.Stability.State, "Its weight rule is 0: standing on it is safe.");
            Assert.IsTrue(unstable.IsAtRest);
        }

        [Test]
        public void D_WhoStandsOnItIsCountedEvenWhileItCannotActOnIt()
        {
            // An unstable structure that weight also bears on: someone stands on it when it is destroyed,
            // then leaves. Restored afterwards, it must not think anyone is still there.
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            settings.stabilitySettings.loadStressPerSecond = 1f;
            var set = new MagicalPropertySet(settings);
            set.SetOccupants(1);
            set.Structural.Destroy();
            set.Tick(Step);
            Assert.IsTrue(set.IsDestroyed);

            Assert.IsTrue(set.SetOccupants(0), "The departure is noted although the object is destroyed.");
            set.Restore();
            Assert.IsFalse(set.Meets(PropertyCondition.Loaded));
            Assert.IsTrue(set.IsAtRest, "Nobody is on it: nothing strains it.");
        }

        // ----------------------------------------- E: enemy armour, heat, stagger

        [Test]
        public void E_ColdArmourIsBarelyMarked_HeatedArmourBreaksUnderAPulseAndTheGuardianStaggers()
        {
            var cold = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(cold, Pulse(1f), Aegis);
            Assert.Greater(cold.Model.Armour.Fraction, 0.9f, "Cold, a full pulse barely marks the plates.");

            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(guardian, Wave(1f), Flame);
            Hit(guardian, Melee(MagicDiscipline.Flame, 0), Flame);
            Assert.IsTrue(CombinationRules.IsHeated(guardian.Model.Thermal), "A full wave and one strike heat a Guardian.");

            Hit(guardian, Pulse(0f), Aegis);
            Assert.IsTrue(guardian.Model.ArmourBroken, "Heated, even an uncharged pulse breaks the plates.");
            Assert.IsTrue(guardian.Model.ArmourBrokenByLastEffect);
            Assert.AreEqual(1, guardian.Impacts, "Breaking the armour staggers it.");
            Assert.AreEqual(EnemyStateId.Stagger, guardian.Brain.State);
            CollectionAssert.Contains(guardian.Announced, $"Structural:{(int)StructuralState.Intact}>{(int)StructuralState.Destroyed}",
                "The change is announced, so it can be shown.");
        }

        [Test]
        public void E_StoneArmourAndAStoneBarrierFollowTheSameRule()
        {
            // The same stone, on a creature and in the world: identical heat and force, identical result.
            MagicalPropertySettings stone = WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone);
            var model = new CreatureEffectModel(stone.thermalSettings, 1f, false, 0f,
                new ArmourSettings { enabled = true, structure = stone.structuralSettings });
            MagicalPropertySet barrier = Example(ExampleObject.HeatSensitiveStone);

            foreach (float force in new[] { 1f, 2.5f, 5f })
            {
                int id = nextEvent++;
                model.Apply(Effect(MagicEffectKind.Force, force, 0, id), out _);
                barrier.Apply(Effect(MagicEffectKind.Force, force, 0, id));
                Assert.AreEqual(barrier.Structural.Integrity, model.Armour.Integrity, 0.0001f, $"Cold, force {force}.");
            }

            int heat = nextEvent++;
            model.Apply(Effect(MagicEffectKind.Heat, 80f, 0, heat), out _);
            barrier.Apply(Effect(MagicEffectKind.Heat, 80f, 0, heat));
            Assert.AreEqual(barrier.Thermal.State, model.Thermal.State);

            int blow = nextEvent++;
            model.Apply(Effect(MagicEffectKind.Force, 2f, 0, blow), out _);
            barrier.Apply(Effect(MagicEffectKind.Force, 2f, 0, blow));
            Assert.AreEqual(barrier.Structural.Integrity, model.Armour.Integrity, 0.0001f, "Heated, the same brittleness.");
            Assert.AreEqual(barrier.Structural.State, model.Armour.State);

            // The Guardian's plates are that same stone, in thinner plates.
            StructuralSettings plates = EnemyArchetypes.GuardianArmour().structure;
            Assert.AreEqual(stone.structuralSettings.brittleness, plates.brittleness);
            Assert.AreEqual(stone.structuralSettings.heatedBrittleness, plates.heatedBrittleness);
            Assert.AreEqual(stone.structuralSettings.forceDamage, plates.forceDamage);
        }

        [Test]
        public void E_ArmourDecidesHowMuchOfAHitGetsThrough_ABurnPassesIt()
        {
            ArmourSettings armour = EnemyArchetypes.GuardianArmour();
            Assert.AreEqual(0.8f, CreatureEffectModel.ArmourMultiplier(armour, StructuralState.Intact), 0.0001f);
            Assert.AreEqual(0.9f, CreatureEffectModel.ArmourMultiplier(armour, StructuralState.Weakened), 0.0001f);
            Assert.AreEqual(1f, CreatureEffectModel.ArmourMultiplier(armour, StructuralState.Fractured), 0.0001f);
            Assert.AreEqual(1.2f, CreatureEffectModel.ArmourMultiplier(armour, StructuralState.Destroyed), 0.0001f,
                "Broken, what it covered is exposed.");

            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Assert.AreEqual(1f, guardian.Model.DamageMultiplier(false), "A burn has no direction and passes the armour.");
        }

        [Test]
        public void E_ArmourBreakingDuringAStaggerDoesNotStaggerTwice()
        {
            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(guardian, Wave(1f), Flame);
            Hit(guardian, Melee(MagicDiscipline.Flame, 0), Flame);

            // A full Force Pulse: its hit staggers first (90 stagger), then its force breaks the plates.
            guardian.Damage(DisciplineDefaults.Charged(MagicDiscipline.Aegis).Charge.damageMax,
                DisciplineDefaults.Charged(MagicDiscipline.Aegis).Charge.staggerMax);
            Assert.AreEqual(EnemyStateId.Stagger, guardian.Brain.State);
            Hit(guardian, Pulse(1f), Aegis);

            Assert.IsTrue(guardian.Model.ArmourBroken);
            Assert.AreEqual(0, guardian.Impacts, "Immune for a moment: no stun-lock.");
            Assert.AreEqual(1, guardian.Body.KnockbackCount, "One stagger, one knockback.");
        }

        [Test]
        public void E_AStructureComingDownBesideAGuardianStaggersIt_AShatteringStoneDoesNot()
        {
            float collapse = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure).emissions[0].magnitude;
            float shatter = WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone).emissions[0].magnitude;

            var near = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(near, new EffectPayload { force = collapse, targets = EffectTargets.All }, -1, depth: 1);
            Assert.AreEqual(1, near.Impacts, "The world's force staggers it as an attack's would.");

            var other = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(other, new EffectPayload { force = shatter, targets = EffectTargets.All }, -1, depth: 1);
            Assert.AreEqual(0, other.Impacts);
            Assert.AreNotEqual(EnemyStateId.Stagger, other.Brain.State);
        }

        [Test]
        public void E_AWardIsDisruptedByArcaneChargeAndFormsAgain()
        {
            var wisp = new Creature(EnemyArchetypes.ArcaneWisp());
            Assert.AreEqual(0.85f, wisp.Model.DamageMultiplier(true), 0.0001f, "The ward takes 15% of a hit.");

            Hit(wisp, Arrow(0f), Arcane);
            Assert.IsTrue(wisp.Model.WardDisruptedByLastEffect, "An uncharged Arcane Arrow disrupts it: arcane + mechanism.");
            Assert.AreEqual(1f, wisp.Model.DamageMultiplier(true));
            Assert.AreEqual(1, wisp.Impacts, "Its disruption knocks the Wisp off balance.");

            wisp.Tick(6.5f);
            Assert.IsTrue(wisp.Model.WardHolds, "The disruption is temporary.");
            Assert.AreEqual(0.85f, wisp.Model.DamageMultiplier(true), 0.0001f);
            CollectionAssert.Contains(wisp.Announced, $"Arcane:{(int)ArcaneState.Activated}>{(int)ArcaneState.Uncharged}",
                "Forming again is announced too.");
        }

        [Test]
        public void E_EachEnemyHasSomeCapabilitiesAndNoneHasThemAll()
        {
            foreach (EnemyTuning tuning in new[] { EnemyArchetypes.RuinsGuardian(), EnemyArchetypes.ArcaneWisp(), EnemyArchetypes.CinderWisp() })
            {
                var model = new CreatureEffectModel(tuning.heat, tuning.pushResistance, true, tuning.staggerForce, tuning.armour, tuning.ward);
                int capabilities = (model.Thermal.CanHeat ? 1 : 0) + (model.Armour != null ? 1 : 0) + (model.Ward != null ? 1 : 0);
                Assert.Less(capabilities, 3, "No enemy answers everything.");
                Assert.IsFalse(model.Armour != null && model.Ward != null, "Armour and ward are not given together.");
            }

            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Assert.AreEqual(EffectResponse.NoProperty, guardian.ReceiveEffect(Effect(MagicEffectKind.ArcaneCharge, 100f)),
                "A Guardian has no ward: arcane charge means nothing to it.");
            Assert.AreEqual(EffectResponse.NoProperty, guardian.ReceiveEffect(Effect(MagicEffectKind.Stabilize, 1f)),
                "No creature can be braced.");
        }

        // ------------------------------------------ hazards and the player

        [Test]
        public void ThePlayerTakesTheWorldsHeatAndForceByExplicitRules()
        {
            var player = new CreatureEffectModel(PlayerHazardRules.DefaultHeat(), 0.5f, true);

            // Standing within reach of a burning barricade: 18 heat a second, as its reaction sends.
            float barricadeHeat = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade).emissions[0].magnitude;
            float seconds = 0f;
            while (!player.Thermal.IsBurning && seconds < 30f)
            {
                player.Apply(Effect(MagicEffectKind.Heat, barricadeHeat, 1), out _);
                player.Thermal.Tick(1f);
                seconds += 1f;
            }

            Assert.That(seconds, Is.InRange(6f, 14f), "Standing in a fire catches up with the player, but not at once.");

            // A structure coming down beside the player shoves them; charge and bracing mean nothing.
            float collapse = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure).emissions[0].magnitude;
            Assert.AreEqual(EffectResponse.Applied, player.Apply(Effect(MagicEffectKind.Force, collapse, 1), out float push));
            Assert.AreEqual(collapse * 0.5f, push, 0.001f);
            Assert.AreEqual(EffectResponse.NoProperty, player.Apply(Effect(MagicEffectKind.ArcaneCharge, 50f, 1), out _));
            Assert.AreEqual(EffectResponse.NoProperty, player.Apply(Effect(MagicEffectKind.Stabilize, 1f, 1), out _));

            // A raised guard takes the same share of an effect from the front as of a hit; not from behind.
            Assert.AreEqual(9f, PlayerHazardRules.Guarded(45f, true, Vector3.forward, Vector3.back, 160f, 0.8f), 0.001f);
            Assert.AreEqual(45f, PlayerHazardRules.Guarded(45f, true, Vector3.forward, Vector3.forward, 160f, 0.8f));
            Assert.AreEqual(45f, PlayerHazardRules.Guarded(45f, false, Vector3.forward, Vector3.back, 160f, 0.8f));
        }

        // ------------------------------------------------- different orders

        [Test]
        public void EffectsThatDoNotActOnEachOtherGiveTheSameResultInEitherOrder()
        {
            // The reinforced door has a structure and a lock: force and charge are independent.
            MagicalPropertySet a = Example(ExampleObject.ReinforcedDoor);
            Hit(a, Pulse(1f), Aegis);
            Hit(a, Arrow(1f), Arcane);
            MagicalPropertySet b = Example(ExampleObject.ReinforcedDoor);
            Hit(b, Arrow(1f), Arcane);
            Hit(b, Pulse(1f), Aegis);

            Assert.AreEqual(a.Structural.Integrity, b.Structural.Integrity, 0.0001f);
            Assert.AreEqual(a.Arcane.Charge, b.Arcane.Charge, 0.0001f);

            // A Wisp heated and charged, in either order.
            var w1 = new Creature(EnemyArchetypes.ArcaneWisp());
            Hit(w1, Wave(1f), Flame);
            Hit(w1, Arrow(0f), Arcane);
            var w2 = new Creature(EnemyArchetypes.ArcaneWisp());
            Hit(w2, Arrow(0f), Arcane);
            Hit(w2, Wave(1f), Flame);
            Assert.AreEqual(w1.Model.Thermal.Heat, w2.Model.Thermal.Heat, 0.0001f);
            Assert.AreEqual(w1.Model.WardHolds, w2.Model.WardHolds);
        }

        // ----------------------------------- duplicates and damage attribution

        [Test]
        public void TheSameEventNeverActsTwice()
        {
            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(guardian, Wave(1f), Flame);
            Hit(guardian, Melee(MagicDiscipline.Flame, 0), Flame);
            int blow = nextEvent++;
            guardian.ReceiveEffect(Effect(MagicEffectKind.Force, 2.5f, 0, blow));
            float afterFirst = guardian.Model.Armour.Integrity;
            Assert.AreEqual(EffectResponse.Duplicate, guardian.ReceiveEffect(Effect(MagicEffectKind.Force, 2.5f, 0, blow)));
            Assert.AreEqual(afterFirst, guardian.Model.Armour.Integrity, "A second collider of the same attack does nothing more.");

            var wisp = new Creature(EnemyArchetypes.ArcaneWisp());
            int charge = nextEvent++;
            wisp.ReceiveEffect(Effect(MagicEffectKind.ArcaneCharge, 20f, 0, charge));
            Assert.AreEqual(EffectResponse.Duplicate, wisp.ReceiveEffect(Effect(MagicEffectKind.ArcaneCharge, 20f, 0, charge)));
            Assert.IsTrue(wisp.Model.WardHolds, "20 and not 40: the duplicate did not disrupt it.");

            MagicalPropertySet kindling = Example(CombinationExample.DryKindling);
            int heat = nextEvent++;
            kindling.Apply(Effect(MagicEffectKind.Heat, 10f, 0, heat));
            Assert.AreEqual(EffectResponse.Duplicate, kindling.Apply(Effect(MagicEffectKind.Heat, 10f, 0, heat)));
            Assert.AreEqual(ThermalState.Heated, kindling.Thermal.State, "Not alight: no burn from a duplicate.");
        }

        [Test]
        public void ADeflectedShotIsCreditedToTheDeflectorAndAWorldReactionToNobody()
        {
            DeflectionPush push = AegisGuardPush(Vector3.forward);
            ProjectileLaunch turned = DeflectionRules.Deflected(CinderMote(), push, Vector3.forward, SpellElement.Fire);
            Assert.AreSame(push.Deflector, turned.Owner, "The damage source is whoever turned it.");
            Assert.AreEqual(push.Discipline, turned.Discipline);

            // A reaction carries how far it is from what the player did; it is never mistaken for an attack.
            var world = new World();
            Thing kindling = world.Add("kindling", CombinationDefaults.Settings(CombinationExample.DryKindling), Vector3.zero);
            Thing stone = world.Add("stone", WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone), new Vector3(0f, 0f, 1f));
            kindling.ReceiveEffect(Effect(MagicEffectKind.Heat, 30f));
            world.Run(2f);
            Assert.IsTrue(world.Log.Exists(line => line.StartsWith("stone<Heat@1")), string.Join(", ", world.Log));
        }

        // ----------------------------------------------------- chain limits

        [Test]
        public void AChainStartedByATurnedMoteStopsAtTheDepthLimit()
        {
            var world = new World();
            Thing first = world.Add("kindling", CombinationDefaults.Settings(CombinationExample.DryKindling), Vector3.zero);
            var row = new List<Thing>();
            for (int i = 1; i <= 5; i++)
            {
                row.Add(world.Add("b" + i, WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade), new Vector3(0f, 0f, i * 2.2f)));
            }

            ProjectileLaunch turned = DeflectionRules.Deflected(CinderMote(), AegisGuardPush(Vector3.forward), Vector3.forward,
                SpellElement.Fire);
            AreaStrike.Deliver(first, turned.Effects, turned.Direction, Vector3.zero, null, turned.Discipline, nextEvent++);
            world.Run(60f);

            Assert.IsTrue(row[0].Set.IsDestroyed, "Depth 1.");
            Assert.IsTrue(row[1].Set.IsDestroyed, "Depth 2.");
            Assert.IsTrue(row[2].Set.IsDestroyed, "Depth 3: the limit.");
            Assert.IsFalse(row[3].Set.IsDestroyed, "The chain ends: no unbounded spread.");
            Assert.IsTrue(world.Simulation.IsIdle, "When it is over, nothing is left running.");
            Assert.AreEqual(0, world.Scheduler.Count);
        }

        [Test]
        public void AnObjectNeverSetsItselfOffAndAReactionNeverComesBackToWhereItStarted()
        {
            var world = new World();
            Thing a = world.Add("a", CombinationDefaults.Settings(CombinationExample.DryKindling), Vector3.zero);
            Thing b = world.Add("b", CombinationDefaults.Settings(CombinationExample.DryKindling), new Vector3(0f, 0f, 1f));
            a.ReceiveEffect(Effect(MagicEffectKind.Heat, 30f));
            world.Run(10f);

            Assert.IsFalse(world.Log.Exists(line => line.StartsWith("a<Heat@1")), "Nothing came back to the first: " +
                string.Join(", ", world.Log));
            Assert.IsTrue(a.Set.IsDestroyed && b.Set.IsDestroyed);
        }

        // ------------------------------------- responsiveness during reactions

        [Test]
        public void AnAttackActsAtOnceWhileReactionsQueueUpAndEachFrameDoesABoundedAmount()
        {
            var budgets = new ReactionBudgets();
            var scheduler = new ReactionScheduler(budgets);
            var sink = new List<MagicalPropertySet>();
            for (int i = 0; i < 200; i++)
            {
                var barricade = Example(ExampleObject.WoodenBarricade);
                sink.Add(barricade);
                scheduler.Enqueue(new SetReceiver(barricade), Effect(MagicEffectKind.Heat, 10f, 1), ReactionPriority.Normal);
            }

            // A swing does not wait in the queue: its effects are delivered in the frame it lands.
            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(guardian, Wave(1f), Flame);
            Hit(guardian, Melee(MagicDiscipline.Flame, 0), Flame);
            Hit(guardian, Pulse(0f), Aegis);
            Assert.IsTrue(guardian.Model.ArmourBroken, "The combination resolved at once, whatever was queued.");
            Assert.AreEqual(EnemyStateId.Stagger, guardian.Brain.State, "And the stagger is immediate.");

            int frames = 0;
            while (scheduler.Count > 0 && frames < 100)
            {
                Assert.LessOrEqual(scheduler.Process(), budgets.reactionsPerFrame, "No frame does more than its budget.");
                frames++;
            }

            Assert.AreEqual(0, scheduler.Count, "The queue drains.");
            Assert.AreEqual(200, scheduler.Statistics.Delivered + scheduler.Statistics.Dropped,
                "What did not fit was refused and counted, never left hanging.");
            Assert.LessOrEqual(scheduler.Statistics.Delivered, budgets.queueCapacity);
        }

        // --------------------------------------------------------- balance

        /// <summary>Seconds of uninterrupted melee one discipline needs to defeat a creature, given what gets through.</summary>
        private static float MeleeSecondsToDefeat(MagicDiscipline discipline, float health, float multiplier)
        {
            MeleeStepProfile[] steps = DisciplineDefaults.Melee(discipline).steps;
            float seconds = 0f;
            for (int i = 0; health > 0f && i < 10000; i++)
            {
                MeleeStepProfile step = steps[i % steps.Length];
                health -= step.damage * multiplier;
                seconds += step.TotalSeconds;
            }

            return seconds;
        }

        [Test]
        public void EachDisciplineAloneStillDefeatsEachStandardEnemy()
        {
            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            var wisp = new Creature(EnemyArchetypes.ArcaneWisp());
            foreach (MagicDiscipline discipline in new[] { MagicDiscipline.Arcane, MagicDiscipline.Flame, MagicDiscipline.Aegis })
            {
                // Without combining, the armour and the ward stay up for the whole fight.
                float before = MeleeSecondsToDefeat(discipline, guardian.Tuning.maxHealth, 1f);
                float after = MeleeSecondsToDefeat(discipline, guardian.Tuning.maxHealth, guardian.Model.DamageMultiplier(true));
                Assert.LessOrEqual(after, before * 1.3f, $"{discipline} alone against a Guardian: at most 30% longer than before LM-06.");

                before = MeleeSecondsToDefeat(discipline, wisp.Tuning.maxHealth, 1f);
                after = MeleeSecondsToDefeat(discipline, wisp.Tuning.maxHealth, wisp.Model.DamageMultiplier(true));
                Assert.LessOrEqual(after, before * 1.35f, $"{discipline} alone against a Wisp: about one swing more at most.");
            }

            float fullArrow = DisciplineDefaults.Charged(MagicDiscipline.Arcane).Charge.damageMax;
            Assert.GreaterOrEqual(fullArrow * wisp.Model.DamageMultiplier(true), wisp.Tuning.maxHealth,
                "A full Arcane Arrow still defeats a Wisp in one hit through its ward.");
        }

        [Test]
        public void CombiningIsRewardedButMeleeStaysTheMainDamage()
        {
            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            float armoured = guardian.Model.DamageMultiplier(true);
            Hit(guardian, Wave(1f), Flame);
            Hit(guardian, Melee(MagicDiscipline.Flame, 0), Flame);
            Hit(guardian, Pulse(0f), Aegis);
            float exposed = guardian.Model.DamageMultiplier(true);

            foreach (MagicDiscipline discipline in new[] { MagicDiscipline.Arcane, MagicDiscipline.Flame, MagicDiscipline.Aegis })
            {
                float slow = MeleeSecondsToDefeat(discipline, guardian.Tuning.maxHealth, armoured);
                float fast = MeleeSecondsToDefeat(discipline, guardian.Tuning.maxHealth, exposed);
                Assert.LessOrEqual(fast, slow * 0.75f, $"{discipline}: breaking the armour makes the fight clearly shorter.");
            }

            // The combination itself is not a kill: the wave and the pulse together do a fraction of a Guardian's health.
            float combinationDamage = DisciplineDefaults.Charged(MagicDiscipline.Flame).Charge.damageMax * armoured +
                                      DisciplineDefaults.Charged(MagicDiscipline.Aegis).Charge.damageMin * armoured;
            Assert.Less(combinationDamage, guardian.Tuning.maxHealth * 0.3f, "Ordinary melee still does most of the work.");
        }

        [Test]
        public void EveryCombinationLeavesTimeToSwitchDisciplineAndCharge()
        {
            // The longest charge of an attack that completes a combination is the Force Pulse's: under a second.
            float fullPulse = DisciplineDefaults.Charged(MagicDiscipline.Aegis).Charge.fullChargeSeconds;
            Assert.Less(fullPulse, 1.5f);

            MagicalPropertySet stone = Example(ExampleObject.HeatSensitiveStone);
            Hit(stone, Wave(1f), Flame);
            float stoneWindow = 0f;
            while (CombinationRules.IsHeated(stone.Thermal) && stoneWindow < 60f)
            {
                stone.Tick(Step);
                stoneWindow += Step;
            }

            var guardian = new Creature(EnemyArchetypes.RuinsGuardian());
            Hit(guardian, Wave(1f), Flame);
            Hit(guardian, Melee(MagicDiscipline.Flame, 0), Flame);
            float guardianWindow = 0f;
            while (CombinationRules.IsHeated(guardian.Model.Thermal) && guardianWindow < 60f)
            {
                guardian.Model.Thermal.Tick(Step);
                guardianWindow += Step;
            }

            Assert.GreaterOrEqual(stoneWindow, fullPulse + 1f, "The stone stays heated long enough.");
            Assert.GreaterOrEqual(guardianWindow, fullPulse + 0.5f, "A heated Guardian stays heated long enough.");
            Assert.GreaterOrEqual(CombinationDefaults.Settings(CombinationExample.RuneLockedSlide).arcaneSettings.activeSeconds, fullPulse + 3f);
            Assert.GreaterOrEqual(EnemyArchetypes.WispWard().arcane.activeSeconds, 3f, "A disrupted ward stays down for a while.");
        }

        // ---------------------------------------------------- the examples

        [Test]
        public void TheCombinationExamplesAreValidAndNoneAnswersEverything()
        {
            for (int i = 0; i < CombinationDefaults.Count; i++)
            {
                var example = (CombinationExample)i;
                MagicalPropertySettings settings = CombinationDefaults.Settings(example);
                var problems = new List<string>();
                Assert.AreEqual(0, MagicalObjectRules.Validate(settings, problems), $"{example}: {string.Join("\n", problems)}");

                int answers = 0;
                for (int kind = 0; kind < 4; kind++)
                {
                    answers += MagicalObjectRules.Answers(settings, (MagicEffectKind)kind) ? 1 : 0;
                }

                Assert.Less(answers, 4, example.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(CombinationDefaults.Description(example)));
            }
        }

        [Test]
        public void OlderEnemyDefinitionsAreGivenTheirDefencesOnceAndHandSetValuesAreKept()
        {
            var old = EnemyArchetypes.RuinsGuardian();
            old.staggerForce = 0f;
            old.armour = new ArmourSettings();
            old.ward = new WardSettings();
            Assert.IsTrue(EnemyArchetypes.FillCombinations(old, EnemyArchetypes.RuinsGuardian()));
            Assert.IsTrue(old.armour.enabled);
            Assert.AreEqual(3.5f, old.staggerForce);

            var tuned = EnemyArchetypes.RuinsGuardian();
            tuned.armour.enabled = false;
            tuned.staggerForce = 2f;
            Assert.IsFalse(EnemyArchetypes.FillCombinations(tuned, EnemyArchetypes.RuinsGuardian()), "A value set by hand keeps all of them.");
            Assert.IsFalse(tuned.armour.enabled);

            var guard = new GuardEffectProfile { stabilize = 1f };
            Assert.IsTrue(DisciplineDefaults.FillCombinations(MagicDiscipline.Aegis, guard));
            Assert.Greater(guard.deflectForce, 0f, "An Aegis guard generated before LM-06 learns to deflect.");
            Assert.IsFalse(DisciplineDefaults.FillCombinations(MagicDiscipline.Flame, new GuardEffectProfile()));
        }

        // ------------------------------------------------ a stand-in world

        private sealed class Thing : IMagicEffectReceiver, ISimulated
        {
            public readonly string Name;
            public readonly MagicalPropertySet Set;
            public Vector3 Position;
            public World World;

            public Thing(string name, MagicalPropertySettings settings, Vector3 position)
            {
                Name = name;
                Position = position;
                Set = new MagicalPropertySet(settings) { KineticAxis = Vector3.forward };
            }

            public bool IsSimulationAtRest => Set.IsAtRest;
            public bool IsSimulationCritical => Set.Settings.critical;

            public EffectResponse ReceiveEffect(in MagicEffect effect)
            {
                EffectResponse response = Set.Apply(effect);
                World.Log.Add($"{Name}<{effect.Kind}@{effect.Depth}:{response}");
                World.Send(this);
                if (!Set.IsAtRest)
                {
                    World.Simulation.Wake(this);
                }

                return response;
            }

            public void Simulate(float deltaTime)
            {
                Set.Tick(deltaTime);
                World.Send(this);
            }
        }

        /// <summary>What the component does in a scene: reactions go to what is in reach, nearest first, through the queue.</summary>
        private sealed class World
        {
            public readonly List<Thing> Things = new List<Thing>();
            public readonly List<string> Log = new List<string>();
            public readonly ReactionScheduler Scheduler;
            public readonly SimulationSet Simulation;

            public World()
            {
                var budgets = new ReactionBudgets();
                Scheduler = new ReactionScheduler(budgets);
                Simulation = new SimulationSet(budgets);
            }

            public Thing Add(string name, MagicalPropertySettings settings, Vector3 position)
            {
                var thing = new Thing(name, settings, position) { World = this };
                Things.Add(thing);
                return thing;
            }

            public void Send(Thing source)
            {
                for (int i = 0; i < source.Set.PendingEmissionCount; i++)
                {
                    EmissionRequest request = source.Set.GetPendingEmission(i);
                    ReactionEmission rule = source.Set.GetEmissionRule(request.Rule);
                    var near = new List<Thing>();
                    foreach (Thing other in Things)
                    {
                        if (other != source && Vector3.Distance(other.Position, source.Position) <= rule.radius)
                        {
                            near.Add(other);
                        }
                    }

                    near.Sort((a, b) => Vector3.Distance(a.Position, source.Position).CompareTo(Vector3.Distance(b.Position, source.Position)));
                    foreach (Thing target in near)
                    {
                        Vector3 direction = (target.Position - source.Position).normalized;
                        Scheduler.Enqueue(target, new MagicEffect(rule.effect, rule.magnitude, direction, rule.duration, target.Position,
                            null, -1, rule.targets, request.EventId, request.Depth), rule.priority);
                    }
                }

                source.Set.ClearPendingEmissions();
            }

            public void Run(float seconds)
            {
                for (float t = 0f; t < seconds - 0.0001f; t += Step)
                {
                    Simulation.Tick(Step);
                    Scheduler.Process();
                }
            }
        }
    }
}
