using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.EditorTools;
using Threefold.Interface;
using Threefold.TouchControls;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// The setup command builds the new mobile controls, and upgrades controls of the earlier layout
    /// in place. These tests need the Unity Editor and replace the open scene, so like the other
    /// scene assembly tests they run only in batch mode.
    /// </summary>
    public sealed class MobileControlsSetupTests
    {
        [Test]
        public void TheAssembledSceneHasTheNewControls()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = FantasyPlaygroundSetup.BuildForAutomation();
            TouchControlsInput touch = SceneSetupUtility.FindInScene<TouchControlsInput>(scene);

            Assert.IsNotNull(touch);
            Assert.AreEqual(TouchControlsInput.CurrentLayoutVersion, touch.LayoutVersion);
            Assert.IsNotNull(touch.AttackGestureButton);
            Assert.IsNull(touch.LegacyAttackButton);
            Assert.IsNotNull(touch.BlockButton);
            Assert.IsNotNull(touch.JumpButton);
            Assert.IsNotNull(touch.DisciplineSelector);
            Assert.IsNotNull(touch.DisciplineSelector.Fan);
            Assert.IsNotNull(touch.Disciplines);
            Assert.IsTrue(touch.SprintButton == null || !touch.SprintButton.gameObject.activeSelf,
                "Only ATTACK, BLOCK and JUMP are permanent on the right.");

            var look = (RectTransform)touch.LookArea.transform;
            Assert.AreEqual(Vector2.zero, look.anchorMin, "The camera can be turned from any free part of the screen.");
            Assert.AreEqual(Vector2.one, look.anchorMax);

            var attack = (RectTransform)touch.AttackGestureButton.transform;
            Assert.Greater(attack.sizeDelta.x, ((RectTransform)touch.BlockButton.transform).sizeDelta.x);
            Assert.Greater(attack.sizeDelta.x, ((RectTransform)touch.JumpButton.transform).sizeDelta.x);
            Assert.IsNotNull(touch.GetComponentInParent<Canvas>().GetComponent<DensityCanvasScaler>());
            Assert.IsNotNull(attack.parent.GetComponent<SafeAreaFitter>(), "Buttons sit inside the safe area.");
        }

        [Test]
        public void ControlsOfTheEarlierLayoutAreUpgradedInPlace()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = FantasyPlaygroundSetup.BuildForAutomation();
            GameObject mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene).gameObject;
            Object.DestroyImmediate(SceneSetupUtility.FindInScene<TouchControlsInput>(scene).gameObject);

            TouchControlsInput old = BuildEarlierLayout(out GameObject oldAttack, out TouchButton sprint, out RectTransform lookZone);
            Assert.AreEqual(0, old.LayoutVersion);

            var report = new StringBuilder();
            TouchControlsSetup.Ensure(scene, mage, report);

            Assert.AreEqual(TouchControlsInput.CurrentLayoutVersion, old.LayoutVersion, report.ToString());
            Assert.IsNotNull(old.AttackGestureButton, report.ToString());
            Assert.AreSame(oldAttack, old.AttackGestureButton.gameObject, "The attack button object is kept.");
            Assert.IsNull(oldAttack.GetComponent<TouchButton>());
            Assert.AreEqual(MobileControlsLayout.Default.Attack.Position, ((RectTransform)oldAttack.transform).anchoredPosition);
            Assert.AreEqual(Vector2.zero, lookZone.anchorMin);
            Assert.IsFalse(sprint.gameObject.activeSelf);
            Assert.IsNotNull(old.BlockButton);
            Assert.IsNotNull(old.DisciplineSelector);

            int before = CountObjects(scene);
            TouchControlsSetup.Ensure(scene, mage, new StringBuilder());
            Assert.AreEqual(before, CountObjects(scene), "A second run must not add anything.");
        }

        /// <summary>Touch controls as Sprint 3 built them: floating joystick zone, right-hand camera area, sprint and a plain attack button.</summary>
        private static TouchControlsInput BuildEarlierLayout(out GameObject attack, out TouchButton sprint, out RectTransform lookZone)
        {
            var root = new GameObject("TouchControls", typeof(Canvas), typeof(CanvasGroup));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.AddComponent<DensityCanvasScaler>();

            RectTransform safeArea = TouchControlsSetup.CreateRect("SafeArea", root.transform);
            TouchControlsSetup.Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            RectTransform moveZone = TouchControlsSetup.CreateRect("MoveZone", safeArea);
            TouchControlsSetup.SetAnchors(moveZone, Vector2.zero, new Vector2(0.42f, 0.8f));
            VirtualJoystick joystick = moveZone.gameObject.AddComponent<VirtualJoystick>();
            RectTransform stickBase = TouchControlsSetup.CreateRect("JoystickBase", moveZone);
            TouchControlsSetup.PlaceAtCorner(stickBase, Vector2.zero, new Vector2(125f, 120f), 150f);
            RectTransform knob = TouchControlsSetup.CreateRect("JoystickKnob", stickBase);
            joystick.Bind(stickBase, knob, null);

            lookZone = TouchControlsSetup.CreateRect("LookZone", safeArea);
            TouchControlsSetup.SetAnchors(lookZone, new Vector2(0.42f, 0f), Vector2.one);
            TouchLookArea look = lookZone.gameObject.AddComponent<TouchLookArea>();

            TouchButton jump = TouchControlsSetup.BuildButton("JumpButton", safeArea, 84f, new Vector2(-82f, 82f), ProceduralShape.ChevronUp);
            sprint = TouchControlsSetup.BuildButton("SprintButton", safeArea, 64f, new Vector2(-184f, 62f), ProceduralShape.DoubleChevronUp);
            TouchButton plainAttack = TouchControlsSetup.BuildButton("AttackButton", safeArea, 92f, new Vector2(-90f, 194f), ProceduralShape.Diamond);
            attack = plainAttack.gameObject;

            TouchControlsInput touch = root.AddComponent<TouchControlsInput>();
            touch.Bind(joystick, look, jump, sprint, root.GetComponent<CanvasGroup>());
            touch.BindAttackButton(plainAttack);
            touch.MarkLayoutVersion(0);
            return touch;
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
