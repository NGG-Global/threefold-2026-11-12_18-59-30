using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.InputHandling;
using Threefold.Interface;
using Threefold.Magic;
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
    /// Builds the mobile controls and connects them to the Mage: a fixed joystick, camera drag on
    /// free screen space, ATTACK (tap, hold to charge, drag to aim), BLOCK, JUMP and the discipline
    /// selector on the ATTACK button. It runs as part of Threefold > Setup > Mage Prototype and the
    /// other scene commands, and is also available on its own.
    ///
    /// It is safe to run repeatedly. Missing parts are added and empty references filled. Controls
    /// built with the earlier layout (floating joystick, sprint button, plain attack button) are
    /// upgraded in place once: a control is moved only if it is still at its old default position,
    /// so positions tuned by hand are kept, and the report lists what changed.
    /// </summary>
    public static class TouchControlsSetup
    {
        private const string MenuPath = "Threefold/Setup/Touch Controls";
        private const string PreviewMenuPath = "Threefold/Controls/Preview Touch Controls In Editor";
        private const string DialogTitle = "Touch Controls Setup";
        private const string RootName = "TouchControls";
        private const string SafeAreaName = "SafeArea";
        private const int UiLayer = 5;

        // The first layout (Sprint 2 and 3), used to recognise controls that were never moved by hand.
        private const float OldMoveZoneWidth = 0.42f;
        private static readonly Vector2 OldJoystickPosition = new Vector2(125f, 120f);
        private const float OldJoystickSize = 150f;
        private static readonly Vector2 OldJumpPosition = new Vector2(-82f, 82f);
        private const float OldJumpSize = 84f;
        private static readonly Vector2 OldAttackPosition = new Vector2(-90f, 194f);
        private const float OldAttackSize = 92f;
        internal static readonly Vector2 OldInteractPosition = new Vector2(-198f, 170f);

        private const float MoveZoneHeight = 0.8f;

        // A warm ivory that reads on both dark and bright scenes without drawing attention.
        private static readonly Color FillColor = new Color(1f, 0.96f, 0.86f, 0.1f);
        private static readonly Color OutlineColor = new Color(1f, 0.96f, 0.86f, 0.55f);
        private static readonly Color KnobColor = new Color(1f, 0.96f, 0.86f, 0.6f);
        private static readonly Color ButtonColor = new Color(1f, 0.96f, 0.86f, 0.28f);
        private static readonly Color IconColor = new Color(1f, 0.98f, 0.92f, 0.9f);
        private static readonly Color SprintColor = new Color(1f, 0.84f, 0.5f, 0.85f);
        private static readonly Color SlotColor = new Color(0.12f, 0.11f, 0.1f, 0.55f);
        private static readonly Color CancelFillColor = new Color(0.12f, 0.05f, 0.05f, 0.45f);

        private static MobileControlsLayout Layout => MobileControlsLayout.Default;

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
                report.AppendLine($"Created '{RootName}' (fixed joystick, camera drag, jump button).");
            }
            else
            {
                report.AppendLine($"Kept existing touch controls on '{touch.name}'.");
            }

            if (touch.LayoutVersion < TouchControlsInput.CurrentLayoutVersion)
            {
                UpgradeEarlierLayout(touch, report);
            }

            EnsureActionControls(touch, report);
            EnsureSprintIndicator(touch, report);
            GiveMovingPartsTheirOwnCanvas(touch, report);
            CombinedCharacterInput combined = EnsureInputRouting(scene, mage, touch, report);
            EnsurePlayerActions(mage, combined, touch, report);
            EnsureSchemeSelector(scene, mage, touch, report);

            if (touch.LayoutVersion < TouchControlsInput.CurrentLayoutVersion)
            {
                Undo.RecordObject(touch, DialogTitle);
                touch.MarkLayoutVersion(TouchControlsInput.CurrentLayoutVersion);
                MarkChanged(touch);
            }
        }

        /// <summary>
        /// The joystick's knob and the ATTACK button's charge ring and aim marker change on every frame while
        /// they are used, and a change inside a canvas rebuilds the whole canvas. A canvas of their own limits
        /// that to them (LM-08). Nothing else changes: a nested canvas takes its parent's sorting and scale, and
        /// the controls read touches directly, not through the canvas.
        /// </summary>
        private static void GiveMovingPartsTheirOwnCanvas(TouchControlsInput touch, StringBuilder report)
        {
            int added = 0;
            foreach (Component part in new Component[] { touch.Joystick, touch.AttackGestureButton })
            {
                if (part != null && part.GetComponent<Canvas>() == null)
                {
                    Undo.AddComponent<Canvas>(part.gameObject);
                    added++;
                }
            }

            if (added > 0)
            {
                report.AppendLine($"Gave {added} touch control(s) a canvas of their own, so moving them no longer rebuilds every control.");
            }
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

            RectTransform safeArea = CreateRect(SafeAreaName, root.transform);
            Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            // The camera area comes first so that it is drawn underneath; it has no visuals anyway.
            RectTransform lookZone = CreateRect("LookZone", safeArea);
            Stretch(lookZone);
            TouchLookArea lookArea = lookZone.gameObject.AddComponent<TouchLookArea>();

            VirtualJoystick joystick = BuildJoystick(safeArea);
            TouchButton jump = BuildButton("JumpButton", safeArea, Layout.Jump, ProceduralShape.ChevronUp);

            TouchControlsInput touch = root.AddComponent<TouchControlsInput>();
            touch.Bind(joystick, lookArea, jump, null, rootGroup);
            touch.MarkLayoutVersion(TouchControlsInput.CurrentLayoutVersion);

            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            return touch;
        }

        private static VirtualJoystick BuildJoystick(RectTransform parent)
        {
            // The zone only groups the stick; the stick is taken within its activation radius.
            RectTransform moveZone = CreateRect("MoveZone", parent);
            SetAnchors(moveZone, Vector2.zero, new Vector2(0.5f, MoveZoneHeight));
            VirtualJoystick joystick = moveZone.gameObject.AddComponent<VirtualJoystick>();
            joystick.Radius = Layout.JoystickTravel;
            joystick.ActivationRadius = Layout.JoystickActivationRadius;

            RectTransform stickBase = CreateRect("JoystickBase", moveZone);
            PlaceAtCorner(stickBase, Layout.Joystick.Anchor, Layout.Joystick.Position, Layout.Joystick.Diameter);

            CanvasGroup stickGroup = stickBase.gameObject.AddComponent<CanvasGroup>();
            stickGroup.alpha = 0.55f;
            stickGroup.interactable = false;
            stickGroup.blocksRaycasts = false;

            Stretch(CreateShape("Fill", stickBase, ProceduralShape.Disc, FillColor).rectTransform);
            Stretch(CreateShape("Outline", stickBase, ProceduralShape.Ring, OutlineColor).rectTransform);

            RectTransform knob = CreateShape("JoystickKnob", stickBase, ProceduralShape.Disc, KnobColor).rectTransform;
            Centre(knob, Layout.JoystickKnobDiameter);

            joystick.Bind(stickBase, knob, stickGroup);
            return joystick;
        }

        /// <summary>A ring around the joystick that lights while sprinting.</summary>
        private static void EnsureSprintIndicator(TouchControlsInput touch, StringBuilder report)
        {
            VirtualJoystick joystick = touch.Joystick;
            if (joystick == null)
            {
                return;
            }

            var serialized = new SerializedObject(joystick);
            SerializedProperty indicator = serialized.FindProperty("sprintIndicator");
            SerializedProperty stickBase = serialized.FindProperty("baseRect");
            if (indicator == null || indicator.objectReferenceValue != null || stickBase == null ||
                !(stickBase.objectReferenceValue is RectTransform baseRect))
            {
                return;
            }

            Image ring = CreateShape("SprintRing", baseRect, ProceduralShape.ThickRing, SprintColor);
            RectTransform ringRect = ring.rectTransform;
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.one;
            ringRect.offsetMin = new Vector2(-10f, -10f);
            ringRect.offsetMax = new Vector2(10f, 10f);
            ring.enabled = false;
            Undo.RegisterCreatedObjectUndo(ring.gameObject, DialogTitle);

            Undo.RecordObject(joystick, DialogTitle);
            joystick.BindSprintIndicator(ring);
            MarkChanged(joystick);
            report.AppendLine("Added the sprint ring to the joystick (push past the ring to sprint).");
        }

        internal static TouchButton BuildButton(string name, Transform parent, ControlPlacement placement, ProceduralShape icon)
        {
            TouchButton button = BuildButton(name, parent, placement.Diameter, placement.Position, icon);
            button.HitPadding = placement.HitPadding;
            return button;
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

        // ---------------------------------------------------- action controls

        /// <summary>The object the buttons sit under, so new parts share their safe area.</summary>
        private static Transform ControlsParent(TouchControlsInput touch)
        {
            if (touch.JumpButton != null)
            {
                return touch.JumpButton.transform.parent;
            }

            Transform safeArea = touch.transform.Find(SafeAreaName);
            return safeArea != null ? safeArea : touch.transform;
        }

        /// <summary>Adds ATTACK (or upgrades the plain attack button), BLOCK and the discipline selector where missing.</summary>
        private static void EnsureActionControls(TouchControlsInput touch, StringBuilder report)
        {
            Transform parent = ControlsParent(touch);

            AttackGestureButton attack = touch.AttackGestureButton;
            if (attack == null)
            {
                attack = touch.LegacyAttackButton != null
                    ? UpgradeAttackButton(touch.LegacyAttackButton, report)
                    : BuildAttackButton(parent, report);
            }

            TouchButton block = touch.BlockButton;
            if (block == null)
            {
                block = BuildButton("BlockButton", parent, Layout.Block, ProceduralShape.Shield);
                Undo.RegisterCreatedObjectUndo(block.gameObject, DialogTitle);
                report.AppendLine("Added the BLOCK button (hold to block).");
            }

            DisciplineSelector selector = touch.DisciplineSelector;
            if (selector == null)
            {
                selector = BuildDisciplineSelector(parent, attack);
                report.AppendLine("Added the discipline selector on the ATTACK button (Arcane, Flame, Aegis).");
            }

            EnsureAttackOverlays(attack, parent, report);

            if (touch.AttackGestureButton != attack || touch.BlockButton != block || touch.DisciplineSelector != selector)
            {
                Undo.RecordObject(touch, DialogTitle);
                touch.BindActionControls(attack, block, selector);
                MarkChanged(touch);
            }
        }

        private static AttackGestureButton BuildAttackButton(Transform parent, StringBuilder report)
        {
            ControlPlacement placement = Layout.Attack;
            RectTransform rect = CreateRect("AttackButton", parent);
            PlaceAtCorner(rect, placement.Anchor, placement.Position, placement.Diameter);

            Image background = CreateShape("Background", rect, ProceduralShape.Disc, ButtonColor);
            Stretch(background.rectTransform);

            AttackGestureButton attack = rect.gameObject.AddComponent<AttackGestureButton>();
            attack.HitPadding = placement.HitPadding;
            BindAttackVisuals(attack, rect, background, null, null);

            Undo.RegisterCreatedObjectUndo(rect.gameObject, DialogTitle);
            report.AppendLine("Added the ATTACK button (tap to strike, hold to charge, drag to aim).");
            return attack;
        }

        /// <summary>
        /// Replaces the plain attack button's component with the gesture button on the same object,
        /// keeping its background and icon. It is moved to the new position only if it was never moved.
        /// </summary>
        private static AttackGestureButton UpgradeAttackButton(TouchButton legacy, StringBuilder report)
        {
            GameObject buttonObject = legacy.gameObject;
            var rect = (RectTransform)buttonObject.transform;

            Graphic background = FindChildGraphic(rect, "Background");
            Graphic outline = FindChildGraphic(rect, "Outline");
            Transform iconTransform = rect.Find("Icon");
            ProceduralImage icon = iconTransform != null ? iconTransform.GetComponent<ProceduralImage>() : null;

            Undo.DestroyObjectImmediate(legacy);
            AttackGestureButton attack = Undo.AddComponent<AttackGestureButton>(buttonObject);
            attack.HitPadding = Layout.Attack.HitPadding;

            if (IsAt(rect, OldAttackPosition, OldAttackSize))
            {
                Undo.RecordObject(rect, DialogTitle);
                rect.anchoredPosition = Layout.Attack.Position;
                rect.sizeDelta = Vector2.one * Layout.Attack.Diameter;
                report.AppendLine("Moved the ATTACK button to the new layout and enlarged it.");
            }
            else
            {
                report.AppendLine("Kept the ATTACK button where it was placed by hand.");
            }

            BindAttackVisuals(attack, rect, background, outline, icon);
            MarkChanged(attack);
            report.AppendLine("Upgraded the attack button to tap, hold-to-charge and drag-to-aim.");
            return attack;
        }

        /// <summary>Adds the parts the gesture button needs that are missing, and connects them.</summary>
        private static void BindAttackVisuals(AttackGestureButton attack, RectTransform rect, Graphic background,
            Graphic accentRing, ProceduralImage icon)
        {
            if (background == null)
            {
                Image fill = CreateShape("Background", rect, ProceduralShape.Disc, ButtonColor);
                Stretch(fill.rectTransform);
                fill.rectTransform.SetAsFirstSibling();
                background = fill;
                Undo.RegisterCreatedObjectUndo(fill.gameObject, DialogTitle);
            }

            if (accentRing == null)
            {
                Image ring = CreateShape("AccentRing", rect, ProceduralShape.Ring, OutlineColor);
                Stretch(ring.rectTransform);
                accentRing = ring;
                Undo.RegisterCreatedObjectUndo(ring.gameObject, DialogTitle);
            }

            Image meter = CreateShape("ChargeMeter", rect, ProceduralShape.ThickRing, Color.white);
            meter.rectTransform.anchorMin = Vector2.zero;
            meter.rectTransform.anchorMax = Vector2.one;
            meter.rectTransform.offsetMin = new Vector2(-8f, -8f);
            meter.rectTransform.offsetMax = new Vector2(8f, 8f);
            meter.type = Image.Type.Filled;
            meter.fillMethod = Image.FillMethod.Radial360;
            meter.fillOrigin = (int)Image.Origin360.Top;
            meter.fillClockwise = true;
            meter.fillAmount = 0f;
            meter.enabled = false;
            Undo.RegisterCreatedObjectUndo(meter.gameObject, DialogTitle);

            if (icon == null)
            {
                Image symbol = CreateShape("Icon", rect, ProceduralShape.Star, IconColor);
                Centre(symbol.rectTransform, Layout.Attack.Diameter * 0.5f);
                icon = symbol.GetComponent<ProceduralImage>();
                Undo.RegisterCreatedObjectUndo(symbol.gameObject, DialogTitle);
            }
            else
            {
                icon.transform.SetAsLastSibling();
            }

            attack.Bind(background, accentRing, icon, meter);
        }

        /// <summary>The aim ring and the cancel zone sit beside the button so pressing it does not scale them.</summary>
        private static void EnsureAttackOverlays(AttackGestureButton attack, Transform parent, StringBuilder report)
        {
            var serialized = new SerializedObject(attack);
            SerializedProperty aimRoot = serialized.FindProperty("aimRoot");
            SerializedProperty cancelZone = serialized.FindProperty("cancelZone");
            if (aimRoot == null || cancelZone == null ||
                (aimRoot.objectReferenceValue != null && cancelZone.objectReferenceValue != null))
            {
                return;
            }

            var attackRect = (RectTransform)attack.transform;
            float aimDiameter = attack.Gesture.aimRadius * 2f;

            RectTransform aim = CreateRect("AttackAim", parent);
            PlaceAtCorner(aim, attackRect.anchorMin, attackRect.anchoredPosition, aimDiameter);
            CanvasGroup aimGroup = aim.gameObject.AddComponent<CanvasGroup>();
            aimGroup.alpha = 0f;
            aimGroup.interactable = false;
            aimGroup.blocksRaycasts = false;
            Stretch(CreateShape("AimRing", aim, ProceduralShape.Ring, OutlineColor).rectTransform);
            Image marker = CreateShape("AimMarker", aim, ProceduralShape.Disc, Color.white);
            Centre(marker.rectTransform, 26f);
            Undo.RegisterCreatedObjectUndo(aim.gameObject, DialogTitle);

            RectTransform cancel = CreateRect("AttackCancelZone", parent);
            PlaceAtCorner(cancel, attackRect.anchorMin, attackRect.anchoredPosition + Layout.CancelOffset, Layout.CancelDiameter);
            CanvasGroup cancelGroup = cancel.gameObject.AddComponent<CanvasGroup>();
            cancelGroup.alpha = 0f;
            cancelGroup.interactable = false;
            cancelGroup.blocksRaycasts = false;
            Stretch(CreateShape("Fill", cancel, ProceduralShape.Disc, CancelFillColor).rectTransform);
            Image cancelRing = CreateShape("Outline", cancel, ProceduralShape.Ring, OutlineColor);
            Stretch(cancelRing.rectTransform);
            Centre(CreateShape("Icon", cancel, ProceduralShape.Cross, IconColor).rectTransform, Layout.CancelDiameter * 0.5f);
            Undo.RegisterCreatedObjectUndo(cancel.gameObject, DialogTitle);

            Undo.RecordObject(attack, DialogTitle);
            attack.BindAimAndCancel(aim, aimGroup, marker.rectTransform, marker, cancel, cancelGroup, cancelRing);
            MarkChanged(attack);
            report.AppendLine("Added the aim ring and the cancel zone for charged attacks.");
        }

        private static DisciplineSelector BuildDisciplineSelector(Transform parent, AttackGestureButton attack)
        {
            var attackRect = (RectTransform)attack.transform;
            Vector2 attackPosition = attackRect.anchoredPosition;
            Vector2 anchor = attackRect.anchorMin;
            int count = DisciplineCatalog.Count;

            // The indicator, on the rim of the attack button.
            RectTransform indicatorRect = CreateRect("DisciplineIndicator", parent);
            PlaceAtCorner(indicatorRect, anchor, attackPosition + Layout.IndicatorOffset, Layout.IndicatorDiameter);
            DisciplineInfo first = DisciplineCatalog.Get(0);
            Image indicatorFill = CreateShape("Background", indicatorRect, ProceduralShape.Disc, first.Color);
            Stretch(indicatorFill.rectTransform);
            Stretch(CreateShape("Outline", indicatorRect, ProceduralShape.Ring, OutlineColor).rectTransform);
            Image indicatorSymbol = CreateShape("Icon", indicatorRect, first.Icon, IconColor);
            Centre(indicatorSymbol.rectTransform, Layout.IndicatorDiameter * 0.62f);
            DisciplineSelector selector = indicatorRect.gameObject.AddComponent<DisciplineSelector>();

            // The fan, centred on the attack button, with the slots in an arc above it.
            float fanSize = (Layout.FanRadius + Layout.SlotDiameter) * 2f;
            RectTransform fanRect = CreateRect("DisciplineFan", parent);
            PlaceAtCorner(fanRect, anchor, attackPosition, fanSize);
            CanvasGroup fanGroup = fanRect.gameObject.AddComponent<CanvasGroup>();
            fanGroup.alpha = 0f;
            fanGroup.interactable = false;
            fanGroup.blocksRaycasts = false;
            DisciplineFan fan = fanRect.gameObject.AddComponent<DisciplineFan>();

            Vector2[] offsets = Layout.FanSlotOffsetsFromAttack(count);
            var slots = new RectTransform[count];
            var fills = new Graphic[count];
            var symbols = new ProceduralImage[count];
            var rings = new Graphic[count];

            for (int i = 0; i < count; i++)
            {
                DisciplineInfo info = DisciplineCatalog.Get(i);
                RectTransform slot = CreateRect("Slot" + info.Name, fanRect);
                PlaceAtCorner(slot, new Vector2(0.5f, 0.5f), offsets[i], Layout.SlotDiameter);

                Image fill = CreateShape("Background", slot, ProceduralShape.Disc, SlotColor);
                Stretch(fill.rectTransform);
                Image ring = CreateShape("CurrentRing", slot, ProceduralShape.ThickRing, OutlineColor);
                Stretch(ring.rectTransform);
                ring.enabled = i == 0;
                Image symbol = CreateShape("Icon", slot, info.Icon, info.Color);
                Centre(symbol.rectTransform, Layout.SlotDiameter * 0.62f);

                slots[i] = slot;
                fills[i] = fill;
                symbols[i] = symbol.GetComponent<ProceduralImage>();
                rings[i] = ring;
            }

            selector.Bind(fan, indicatorFill, indicatorSymbol.GetComponent<ProceduralImage>(), fanGroup, slots, fills,
                symbols, rings);

            Undo.RegisterCreatedObjectUndo(indicatorRect.gameObject, DialogTitle);
            Undo.RegisterCreatedObjectUndo(fanRect.gameObject, DialogTitle);
            return selector;
        }

        // ------------------------------------------------------------- upgrade

        /// <summary>
        /// Converts controls built with the earlier layout. Each change is made only if the part still
        /// has its old default value, so hand-tuned parts are kept; the report says which.
        /// </summary>
        private static void UpgradeEarlierLayout(TouchControlsInput touch, StringBuilder report)
        {
            report.AppendLine("Upgrading touch controls from the earlier layout:");

            TouchLookArea lookArea = touch.LookArea;
            if (lookArea != null)
            {
                var rect = (RectTransform)lookArea.transform;
                bool oldAnchors = Mathf.Approximately(rect.anchorMin.x, OldMoveZoneWidth) && rect.anchorMin.y == 0f &&
                                  rect.anchorMax == Vector2.one;
                if (oldAnchors)
                {
                    Undo.RecordObject(rect, DialogTitle);
                    Stretch(rect);
                    report.AppendLine("- The camera can now be turned from any free part of the screen.");
                }
                else if (rect.anchorMin != Vector2.zero || rect.anchorMax != Vector2.one)
                {
                    report.AppendLine("- Kept the camera area's hand-made size; it does not cover the whole screen.");
                }
            }

            if (touch.Joystick != null)
            {
                report.AppendLine("- The joystick is now fixed: it no longer jumps to the thumb, and touches away from it do nothing.");

                SerializedProperty stickBase = new SerializedObject(touch.Joystick).FindProperty("baseRect");
                if (stickBase != null && stickBase.objectReferenceValue is RectTransform baseRect &&
                    IsAt(baseRect, OldJoystickPosition, OldJoystickSize))
                {
                    Undo.RecordObject(baseRect, DialogTitle);
                    baseRect.anchoredPosition = Layout.Joystick.Position;
                    report.AppendLine("- Moved the joystick to the new layout position.");
                }
            }

            TouchButton jump = touch.JumpButton;
            if (jump != null && IsAt((RectTransform)jump.transform, OldJumpPosition, OldJumpSize))
            {
                var rect = (RectTransform)jump.transform;
                Undo.RecordObject(rect, DialogTitle);
                rect.anchoredPosition = Layout.Jump.Position;
                rect.sizeDelta = Vector2.one * Layout.Jump.Diameter;
                report.AppendLine("- Moved the JUMP button to the new layout.");
            }

            TouchButton sprint = touch.SprintButton;
            if (sprint != null && sprint.gameObject.activeSelf)
            {
                Undo.RecordObject(sprint.gameObject, DialogTitle);
                sprint.gameObject.SetActive(false);
                report.AppendLine("- Hid the sprint button: push the joystick past its ring to sprint. " +
                                  "Activate 'SprintButton' again to bring the button back.");
            }

            TouchButton interact = touch.InteractButton;
            if (interact != null && IsAt((RectTransform)interact.transform, OldInteractPosition, Layout.Interact.Diameter))
            {
                var rect = (RectTransform)interact.transform;
                Undo.RecordObject(rect, DialogTitle);
                rect.anchoredPosition = Layout.Interact.Position;
                report.AppendLine("- Moved the interact button clear of BLOCK.");
            }
        }

        private static bool IsAt(RectTransform rect, Vector2 position, float size)
        {
            return (rect.anchoredPosition - position).sqrMagnitude < 0.25f &&
                   Mathf.Abs(rect.sizeDelta.x - size) < 0.5f && Mathf.Abs(rect.sizeDelta.y - size) < 0.5f;
        }

        private static Graphic FindChildGraphic(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            return child != null ? child.GetComponent<Graphic>() : null;
        }

        // --------------------------------------------------------------- wiring

        /// <summary>
        /// Routes the Mage and the camera through one combined input source that merges the desktop
        /// input with the touch controls. This is the one place where setup replaces an existing
        /// reference: the movement and camera scripts must read the combined source to see touch.
        /// </summary>
        private static CombinedCharacterInput EnsureInputRouting(Scene scene, GameObject mage, TouchControlsInput touch,
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

            return combined;
        }

        /// <summary>The block and discipline components on the Mage, and the touch display of the discipline.</summary>
        private static void EnsurePlayerActions(GameObject mage, CombinedCharacterInput combined, TouchControlsInput touch,
            StringBuilder report)
        {
            if (!mage.TryGetComponent(out PlayerDisciplines disciplines))
            {
                disciplines = Undo.AddComponent<PlayerDisciplines>(mage);
                disciplines.Bind(combined);
                MarkChanged(disciplines);
                report.AppendLine("Added discipline selection to the Mage (Arcane, Flame, Aegis; keys 1 to 3).");
            }
            else if (disciplines.InputSource == null)
            {
                Undo.RecordObject(disciplines, DialogTitle);
                disciplines.Bind(combined);
                MarkChanged(disciplines);
            }

            if (!mage.TryGetComponent(out PlayerGuard guard))
            {
                guard = Undo.AddComponent<PlayerGuard>(mage);
                guard.Bind(combined);
                MarkChanged(guard);
                report.AppendLine("Added block input to the Mage (press, hold, release; no damage reduction yet).");
            }
            else if (guard.InputSource == null)
            {
                Undo.RecordObject(guard, DialogTitle);
                guard.Bind(combined);
                MarkChanged(guard);
            }

            if (touch.Disciplines == null)
            {
                Undo.RecordObject(touch, DialogTitle);
                touch.BindDisciplines(disciplines);
                MarkChanged(touch);
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
