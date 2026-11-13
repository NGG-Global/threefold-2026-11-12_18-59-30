using System.Text;
using Threefold.Characters;
using Threefold.CombatFeedback;
using Threefold.InputHandling;
using Threefold.Interaction;
using Threefold.Interface;
using Threefold.TouchControls;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Lets the Mage use things in the world: adds the interactor to the Mage, the context-sensitive
    /// prompt to the HUD and the interact button to the touch controls. It runs as part of every
    /// scene setup command that builds the playable rig. Existing parts are kept.
    /// </summary>
    internal static class InteractionSetup
    {
        private const string DialogTitle = "Interaction Setup";
        private const string PromptName = "InteractionPrompt";
        private const string ButtonName = "InteractButton";

        // Beside the attack button, above sprint, within reach of the right thumb.
        private const float ButtonSize = 72f;
        private static readonly Vector2 ButtonPosition = new Vector2(-198f, 170f);

        private static readonly Color ButtonColor = new Color(1f, 0.96f, 0.86f, 0.28f);
        private static readonly Color OutlineColor = new Color(1f, 0.96f, 0.86f, 0.55f);
        private static readonly Color TextColor = new Color(1f, 0.97f, 0.9f, 0.95f);
        private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.08f, 0.62f);
        private static readonly Color BadgeColor = new Color(1f, 0.96f, 0.86f, 0.9f);
        private static readonly Color BadgeTextColor = new Color(0.06f, 0.07f, 0.1f, 1f);

        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            Interactor interactor = EnsureInteractor(mage, report);

            TouchControlsInput touch = FindInScene<TouchControlsInput>(scene);
            if (touch != null)
            {
                EnsureTouchButton(touch, report);
            }
            else
            {
                report.AppendLine("WARNING: there are no touch controls, so there is no interact button for touch.");
            }

            EnsurePrompt(scene, mage, interactor, touch, report);
        }

        private static Interactor EnsureInteractor(GameObject mage, StringBuilder report)
        {
            if (!mage.TryGetComponent(out Interactor interactor))
            {
                interactor = Undo.AddComponent<Interactor>(mage);
                report.AppendLine("Added the interactor to the Mage (interact with E, the gamepad's north button, or the touch button).");
            }

            if (interactor.InputSource == null)
            {
                CharacterInputSource input = FindInput(mage);
                if (input != null)
                {
                    Undo.RecordObject(interactor, DialogTitle);
                    interactor.InputSource = input;
                    MarkChanged(interactor);
                    report.AppendLine("Connected the interactor to the Mage's input.");
                }
                else
                {
                    report.AppendLine("WARNING: the Mage has no input source, so the interactor cannot be used.");
                }
            }

            return interactor;
        }

        /// <summary>The combined input if there is one, so keyboard, gamepad and touch all work.</summary>
        private static CharacterInputSource FindInput(GameObject mage)
        {
            if (mage.TryGetComponent(out CombinedCharacterInput combined))
            {
                return combined;
            }

            if (mage.TryGetComponent(out ThirdPersonMotor motor) && motor.InputSource != null)
            {
                return motor.InputSource;
            }

            return mage.TryGetComponent(out CharacterInputSource source) ? source : null;
        }

        /// <summary>
        /// Adds the interact button to touch controls that do not have one yet. It starts hidden and
        /// appears only while there is something to use.
        /// </summary>
        private static void EnsureTouchButton(TouchControlsInput touch, StringBuilder report)
        {
            if (touch.InteractButton != null)
            {
                return;
            }

            // Sit beside the existing buttons so the new one shares their safe-area parent.
            Transform parent = touch.JumpButton != null ? touch.JumpButton.transform.parent : touch.transform;

            RectTransform rect = TouchControlsSetup.CreateRect(ButtonName, parent);
            TouchControlsSetup.PlaceAtCorner(rect, new Vector2(1f, 0f), ButtonPosition, ButtonSize);

            Image background = TouchControlsSetup.CreateShape("Background", rect, ProceduralShape.Disc, ButtonColor);
            TouchControlsSetup.Stretch(background.rectTransform);
            TouchControlsSetup.Stretch(TouchControlsSetup.CreateShape("Outline", rect, ProceduralShape.Ring, OutlineColor).rectTransform);

            Text label = CreateText("Label", rect, "Use", 16, TextColor, TextAnchor.MiddleCenter);
            TouchControlsSetup.Stretch(label.rectTransform);

            TouchButton button = rect.gameObject.AddComponent<TouchButton>();
            button.Bind(background);
            rect.gameObject.SetActive(false);
            Undo.RegisterCreatedObjectUndo(rect.gameObject, DialogTitle);

            Undo.RecordObject(touch, DialogTitle);
            touch.BindInteractButton(button, label);
            MarkChanged(touch);
            report.AppendLine("Added the interact button to the touch controls. It appears only while something can be used.");
        }

        /// <summary>A small label that follows the thing in reach: the key, then the verb.</summary>
        private static void EnsurePrompt(Scene scene, GameObject mage, Interactor interactor, TouchControlsInput touch,
            StringBuilder report)
        {
            if (FindInScene<InteractionPrompt>(scene) != null)
            {
                return;
            }

            PlayerHud hud = FindInScene<PlayerHud>(scene);
            if (hud == null)
            {
                report.AppendLine("WARNING: the combat HUD was not found, so no interaction prompt was added.");
                return;
            }

            var canvasRect = (RectTransform)hud.transform;
            RectTransform marker = TouchControlsSetup.CreateRect(PromptName, canvasRect);
            marker.anchorMin = new Vector2(0.5f, 0.5f);
            marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0f);
            marker.sizeDelta = new Vector2(150f, 34f);
            marker.anchoredPosition = Vector2.zero;

            CanvasGroup group = marker.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Image panel = marker.gameObject.AddComponent<Image>();
            panel.color = PanelColor;
            panel.raycastTarget = false;

            Image badge = TouchControlsSetup.CreateShape("KeyBadge", marker, ProceduralShape.Disc, BadgeColor);
            RectTransform badgeRect = badge.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(26f, 26f);
            badgeRect.anchoredPosition = new Vector2(19f, 0f);

            Text key = CreateText("Key", badgeRect, "E", 15, BadgeTextColor, TextAnchor.MiddleCenter);
            TouchControlsSetup.Stretch(key.rectTransform);

            Text verb = CreateText("Verb", marker, "Use", 17, TextColor, TextAnchor.MiddleLeft);
            RectTransform verbRect = verb.rectTransform;
            verbRect.anchorMin = Vector2.zero;
            verbRect.anchorMax = Vector2.one;
            verbRect.offsetMin = new Vector2(40f, 0f);
            verbRect.offsetMax = new Vector2(-8f, 0f);

            InteractionPrompt prompt = marker.gameObject.AddComponent<InteractionPrompt>();
            prompt.Bind(interactor, canvasRect, marker, group, verb, badge.gameObject, key,
                FindInScene<ControlSchemeSelector>(scene), mage.GetComponent<InputSystemCharacterInput>(), touch);

            Undo.RegisterCreatedObjectUndo(marker.gameObject, DialogTitle);
            report.AppendLine("Added the interaction prompt to the HUD.");
        }

        private static Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor alignment)
        {
            RectTransform rect = TouchControlsSetup.CreateRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont.Default;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
