using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Threefold.EditorTools;
using Threefold.Magic;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// The setup command generates the six example objects, places them in the Discipline Range and
    /// leaves nothing for the validation to report. These tests need the Unity Editor; those that
    /// generate assets or replace the open scene run only in batch mode, like the other assembly tests.
    /// </summary>
    public sealed class MagicalObjectSetupTests
    {
        private static void RequireBatchMode(string reason)
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it " + reason + ".");
            }
        }

        private static MagicEffect Effect(MagicEffectKind kind, float magnitude, int eventId)
        {
            return new MagicEffect(kind, magnitude, Vector3.forward, 1.5f, Vector3.zero, null, -1, EffectTargets.All, eventId);
        }

        private static bool HasSolidCollider(GameObject target)
        {
            foreach (Collider part in target.GetComponentsInChildren<Collider>(true))
            {
                if (!part.isTrigger)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void EveryExampleIsGeneratedCompleteAndValid()
        {
            RequireBatchMode("generates assets");

            MagicalObjectSetup.ExampleAssets assets = MagicalObjectSetup.EnsureAssets(new StringBuilder());

            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                MagicalObjectDefinition definition = assets.Definitions[i];
                GameObject prefab = assets.Prefabs[i];
                Assert.IsNotNull(definition, example.ToString());
                Assert.IsNotNull(prefab, example.ToString());

                // Only meaningful for freshly generated assets, which is what a build server has.
                MagicalPropertySettings defaults = WorldMagicDefaults.Settings(example);
                Assert.AreEqual(defaults.PropertyCount, definition.Properties.PropertyCount, example.ToString());
                for (int kind = 0; kind < 5; kind++)
                {
                    Assert.AreEqual(defaults.Has((WorldPropertyKind)kind), definition.Properties.Has((WorldPropertyKind)kind),
                        $"{example} / {(WorldPropertyKind)kind}");
                }

                MagicalObject magical = prefab.GetComponent<MagicalObject>();
                Assert.IsNotNull(magical, $"{example} has a MagicalObject on its root.");
                Assert.AreSame(definition, magical.Definition, example.ToString());
                Assert.IsTrue(HasSolidCollider(prefab), $"{example} has a solid collider.");
                Assert.Greater(magical.ListedColliderCount, 0, $"{example} lists the colliders that go when it is destroyed.");

                PropertyStateVisuals visuals = prefab.GetComponent<PropertyStateVisuals>();
                Assert.IsNotNull(visuals, $"{example} shows its state.");
                Assert.AreSame(magical, visuals.Source, example.ToString());
                Assert.AreEqual(PropertyCondition.None,
                    visuals.WatchedConditions & ~MagicalObjectRules.Reachable(definition.Properties),
                    $"{example} shows only states it can reach.");

                var problems = new List<string>();
                MagicalObjectValidator.ValidateObject(magical, problems.Add);
                Assert.AreEqual(0, problems.Count, $"{example}: {string.Join(" | ", problems)}");
            }

            // The kinetic example moves a part of its own, and the door is opened by its own lock.
            MagicalObject platform = assets.Prefabs[(int)ExampleObject.MovablePlatform].GetComponent<MagicalObject>();
            Assert.AreNotSame(platform.transform, platform.MoveTarget, "The platform moves on its rail; the rail stays.");
            Assert.IsNotNull(platform.MoveTarget.GetComponentInChildren<Collider>(), "The moving part carries the collider.");

            GameObject doorPrefab = assets.Prefabs[(int)ExampleObject.ReinforcedDoor];
            PuzzleDoor door = doorPrefab.GetComponent<PuzzleDoor>();
            PropertyStateTrigger doorLock = doorPrefab.GetComponent<PropertyStateTrigger>();
            Assert.IsNotNull(door);
            Assert.AreSame(doorLock, door.InputElement, "The door follows its own properties.");
            Assert.AreEqual(PropertyCondition.Activated | PropertyCondition.Fractured, doorLock.ActiveWhen,
                "It gives way to its arcane lock or to heavy force.");
            Assert.IsNotNull(door.Leaf.target);

            Assert.IsNotNull(assets.Prefabs[(int)ExampleObject.ArcaneCrystal].GetComponent<PropertyStateTrigger>(),
                "The crystal can drive a mechanism.");
            Assert.IsNotNull(assets.Gate.GetComponent<PuzzleDoor>());
        }

        [Test]
        public void EachGeneratedObjectRespondsThroughItsPropertiesOnly()
        {
            RequireBatchMode("generates assets");

            MagicalObjectSetup.ExampleAssets assets = MagicalObjectSetup.EnsureAssets(new StringBuilder());
            int eventId = 1;

            // What each example must accept and must ignore, by kind of effect: heat, charge, force, stabilise.
            var accepts = new Dictionary<ExampleObject, bool[]>
            {
                { ExampleObject.ArcaneCrystal, new[] { false, true, false, false } },
                { ExampleObject.WoodenBarricade, new[] { true, false, true, false } },
                { ExampleObject.HeatSensitiveStone, new[] { true, false, true, false } },
                { ExampleObject.MovablePlatform, new[] { false, false, true, false } },
                { ExampleObject.UnstableStructure, new[] { false, false, true, true } },
                { ExampleObject.ReinforcedDoor, new[] { false, true, true, false } },
            };

            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                GameObject instance = Object.Instantiate(assets.Prefabs[i]);
                try
                {
                    MagicalObject magical = instance.GetComponent<MagicalObject>();
                    for (int kind = 0; kind < 4; kind++)
                    {
                        EffectResponse response = magical.ReceiveEffect(Effect((MagicEffectKind)kind, 2f, eventId++));
                        if (accepts[example][kind])
                        {
                            Assert.AreNotEqual(EffectResponse.NoProperty, response, $"{example} answers {(MagicEffectKind)kind}.");
                        }
                        else
                        {
                            Assert.AreEqual(EffectResponse.NoProperty, response, $"{example} ignores {(MagicEffectKind)kind}.");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void ADestroyedObjectStopsBlockingAndComesBackWhenRestored()
        {
            RequireBatchMode("generates assets");

            MagicalObjectSetup.ExampleAssets assets = MagicalObjectSetup.EnsureAssets(new StringBuilder());
            GameObject instance = Object.Instantiate(assets.Prefabs[(int)ExampleObject.WoodenBarricade]);
            try
            {
                MagicalObject barricade = instance.GetComponent<MagicalObject>();
                Collider body = instance.GetComponentInChildren<Collider>();
                int destroyed = 0;
                barricade.StateChanged += (changed, kind, from, to) =>
                {
                    if (kind == WorldPropertyKind.Structural && to == (int)StructuralState.Destroyed)
                    {
                        destroyed++;
                    }
                };

                // Enough force to break it outright, whatever its tuned integrity.
                for (int i = 0; i < 40 && !barricade.IsDestroyed; i++)
                {
                    barricade.ReceiveEffect(Effect(MagicEffectKind.Force, 12f, 100 + i));
                }

                Assert.IsTrue(barricade.IsDestroyed);
                Assert.AreEqual(1, destroyed, "Destruction is announced once.");
                Assert.IsFalse(body.enabled, "Nothing blocks the way any more.");
                Assert.AreEqual(EffectResponse.Spent, barricade.ReceiveEffect(Effect(MagicEffectKind.Heat, 50f, 999)),
                    "What is destroyed takes no further effect.");

                barricade.Restore();
                Assert.IsFalse(barricade.IsDestroyed);
                Assert.IsTrue(body.enabled);
                Assert.AreEqual(EffectResponse.Applied, barricade.ReceiveEffect(Effect(MagicEffectKind.Heat, 5f, 1000)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheDisciplineRangeHoldsTheExamplesAndRunningAgainAddsNothing()
        {
            RequireBatchMode("replaces the open scene without saving it");

            Scene scene = DisciplineRangeSetup.BuildForAutomation();

            var problems = new StringBuilder();
            Assert.AreEqual(0, MagicalObjectValidator.Validate(scene, problems), problems.ToString());

            GameObject group = MagicalObjectSetup.FindGroup(scene);
            Assert.IsNotNull(group, "The range has the group of magical objects.");

            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                Transform placed = group.transform.Find(WorldMagicDefaults.Name((ExampleObject)i));
                Assert.IsNotNull(placed, ((ExampleObject)i).ToString());
                Assert.IsNotNull(placed.GetComponent<MagicalObject>().Definition, ((ExampleObject)i).ToString());
            }

            Transform crystal = group.transform.Find(WorldMagicDefaults.Name(ExampleObject.ArcaneCrystal));
            Transform gate = group.transform.Find(MagicalObjectSetup.GateInstanceName);
            Assert.IsNotNull(gate);
            Assert.AreSame(crystal.GetComponent<PropertyStateTrigger>(), gate.GetComponent<PuzzleDoor>().InputElement,
                "The gate follows the crystal.");

            int before = CountObjects(scene);
            MagicalObjectSetup.ExampleAssets assets = MagicalObjectSetup.EnsureAssets(new StringBuilder());
            MagicalObjectSetup.EnsureExamples(scene, assets, MagicalObjectSetup.RangeLaneOrigin, 0f, new StringBuilder());
            Assert.AreEqual(before, CountObjects(scene), "A second run places nothing.");
        }

        [Test]
        public void MakingAnObjectMagicalAddsTheComponentsOnceAndKeepsWhatIsThere()
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            var other = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            try
            {
                definition.Configure("Test", string.Empty, WorldMagicDefaults.Settings(ExampleObject.ArcaneCrystal));
                other.Configure("Other", string.Empty, WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));

                MagicalObject magical = MagicalObjectSetup.MakeMagical(target, definition, new StringBuilder());
                Assert.AreSame(definition, magical.Definition);
                Assert.AreEqual(1, magical.ListedColliderCount, "The cube's collider is listed.");
                Assert.AreEqual(1, target.GetComponent<PropertyStateVisuals>().TintRendererCount);

                // A second run, even with another definition, changes nothing.
                MagicalObjectSetup.MakeMagical(target, other, new StringBuilder());
                Assert.AreEqual(1, target.GetComponents<MagicalObject>().Length);
                Assert.AreEqual(1, target.GetComponents<PropertyStateVisuals>().Length);
                Assert.AreSame(definition, target.GetComponent<MagicalObject>().Definition, "The definition it had is kept.");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void AnObjectWithoutADefinitionOrAColliderIsReported()
        {
            var target = new GameObject("Bare");
            try
            {
                MagicalObject magical = target.AddComponent<MagicalObject>();
                var problems = new List<string>();
                MagicalObjectValidator.ValidateObject(magical, problems.Add);

                Assert.AreEqual(2, problems.Count, string.Join(" | ", problems));
                StringAssert.Contains("has no definition", problems[0]);
                StringAssert.Contains("has no solid collider", problems[1]);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ADisciplineGeneratedEarlierIsBroughtUpToDateOnce()
        {
            var definition = ScriptableObject.CreateInstance<DisciplineDefinition>();
            try
            {
                // An Aegis asset as LM-02 generated it: no force on its swings, no brace on its guard.
                MeleeComboProfile melee = DisciplineDefaults.Melee(MagicDiscipline.Aegis);
                foreach (MeleeStepProfile step in melee.steps)
                {
                    step.force = 0f;
                }

                definition.ConfigureIdentity(MagicDiscipline.Aegis, "Aegis", Color.white, default,
                    DisciplineDefaults.Element(MagicDiscipline.Aegis));
                definition.ConfigureCombat(melee, null);
                Assert.AreEqual(0, definition.DataVersion);
                Assert.IsFalse(definition.GuardEffect.HasEffect);

                Assert.IsTrue(definition.Upgrade());
                Assert.AreEqual(DisciplineDefaults.DataVersion, definition.DataVersion);
                Assert.IsTrue(definition.GuardEffect.HasEffect, "The Aegis guard now braces.");
                Assert.Greater(definition.Melee.steps[0].force, 0f, "Aegis swings now carry force.");

                // Once up to date it is left alone, so a value the owner then sets to zero stays zero.
                definition.Melee.steps[0].force = 0f;
                Assert.IsFalse(definition.Upgrade());
                Assert.AreEqual(0f, definition.Melee.steps[0].force);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
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
