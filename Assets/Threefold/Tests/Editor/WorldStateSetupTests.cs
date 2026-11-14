using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Threefold.EditorTools;
using Threefold.Persistence;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// What Sprint LM-04 added to the setup and the validation: identifiers, the chain-reaction row,
    /// the crystal's link and the update of definitions generated earlier. These tests need the Unity
    /// Editor; those that generate assets or replace the open scene run only in batch mode.
    ///
    /// Saving and restoring through the component itself only happens in Play mode and is therefore
    /// not covered here; the rules it uses are covered by the tests that run without Unity.
    /// </summary>
    public sealed class WorldStateSetupTests
    {
        private static void RequireBatchMode(string reason)
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it " + reason + ".");
            }
        }

        [Test]
        public void TheRangeHasIdentifiersTheChainRowAndTheCrystalsLink()
        {
            RequireBatchMode("replaces the open scene without saving it");

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            GameObject group = MagicalObjectSetup.FindGroup(scene);
            Assert.IsNotNull(group);

            // Every example that keeps something has an id, and no two share one.
            var ids = new List<string>();
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                MagicalObject magical = group.transform.Find(WorldMagicDefaults.Name(example)).GetComponent<MagicalObject>();
                bool keeps = WorldMagicDefaults.Settings(example).persisted != PersistedState.None;
                Assert.AreEqual(keeps, !string.IsNullOrEmpty(magical.PersistentId), example.ToString());
                ids.Add(magical.PersistentId);
            }

            Assert.AreEqual(0, PersistentIdRegistry.FindDuplicates(ids, new List<string>()));

            // The chain row: the same barricade several times, without ids, so a restart brings them back.
            for (int i = 1; i <= MagicalObjectSetup.ChainRowCount; i++)
            {
                Transform barricade = group.transform.Find($"{MagicalObjectSetup.ChainInstanceName} {i}");
                Assert.IsNotNull(barricade, $"Chain barricade {i}.");
                Assert.IsEmpty(barricade.GetComponent<MagicalObject>().PersistentId);
            }

            MagicalObject crystal = group.transform.Find(WorldMagicDefaults.Name(ExampleObject.ArcaneCrystal)).GetComponent<MagicalObject>();
            GameObject door = group.transform.Find(WorldMagicDefaults.Name(ExampleObject.ReinforcedDoor)).gameObject;
            Assert.AreEqual(1, crystal.LinkedTargetCount);
            Assert.AreSame(door, crystal.GetLinkedTarget(0));
            Assert.IsNotNull(group.GetComponent<WorldSimulationBudgets>());

            var problems = new StringBuilder();
            Assert.AreEqual(0, MagicalObjectValidator.Validate(scene, problems), problems.ToString());
        }

        [Test]
        public void TwoObjectsWithOneIdentifierAreReported()
        {
            var definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            GameObject first = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject second = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                definition.Configure("Test", string.Empty, WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
                first.name = "First";
                second.name = "Second";
                MagicalObjectSetup.MakeMagical(first, definition, new StringBuilder()).FillPersistentId("Shared/Id");
                MagicalObjectSetup.MakeMagical(second, definition, new StringBuilder()).FillPersistentId("Shared/Id");

                var problems = new StringBuilder();
                int count = MagicalObjectValidator.Validate(first.scene, problems);

                Assert.GreaterOrEqual(count, 1);
                StringAssert.Contains("'Shared/Id' is used by 2 objects", problems.ToString());
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void AssigningIdentifiersKeepsExistingOnesAndMakesNewOnesUnique()
        {
            var definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            var objects = new List<GameObject>();
            try
            {
                definition.Configure("Test", string.Empty, WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
                var targets = new List<MagicalObject>();
                for (int i = 0; i < 3; i++)
                {
                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Barrier";
                    objects.Add(cube);
                    targets.Add(MagicalObjectSetup.MakeMagical(cube, definition, new StringBuilder()));
                }

                targets[0].FillPersistentId("Hand/Written");
                Scene scene = objects[0].scene;

                int assigned = MagicalObjectSetup.AssignPersistentIds(scene, targets, new StringBuilder());

                Assert.AreEqual(2, assigned);
                Assert.AreEqual("Hand/Written", targets[0].PersistentId, "An id that exists is never changed.");
                Assert.IsNotEmpty(targets[1].PersistentId);
                Assert.AreNotEqual(targets[1].PersistentId, targets[2].PersistentId, "Objects with the same name get different ids.");

                Assert.AreEqual(0, MagicalObjectSetup.AssignPersistentIds(scene, targets, new StringBuilder()), "A second run assigns nothing.");
            }
            finally
            {
                foreach (GameObject cube in objects)
                {
                    Object.DestroyImmediate(cube);
                }

                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void ADefinitionGeneratedEarlierIsBroughtUpToDateOnce()
        {
            var definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            try
            {
                // As LM-03 left it: properties set, nothing about saving or reactions, version 0.
                MagicalPropertySettings old = definition.Properties;
                MagicalPropertySettings defaults = WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade);
                old.thermal = true;
                old.thermalSettings.capacity = 77f;
                old.structural = true;
                Assert.AreEqual(0, definition.DataVersion);
                Assert.AreEqual(0, old.emissions.Length);

                Assert.IsTrue(definition.Upgrade(defaults));

                Assert.AreEqual(WorldMagicDefaults.DataVersion, definition.DataVersion);
                Assert.AreEqual(defaults.persisted, definition.Properties.persisted);
                Assert.AreEqual(1, definition.Properties.emissions.Length, "The barricade's fire now spreads.");
                Assert.AreEqual(77f, definition.Properties.thermalSettings.capacity, "A value tuned before is kept.");

                // Once up to date it is left alone, so reactions the owner then removes stay removed.
                definition.Properties.emissions = new ReactionEmission[0];
                Assert.IsFalse(definition.Upgrade(defaults));
                Assert.AreEqual(0, definition.Properties.emissions.Length);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void AnIdentifierWithNothingToSaveAndABrokenLinkAreReported()
        {
            var definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            GameObject crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject plain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                // The crystal saves nothing, and its reaction goes to linked targets only.
                definition.Configure("Crystal", string.Empty, WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));
                MagicalObject magical = MagicalObjectSetup.MakeMagical(crystal, definition, new StringBuilder());

                var problems = new List<string>();
                MagicalObjectValidator.ValidateObject(magical, problems.Add);
                Assert.AreEqual(1, problems.Count, string.Join(" | ", problems));
                StringAssert.Contains("linked targets only, but none", problems[0]);

                magical.FillPersistentId("Some/Id");
                magical.FillLinkedTargets(new[] { plain });
                problems.Clear();
                MagicalObjectValidator.ValidateObject(magical, problems.Add);

                Assert.AreEqual(2, problems.Count, string.Join(" | ", problems));
                StringAssert.Contains("its definition saves nothing", problems[0]);
                StringAssert.Contains("cannot receive magical effects", problems[1]);
            }
            finally
            {
                Object.DestroyImmediate(crystal);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(definition);
            }
        }
    }
}
