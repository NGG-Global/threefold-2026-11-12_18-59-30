using NUnit.Framework;
using Threefold.DevTools;
using Threefold.EditorTools;

namespace Threefold.Tests
{
    /// <summary>
    /// The development-only way between scenes: the finger gestures, the order of scenes and their names, and
    /// the list of scenes the development build holds. Plain C#: these run outside Unity too.
    /// </summary>
    public sealed class DevSceneSwitcherTests
    {
        private const float Frame = 1f / 60f;

        private static DevGesture Tap(DevSceneSwitcher switcher, bool shown, float seconds, params int[] fingers)
        {
            DevGesture result = DevGesture.None;
            float each = seconds / fingers.Length;
            foreach (int count in fingers)
            {
                for (float held = 0f; held < each; held += Frame)
                {
                    result = switcher.Update(count, Frame, shown);
                    Assert.AreEqual(DevGesture.None, result, "Nothing is decided while fingers are down.");
                }
            }

            return switcher.Update(0, Frame, shown);
        }

        [Test]
        public void AThreeFingerTapShowsOrHidesTheDiagnostics()
        {
            var switcher = new DevSceneSwitcher();
            Assert.AreEqual(DevGesture.ToggleDiagnostics, Tap(switcher, false, 0.2f, 1, 2, 3, 2, 1));
            Assert.AreEqual(DevGesture.ToggleDiagnostics, Tap(switcher, true, 0.2f, 3));
        }

        [Test]
        public void AFourFingerTapLoadsTheNextSceneOnlyWhileTheDiagnosticsAreShown()
        {
            var switcher = new DevSceneSwitcher();
            Assert.AreEqual(DevGesture.NextScene, Tap(switcher, true, 0.3f, 2, 3, 4, 3, 1), "Passing through three fingers does not toggle.");
            Assert.AreEqual(DevGesture.NextScene, Tap(switcher, true, 0.3f, 5));
            Assert.AreEqual(DevGesture.None, Tap(switcher, false, 0.3f, 4), "Hidden diagnostics: four fingers do nothing.");
        }

        [Test]
        public void PlayingWithSeveralFingersNeverChangesAnything()
        {
            var switcher = new DevSceneSwitcher();

            // Moving, looking, attacking and blocking for three seconds, with the diagnostics shown or hidden.
            Assert.AreEqual(DevGesture.None, Tap(switcher, true, 3f, 1, 2, 3, 4, 3, 2));
            Assert.AreEqual(DevGesture.None, Tap(switcher, false, 3f, 1, 2, 3, 2));
            Assert.AreEqual(DevGesture.None, Tap(switcher, true, 0.2f, 1));
            Assert.AreEqual(DevGesture.None, Tap(switcher, true, 0.2f, 2));

            // And the next tap is counted afresh.
            Assert.AreEqual(DevGesture.ToggleDiagnostics, Tap(switcher, true, 0.2f, 3));
        }

        [Test]
        public void NoFingersIsNoGesture()
        {
            var switcher = new DevSceneSwitcher();
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(DevGesture.None, switcher.Update(0, Frame, true));
            }
        }

        [TestCase(0, 6, 1)]
        [TestCase(5, 6, 0)]
        [TestCase(-1, 6, 0)]
        [TestCase(0, 1, 0)]
        [TestCase(3, 0, 0)]
        public void TheNextSceneGoesRound(int current, int count, int expected)
        {
            Assert.AreEqual(expected, DevSceneSwitcher.NextIndex(current, count));
        }

        [TestCase("Assets/Scenes/ReferenceCourtyard.unity", "Reference Courtyard")]
        [TestCase("Assets/Scenes/StyleLab.unity", "Style Lab")]
        [TestCase("Assets/Scenes/MainMenu.unity", "Main Menu")]
        [TestCase("FantasyPlayground", "Fantasy Playground")]
        [TestCase("Assets\\Scenes\\CombatArena.unity", "Combat Arena")]
        [TestCase("", "(unnamed scene)")]
        [TestCase(null, "(unnamed scene)")]
        public void AScenesNameIsReadFromItsPath(string path, string expected)
        {
            Assert.AreEqual(expected, DevSceneSwitcher.DisplayName(path));
        }

        [Test]
        public void TheDevelopmentBuildHoldsEveryRequiredSceneOnce()
        {
            string[] paths = PrototypeBuild.ScenePaths();
            string[] required =
            {
                "Assets/Scenes/FantasyPlayground.unity", "Assets/Scenes/CombatArena.unity", "Assets/Scenes/BrokenFoundry.unity",
                "Assets/Scenes/StyleLab.unity", "Assets/Scenes/MainMenu.unity", "Assets/Scenes/ReferenceCourtyard.unity",
            };

            CollectionAssert.AreEquivalent(required, paths);
            CollectionAssert.AllItemsAreUnique(paths);
        }

        [Test]
        public void TheGameStillStartsInThePlaygroundAndTheGameplayScenesComeFirst()
        {
            string[] paths = PrototypeBuild.ScenePaths();
            Assert.AreEqual("Assets/Scenes/FantasyPlayground.unity", paths[0], "The first scene is the one the game starts in.");
            Assert.AreEqual("Assets/Scenes/CombatArena.unity", paths[1]);
            Assert.AreEqual("Assets/Scenes/BrokenFoundry.unity", paths[2]);

            PrototypeBuild.DevelopmentScene[] order = PrototypeBuild.AssemblyOrder();
            Assert.AreEqual(paths[0], order[order.Length - 1].Path, "The first scene is assembled last, so it is the one left open.");
        }

        [Test]
        public void ABuildStopsAtTheFirstSceneThatWasNotWritten()
        {
            string[] paths = { "a.unity", "b.unity", "c.unity" };
            Assert.IsNull(PrototypeBuild.FirstMissingScene(paths, path => true));
            Assert.AreEqual("b.unity", PrototypeBuild.FirstMissingScene(paths, path => path == "a.unity"));
            Assert.AreEqual("a.unity", PrototypeBuild.FirstMissingScene(paths, path => false));
        }
    }
}
