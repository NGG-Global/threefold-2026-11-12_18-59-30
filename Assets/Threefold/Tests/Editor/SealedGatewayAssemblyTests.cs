using NUnit.Framework;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.EditorTools;
using Threefold.Interaction;
using Threefold.Puzzles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// Builds the Fantasy Playground with the Sealed Gateway from code, then checks the assembled
    /// puzzle: its references, that its detection zones sit where the player stands, that a bolt
    /// from the lookout can reach the crystal, that the seal blocks the way until the puzzle is
    /// solved, and finally solves it with the real Mage.
    ///
    /// Assembling the scene replaces whatever scene is open without saving it, so the tests only
    /// run in batch mode (on a build server or from the command line), never in an open Editor.
    /// </summary>
    public sealed class SealedGatewayAssemblyTests
    {
        private Scene scene;
        private PuzzleSite site;
        private ThirdPersonMotor mage;
        private PresenceZone lookout;
        private SpellActivator crystal;
        private PressurePlate plate;
        private TimedMechanism timer;
        private PuzzleSequence sequence;
        private PuzzleDoor seal;

        [SetUp]
        public void SetUp()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            PuzzleSessionState.Clear();
            scene = FantasyPlaygroundSetup.BuildForAutomation();
            site = SealedGatewaySetup.FindSite(scene);
            Assert.IsNotNull(site, "The playground has no Sealed Gateway puzzle.");

            mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene);
            lookout = site.transform.Find("Lookout").GetComponent<PresenceZone>();
            crystal = site.GetComponentInChildren<SpellActivator>(true);
            plate = site.GetComponentInChildren<PressurePlate>(true);
            timer = site.GetComponentInChildren<TimedMechanism>(true);
            sequence = site.GetComponentInChildren<PuzzleSequence>(true);
            seal = site.GetComponentInChildren<PuzzleDoor>(true);
        }

        [TearDown]
        public void TearDown()
        {
            PuzzleSessionState.Clear();
        }

        private Vector3 LookoutTop => site.transform.TransformPoint(SealedGatewaySetup.LookoutPosition + Vector3.up * SealedGatewaySetup.LookoutHeight);
        private Vector3 PlateTop => site.transform.TransformPoint(SealedGatewaySetup.PlatePosition + Vector3.up * 0.08f);

        private void PlaceMage(Vector3 position)
        {
            mage.Teleport(position + Vector3.up * 0.02f, Quaternion.identity);
            Physics.SyncTransforms();
        }

        private void Step(float deltaTime)
        {
            lookout.Tick(deltaTime);
            plate.Tick(deltaTime);
            timer.Tick(deltaTime);
            sequence.Evaluate();
            seal.Tick(deltaTime);
        }

        [Test]
        public void PuzzleIsConnected()
        {
            Assert.AreEqual(3, sequence.StepCount);
            Assert.AreSame(lookout, sequence.Steps[0].element);
            Assert.AreSame(crystal, sequence.Steps[1].element);
            Assert.AreSame(plate, sequence.Steps[2].element);
            Assert.AreSame(lookout, crystal.ReceptiveWhile);
            Assert.AreSame(crystal, timer.Trigger);
            Assert.Contains(crystal, (System.Collections.ICollection)timer.ResetWhenExpired);
            Assert.AreSame(sequence, seal.InputElement);
            Assert.IsNotNull(seal.Leaf.target);
            Assert.IsTrue(crystal.AcceptedElements.HasFlag(SpellElement.Arcane));
            Assert.IsNotNull(mage.GetComponent<Interactor>(), "The Mage cannot interact.");
        }

        [Test]
        public void BoltFromTheLookoutReachesTheCrystal()
        {
            PlaceMage(LookoutTop);

            // The Mage casts from 1.25 m above the feet (Ability Caster's default cast offset).
            Vector3 origin = mage.transform.position + Vector3.up * 1.25f;
            Vector3 target = crystal.AimPoint;
            Vector3 direction = target - origin;

            Assert.IsTrue(Physics.SphereCast(origin, 0.18f, direction.normalized, out RaycastHit hit, direction.magnitude + 1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
            Assert.IsNotNull(hit.collider.GetComponentInParent<SpellActivator>(),
                $"A bolt from the lookout hits '{hit.collider.name}' before the crystal.");
        }

        [Test]
        public void LookoutAndPlateDetectTheMage()
        {
            PlaceMage(LookoutTop);
            Step(0.5f);
            Assert.IsTrue(lookout.IsActive, "The Mage standing on the lookout is not detected.");
            Assert.IsFalse(plate.IsActive);

            PlaceMage(PlateTop);
            Step(0.5f);
            Assert.IsFalse(lookout.IsActive);
            Assert.IsTrue(plate.IsActive, "The Mage standing on the plate is not detected.");
        }

        [Test]
        public void StandingOnTheCrateDoesNotCountAsTheLookout()
        {
            // At the crate's edge nearest the lookout, where the Mage's capsule touches the lookout's side.
            Vector3 crateTop = site.transform.TransformPoint(new Vector3(-9.7f + 0.35f, 1.1f, -1.8f));
            PlaceMage(crateTop);
            Step(0.5f);

            Assert.IsFalse(lookout.IsActive);
        }

        [Test]
        public void SealBlocksTheGatewayUntilThePuzzleIsSolved()
        {
            Vector3 front = site.transform.TransformPoint(new Vector3(0f, 1.3f, 1.5f));
            Vector3 inside = site.transform.TransformPoint(new Vector3(0f, 1.3f, -1.5f));

            Assert.IsTrue(Physics.Linecast(front, inside, out RaycastHit blocked, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore));
            Assert.IsTrue(blocked.transform.IsChildOf(seal.transform), $"Expected the seal, found '{blocked.collider.name}'.");

            SolveWithTheMage();
            for (int i = 0; i < 150; i++)
            {
                Step(1f / 30f);
            }

            Physics.SyncTransforms();
            Assert.IsTrue(seal.IsFullyOpen);
            Assert.IsFalse(Physics.Linecast(front, inside, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                "Something still blocks the open gateway.");
        }

        [Test]
        public void CrystalStruckFromTheGroundFizzles()
        {
            PlaceMage(PlateTop);
            Step(0.5f);
            crystal.ReceiveSpell(new SpellImpact(SpellElement.Arcane, crystal.AimPoint, Vector3.up, mage.gameObject));

            Assert.IsFalse(crystal.IsActive);
            Assert.AreEqual(SpellResponse.RejectedDormant, crystal.LastResponse);
        }

        [Test]
        public void ReliquaryCanBeOpenedOnceTheGatewayIsOpen()
        {
            InteractiveSwitch reliquary = site.GetComponentInChildren<InteractiveSwitch>(true);
            Assert.IsNotNull(reliquary);
            Assert.AreEqual("Open", reliquary.InteractionVerb);

            SolveWithTheMage();
            Assert.AreEqual(SwitchResponse.TurnedOn, reliquary.Use());
            Assert.IsTrue(reliquary.IsActive);
        }

        private void SolveWithTheMage()
        {
            PlaceMage(LookoutTop);
            Step(0.5f);
            crystal.ReceiveSpell(new SpellImpact(SpellElement.Arcane, crystal.AimPoint, Vector3.up, mage.gameObject));
            Step(0.1f);
            Assert.AreEqual(2, sequence.CompletedSteps);

            PlaceMage(PlateTop);
            Step(0.5f);
            Assert.IsTrue(sequence.IsSolved, "Lookout, crystal and plate did not solve the puzzle.");
        }
    }
}
