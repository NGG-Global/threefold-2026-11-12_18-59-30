using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Threefold.Interface;
using Threefold.TouchControls;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The control layout on small phones and large tablets: everything inside the safe area, no
    /// overlaps, touch targets of at least 48 dp, ATTACK the largest button, and a full aim never
    /// reaching the cancel zone. Also checks that the desktop has an equivalent for every touch action.
    /// </summary>
    public sealed class MobileControlsLayoutTests
    {
        // Safe-area sizes in dp. The canvas is never shorter than 340 dp (DensityCanvasScaler); 316 allows
        // for a 24 dp system bar. Phones from 16:9 to 21:9, foldables and tablets from 4:3 to 16:10.
        private static readonly Vector2[] SafeAreas =
        {
            new Vector2(560f, 316f), new Vector2(640f, 340f), new Vector2(740f, 340f), new Vector2(800f, 360f),
            new Vector2(915f, 412f), new Vector2(860f, 360f), new Vector2(1024f, 768f), new Vector2(1180f, 820f),
            new Vector2(1280f, 800f), new Vector2(1366f, 1024f),
        };

        [Test]
        public void TheDefaultLayoutFitsEverySupportedScreen()
        {
            var problems = new List<string>();
            foreach (Vector2 size in SafeAreas)
            {
                MobileControlsLayout.Default.Validate(size, 3, problems);
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheValidatorReportsAnOverlap()
        {
            var broken = new MobileControlsLayout
            {
                Block = new ControlPlacement("Block", ScreenCorner.BottomRight, new Vector2(-130f, 110f), 72f, 10f),
            };
            var problems = new List<string>();
            broken.Validate(MobileControlsLayout.MinimumSafeArea, 3, problems);

            Assert.IsTrue(problems.Exists(p => p.Contains("Attack and Block overlap")), string.Join("\n", problems));
        }

        [Test]
        public void TheValidatorReportsATouchTargetBelow48Dp()
        {
            var broken = new MobileControlsLayout { IndicatorDiameter = 30f, IndicatorHitPadding = 2f };
            var problems = new List<string>();
            broken.Validate(MobileControlsLayout.MinimumSafeArea, 3, problems);

            Assert.IsTrue(problems.Exists(p => p.Contains("DisciplineIndicator: touch area")), string.Join("\n", problems));
        }

        [Test]
        public void TheValidatorReportsACancelZoneWithinAimReach()
        {
            var broken = new MobileControlsLayout { CancelOffset = new Vector2(0f, 80f) };
            var problems = new List<string>();
            broken.Validate(new Vector2(800f, 400f), 3, problems);

            Assert.IsTrue(problems.Exists(p => p.Contains("reaches the cancel zone")), string.Join("\n", problems));
        }

        [Test]
        public void AttackIsTheLargestButton()
        {
            MobileControlsLayout layout = MobileControlsLayout.Default;
            Assert.Greater(layout.Attack.Diameter, layout.Block.Diameter);
            Assert.Greater(layout.Attack.Diameter, layout.Jump.Diameter);
            Assert.Greater(layout.Attack.Diameter, layout.Interact.Diameter);
            Assert.Greater(layout.Attack.Diameter, layout.IndicatorDiameter);
        }

        [Test]
        public void ControlsStayAnchoredToTheirCornerWhateverTheAspectRatio()
        {
            MobileControlsLayout layout = MobileControlsLayout.Default;
            Vector2 phone = new Vector2(740f, 340f);
            Vector2 tablet = new Vector2(1280f, 800f);

            Assert.AreEqual(layout.Joystick.Resolve(phone), layout.Joystick.Resolve(tablet));
            Assert.AreEqual(phone.x - layout.Attack.Resolve(phone).x, tablet.x - layout.Attack.Resolve(tablet).x, 0.001f);
            Assert.AreEqual(layout.Attack.Resolve(phone).y, layout.Attack.Resolve(tablet).y, 0.001f);
        }

        [Test]
        public void FanSlotsAreOrderedLeftToRightAboveTheAttackButton()
        {
            Vector2[] slots = MobileControlsLayout.Default.FanSlotOffsetsFromAttack(3);
            Assert.Less(slots[0].x, slots[1].x);
            Assert.Less(slots[1].x, slots[2].x);
            foreach (Vector2 slot in slots)
            {
                Assert.Greater(slot.y, MobileControlsLayout.Default.Attack.Radius);
            }
        }

        [Test]
        public void FanSlotsDoNotOverlapEachOtherForAFourthDiscipline()
        {
            // Only the spacing is checked: a fourth slot needs the arc retuned to stay on screen.
            var problems = new List<string>();
            MobileControlsLayout.Default.Validate(new Vector2(800f, 360f), 4, problems);
            Assert.IsFalse(problems.Exists(p => p.StartsWith("Slots ")), string.Join("\n", problems));
        }

        [Test]
        public void TheCancelZoneIsKeptInsideTheSafeArea()
        {
            var bounds = new Rect(0f, 0f, 560f, 316f);
            Vector2 centre = MobileControlsLayout.PlaceCancelZone(new Vector2(456f, 160f), new Vector2(0f, 150f), 36f, 8f, bounds);

            Assert.LessOrEqual(centre.y + 36f, bounds.yMax - 8f + 0.001f);
            Assert.AreEqual(456f, centre.x, 0.001f);
        }

        [Test]
        public void TheCancelZoneIsAboveThePressWhenThereIsRoom()
        {
            var bounds = new Rect(0f, 0f, 1280f, 800f);
            Vector2 centre = MobileControlsLayout.PlaceCancelZone(new Vector2(1176f, 100f), new Vector2(0f, 150f), 36f, 8f, bounds);
            Assert.AreEqual(new Vector2(1176f, 250f), centre);
        }

        [Test]
        public void SafeAreaInsetsShrinkTheAreaTheLayoutIsPlacedIn()
        {
            // A notch on the left in landscape: the safe area starts 80 px in.
            SafeAreaFitter.CalculateAnchors(new Rect(80f, 0f, 2320f, 1080f), 2400, 1080, out Vector2 min, out Vector2 max);
            Assert.AreEqual(80f / 2400f, min.x, 0.0001f);
            Assert.AreEqual(1f, max.x, 0.0001f);
        }

        [Test]
        public void EarlierIconShapesKeepTheirSavedValues()
        {
            // Scenes store the shape as a number; new shapes are appended so existing icons do not change.
            Assert.AreEqual(0, (int)ProceduralShape.Disc);
            Assert.AreEqual(1, (int)ProceduralShape.Ring);
            Assert.AreEqual(2, (int)ProceduralShape.ChevronUp);
            Assert.AreEqual(3, (int)ProceduralShape.DoubleChevronUp);
            Assert.AreEqual(4, (int)ProceduralShape.Diamond);
        }

        // ------------------------------------------------------------ desktop

        [Test]
        public void TheDesktopHasAnEquivalentForEveryTouchAction()
        {
            string path = Path.Combine("Assets", "InputSystem_Actions.inputactions");
            Assert.IsTrue(File.Exists(path), $"{path} was not found from {Directory.GetCurrentDirectory()}.");
            string json = File.ReadAllText(path);

            foreach (string action in new[] { "Move", "Look", "Attack", "Block", "Jump", "DisciplineSlot1", "DisciplineSlot2",
                         "DisciplineSlot3", "Previous", "Next" })
            {
                StringAssert.Contains($"\"name\": \"{action}\"", json, $"Action '{action}' is missing.");
            }

            AssertBinding(json, "Block", "<Mouse>/rightButton");
            AssertBinding(json, "Block", "<Keyboard>/q");
            AssertBinding(json, "Block", "<Gamepad>/leftTrigger");
            AssertBinding(json, "DisciplineSlot1", "<Keyboard>/1");
            AssertBinding(json, "DisciplineSlot2", "<Keyboard>/2");
            AssertBinding(json, "DisciplineSlot3", "<Keyboard>/3");
            AssertBinding(json, "Attack", "<Mouse>/leftButton");
            AssertBinding(json, "Jump", "<Keyboard>/space");
        }

        [Test]
        public void NumberKeysChooseADisciplineAndDoNotAlsoStep()
        {
            string json = File.ReadAllText(Path.Combine("Assets", "InputSystem_Actions.inputactions"));
            Assert.IsFalse(HasBinding(json, "Previous", "<Keyboard>/1"));
            Assert.IsFalse(HasBinding(json, "Next", "<Keyboard>/2"));
        }

        private static void AssertBinding(string json, string action, string path)
        {
            Assert.IsTrue(HasBinding(json, action, path), $"'{action}' has no binding to {path}.");
        }

        private static bool HasBinding(string json, string action, string path)
        {
            // Bindings are written with "path" before "action" in the same object.
            string pattern = "\"path\": \"" + Regex.Escape(path) + "\"[^{}]*\"action\": \"" + Regex.Escape(action) + "\"";
            return Regex.IsMatch(json, pattern);
        }
    }
}
