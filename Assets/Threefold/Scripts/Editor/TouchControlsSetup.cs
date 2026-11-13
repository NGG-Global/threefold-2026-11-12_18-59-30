using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.InputHandling;
using Threefold.Interface;
using Threefold.TouchControls;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the on-screen touch controls and connects them to the Mage. It runs as part of
    /// Threefold > Setup > Mage Prototype and is also available on its own.
    ///
    /// It is safe to run repeatedly. Existing touch controls are kept exactly as they are; to
    /// rebuild them with the default layout, delete the "TouchControls" object and run it again.
    /// </summary>
    public static class TouchControlsSetup
    {
        private const string MenuPath = "Threefold/Setup/Touch Controls";
        private const string PreviewMenuPath = "Threefold/Controls/Preview Touch Controls In Editor";
        private const string DialogTitle = "Touch Controls Setup";
        private const string RootName = "TouchControls";
        private const int UiLayer = 5;

        // Layout, in canvas units. One unit is one density-independent pixel (see DensityCanvasScaler).
        private const float MoveZoneWidth = 0.42f;
        private const float MoveZoneHeight = 0.8f;
        private const float JoystickSize = 150f;
        private const float KnobSize = 62f;
        private const float JumpButtonSize = 84f;
        private const float SprintButtonSize = 64f;
        private const float AttackButtonSize = 92f;
        private static readonly Vector2 JoystickRestPosition = new Vector2(125f, 120f);
        private static readonly Vector2 JumpButtonPosition = new Vector2(-82f, 82f);
        private static readonly Vector2 SprintButtonPosition = new Vector2(-184f, 62f);
        private static readonly Vector2 AttackButtonPosition = new Vector2(-90f, 194f);

        // A warm ivory that reads on both dark and bright scenes without drawing attention.
        private static readonly Color FillColor = new Color(1f, 0.96f, 0.86f, 0.1f);
        private static readonly Color OutlineColor = new Color(1f, 0.96f, 0.86f, 0.55f);
        private static readonly Color KnobColor = new Color(1f, 0.96f, 0.86f, 0.6f);
        private static readonly Color ButtonColor = new Color(1f, 0.96f, 0.86f, 0.28f);
        private static readonly Color IconColor = new Color(1f, 0.98f, 0.92f, 0.9f);

        [MenuItem(MenuPath, false, 1)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
            if (motor == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "The open scene has no Mage yet. Run Threefold > Setup > Mage Prototype instead; " +
                    "it creates the Mage and the touch controls together.", "OK");
                return;
            }

            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            Ensure(scene, motor.gameObject, report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nSave the scene with Ctrl+S.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(PreviewMenuPath, false, 20)]
        private static void TogglePreview()
        {
            bool enabled = !EditorPrefs.GetBool(ControlSchemeSelector.EditorPreviewPreferenceKey, false);
            EditorPrefs.SetBool(ControlSchemeSelector.EditorPreviewPreferenceKey, enabled);
        }

        [MenuItem(PreviewMenuPath, true)]
        private static bool TogglePreviewValidate()
        {
            Menu.SetChecked(PreviewMenuPath,
                EditorPrefs.GetBool(ControlSchemeSelector.EditorPreviewPreferenceKey, false));
            return true;
        }

        /// <summary>Adds whatever is missing for touch control of the given Mage in the given scene.</summary>
        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            TouchControlsInput touch = FindInScene<TouchControlsInput>(scene);
            if (touch == null)
            {
                touch = BuildControls();
                report.AppendLine($"Created '{RootName}' (joystick, camera look area, jump and sprint buttons).");
            }
            else
            {
                report.AppendLine($"Kept existing touch controls on '{touch.name}'.");
            }

            EnsureAttackButton(touch, report);
            EnsureInputRouting(scene, mage, touch, report);
            EnsureSchemeSelector(scene, mage, touch, report);
        }

        /// <summary>
        /// Adds the attack button to touch controls that do not have one yet. This also upgrades
        /// controls that were created before combat existed, without changing their other parts.
        /// </summary>
        private static void EnsureAttackButton(TouchControlsInput touch, StringBuilder report)
        {
            if (touch.AttackButton != null)
            {
                return;
            }

            // Sit beside the existing buttons so the new one shares their safe-area parent.
            Transform parent = touch.JumpButton != null ? touch.JumpButton.transform.parent : touch.transform;

            TouchButton attack = BuildButton("AttackButton", parent, AttackButtonSize, AttackButtonPosition,
                ProceduralShape.Diamond);
            Undo.RegisterCreatedObjectUndo(attack.gameObject, DialogTitle);

            Undo.RecordObject(touch, DialogTitle);
            touch.BindAttackButton(attack);
            MarkChanged(touch);
            report.AppendLine("Added the attack button to the touch controls.");
        }

        // ------------------------------------------------------------ hierarchy

        private static TouchControlsInput BuildControls()
        {
            var root = new GameObject(RootName, typeof(Canvas), typeof(CanvasGroup));
            root.layer = UiLayer;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            root.AddComponent<DensityCanvasScaler>();

            CanvasGroup rootGroup = root.GetComponent<CanvasGroup>();
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;

            RectTransform safeArea = CreateRect("SafeArea", root.transform);
            Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            VirtualJoystick joystick = BuildJoystick(safeArea);

            RectTransform lookZone = CreateRect("LookZone", safeArea);
            SetAnchors(lookZone, new Vector2(MoveZoneWidth, 0f), Vector2.one);
            TouchLookArea lookArea = lookZone.gameObject.AddComponent<TouchLookArea>();

            TouchButton jump = BuildButton("JumpButton", safeArea, JumpButtonSize, JumpButtonPosition,
                ProceduralShape.ChevronUp);
            TouchButton sprint = BuildButton("SprintButton", safeArea, SprintButtonSize, SprintButtonPosition,
                ProceduralShape.DoubleChevronUp);

            TouchControlsInput touch = root.AddComponent<TouchControlsInput>();
            touch.Bind(joystick, lookArea, jump, sprint, rootGroup);

            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            return touch;
        }

        private static VirtualJoystick BuildJoystick(RectTransform parent)
        {
            RectTransform moveZone = CreateRect("MoveZone", parent);
            SetAnchors(moveZone, Vector2.zero, new Vector2(MoveZoneWidth, MoveZoneHeight));
            VirtualJoystick joystick = moveZone.gameObject.AddComponent<VirtualJoystick>();

            RectTransform stickBase = CreateRect("JoystickBase", moveZone);
            PlaceAtCorner(stickBase, Vector2.zero, JoystickRestPosition, JoystickSize);

            CanvasGroup stickGroup = stickBase.gameObject.AddComponent<CanvasGroup>();
            stickGroup.alpha = 0.45f;
            stickGroup.interactable = false;
            stickGroup.blocksRaycasts = false;

            Stretch(CreateShape("Fill", stickBase, ProceduralShape.Disc, FillColor).rectTransform);
            Stretch(CreateShape("Outline", stickBase, ProceduralShape.Ring, OutlineColor).rectTransform);

            RectTransform knob = CreateShape("JoystickKnob", stickBase, ProceduralShape.Disc, KnobColor).rectTransform;
            Centre(knob, KnobSize);

            joystick.Bind(stickBase, knob, stickGroup);
            return joystick;
        }

        internal static TouchButton BuildButton(string name, Transform parent, float size, Vector2 position,
            ProceduralShape icon)
        {
            RectTransform rect = CreateRect(name, parent);
            PlaceAtCorner(rect, new Vector2(1f, 0f), position, size);

            Image background = CreateShape("Background", rect, ProceduralShape.Disc, ButtonColor);
            Stretch(background.rectTransform);
            Stretch(CreateShape("Outline", rect, ProceduralShape.Ring, OutlineColor).rectTransform);
            Centre(CreateShape("Icon", rect, icon, IconColor).rectTransform, size * 0.62f);

            TouchButton button = rect.gameObject.AddComponent<TouchButton>();
            button.Bind(background);
            return button;
        }

        internal static Image CreateShape(string name, Transform parent, ProceduralShape shape, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            rect.gameObject.AddComponent<ProceduralImage>().Shape = shape;
            return image;
        }

        internal static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = UiLayer;
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        internal static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void Stretch(RectTransform rect)
        {
            SetAnchors(rect, Vector2.zero, Vector2.one);
        }

        internal static void Centre(RectTransform rect, float size)
        {
            PlaceAtCorner(rect, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        }

        /// <summary>Gives the rect a fixed square size, positioned relative to an anchor point of its parent.</summary>
        internal static void PlaceAtCorner(RectTransform rect, Vector2 anchor, Vector2 position, float size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = position;
        }

        // --------------------------------------------------------------- wiring

        /// <summary>
        /// Routes the Mage and the camera through one combined input source that merges the desktop
        /// input with the touch controls. This is the one place where setup replaces an existing
        /// reference: the movement and camera scripts must read the combined source to see touch.
        /// </summary>
        private static void EnsureInputRouting(Scene scene, GameObject mage, TouchControlsInput touch,
            StringBuilder report)
        {
            ThirdPersonMotor motor = mage.GetComponent<ThirdPersonMotor>();

            if (!mage.TryGetComponent(out CombinedCharacterInput combined))
            {
                combined = Undo.AddComponent<CombinedCharacterInput>(mage);
                report.AppendLine($"Added {nameof(CombinedCharacterInput)} to merge desktop and touch input.");
            }

            Undo.RecordObject(combined, DialogTitle);
            bool sourcesChanged = false;

            if (motor != null && motor.InputSource != null)
            {
                sourcesChanged |= combined.AddSource(motor.InputSource);
            }

            foreach (CharacterInputSource source in mage.GetComponents<CharacterInputSource>())
            {
                sourcesChanged |= combined.AddSource(source);
            }

            sourcesChanged |= combined.AddSource(touch);

            if (sourcesChanged)
            {
                MarkChanged(combined);
                report.AppendLine("Registered the desktop and touch input sources.");
            }

            if (motor != null && motor.InputSource != combined)
            {
                Undo.RecordObject(motor, DialogTitle);
                motor.InputSource = combined;
                MarkChanged(motor);
                report.AppendLine("Pointed the movement script at the combined input.");
            }

            ThirdPersonCamera cameraRig = FindInScene<ThirdPersonCamera>(scene);
            bool followsMage = cameraRig != null && (cameraRig.Target == null || cameraRig.Target == mage.transform);
            if (followsMage && cameraRig.InputSource != combined)
            {
                Undo.RecordObject(cameraRig, DialogTitle);
                cameraRig.InputSource = combined;
                MarkChanged(cameraRig);
                report.AppendLine("Pointed the camera at the combined input.");
            }
        }

        private static void EnsureSchemeSelector(Scene scene, GameObject mage, TouchControlsInput touch,
            StringBuilder report)
        {
            if (FindInScene<ControlSchemeSelector>(scene) != null)
            {
                return;
            }

            ControlSchemeSelector selector = Undo.AddComponent<ControlSchemeSelector>(touch.gameObject);
            InputSystemCharacterInput desktop = mage.GetComponent<InputSystemCharacterInput>();
            selector.Bind(desktop, touch);
            MarkChanged(selector);
            report.AppendLine($"Added {nameof(ControlSchemeSelector)} (automatic choice between touch and desktop).");

            if (desktop == null)
            {
                report.AppendLine($"WARNING: '{mage.name}' has no {nameof(InputSystemCharacterInput)}, so mouse " +
                                  "capture cannot be switched off while touch controls are shown.");
            }
        }
    }
}
