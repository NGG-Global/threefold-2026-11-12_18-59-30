using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.Interface;
using Threefold.Objectives;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Adds the objective loop to a scene: the objective itself, the session that restarts the
    /// level, and their display. It runs as part of the scene setup commands. Existing parts are kept.
    /// </summary>
    internal static class ObjectiveSetup
    {
        /// <summary>What the objective asks of the player, for levels other than the training courtyard.</summary>
        internal sealed class ObjectiveOptions
        {
            public string Title = "Defeat the training constructs";
            public string CompletionHeading = "Training complete";

            /// <summary>What must be defeated. Null means the training constructs.</summary>
            public Health[] Targets;
        }

        private const string DialogTitle = "Objective Setup";
        private const string SessionName = "PrototypeSession";
        private const string SafeAreaName = "SafeArea";

        private static readonly Color TextColor = new Color(1f, 0.97f, 0.9f, 0.95f);
        private static readonly Color AccentColor = new Color(1f, 0.84f, 0.5f, 1f);
        private static readonly Color PendingColor = new Color(1f, 0.96f, 0.86f, 0.3f);
        private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.08f, 0.74f);
        private static readonly Color ButtonColor = new Color(1f, 0.96f, 0.86f, 0.22f);
        private static readonly Color DefeatColor = new Color(1f, 0.5f, 0.42f, 1f);

        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            Ensure(scene, mage, report, null);
        }

        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report, ObjectiveOptions options)
        {
            options = options ?? new ObjectiveOptions();

            DefeatTargetsObjective objective = FindInScene<DefeatTargetsObjective>(scene);
            PrototypeSession session = FindInScene<PrototypeSession>(scene);

            if (objective == null)
            {
                GameObject host = session != null ? session.gameObject : new GameObject(SessionName);
                if (session == null)
                {
                    Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                }

                objective = Undo.AddComponent<DefeatTargetsObjective>(host);
                objective.SetTitle(options.Title);
                MarkChanged(objective);
                report.AppendLine($"Added the objective: {options.Title}.");
            }

            if (session == null)
            {
                session = Undo.AddComponent<PrototypeSession>(objective.gameObject);
                session.Bind(objective, mage.GetComponent<Health>(), mage.GetComponent<Mana>(),
                    mage.GetComponent<ThirdPersonMotor>(), FindInScene<ThirdPersonCamera>(scene));
                MarkChanged(session);
                report.AppendLine("Added the session that restarts the level (R key, Start button, or the restart button).");
            }

            // Targets are (re)assigned when there are none, or when some were deleted, for example
            // after the arena's enemies were removed to be generated again.
            if (objective.TotalCount == 0 || HasMissingTargets(objective))
            {
                Health[] targets = options.Targets;
                if (targets == null)
                {
                    GameObject constructs = FindByName(scene, CombatSetup.ConstructsName);
                    targets = constructs != null ? constructs.GetComponentsInChildren<Health>(true) : new Health[0];
                }

                if (targets.Length > 0)
                {
                    Undo.RecordObject(objective, DialogTitle);
                    objective.SetTargets(targets);
                    MarkChanged(objective);
                    report.AppendLine($"Assigned {targets.Length} target(s) to the objective.");
                }
                else
                {
                    report.AppendLine("WARNING: no targets were found for the objective, so it cannot be completed.");
                }
            }

            EnsureHud(scene, objective, session, options.CompletionHeading, report);
            EnsureDefeatPanel(scene, session, report);
        }

        private static bool HasMissingTargets(DefeatTargetsObjective objective)
        {
            foreach (Health target in objective.Targets)
            {
                if (target == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureHud(Scene scene, DefeatTargetsObjective objective, PrototypeSession session,
            string completionHeading, StringBuilder report)
        {
            if (FindInScene<ObjectiveHud>(scene) != null)
            {
                return;
            }

            PlayerHud playerHud = FindInScene<PlayerHud>(scene);
            if (playerHud == null)
            {
                report.AppendLine("WARNING: the combat HUD was not found, so the objective is not shown on screen.");
                return;
            }

            Transform canvasRoot = playerHud.transform;
            Transform safeArea = canvasRoot.Find(SafeAreaName);
            if (safeArea == null)
            {
                safeArea = canvasRoot;
            }

            // Progress: a title and one marker per target, top centre.
            RectTransform strip = TouchControlsSetup.CreateRect("Objective", safeArea);
            Anchor(strip, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(380f, 46f));
            Undo.RegisterCreatedObjectUndo(strip.gameObject, DialogTitle);

            Text title = CreateText("Title", strip, objective.Title, 15, TextColor);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(380f, 22f));

            int count = Mathf.Max(1, objective.TotalCount);
            var markers = new Graphic[count];
            for (int i = 0; i < count; i++)
            {
                Image marker = TouchControlsSetup.CreateShape("Marker " + (i + 1), strip, ProceduralShape.Disc, PendingColor);
                float x = (i - (count - 1) * 0.5f) * 24f;
                Anchor(marker.rectTransform, new Vector2(0.5f, 0f), new Vector2(x, 9f), new Vector2(14f, 14f));
                markers[i] = marker;
            }

            // Completion: a panel above the crosshair, hidden until the objective is complete.
            RectTransform panel = TouchControlsSetup.CreateRect("CompletionPanel", canvasRoot);
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 76f), new Vector2(330f, 176f));
            Undo.RegisterCreatedObjectUndo(panel.gameObject, DialogTitle);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;

            Text heading = CreateText("Heading", panel, completionHeading, 26, AccentColor);
            Anchor(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(310f, 34f));

            Text time = CreateText("Time", panel, "Time", 16, TextColor);
            Anchor(time.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(310f, 22f));

            RectTransform button = TouchControlsSetup.CreateRect("RestartButton", panel);
            Anchor(button, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(160f, 44f));
            Image buttonImage = button.gameObject.AddComponent<Image>();
            buttonImage.color = ButtonColor;
            buttonImage.raycastTarget = false;
            TapButton restart = button.gameObject.AddComponent<TapButton>();
            Text buttonLabel = CreateText("Label", button, "Restart", 18, TextColor);
            TouchControlsSetup.Stretch(buttonLabel.rectTransform);

            Text hint = CreateText("KeyboardHint", panel, "Press R to restart", 13, TextColor);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(310f, 18f));

            panel.gameObject.SetActive(false);

            ObjectiveHud hud = Undo.AddComponent<ObjectiveHud>(playerHud.gameObject);
            hud.Bind(objective, session, title, markers, panel.gameObject, time, restart, hint.gameObject);
            MarkChanged(hud);
            report.AppendLine("Added the objective display and the completion panel to the HUD.");
        }

        /// <summary>
        /// A panel shown while the player is defeated, with a restart button. Added to a HUD that
        /// lacks one, including HUDs created before enemies existed.
        /// </summary>
        private static void EnsureDefeatPanel(Scene scene, PrototypeSession session, StringBuilder report)
        {
            ObjectiveHud hud = FindInScene<ObjectiveHud>(scene);
            if (hud == null || hud.HasDefeatPanel)
            {
                return;
            }

            RectTransform panel = TouchControlsSetup.CreateRect("DefeatPanel", hud.transform);
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 76f), new Vector2(330f, 176f));
            Undo.RegisterCreatedObjectUndo(panel.gameObject, DialogTitle);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;

            Text heading = CreateText("Heading", panel, "Defeated", 26, DefeatColor);
            Anchor(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(310f, 34f));

            Text advice = CreateText("Advice", panel, "Watch for the glow before each attack", 15, TextColor);
            Anchor(advice.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(310f, 22f));

            RectTransform button = TouchControlsSetup.CreateRect("RestartButton", panel);
            Anchor(button, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(160f, 44f));
            Image buttonImage = button.gameObject.AddComponent<Image>();
            buttonImage.color = ButtonColor;
            buttonImage.raycastTarget = false;
            TapButton restart = button.gameObject.AddComponent<TapButton>();
            Text buttonLabel = CreateText("Label", button, "Try again", 18, TextColor);
            TouchControlsSetup.Stretch(buttonLabel.rectTransform);

            Text hint = CreateText("KeyboardHint", panel, "Press R to restart", 13, TextColor);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(310f, 18f));

            panel.gameObject.SetActive(false);

            Undo.RecordObject(hud, DialogTitle);
            hud.BindDefeat(panel.gameObject, restart, hint.gameObject);
            MarkChanged(hud);

            if (session == null)
            {
                report.AppendLine("WARNING: there is no session, so the defeat panel cannot restart the level.");
            }

            report.AppendLine("Added the defeat panel to the HUD.");
        }

        private static Text CreateText(string name, Transform parent, string content, int size, Color color)
        {
            RectTransform rect = TouchControlsSetup.CreateRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont.Default;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Gives the rect a fixed size, positioned relative to one anchor point of its parent.</summary>
        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
