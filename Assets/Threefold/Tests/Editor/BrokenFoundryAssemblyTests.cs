using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.EditorTools;
using Threefold.Levels;
using Threefold.Objectives;
using Threefold.Persistence;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// Sprint LM-07, needing Unity. Two tests build the Broken Foundry from code and check the result:
    /// it validates, every part is in place and connected, the Guardian is on its navigation mesh and
    /// cannot leave the casting floor, every respawn point stands on solid ground, and a second run adds
    /// nothing. Those run only in batch mode, because they replace the open scene without saving it.
    /// The others run in any Edit Mode session: the traversal figures the layout is checked against are
    /// the components' own defaults, an objective completes on a goal, and a checkpoint's box.
    /// </summary>
    public sealed class BrokenFoundryAssemblyTests
    {
        [Test]
        public void FoundryIsCompleteConnectedAndValid()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = BrokenFoundrySetup.BuildForAutomation();

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());

            BrokenFoundrySetup.Gameplay gameplay = BrokenFoundrySetup.Find(scene);
            var levelProblems = new List<string>();
            Assert.AreEqual(0, BrokenFoundrySetup.CheckLevel(gameplay, levelProblems.Add), string.Join("\n", levelProblems));
            Assert.AreEqual(FoundryLayout.Checkpoints.Length, gameplay.Checkpoints.Count);
            Assert.AreEqual(FoundryLayout.Checkpoints.Length, gameplay.Route.CheckpointCount);
            Assert.AreEqual(1, gameplay.Route.HazardCount);
            Assert.IsNotNull(gameplay.Diagnostics);

            DefeatTargetsObjective objective = Object.FindAnyObjectByType<DefeatTargetsObjective>();
            Assert.AreEqual(1, objective.TotalCount, "One goal: the way out.");
            Assert.AreEqual(gameplay.Checkpoints[gameplay.Checkpoints.Count - 1], objective.Goals[0]);

            // The Guardian is on its navigation mesh, which stops at the casting floor's gates.
            Assert.IsTrue(NavMesh.SamplePosition(gameplay.Guardian.transform.position, out NavMeshHit guardian, 1f, NavMesh.AllAreas));
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(5.3f, 2f, 18f), out _, 1.5f, NavMesh.AllAreas), "Not on the stair.");
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(5.3f, 0f, 21.6f), out _, 0.3f, NavMesh.AllAreas), "Not at the stair's foot.");
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(0f, 0f, 42.5f), out _, 0.3f, NavMesh.AllAreas), "Not in the gate.");
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(0f, 0f, 48f), out _, 1.5f, NavMesh.AllAreas), "Not in the exit court.");
            var path = new NavMeshPath();
            foreach (Vector3 point in FoundryLayout.GuardianRoute)
            {
                Assert.IsTrue(NavMesh.SamplePosition(point, out NavMeshHit hit, 1f, NavMesh.AllAreas));
                Assert.IsTrue(NavMesh.CalculatePath(guardian.position, hit.position, NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "The patrol can be walked.");
            }

            // Every place the player comes back to has solid ground under it and room above it.
            foreach (Checkpoint checkpoint in gameplay.Checkpoints)
            {
                Vector3 respawn = checkpoint.RespawnPosition;
                Assert.IsTrue(Physics.Raycast(respawn + Vector3.up, Vector3.down, out RaycastHit ground, 1.2f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore), $"'{checkpoint.name}' has ground under its respawn point.");
                Assert.AreEqual(respawn.y, ground.point.y, 0.15f);
                // The Mage's own capsule (radius 0.35, height 1.8), lifted clear of the floor by a few centimetres.
                Assert.IsFalse(Physics.CheckCapsule(respawn + Vector3.up * 0.4f, respawn + Vector3.up * 1.5f, 0.35f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore), $"'{checkpoint.name}': the Mage fits there.");
            }

            // The pieces that last are saved under the level's prefix, so a replay forgets exactly them.
            foreach (MagicalObject magical in new[] { gameplay.Crystal, gameplay.Barricade, gameplay.Lift, gameplay.Plug, gameplay.Counterweight })
            {
                StringAssert.StartsWith(FoundryLayout.KeyPrefix, magical.PersistentId);
            }
        }

        [Test]
        public void AssemblingTheFoundryTwiceGivesTheSameScene()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            BrokenFoundrySetup.BuildForAutomation();
            Scene first = SceneManager.GetActiveScene();
            int firstCount = CountObjects(first);
            int firstComponents = CountComponents(first);
            BrokenFoundrySetup.Gameplay before = BrokenFoundrySetup.Find(first);
            string crystalId = before.Crystal.PersistentId;

            // A value tuned by hand, which a second run must keep.
            before.Hazard.Configure(before.Hazard.Size, 7f);

            Scene scene = BrokenFoundrySetup.BuildForAutomation();
            BrokenFoundrySetup.Gameplay after = BrokenFoundrySetup.Find(scene);
            Assert.AreEqual(firstCount, CountObjects(scene), "Running the setup a second time changed the number of objects.");
            Assert.AreEqual(firstComponents, CountComponents(scene), "Running the setup a second time added components.");
            Assert.AreEqual(crystalId, after.Crystal.PersistentId, "An id that exists is never changed.");
            Assert.AreEqual(7f, after.Hazard.Damage, "A tuned value is kept.");
            Assert.AreEqual(FoundryLayout.Checkpoints.Length, after.Route.CheckpointCount, "The route lists each checkpoint once.");

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());
        }

        [Test]
        public void TheLayoutIsCheckedAgainstTheComponentsOwnDefaults()
        {
            var host = new GameObject("Traversal defaults");
            try
            {
                var motor = new SerializedObject(host.AddComponent<ThirdPersonMotor>());
                var traversal = new SerializedObject(host.AddComponent<TraversalController>());
                TraversalReach reach = TraversalReach.Defaults;

                Assert.AreEqual(reach.WalkSpeed, motor.FindProperty("walkSpeed").floatValue);
                Assert.AreEqual(reach.SprintSpeed, motor.FindProperty("sprintSpeed").floatValue);
                Assert.AreEqual(reach.JumpHeight, motor.FindProperty("jumpHeight").floatValue);
                Assert.AreEqual(reach.Gravity, motor.FindProperty("gravity").floatValue);
                Assert.AreEqual(reach.FallGravityMultiplier, motor.FindProperty("fallGravityMultiplier").floatValue);
                Assert.AreEqual(reach.ApexGravityMultiplier, motor.FindProperty("apexGravityMultiplier").floatValue);
                Assert.AreEqual(reach.ApexSpeedThreshold, motor.FindProperty("apexSpeedThreshold").floatValue);
                Assert.AreEqual(reach.GrabMinHeight, traversal.FindProperty("grabMinHeight").floatValue);
                Assert.AreEqual(reach.GrabMaxHeight, traversal.FindProperty("grabMaxHeight").floatValue);
                Assert.AreEqual(reach.GrabMaxRiseSpeed, traversal.FindProperty("grabMaxRiseSpeed").floatValue);
                Assert.AreEqual(reach.MantleMaxHeight, traversal.FindProperty("mantleMaxHeight").floatValue);
                Assert.AreEqual(reach.VaultMaxHeight, traversal.FindProperty("vaultMaxHeight").floatValue);
                Assert.AreEqual(reach.EdgeGuardDepth, traversal.FindProperty("edgeGuardDepth").floatValue);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AnObjectiveWithAGoalCompletesWhenTheGoalIsReached()
        {
            WorldState.ResetSession();
            var host = new GameObject("Objective");
            var goalHost = new GameObject("Way Out");
            try
            {
                var objective = host.AddComponent<DefeatTargetsObjective>();
                var goal = goalHost.AddComponent<Checkpoint>();
                goal.PersistenceKey = CheckpointProgress.KeyFor("Test/", 5);
                int completed = 0;
                objective.Completed += () => completed++;
                objective.SetGoals(new[] { goal });
                Assert.AreEqual(1, objective.TotalCount);
                Assert.IsFalse(objective.IsComplete);

                Assert.IsTrue(goal.MarkReached());
                Assert.IsTrue(objective.IsComplete);
                Assert.AreEqual(1, completed);
                Assert.IsFalse(goal.MarkReached(), "Reached once: it is locked.");
                Assert.IsTrue(WorldState.Current.TryGetPuzzle(goal.PersistenceKey, out _), "Remembered in the world state.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(goalHost);
                WorldState.ResetSession();
            }
        }

        [Test]
        public void NewGameAndRetry_PutThePlayerInTheRightPlace_AndARetryNeverReplaysTheLevel()
        {
            WorldState.ResetSession();
            var host = new GameObject("Session");
            var goalHost = new GameObject("Way Out");
            try
            {
                var objective = host.AddComponent<DefeatTargetsObjective>();
                var session = host.AddComponent<PrototypeSession>();
                session.Bind(objective, null, null, null, null);
                session.ReplayScope = "Test/";

                // A new game forgets the checkpoint the player had reached.
                session.SetRespawnPoint(new Vector3(0f, 0f, 44f), Quaternion.identity);
                session.NewGame();
                Assert.IsFalse(session.HasRespawnPoint, "After a new game the player starts at the start.");

                // Complete the objective after a checkpoint was taken, with something of the level saved in it.
                Assert.IsFalse(session.RetryFromCheckpoint(), "No checkpoint taken yet.");
                WorldState.Current.SetPuzzle("Test/Plug", new PuzzleRecord(true, true));
                WorldState.SaveCheckpoint();
                var goal = goalHost.AddComponent<Checkpoint>();
                goal.PersistenceKey = "Test/Checkpoint/5";
                objective.SetGoals(new[] { goal });
                goal.MarkReached();
                Assert.IsTrue(objective.IsComplete);

                Assert.IsTrue(session.RetryFromCheckpoint());
                Assert.IsTrue(WorldState.Current.TryGetPuzzle("Test/Plug", out _),
                    "A retry goes back to the checkpoint; it does not replay the level, which would forget everything under Test/.");
                Assert.IsFalse(WorldState.Current.TryGetPuzzle("Test/Checkpoint/5", out _), "The way out, reached after it, is undone.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(goalHost);
                WorldState.ResetSession();
            }
        }

        [Test]
        public void ACheckpointsBoxStandsOnItAndTurnsWithIt()
        {
            var host = new GameObject("Checkpoint");
            try
            {
                host.transform.SetPositionAndRotation(new Vector3(10f, 2f, 5f), Quaternion.Euler(0f, 90f, 0f));
                var checkpoint = host.AddComponent<Checkpoint>();
                checkpoint.Configure(1, new Vector3(4f, 3f, 1f), null);

                Assert.IsTrue(checkpoint.Contains(new Vector3(10f, 2.1f, 5f)), "At its feet.");
                Assert.IsTrue(checkpoint.Contains(new Vector3(10f, 4.9f, 6.9f)), "Turned: its 4 m now run along z.");
                Assert.IsFalse(checkpoint.Contains(new Vector3(11f, 3f, 5f)), "Its 1 m depth now runs along x.");
                Assert.IsTrue(checkpoint.Contains(new Vector3(10f, 1.85f, 5f)), "Feet a little below the floor still count.");
                Assert.IsFalse(checkpoint.Contains(new Vector3(10f, 1.6f, 5f)), "Well below it does not.");
                Assert.AreEqual(host.transform.position, checkpoint.RespawnPosition, "Without a respawn point, its own position.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static int CountComponents(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<Component>(true).Length;
            }

            return count;
        }

        private static int CountObjects(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<Transform>(true).Length;
            }

            return count;
        }
    }
}
