using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The puzzle components working together, built the way the Sealed Gateway is: a lookout
    /// zone, a crystal that listens only while the player is on the lookout, a timer the crystal
    /// starts, a pressure plate, a sequence of the three and a door that follows the sequence.
    ///
    /// Presence is detected with real physics queries against a stand-in player (a
    /// CharacterController with a Third Person Motor). Edit Mode runs no Update, so the tests call
    /// each component's Tick and Evaluate in the order a frame would.
    /// </summary>
    public sealed class PuzzleComponentTests
    {
        private const float Frame = 1f / 30f;

        private static readonly Vector3 LookoutTop = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 PlateSpot = new Vector3(10f, 0f, 0f);
        private static readonly Vector3 Elsewhere = new Vector3(-10f, 0f, 0f);

        private readonly List<GameObject> created = new List<GameObject>();
        private GameObject player;
        private PresenceZone lookout;
        private SpellActivator crystal;
        private PressurePlate plate;
        private TimedMechanism timer;
        private PuzzleSequence sequence;
        private PuzzleDoor door;
        private Transform leaf;

        [SetUp]
        public void SetUp()
        {
            PuzzleSessionState.Clear();

            player = Create("Player", Elsewhere);
            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            player.AddComponent<ThirdPersonMotor>();

            lookout = Create("Lookout", LookoutTop).AddComponent<PresenceZone>();
            lookout.ConfigureDetection(new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), 0f, 0f);
            lookout.ConfigureFilter(true, false, false, 0f);

            plate = Create("Plate", PlateSpot).AddComponent<PressurePlate>();
            plate.ConfigureDetection(new Vector3(0f, 0.5f, 0f), new Vector3(1.3f, 1f, 1.3f), 0f, 0f);
            plate.ConfigureFilter(true, false, false, 0f);

            crystal = Create("Crystal", new Vector3(5f, 5f, 0f)).AddComponent<SpellActivator>();
            crystal.AcceptedElements = SpellElement.Arcane;
            crystal.ReceptiveWhile = lookout;

            GameObject mechanism = Create("Mechanism", Vector3.zero);
            sequence = mechanism.AddComponent<PuzzleSequence>();
            timer = mechanism.AddComponent<TimedMechanism>();
            timer.Trigger = crystal;
            timer.Duration = 6f;
            timer.AddResetTarget(crystal);
            sequence.SetSteps(new[]
            {
                new SequenceStep(lookout, StepRule.HoldUntilNextStep),
                new SequenceStep(crystal, StepRule.HoldUntilNextStep),
                new SequenceStep(plate, StepRule.Latch),
            });
            sequence.AddLockWhenSolved(timer);

            door = Create("Door", new Vector3(5f, 0f, 0f)).AddComponent<PuzzleDoor>();
            leaf = Create("Leaf", Vector3.zero).transform;
            leaf.SetParent(door.transform, false);
            door.ConfigureMotion(leaf, new Vector3(0f, -3f, 0f), Vector3.zero, 2f, 1f);
            door.InputElement = sequence;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in created)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            created.Clear();
            PuzzleSessionState.Clear();
        }

        private GameObject Create(string name, Vector3 position)
        {
            var item = new GameObject(name);
            item.transform.position = position;
            created.Add(item);
            return item;
        }

        private void MovePlayer(Vector3 position)
        {
            player.transform.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>One frame of the puzzle, in the order the components would update.</summary>
        private void Step(float deltaTime = Frame)
        {
            lookout.Tick(deltaTime);
            plate.Tick(deltaTime);
            timer.Tick(deltaTime);
            sequence.Evaluate();
            door.Tick(deltaTime);
        }

        private void StepFor(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
            {
                Step();
            }
        }

        private void StrikeCrystal(SpellElement element = SpellElement.Arcane)
        {
            crystal.ReceiveSpell(new SpellImpact(element, crystal.transform.position, Vector3.forward, player));
        }

        private void Solve()
        {
            MovePlayer(LookoutTop);
            Step();
            StrikeCrystal();
            Step();
            MovePlayer(PlateSpot);
            Step();
        }

        [Test]
        public void PlayerIsDetectedOnlyInsideTheZone()
        {
            MovePlayer(LookoutTop);
            Step();
            Assert.IsTrue(lookout.IsActive);

            MovePlayer(Elsewhere);
            Step();
            Assert.IsFalse(lookout.IsActive);
        }

        [Test]
        public void GatewayOpensAfterTheThreeActionsInOrder()
        {
            MovePlayer(LookoutTop);
            Step();
            Assert.AreEqual(1, sequence.CompletedSteps, "Reaching the lookout is the first step.");

            StrikeCrystal();
            Step();
            Assert.IsTrue(crystal.IsActive);
            Assert.IsTrue(timer.IsRunning, "Lighting the crystal starts the timer.");
            Assert.AreEqual(2, sequence.CompletedSteps);

            MovePlayer(PlateSpot);
            StepFor(1f);
            Assert.IsTrue(plate.IsActive);
            Assert.IsTrue(sequence.IsSolved);
            Assert.IsTrue(door.IsActive);

            StepFor(2.5f);
            Assert.IsTrue(door.IsFullyOpen);
            Assert.AreEqual(-3f, leaf.localPosition.y, 0.001f, "The seal has sunk out of the way.");
        }

        [Test]
        public void CrystalFizzlesUnlessThePlayerIsOnTheLookout()
        {
            int rejections = 0;
            crystal.Rejected += element => rejections++;

            MovePlayer(PlateSpot);
            Step();
            StrikeCrystal();

            Assert.IsFalse(crystal.IsActive);
            Assert.AreEqual(SpellResponse.RejectedDormant, crystal.LastResponse);
            Assert.AreEqual(1, rejections);
        }

        [Test]
        public void CrystalRejectsTheWrongKindOfMagic()
        {
            MovePlayer(LookoutTop);
            Step();
            StrikeCrystal(SpellElement.Fire);

            Assert.IsFalse(crystal.IsActive);
            Assert.AreEqual(SpellResponse.RejectedElement, crystal.LastResponse);
        }

        [Test]
        public void TooSlowLetsTheCrystalFadeAndTheGatewayStaysShut()
        {
            MovePlayer(LookoutTop);
            Step();
            StrikeCrystal();
            Step();
            MovePlayer(Elsewhere);

            StepFor(6.5f);

            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(crystal.IsActive, "The timer puts the crystal out when it runs out.");
            Assert.AreEqual(0, sequence.CompletedSteps);

            MovePlayer(PlateSpot);
            StepFor(0.5f);
            Assert.IsTrue(plate.IsActive);
            Assert.IsFalse(sequence.IsSolved);
            Assert.IsFalse(door.IsActive);
        }

        [Test]
        public void PlateBeforeCrystalDoesNothing()
        {
            MovePlayer(PlateSpot);
            StepFor(0.5f);

            Assert.IsTrue(plate.IsActive);
            Assert.AreEqual(0, sequence.CompletedSteps);
            Assert.IsFalse(door.IsActive);
        }

        [Test]
        public void SolvedPuzzleKeepsItsOutcome()
        {
            Solve();
            Assert.IsTrue(sequence.IsSolved);

            MovePlayer(Elsewhere);
            StepFor(8f);

            Assert.IsTrue(sequence.IsLocked);
            Assert.IsTrue(crystal.IsActive && crystal.IsLocked, "The crystal stays lit.");
            Assert.IsTrue(timer.IsActive && timer.IsLocked && !timer.IsRunning, "The timer stops and stays complete.");
            Assert.IsTrue(plate.IsActive, "The plate stays down.");
            Assert.IsTrue(door.IsFullyOpen);
        }

        [Test]
        public void SolvedStateIsRememberedForTheRestOfTheSession()
        {
            sequence.PersistenceKey = "Test.Gateway.Sequence";
            crystal.PersistenceKey = "Test.Gateway.Crystal";
            Solve();

            // The scene is loaded again: new objects with the same keys.
            PuzzleSequence reloaded = Create("ReloadedMechanism", Vector3.zero).AddComponent<PuzzleSequence>();
            reloaded.PersistenceKey = "Test.Gateway.Sequence";
            SpellActivator reloadedCrystal = Create("ReloadedCrystal", Vector3.zero).AddComponent<SpellActivator>();
            reloadedCrystal.PersistenceKey = "Test.Gateway.Crystal";

            Assert.IsTrue(reloaded.RestoreFromSession());
            Assert.IsTrue(reloaded.IsSolved && reloaded.IsLocked);
            Assert.IsTrue(reloadedCrystal.RestoreFromSession());
            Assert.IsTrue(reloadedCrystal.IsActive && reloadedCrystal.IsLocked);

            PuzzleDoor reloadedDoor = Create("ReloadedDoor", Vector3.zero).AddComponent<PuzzleDoor>();
            reloadedDoor.InputElement = reloaded;
            reloadedDoor.Evaluate(true);
            Assert.IsTrue(reloadedDoor.IsFullyOpen, "A gateway opened earlier appears open at once.");
        }

        [Test]
        public void MomentaryStatesAreNotRemembered()
        {
            crystal.PersistenceKey = "Test.Gateway.Crystal";
            MovePlayer(LookoutTop);
            Step();
            StrikeCrystal();

            Assert.IsFalse(PuzzleSessionState.TryGet("Test.Gateway.Crystal", out _),
                "A lit crystal whose timer is still running is not a lasting outcome.");
        }

        [Test]
        public void OneShotChestOpensOnceAndMovesItsLid()
        {
            InteractiveSwitch chest = Create("Chest", Vector3.zero).AddComponent<InteractiveSwitch>();
            Transform lid = Create("Lid", new Vector3(0f, 0.66f, 0f)).transform;
            lid.SetParent(chest.transform, true);
            chest.Configure(SwitchMode.OneShot, "Open", Vector3.up, lid, new Vector3(0f, 0f, -0.5f), Vector3.zero, 0.5f);

            Assert.IsTrue(chest.CanInteract(player));
            Assert.AreEqual("Open", chest.InteractionVerb);
            Assert.AreEqual(SwitchResponse.TurnedOn, chest.Use());
            Assert.IsFalse(chest.CanInteract(player), "An opened chest is no longer offered.");
            Assert.AreEqual(SwitchResponse.Ignored, chest.Use());

            for (int i = 0; i < 30; i++)
            {
                chest.Tick(Frame);
            }

            Assert.AreEqual(-0.5f, lid.localPosition.z, 0.001f);
        }

        [Test]
        public void SwitchRefusesUntilItsConditionIsMet()
        {
            InteractiveSwitch lever = Create("Lever", Vector3.zero).AddComponent<InteractiveSwitch>();
            lever.Requires = lookout;
            int rejections = 0;
            lever.Rejected += element => rejections++;

            Assert.AreEqual(SwitchResponse.Rejected, lever.Use());
            Assert.AreEqual(1, rejections);

            MovePlayer(LookoutTop);
            Step();
            Assert.AreEqual(SwitchResponse.TurnedOn, lever.Use());
            Assert.AreEqual(SwitchResponse.TurnedOff, lever.Use());
        }

        [Test]
        public void PlayerCanOpenAndCloseAnInteractiveDoor()
        {
            PuzzleDoor handDoor = Create("HandDoor", Vector3.zero).AddComponent<PuzzleDoor>();
            Transform handLeaf = Create("HandLeaf", Vector3.zero).transform;
            handLeaf.SetParent(handDoor.transform, false);
            handDoor.ConfigureMotion(handLeaf, Vector3.zero, new Vector3(0f, 90f, 0f), 0.5f, 0.5f);
            handDoor.Control = DoorControl.Interact;

            Assert.AreEqual("Open", handDoor.InteractionVerb);
            handDoor.Interact(player);
            Assert.IsTrue(handDoor.IsActive);
            Assert.AreEqual("Close", handDoor.InteractionVerb);

            for (int i = 0; i < 30; i++)
            {
                handDoor.Tick(Frame);
            }

            Assert.AreEqual(90f, handLeaf.localEulerAngles.y, 0.5f);

            handDoor.Interact(player);
            Assert.IsFalse(handDoor.IsActive);
        }

        [Test]
        public void LogicGateNeedsAllInputs()
        {
            InteractiveSwitch first = Create("First", Vector3.zero).AddComponent<InteractiveSwitch>();
            InteractiveSwitch second = Create("Second", Vector3.zero).AddComponent<InteractiveSwitch>();
            PuzzleLogicGate gate = Create("Gate", Vector3.zero).AddComponent<PuzzleLogicGate>();
            gate.Mode = GateMode.All;
            gate.SetInputs(new PuzzleElement[] { first, second });

            first.Use();
            gate.Evaluate();
            Assert.IsFalse(gate.IsActive);

            second.Use();
            gate.Evaluate();
            Assert.IsTrue(gate.IsActive);
        }

        [Test]
        public void WrongOrderCanResetTheSequence()
        {
            InteractiveSwitch first = Create("RuneA", Vector3.zero).AddComponent<InteractiveSwitch>();
            InteractiveSwitch second = Create("RuneB", Vector3.zero).AddComponent<InteractiveSwitch>();
            InteractiveSwitch third = Create("RuneC", Vector3.zero).AddComponent<InteractiveSwitch>();
            PuzzleSequence runes = Create("Runes", Vector3.zero).AddComponent<PuzzleSequence>();
            runes.SetSteps(new[]
            {
                new SequenceStep(first, StepRule.Latch),
                new SequenceStep(second, StepRule.Latch),
                new SequenceStep(third, StepRule.Latch),
            });
            runes.ResetOnWrongOrder = true;
            int lost = 0;
            runes.ProgressLost += remaining => lost++;

            first.Use();
            runes.Evaluate();
            Assert.AreEqual(1, runes.CompletedSteps);

            third.Use();
            runes.Evaluate();
            Assert.AreEqual(0, runes.CompletedSteps, "Touching the third rune before the second starts over.");
            Assert.AreEqual(1, lost);
        }
    }
}
