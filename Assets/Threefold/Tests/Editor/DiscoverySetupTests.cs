using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.Discovery;
using Threefold.EditorTools;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// What the setup does for discovery: the director in the scene, and the clue component and
    /// built clues on the example prefabs. These tests need the Unity Editor; those that generate
    /// assets or replace the open scene run only in batch mode.
    ///
    /// What the director shows while the game runs is not covered here; the rules it uses are
    /// covered by the tests that run without Unity.
    /// </summary>
    public sealed class DiscoverySetupTests
    {
        private static void RequireBatchMode(string reason)
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it " + reason + ".");
            }
        }

        [Test]
        public void TheRangeHasADirectorConnectedToTheMage()
        {
            RequireBatchMode("replaces the open scene without saving it");

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            DiscoveryDirector director = SceneSetupUtility.FindInScene<DiscoveryDirector>(scene);
            GameObject mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene).gameObject;

            Assert.IsNotNull(director);
            Assert.AreSame(mage.transform, director.Player);
            Assert.IsNotNull(director.View, "It needs the camera to tell what is aimed at.");
            Assert.IsNotNull(director.Disciplines);
            Assert.IsNotNull(director.Combat);
            Assert.IsNull(director.GetComponentInParent<ThirdPersonMotor>(), "It is an object of its own, not part of the Mage.");
            Assert.IsNotNull(director.GetComponent<DiscoveryAudio>());
            Assert.IsNotNull(director.GetComponent<DiscoveryHaptics>());

            // A second run adds nothing and keeps the options.
            director.Settings.clueIntensity = 0.4f;
            DiscoverySetup.Ensure(scene, mage, new StringBuilder());
            int directors = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                directors += root.GetComponentsInChildren<DiscoveryDirector>(true).Length;
            }

            Assert.AreEqual(1, directors);
            Assert.AreEqual(0.4f, director.Settings.clueIntensity);

            var problems = new StringBuilder();
            Assert.AreEqual(0, PrototypeValidator.Validate(scene, problems), problems.ToString());
        }

        [Test]
        public void EveryExamplePrefabHasItsCluesAndAPartOfItsOwnToMove()
        {
            RequireBatchMode("generates assets");

            MagicalObjectSetup.ExampleAssets assets = MagicalObjectSetup.EnsureAssets(new StringBuilder());

            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                GameObject prefab = assets.Prefabs[i];
                DiscoveryClues clues = prefab.GetComponent<DiscoveryClues>();
                MagicalObject magical = prefab.GetComponent<MagicalObject>();

                Assert.IsNotNull(clues, example.ToString());
                Assert.AreSame(magical, clues.Source, example.ToString());
                Assert.IsNotNull(clues.MotionTarget, $"{example}: without a moving part its answers would rely on colour.");
                Assert.AreNotSame(prefab.transform, clues.MotionTarget, example.ToString());
                Assert.AreNotSame(magical.MoveTarget, clues.MotionTarget, $"{example}: the part the object itself places is left alone.");
                Assert.IsTrue(clues.MotionTarget.IsChildOf(prefab.transform), example.ToString());
                Assert.Greater(clues.AmbientStrength, 0f, example.ToString());
                Assert.LessOrEqual(clues.AmbientStrength, 0.3f, $"{example}: an idle clue stays faint.");

                // The clue it should carry is the one its properties call for.
                Assert.AreEqual(Compatibility.CluesFor(WorldMagicDefaults.Settings(example)), clues.Clues, example.ToString());
            }

            // Built clues: straw, vents and cracks are parts of the object, hidden with it when it is destroyed.
            Transform straw = assets.Prefabs[(int)ExampleObject.WoodenBarricade].transform.Find("Whole/" + MagicalObjectSetup.CluesName);
            Transform vents = assets.Prefabs[(int)ExampleObject.HeatSensitiveStone].transform.Find("Whole/" + MagicalObjectSetup.CluesName);
            Transform cracks = assets.Prefabs[(int)ExampleObject.UnstableStructure].transform.Find("Body/Sway/" + MagicalObjectSetup.CluesName);
            Assert.IsNotNull(straw);
            Assert.IsNotNull(vents);
            Assert.IsNotNull(cracks);
            Assert.Greater(straw.childCount, 0);
            Assert.Greater(vents.childCount, 0);
            Assert.Greater(cracks.childCount, 0);
            foreach (Transform part in new[] { straw, vents, cracks })
            {
                Assert.IsNull(part.GetComponentInChildren<Collider>(true), "A clue is something to see, not something to bump into.");
            }

            // A second run changes nothing.
            int strawParts = straw.childCount;
            assets = MagicalObjectSetup.EnsureAssets(new StringBuilder());
            Assert.AreEqual(1, assets.Prefabs[(int)ExampleObject.WoodenBarricade].GetComponents<DiscoveryClues>().Length);
            Assert.AreEqual(strawParts,
                assets.Prefabs[(int)ExampleObject.WoodenBarricade].transform.Find("Whole/" + MagicalObjectSetup.CluesName).childCount);
        }

        [Test]
        public void CluesWithoutAMovingPartOrWithoutADirectorAreReported()
        {
            var definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                definition.Configure("Test", string.Empty, WorldMagicDefaults.Settings(ExampleObject.WoodenBarricade));
                cube.name = "Lonely";
                MagicalObjectSetup.MakeMagical(cube, definition, new StringBuilder());
                Assert.IsNotNull(cube.GetComponent<DiscoveryClues>(), "Making an object magical gives it its clues as well.");

                var problems = new StringBuilder();
                MagicalObjectValidator.Validate(cube.scene, problems);
                string text = problems.ToString();

                StringAssert.Contains("'Lonely' have no moving part", text);
                if (SceneSetupUtility.FindInScene<DiscoveryDirector>(cube.scene) == null)
                {
                    StringAssert.Contains("no discovery director", text);
                }
            }
            finally
            {
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void EveryDiscoverySoundHasAPlaceholderAndNoTwoShareOne()
        {
            var tones = new HashSet<PuzzleTone>();
            foreach (DiscoverySound sound in new[]
                     {
                         DiscoverySound.Resonance, DiscoverySound.Refusal, DiscoverySound.Resistance, DiscoverySound.Progress,
                         DiscoverySound.Transition, DiscoverySound.Completion, DiscoverySound.Held,
                     })
            {
                PuzzleTone tone = DiscoveryAudio.PlaceholderFor(sound);
                Assert.AreNotEqual(PuzzleTone.None, tone, sound.ToString());
                Assert.IsTrue(tones.Add(tone), $"{sound} shares its placeholder with another sound.");
            }

            Assert.AreEqual(PuzzleTone.None, DiscoveryAudio.PlaceholderFor(DiscoverySound.None));
        }
    }
}
