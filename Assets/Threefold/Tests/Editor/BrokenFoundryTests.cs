using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Threefold.Combat;
using Threefold.DevTools;
using Threefold.Enemies;
using Threefold.Levels;
using Threefold.Magic;
using Threefold.Objectives;
using Threefold.Persistence;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Sprint LM-07, the Broken Foundry: its progression as a graph, every object of the level driven
    /// by the disciplines' real default attacks, what lasts and what does not across a reload, the
    /// checkpoints, the hazard rule, forgetting one level's saved state, and the development
    /// diagnostics. Plain C#. The components that carry these rules out need Unity:
    /// <see cref="BrokenFoundryAssemblyTests"/> builds and checks the scene and calls the session's
    /// restart, retry and new game directly, but what runs frame by frame (the checkpoint route, the
    /// hazard, a remembered defeat hiding its enemy) needs Play Mode tests, which the project does not have yet.
    /// </summary>
    public sealed class BrokenFoundryTests
    {
        private const int Arcane = (int)MagicDiscipline.Arcane;
        private const int Flame = (int)MagicDiscipline.Flame;
        private const int Aegis = (int)MagicDiscipline.Aegis;
        private const float Step = 0.1f;
        private const DisciplineSet All = FoundryRoutes.AllDisciplines;

        private int nextEvent = 170000;

        [SetUp]
        public void SetUp()
        {
            WorldState.ResetSession();
        }

        [TearDown]
        public void TearDown()
        {
            WorldState.ResetSession();
        }

        // ------------------------------------------------------------- helpers

        private static EffectPayload Melee(MagicDiscipline discipline, int step) => DisciplineDefaults.Melee(discipline).steps[step].Effects;
        private static EffectPayload Arrow(float power) => DisciplineDefaults.Charged(MagicDiscipline.Arcane).Projectile.Effects(power);
        private static EffectPayload Wave(float power) => DisciplineDefaults.Charged(MagicDiscipline.Flame).Area.Effects(power);
        private static EffectPayload Pulse(float power) => DisciplineDefaults.Charged(MagicDiscipline.Aegis).Area.Effects(power);
        private static EffectPayload Guard() => DisciplineDefaults.Guard(MagicDiscipline.Aegis).Effects;
        private static float GuardInterval => DisciplineDefaults.Guard(MagicDiscipline.Aegis).pulseSeconds;

        private static MagicalPropertySet Make(MagicalPropertySettings settings) => new MagicalPropertySet(settings) { KineticAxis = Vector3.forward };
        private static MagicalPropertySet Make(FoundryObject kind) => Make(FoundryDefaults.Settings(kind));
        private static MagicalPropertySet Make(ExampleObject kind) => Make(WorldMagicDefaults.Settings(kind));
        private static MagicalPropertySet Make(CombinationExample kind) => Make(CombinationDefaults.Settings(kind));

        private static void Run(MagicalPropertySet set, float seconds)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += Step)
            {
                set.Tick(Step);
            }
        }

        private bool Hit(MagicalPropertySet target, EffectPayload payload, int discipline)
        {
            return AreaStrike.Deliver(new SetReceiver(target), payload, Vector3.forward, Vector3.zero, null, discipline, nextEvent++);
        }

        private MagicEffect Effect(MagicEffectKind kind, float magnitude, int depth = 1)
        {
            return new MagicEffect(kind, magnitude, Vector3.forward, 0f, Vector3.zero, null, -1, EffectTargets.All, nextEvent++, depth);
        }

        private sealed class SetReceiver : IMagicEffectReceiver
        {
            private readonly MagicalPropertySet set;
            public SetReceiver(MagicalPropertySet target) { set = target; }
            public EffectResponse ReceiveEffect(in MagicEffect effect) => set.Apply(effect);
        }

        // ============================================================= the level as a graph

        [Test]
        public void Routes_TheWayOutCanBeReachedFromTheStart()
        {
            Assert.IsTrue(FoundryRoutes.CanFinish(FoundryPlace.Forecourt, FoundryProgress.None, All));
            Assert.IsFalse(FoundryRoutes.Reachable(FoundryPlace.Forecourt, FoundryProgress.None, All).Contains(FoundryPlace.OreHallEntry),
                "At the start only the forecourt is open: the crystal comes first.");
        }

        [Test]
        public void Routes_TheProgressionIsOpeningMiddleCombinationCombatFinal()
        {
            // Each part of the level needs what the part before it taught, in this order.
            Assert.AreEqual(FoundryProgress.GateOpen, FoundryRoutes.LeastProgressToReach(FoundryPlace.OreHallEntry, All));
            Assert.AreEqual(FoundryProgress.GateOpen | FoundryProgress.BarricadeCleared,
                FoundryRoutes.LeastProgressToReach(FoundryPlace.LiftHall, All), "The middle: a lasting change, then a temporary one (the brace).");
            Assert.AreEqual(FoundryProgress.GateOpen | FoundryProgress.BarricadeCleared | FoundryProgress.LiftInPlace,
                FoundryRoutes.LeastProgressToReach(FoundryPlace.CastingFloor, All), "The combination: Arcane and Aegis on the lift.");
            Assert.AreEqual(FoundryProgress.GateOpen | FoundryProgress.BarricadeCleared | FoundryProgress.LiftInPlace | FoundryProgress.GuardianDefeated,
                FoundryRoutes.LeastProgressToReach(FoundryPlace.ExitCourt, All), "Combat opens the final court.");
        }

        [Test]
        public void Routes_TheSealedExitHasThreeCompleteSolutions_EachEnoughOnItsOwn()
        {
            FoundryProgress atCourt = FoundryRoutes.LeastProgressToReach(FoundryPlace.ExitCourt, All);
            CollectionAssert.AreEquivalent(new[] { ExitRoute.Culvert, ExitRoute.Gate, ExitRoute.Breach }, FoundryRoutes.RoutesAvailable(atCourt, All));

            foreach (ExitRoute route in new[] { ExitRoute.Culvert, ExitRoute.Gate, ExitRoute.Breach })
            {
                // Only the disciplines this route needs: it still works, with the other routes closed.
                Assert.IsTrue(FoundryRoutes.RouteCanFinish(route, atCourt, FoundryRoutes.DisciplinesFor(route)), $"{route} alone reaches the way out.");
            }
        }

        [Test]
        public void Routes_TheSolutionsAreGenuinelyDifferent_NotOneActionInThreeColours()
        {
            // Different actions: break a structure for good, move a mechanism for good, or cross on a temporary brace.
            Assert.AreEqual(FoundryProgress.PlugBroken, FoundryRoutes.ChangeFor(ExitRoute.Culvert));
            Assert.AreEqual(FoundryProgress.ExitGateOpen, FoundryRoutes.ChangeFor(ExitRoute.Gate));
            Assert.AreEqual(FoundryProgress.None, FoundryRoutes.ChangeFor(ExitRoute.Breach), "The breach changes nothing for good.");

            // Different disciplines, and no route answers merely to any one discipline.
            DisciplineSet culvert = FoundryRoutes.DisciplinesFor(ExitRoute.Culvert);
            DisciplineSet gate = FoundryRoutes.DisciplinesFor(ExitRoute.Gate);
            DisciplineSet breach = FoundryRoutes.DisciplinesFor(ExitRoute.Breach);
            Assert.AreNotEqual(culvert, gate);
            Assert.AreNotEqual(culvert, breach);
            Assert.AreNotEqual(gate, breach);

            FoundryProgress atCourt = FoundryRoutes.LeastProgressToReach(FoundryPlace.ExitCourt, All);
            Assert.IsFalse(FoundryRoutes.RouteCanFinish(ExitRoute.Culvert, atCourt, DisciplineSet.Flame), "Heat alone does not break the plug.");
            Assert.IsFalse(FoundryRoutes.RouteCanFinish(ExitRoute.Gate, atCourt, DisciplineSet.Arcane), "A lit lock alone does not move the weight.");
        }

        [Test]
        public void Routes_NoOrderOfActionsLeavesThePlayerWhereTheWayOutCannotBeReached()
        {
            int checkedStates = 0;
            foreach (FoundryProgress state in FoundryRoutes.ReachableStates(FoundryPlace.Forecourt, FoundryProgress.None, All))
            {
                foreach (FoundryPlace place in FoundryRoutes.Reachable(FoundryPlace.Forecourt, state, All))
                {
                    Assert.IsTrue(FoundryRoutes.CanFinish(place, state, All), $"Softlock at {place} with {state}.");
                    checkedStates++;
                }
            }

            Assert.Greater(checkedStates, 20, "Every lasting state the level can be in, at every place it allows.");
        }

        [Test]
        public void Routes_EveryCheckpointCanStillReachTheWayOut()
        {
            foreach (CheckpointLayout checkpoint in FoundryLayout.Checkpoints)
            {
                FoundryProgress least = FoundryRoutes.LeastProgressToReach(checkpoint.Place, All);
                Assert.IsTrue(FoundryRoutes.CanFinish(checkpoint.Place, least, All), $"From checkpoint '{checkpoint.Name}'.");
            }
        }

        [Test]
        public void Routes_WhichDisciplinesTheLevelNeeds()
        {
            // Stated, not hidden: Arcane wakes the crystal, Aegis braces the bridge. Flame is never required,
            // because force also breaks the barricade, and the gate and the breach need no heat.
            Assert.IsFalse(FoundryRoutes.CanFinish(FoundryPlace.Forecourt, FoundryProgress.None, DisciplineSet.Flame | DisciplineSet.Aegis));
            Assert.IsFalse(FoundryRoutes.CanFinish(FoundryPlace.Forecourt, FoundryProgress.None, DisciplineSet.Arcane | DisciplineSet.Flame));
            Assert.IsTrue(FoundryRoutes.CanFinish(FoundryPlace.Forecourt, FoundryProgress.None, DisciplineSet.Arcane | DisciplineSet.Aegis));
        }

        // ============================================================= opening: the dormant crystal

        [Test]
        public void Crystal_AFullArrowWakesIt_ThreeQuickUnchargedArrowsToo_AndItStaysAwake()
        {
            MagicalPropertySet full = Make(FoundryObject.DormantCrystal);
            Hit(full, Arrow(1f), Arcane);
            Assert.IsTrue(full.Meets(PropertyCondition.Activated));
            Run(full, 120f);
            Assert.IsTrue(full.Meets(PropertyCondition.Activated), "It latches: two minutes later it is still awake.");

            MagicalPropertySet quick = Make(FoundryObject.DormantCrystal);
            for (int i = 0; i < 3; i++)
            {
                Hit(quick, Arrow(0f), Arcane);
                Run(quick, 1f);
            }

            Assert.IsTrue(quick.Meets(PropertyCondition.Activated), "Three uncharged arrows a second apart.");

            MagicalPropertySet slow = Make(FoundryObject.DormantCrystal);
            for (int i = 0; i < 3; i++)
            {
                Hit(slow, Arrow(0f), Arcane);
                Run(slow, 6f);
            }

            Assert.IsFalse(slow.Meets(PropertyCondition.Activated), "Spaced out, the charge drains in between: the attempt can be retried.");
        }

        [Test]
        public void Crystal_HeatAndForceDoNothing_ItsWakingIsSaved_AndLoadsSilently()
        {
            MagicalPropertySet crystal = Make(FoundryObject.DormantCrystal);
            Assert.AreEqual(EffectResponse.NoProperty, crystal.Apply(Effect(MagicEffectKind.Heat, 80f, 0)));
            Assert.AreEqual(EffectResponse.NoProperty, crystal.Apply(Effect(MagicEffectKind.Force, 5f, 0)));

            Hit(crystal, Arrow(1f), Arcane);
            PersistedState kept = FoundryDefaults.Settings(FoundryObject.DormantCrystal).persisted;
            ObjectRecord record = crystal.Capture(kept);

            MagicalPropertySet loaded = Make(FoundryObject.DormantCrystal);
            loaded.Load(record, kept);
            Assert.IsTrue(loaded.Meets(PropertyCondition.Activated), "Awake after a reload, so the gate stays open.");
        }

        // ============================================================= middle: barricade and bridge

        [Test]
        public void Barricade_FlameBurnsItAway_ForceBreaksIt_ArcaneDoesNothing_AndItStaysGone()
        {
            MagicalPropertySet burnt = Make(ExampleObject.WoodenBarricade);
            Hit(burnt, Wave(1f), Flame);
            Assert.IsTrue(burnt.Meets(PropertyCondition.Burning), "A charged Flame Wave sets it alight.");
            Run(burnt, 8f);
            Assert.IsTrue(burnt.IsDestroyed, "It burns away.");

            MagicalPropertySet broken = Make(ExampleObject.WoodenBarricade);
            Hit(broken, Pulse(1f), Aegis);
            Hit(broken, Pulse(1f), Aegis);
            Assert.IsTrue(broken.IsDestroyed, "Two full Force Pulses break it: a way through without Flame.");

            MagicalPropertySet charged = Make(ExampleObject.WoodenBarricade);
            Assert.IsFalse(Hit(charged, Arrow(1f), Arcane), "Arcane charge does nothing to wood.");

            PersistedState kept = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade).persisted;
            MagicalPropertySet loaded = Make(ExampleObject.WoodenBarricade);
            loaded.Load(burnt.Capture(kept), kept);
            Assert.IsTrue(loaded.IsDestroyed, "Gone for good: a lasting change.");
            Assert.IsFalse(loaded.Meets(PropertyCondition.Burning), "The fire itself is temporary and does not come back.");
        }

        [Test]
        public void Bridge_UnbracedItGivesWayBeforeAnyoneIsAcross_BracedItCarriesTheCrossing()
        {
            var reach = TraversalReach.Defaults;
            StabilitySettings settings = FoundryDefaults.Settings(FoundryObject.CrumblingGantry).stabilitySettings;
            float giveWay = settings.stressLimit / settings.loadStressPerSecond;
            float span = FoundryLayout.PitZ.y - FoundryLayout.PitZ.x;
            Assert.Less(giveWay * reach.SprintSpeed, span, "Even sprinting, nobody gets across unbraced.");

            // Nor with two sprinting jumps, landing on the bridge between them: it is gone before the first lands.
            float airTime = reach.LongestJump / reach.SprintSpeed;
            Assert.Greater(reach.LongestJump * 2f, span, "Two sprinting jumps would cover the pit if the bridge held long enough.");
            Assert.Less(giveWay, airTime - 0.2f, "The bridge gives way while the first jump is still in the air.");
            Assert.Less(reach.LongestJump, span - 1.5f, "And one jump does not reach the far side.");

            MagicalPropertySet unbraced = Make(FoundryObject.CrumblingGantry);
            unbraced.SetOccupants(1);
            Run(unbraced, giveWay + 0.2f);
            Assert.IsTrue(unbraced.Meets(PropertyCondition.Collapsed));

            // Hold the Aegis guard at the foot for 2.5 s, then walk across at walking pace.
            MagicalPropertySet braced = Make(FoundryObject.CrumblingGantry);
            for (float t = 0f; t < 2.5f; t += GuardInterval)
            {
                Hit(braced, Guard(), Aegis);
                Run(braced, GuardInterval);
            }

            braced.SetOccupants(1);
            Run(braced, FoundryLayout.BridgeLength / reach.WalkSpeed + 0.5f);
            Assert.IsFalse(braced.Meets(PropertyCondition.Collapsed), "The brace outlasts a walk across.");
        }

        [Test]
        public void Bridge_AFailedCrossingCanBeTriedAgain_ItStandsAgainOnceNobodyIsOnIt()
        {
            MagicalPropertySet bridge = Make(FoundryObject.CrumblingGantry);
            bridge.SetOccupants(1);
            Run(bridge, 1f);
            Assert.IsTrue(bridge.Meets(PropertyCondition.Collapsed));
            Assert.IsTrue(bridge.Stability.IsRecovering);

            Run(bridge, FoundryDefaults.GantryRecoverSeconds + 2f);
            Assert.IsTrue(bridge.Meets(PropertyCondition.Collapsed), "It does not rise while someone is on what is left of it.");

            bridge.SetOccupants(0);
            Run(bridge, FoundryDefaults.GantryRecoverSeconds - 0.5f);
            Assert.IsTrue(bridge.Meets(PropertyCondition.Collapsed));
            Run(bridge, 1f);
            Assert.IsFalse(bridge.Meets(PropertyCondition.Collapsed), "Five seconds after the last person left it, it stands again.");
            Assert.IsTrue(bridge.Meets(PropertyCondition.Unstable), "As it started: unstable, ready to be braced again.");
            Assert.IsTrue(bridge.IsAtRest, "Nothing more to simulate.");

            // A heavy blow brings it down too, and it comes back the same way.
            Hit(bridge, Pulse(1f), Aegis);
            Assert.IsTrue(bridge.Meets(PropertyCondition.Collapsed));
            Run(bridge, FoundryDefaults.GantryRecoverSeconds + 0.5f);
            Assert.IsFalse(bridge.Meets(PropertyCondition.Collapsed));
        }

        [Test]
        public void Bridge_ACollapseThatRecoversIsTemporary_NeverSaved_AndClearedByAReload()
        {
            MagicalPropertySettings settings = FoundryDefaults.Settings(FoundryObject.CrumblingGantry);
            MagicalPropertySet bridge = Make(settings);
            Hit(bridge, Pulse(1f), Aegis);
            Assert.IsTrue(bridge.Stability.IsRecovering);

            ObjectRecord record = bridge.Capture(PersistedState.Collapse);
            Assert.IsTrue(record.IsEmpty, "Even when asked to, a recovering collapse is not saved.");

            bridge.ClearTemporary();
            Assert.IsFalse(bridge.Meets(PropertyCondition.Collapsed), "Clearing what is temporary stands it up again.");

            var problems = new List<string>();
            MagicalPropertySettings contradictory = FoundryDefaults.Settings(FoundryObject.CrumblingGantry);
            contradictory.persisted = PersistedState.Collapse;
            Assert.Greater(MagicalObjectRules.Validate(contradictory, problems), 0, "Saving a collapse that recovers is reported.");
            StringAssert.Contains("stands again by itself", string.Join(" ", problems));
        }

        [Test]
        public void Recovery_NeverStandsUpSomethingDestroyed_AndIsRefusedOnAStructure()
        {
            MagicalPropertySettings settings = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            settings.stabilitySettings.recoverSeconds = 3f;

            var problems = new List<string>();
            Assert.Greater(MagicalObjectRules.Validate(settings, problems), 0, "Recovery on something with a structure is reported.");
            StringAssert.Contains("stability alone", string.Join(" ", problems));

            // Even so, at run time a destroyed object stays down and comes to rest.
            MagicalPropertySet set = Make(settings);
            for (int i = 0; i < 8 && !set.IsDestroyed; i++)
            {
                Hit(set, Pulse(1f), Aegis);
            }

            Assert.IsTrue(set.IsDestroyed);
            Run(set, 10f);
            Assert.IsTrue(set.Meets(PropertyCondition.Collapsed), "Destroyed, it does not stand again.");
            Assert.IsFalse(set.Stability.IsRecovering);
            Assert.IsTrue(set.IsAtRest, "Nothing keeps it in the simulation.");
        }

        [Test]
        public void Recovery_IsOffByDefault_SoEveryEarlierStructureStaysDown()
        {
            Assert.AreEqual(0f, new StabilitySettings().recoverSeconds);
            Assert.AreEqual(0f, CombinationDefaults.Settings(CombinationExample.CrumblingWalkway).stabilitySettings.recoverSeconds);
            Assert.AreEqual(0f, WorldMagicDefaults.Settings(ExampleObject.UnstableStructure).stabilitySettings.recoverSeconds);

            MagicalPropertySet structure = Make(ExampleObject.UnstableStructure);
            Hit(structure, Pulse(1f), Aegis);
            Run(structure, 60f);
            Assert.IsTrue(structure.Meets(PropertyCondition.Collapsed), "LM-03's unstable structure is unchanged: down until a restart.");
            Assert.IsFalse(structure.Stability.IsRecovering);
        }

        // ============================================================= combination: the rune lift

        [Test]
        public void Lift_RespondsToTwoDisciplines_LockedUntilLit_ThenPushedToItsStop_AndItStaysThere()
        {
            MagicalPropertySet lift = Make(CombinationExample.RuneLockedSlide);
            Assert.IsFalse(Hit(lift, Pulse(1f), Aegis), "Unlit, force does not move it.");

            Hit(lift, Arrow(1f), Arcane);
            Assert.IsTrue(lift.Meets(PropertyCondition.Activated), "A full Arcane Arrow lights the lock.");
            Hit(lift, Pulse(1f), Aegis);
            Run(lift, 3f);
            Hit(lift, Pulse(0f), Aegis);
            Run(lift, 3f);
            Assert.IsTrue(lift.Meets(PropertyCondition.AtTravelLimit), "A full and an uncharged Force Pulse slide it to its stop.");
            Assert.AreEqual(FoundryLayout.SlideTravel, lift.Kinetic.Travel(Vector3.forward), 0.01f);

            PersistedState kept = CombinationDefaults.Settings(CombinationExample.RuneLockedSlide).persisted;
            MagicalPropertySet loaded = Make(CombinationExample.RuneLockedSlide);
            loaded.Load(lift.Capture(kept), kept);
            Assert.IsTrue(loaded.Meets(PropertyCondition.AtTravelLimit), "Under the ledge after a reload: the way on stays open.");
            Assert.IsFalse(loaded.Meets(PropertyCondition.Activated), "The lit lock is temporary.");
        }

        [Test]
        public void Lift_WhenTheLockGoesDarkHalfway_TheBlockStays_AndCanBeLitAndPushedAgain()
        {
            MagicalPropertySet lift = Make(CombinationExample.RuneLockedSlide);
            Hit(lift, Arrow(1f), Arcane);
            Hit(lift, Pulse(0f), Aegis);
            Run(lift, 12f);
            Assert.IsFalse(lift.Meets(PropertyCondition.Activated), "The lock has gone dark.");
            float halfway = lift.Kinetic.Travel(Vector3.forward);
            Assert.That(halfway, Is.InRange(1f, 4f));

            Hit(lift, Pulse(1f), Aegis);
            Run(lift, 2f);
            Assert.AreEqual(halfway, lift.Kinetic.Travel(Vector3.forward), 0.01f, "Dark, it does not move.");

            Hit(lift, Arrow(1f), Arcane);
            Hit(lift, Pulse(1f), Aegis);
            Run(lift, 3f);
            Assert.IsTrue(lift.Meets(PropertyCondition.AtTravelLimit), "Relit, it goes the rest of the way: a failed attempt is retried.");
        }

        // ============================================================= combat: guardian, furnace, scaffolds

        [Test]
        public void Furnace_StokedByFlame_StaysHotForAWhile_ThenCools_AndNothingOfItIsSaved()
        {
            MagicalPropertySet furnace = Make(FoundryObject.Furnace);
            Assert.AreEqual(EffectResponse.NoProperty, furnace.Apply(Effect(MagicEffectKind.ArcaneCharge, 100f, 0)));
            Assert.AreEqual(EffectResponse.NoProperty, furnace.Apply(Effect(MagicEffectKind.Force, 5f, 0)));

            Hit(furnace, Wave(1f), Flame);
            Assert.IsTrue(furnace.Meets(PropertyCondition.Heated), "A full Flame Wave stokes it.");
            Run(furnace, 10f);
            Assert.IsTrue(furnace.Meets(PropertyCondition.Heated), "Ten seconds later it is still hot.");
            Run(furnace, 30f);
            Assert.IsFalse(furnace.Meets(PropertyCondition.Heated), "It cools by itself: a temporary change.");
            Assert.IsFalse(furnace.Meets(PropertyCondition.Burning), "It never burns.");

            Assert.AreEqual(PersistedState.None, FoundryDefaults.Settings(FoundryObject.Furnace).persisted);
            var problems = new List<string>();
            Assert.AreEqual(0, MagicalObjectRules.Validate(FoundryDefaults.Settings(FoundryObject.Furnace), problems), string.Join("; ", problems));
        }

        [Test]
        public void Furnace_HeatsAGuardianStandingInItsReach_AndThenForceBreaksItsPlates()
        {
            ReactionEmission heat = FoundryDefaults.Settings(FoundryObject.Furnace).emissions.Single();
            Assert.AreEqual(PropertyCondition.Heated, heat.when);
            Assert.AreEqual(EmissionTiming.WhileHolding, heat.timing);
            Assert.AreEqual(EffectTargets.All, heat.targets, "Creatures as well as objects: the Mage too.");

            EnemyTuning tuning = EnemyArchetypes.RuinsGuardian();
            var guardian = new CreatureEffectModel(tuning.heat, tuning.pushResistance, true, tuning.staggerForce, tuning.armour, tuning.ward);

            int seconds = 0;
            while (!CombinationRules.IsHeated(guardian.Thermal) && seconds < 20)
            {
                guardian.Apply(Effect(MagicEffectKind.Heat, heat.magnitude), out _);
                guardian.Thermal.Tick(heat.interval);
                seconds++;
            }

            Assert.That(seconds, Is.InRange(2, 6), "Lured into the furnace's heat, a Guardian is heated within a few seconds.");
            guardian.Apply(Effect(MagicEffectKind.Force, Pulse(0f).force, 0), out _);
            Assert.IsTrue(guardian.ArmourBroken, "Then even an uncharged Force Pulse breaks its stone plates.");
        }

        [Test]
        public void Scaffold_TheGuardiansShockwaveBringsItDown_AndItsFallStaggersTheGuardian()
        {
            MagicalPropertySettings scaffold = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure);
            MagicalPropertySet standing = Make(scaffold);
            Assert.IsTrue(AreaStrike.Deliver(new SetReceiver(standing), EnemyArchetypes.GuardianShockwaveEffects, Vector3.forward, Vector3.zero, null,
                -1, nextEvent++), "The Guardian's shockwave carries force for objects.");
            Assert.IsTrue(standing.Meets(PropertyCondition.Collapsed), "Enough to topple a loose scaffold beside it.");

            EnemyTuning tuning = EnemyArchetypes.RuinsGuardian();
            float fall = scaffold.emissions.Single(e => e.when == PropertyCondition.Collapsed).magnitude;
            Assert.IsTrue(CombinationRules.Overcomes(fall, tuning.staggerForce), "The fall staggers a Guardian within its reach.");

            var player = Make(scaffold);
            Hit(player, Pulse(1f), Aegis);
            Assert.IsTrue(player.Meets(PropertyCondition.Collapsed), "The player can bring it down on the Guardian with a full Force Pulse.");
        }

        // ============================================================= final: the slag plug

        [Test]
        public void Plug_ColdForceBarelyMarksIt_HeatedAFullPulseShattersIt_AndTheWindowIsLongEnough()
        {
            MagicalPropertySet cold = Make(ExampleObject.HeatSensitiveStone);
            Hit(cold, Pulse(1f), Aegis);
            Assert.Greater(cold.Structural.Fraction, 0.95f);

            MagicalPropertySet heated = Make(ExampleObject.HeatSensitiveStone);
            Hit(heated, Wave(1f), Flame);
            float charge = DisciplineDefaults.Charged(MagicDiscipline.Aegis).Charge.fullChargeSeconds;
            Run(heated, charge + 1f);
            Assert.IsTrue(heated.Meets(PropertyCondition.Heated), "Still hot after switching to Aegis and charging a full pulse, with a second to spare.");
            Hit(heated, Pulse(1f), Aegis);
            Assert.IsTrue(heated.IsDestroyed, "Shattered: the culvert is open.");

            PersistedState kept = WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone).persisted;
            MagicalPropertySet loaded = Make(ExampleObject.HeatSensitiveStone);
            loaded.Load(heated.Capture(kept), kept);
            Assert.IsTrue(loaded.IsDestroyed, "Open for good.");
        }

        [Test]
        public void Plug_IsTheSameStoneAsTheGuardiansPlates_SoTheCombatTeachesTheFinalSolution()
        {
            StructuralSettings plug = WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone).structuralSettings;
            StructuralSettings plates = EnemyArchetypes.GuardianArmour().structure;
            Assert.AreEqual(plug.brittleness, plates.brittleness);
            Assert.AreEqual(plug.heatedBrittleness, plates.heatedBrittleness);
            Assert.AreEqual(plug.forceDamage, plates.forceDamage);
        }

        [Test]
        public void Counterweight_TheSameMechanismAsTheLift_SlidToItsStopOpensTheGateForGood()
        {
            MagicalPropertySet weight = Make(CombinationExample.RuneLockedSlide);
            bool gateWasOpen = false;
            Hit(weight, Arrow(0f), Arcane);
            Hit(weight, Arrow(0f), Arcane);
            Assert.IsTrue(weight.Meets(PropertyCondition.Activated), "Two uncharged arrows in quick succession light it too.");
            Hit(weight, Pulse(1f), Aegis);
            Run(weight, 2f);
            Hit(weight, Pulse(1f), Aegis);
            Run(weight, 2f);

            // The gate's input is a latched trigger on "at its far stop" (PropertyStateTrigger.Decide).
            gateWasOpen = PropertyStateTrigger.Decide(weight.Meets(PropertyCondition.AtTravelLimit), false, true);
            Assert.IsTrue(gateWasOpen);
            Run(weight, 30f);
            Assert.IsTrue(PropertyStateTrigger.Decide(weight.Meets(PropertyCondition.AtTravelLimit), gateWasOpen, true), "It stays open.");
        }

        // ============================================================= save, reload, temporary states

        [Test]
        public void Reload_LastingChangesStay_TemporaryOnesAreReset_EveryObjectOfTheLevel()
        {
            var objects = new (string Name, MagicalPropertySettings Settings)[]
            {
                ("crystal", FoundryDefaults.Settings(FoundryObject.DormantCrystal)),
                ("barricade", WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade)),
                ("lift", CombinationDefaults.Settings(CombinationExample.RuneLockedSlide)),
                ("furnace", FoundryDefaults.Settings(FoundryObject.Furnace)),
                ("plug", WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone)),
                ("gantry", FoundryDefaults.Settings(FoundryObject.CrumblingGantry)),
            };

            var sets = objects.Select(o => Make(o.Settings)).ToArray();
            Hit(sets[0], Arrow(1f), Arcane);                         // crystal awake: lasting
            Hit(sets[1], Pulse(1f), Aegis);                          // barricade cracked: damage is not kept
            Hit(sets[2], Arrow(1f), Arcane);
            Hit(sets[2], Pulse(1f), Aegis);
            Run(sets[2], 2f);                                        // lift moved 4 m: position is kept
            Hit(sets[3], Wave(1f), Flame);                           // furnace hot: temporary
            Hit(sets[4], Wave(1f), Flame);                           // plug heated, not broken: temporary
            for (int i = 0; i < 4; i++) { Hit(sets[5], Guard(), Aegis); }   // gantry braced: temporary

            for (int i = 0; i < sets.Length; i++)
            {
                PersistedState kept = objects[i].Settings.persisted;
                ObjectRecord record = sets[i].Capture(kept);
                sets[i] = Make(objects[i].Settings);
                sets[i].Load(record, kept);
            }

            Assert.IsTrue(sets[0].Meets(PropertyCondition.Activated), "The crystal stays awake.");
            Assert.AreEqual(4f, sets[2].Kinetic.Travel(Vector3.forward), 0.01f, "The lift stays where it was pushed.");
            Assert.IsFalse(sets[3].Meets(PropertyCondition.Heated), "The furnace's heat is gone.");
            Assert.IsFalse(sets[4].Meets(PropertyCondition.Heated), "The plug's heat is gone, and it is whole.");
            Assert.IsFalse(sets[4].IsDestroyed);
            Assert.IsFalse(sets[5].Meets(PropertyCondition.Braced), "The brace is gone.");
            Assert.IsFalse(sets[1].IsDestroyed);
        }

        [Test]
        public void Reload_TheLevelsSavedStateSurvivesTheSaveFile()
        {
            var store = new WorldStateStore();
            MagicalPropertySet crystal = Make(FoundryObject.DormantCrystal);
            Hit(crystal, Arrow(1f), Arcane);
            store.SetObject(FoundryLayout.KeyPrefix + "Dormant Crystal", crystal.Capture(PersistedState.Activation));
            MagicalPropertySet lift = Make(CombinationExample.RuneLockedSlide);
            Hit(lift, Arrow(1f), Arcane);
            Hit(lift, Pulse(1f), Aegis);
            Run(lift, 2f);
            store.SetObject(FoundryLayout.KeyPrefix + "Rune Lift", lift.Capture(PersistedState.Position));
            store.SetPuzzle(CheckpointProgress.KeyFor(FoundryLayout.KeyPrefix, 2), new PuzzleRecord(true, true));
            store.SetPuzzle(FoundryLayout.KeyPrefix + "Guardian Defeated", new PuzzleRecord(true, true));

            var read = new WorldStateStore();
            WorldStateText.Read(WorldStateText.Write(store), read);
            Assert.AreEqual(WorldStateText.Write(store), WorldStateText.Write(read), "What was written is what is read.");

            Assert.IsTrue(read.TryGetObject(FoundryLayout.KeyPrefix + "Rune Lift", out ObjectRecord record));
            MagicalPropertySet loaded = Make(CombinationExample.RuneLockedSlide);
            loaded.Load(record, PersistedState.Position);
            Assert.AreEqual(4f, loaded.Kinetic.Travel(Vector3.forward), 0.01f);
            Assert.IsTrue(read.TryGetPuzzle(FoundryLayout.KeyPrefix + "Guardian Defeated", out PuzzleRecord guardian) && guardian.Locked);
        }

        [Test]
        public void Forget_OneLevelsStateIsForgotten_OtherLevelsAreKept_AndTheCheckpointIsDropped()
        {
            WorldState.Current.SetPuzzle("SealedGateway/Sequence", new PuzzleRecord(true, true));
            WorldState.Current.SetPuzzle(CheckpointProgress.KeyFor(FoundryLayout.KeyPrefix, 3), new PuzzleRecord(true, true));
            WorldState.Current.SetObject(FoundryLayout.KeyPrefix + "Slag Plug",
                new ObjectRecord { Saved = PersistedState.Destruction, Integrity = 0f });
            WorldState.Current.SetObject("DisciplineRange/Reinforced Door", new ObjectRecord { Saved = PersistedState.Activation, Activated = true });
            WorldState.SaveCheckpoint();

            var reloads = new List<WorldReload>();
            WorldState.Reloaded += reloads.Add;
            try
            {
                Assert.AreEqual(2, WorldState.ForgetScope(FoundryLayout.KeyPrefix));
            }
            finally
            {
                WorldState.Reloaded -= reloads.Add;
            }

            CollectionAssert.AreEqual(new[] { WorldReload.StateReplaced }, reloads, "The world is told, so the level puts itself back.");
            Assert.IsFalse(WorldState.Current.TryGetObject(FoundryLayout.KeyPrefix + "Slag Plug", out _));
            Assert.IsTrue(WorldState.Current.TryGetPuzzle("SealedGateway/Sequence", out _), "Other levels keep what they saved.");
            Assert.IsTrue(WorldState.Current.TryGetObject("DisciplineRange/Reinforced Door", out _));
            Assert.IsFalse(WorldState.HasCheckpoint, "A checkpoint taken before may hold what was forgotten.");
            Assert.AreEqual(0, WorldState.ForgetScope(string.Empty), "An empty prefix forgets nothing.");
        }

        [Test]
        public void Keys_EverythingTheLevelSavesIsUnderItsPrefix_SoAReplayForgetsAllOfIt()
        {
            // The keys the setup gives the gates' triggers and the Guardian's defeat, and the checkpoints' keys.
            var keys = new List<string>
            {
                Threefold.EditorTools.BrokenFoundrySetup.ForecourtGateKey,
                Threefold.EditorTools.BrokenFoundrySetup.ExitGateKey,
                Threefold.EditorTools.BrokenFoundrySetup.GuardianKey,
            };
            keys.AddRange(FoundryLayout.Checkpoints.Select(c => CheckpointProgress.KeyFor(FoundryLayout.KeyPrefix, c.Order)));

            foreach (string key in keys)
            {
                StringAssert.StartsWith(FoundryLayout.KeyPrefix, key);
            }

            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "Every key is different.");

            // Persistent ids are "<scene>/<object>" (MagicalObjectSetup.MakePersistentId); the scene is saved under this name.
            StringAssert.EndsWith(FoundryLayout.SceneName + ".unity", Threefold.EditorTools.BrokenFoundrySetup.ScenePath);
            Assert.AreEqual(FoundryLayout.SceneName + "/", FoundryLayout.KeyPrefix);

            // And forgetting the prefix takes all of them, and nothing else.
            foreach (string key in keys)
            {
                WorldState.Current.SetPuzzle(key, new PuzzleRecord(true, true));
            }

            WorldState.Current.SetPuzzle("CombatArena/Something", new PuzzleRecord(true, true));
            Assert.AreEqual(keys.Count, WorldState.ForgetScope(FoundryLayout.KeyPrefix));
            Assert.AreEqual(1, WorldState.Current.PuzzleCount);
        }

        // ============================================================= checkpoints and the hazard

        [Test]
        public void Checkpoints_TheFurthestReachedIsCurrent_AndWalkingBackDoesNotMoveIt()
        {
            var progress = new CheckpointProgress(6);
            Assert.IsFalse(progress.HasCurrent);
            Assert.IsTrue(progress.Reach(0));
            Assert.IsTrue(progress.Reach(2));
            Assert.IsFalse(progress.Reach(1), "An earlier checkpoint does not move the player back.");
            Assert.AreEqual(2, progress.Current);
            Assert.IsTrue(progress.IsReached(1));
            Assert.IsFalse(progress.Reach(7));
            Assert.IsFalse(progress.Reach(-1));
        }

        [Test]
        public void Checkpoints_ComeBackFromWhatIsRemembered_AndAreForgottenWithIt()
        {
            var remembered = new HashSet<int> { 0, 1, 3 };
            var progress = new CheckpointProgress(6);
            progress.Restore(order => remembered.Contains(order));
            Assert.AreEqual(3, progress.Current);

            progress.Restore(order => false);
            Assert.IsFalse(progress.HasCurrent, "After a replay the level starts from its beginning.");

            progress.Reach(4);
            progress.Clear();
            Assert.AreEqual(-1, progress.Current);
        }

        [Test]
        public void Checkpoints_TheBoxTest()
        {
            var half = new Vector3(1f, 1.5f, 0.5f);
            Assert.IsTrue(CheckpointProgress.Contains(Vector3.zero, half));
            Assert.IsTrue(CheckpointProgress.Contains(new Vector3(1f, -1.5f, 0.5f), half), "Edges count.");
            Assert.IsFalse(CheckpointProgress.Contains(new Vector3(1.01f, 0f, 0f), half));
        }

        [Test]
        public void Hazard_PutsThePlayerBackOnlyWhileStanding_OnceAFall()
        {
            Assert.IsTrue(HazardRules.ReturnsPlayer(75f));
            Assert.IsFalse(HazardRules.ReturnsPlayer(0f), "Defeated: the usual defeat and restart follow.");
            Assert.IsTrue(HazardRules.Ready(10f, float.NegativeInfinity, 1f));
            Assert.IsFalse(HazardRules.Ready(10.5f, 10f, 1f));
            Assert.IsTrue(HazardRules.Ready(11f, 10f, 1f));
            const float mageHealth = 100f; // What CombatSetup gives the Mage.
            Assert.AreEqual(4, Mathf.CeilToInt(mageHealth / FoundryLayout.SlagDamage), "The fourth fall defeats a Mage at full health.");
        }

        // ============================================================= diagnostics

        [Test]
        public void Diagnostics_FrameTiming_AverageWorstAndSlowestFrames()
        {
            var stats = new FrameTimingStats(100);
            Assert.AreEqual(0, stats.Summarize().Samples);
            for (int i = 0; i < 95; i++)
            {
                stats.Add(1f / 60f);
            }

            for (int i = 0; i < 5; i++)
            {
                stats.Add(0.05f);
            }

            stats.Add(0f);
            stats.Add(float.NaN);
            stats.Add(-1f);

            FrameTimingSummary summary = stats.Summarize();
            Assert.AreEqual(100, summary.Samples, "Invalid frame times are ignored.");
            Assert.AreEqual(50f, summary.WorstMs, 0.01f);
            Assert.AreEqual(50f, summary.SlowestMs, 0.01f, "The slowest twentieth: the five 50 ms frames.");
            Assert.AreEqual((95f * 1000f / 60f + 5f * 50f) / 100f, summary.AverageMs, 0.01f);

            for (int i = 0; i < 100; i++)
            {
                stats.Add(1f / 30f);
            }

            Assert.AreEqual(30f, stats.Summarize().AverageFps, 0.1f, "The window keeps only the latest frames.");
        }

        [Test]
        public void Diagnostics_DescribeProperties_AndOnlyTheTemporaryStatesThatRun()
        {
            Assert.AreEqual("thermal; structural", MagicalStateText.Properties(WorldMagicDefaults.Settings(ExampleObject.HeatSensitiveStone)));

            MagicalPropertySet furnace = Make(FoundryObject.Furnace);
            Assert.AreEqual(string.Empty, MagicalStateText.Temporary(furnace), "At rest nothing temporary runs.");
            Hit(furnace, Wave(1f), Flame);
            StringAssert.Contains("heat 80 (Heated)", MagicalStateText.Temporary(furnace));
            StringAssert.Contains("heat 80/120 Heated", MagicalStateText.State(furnace));

            MagicalPropertySet gantry = Make(FoundryObject.CrumblingGantry);
            Hit(gantry, Guard(), Aegis);
            StringAssert.Contains("braced", MagicalStateText.Temporary(gantry));
            gantry = Make(FoundryObject.CrumblingGantry);
            Hit(gantry, Pulse(1f), Aegis);
            StringAssert.Contains("stands again in", MagicalStateText.Temporary(gantry));
            StringAssert.DoesNotContain("stress", MagicalStateText.Temporary(gantry), "Stress means nothing for what is down.");

            MagicalPropertySet plug = Make(ExampleObject.HeatSensitiveStone);
            Hit(plug, Wave(1f), Flame);
            Hit(plug, Pulse(1f), Aegis);
            Assert.AreEqual(string.Empty, MagicalStateText.Temporary(plug), "What is destroyed runs nothing.");
        }

        // ============================================================= the new definitions

        [Test]
        public void Definitions_AreValid_AndNoneAnswersEveryDiscipline()
        {
            for (int i = 0; i < FoundryDefaults.Count; i++)
            {
                var kind = (FoundryObject)i;
                var problems = new List<string>();
                Assert.AreEqual(0, MagicalObjectRules.Validate(FoundryDefaults.Settings(kind), problems), $"{kind}: {string.Join("; ", problems)}");
            }

            // Charge, heat, force, brace: each object answers some kinds of effect and not others.
            var kinds = new[] { MagicEffectKind.ArcaneCharge, MagicEffectKind.Heat, MagicEffectKind.Force, MagicEffectKind.Stabilize };
            for (int i = 0; i < FoundryDefaults.Count; i++)
            {
                MagicalPropertySet set = Make((FoundryObject)i);
                int answered = kinds.Count(kind => set.CouldRespondTo(kind));
                Assert.That(answered, Is.InRange(1, 3), $"{(FoundryObject)i} answers {answered} kinds of effect.");
            }
        }
    }
}
