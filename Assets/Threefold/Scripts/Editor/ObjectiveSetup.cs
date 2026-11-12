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
        private const string DialogTitle = "Objective Setup";
        private const string SessionName = "PrototypeSession";
        private const string SafeAreaName = "SafeArea";

        private static readonly Color TextColor = new Color(1f, 0.97f, 0.9f, 0.95f);
        private static readonly Color AccentColor = new Color(1f, 0.84f, 0.5f, 1f);
        private static readonly Color PendingColor = new Color(1f, 0.96f, 0.86f, 0.3f);
        private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.08f, 0.74f);
        private static readonly Color ButtonColor = new Color(1f, 0.96f, 0.86f, 0.22f);

        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
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
                report.AppendLine("Added the objective: defeat the training constructs.");
            }

            if (session == null)
            {
                session = Undo.AddComponent<PrototypeSession>(objective.gameObject);
                session.Bind(objective, mage.GetComponent<Health>(), mage.GetComponent<Mana>(),
                    mage.GetComponent<ThirdPersonMotor>(), FindInScene<ThirdPersonCamera>(scene));
                MarkChanged(session);
                report.AppendLine("Added the session that restarts the level (R key, Start button, or the restart button).");
            }

            if (objective.TotalCount == 0)
            {
                GameObject constructs = FindByName(scene, CombatSetup.ConstructsName);
                Health[] targets = constructs != null ? constructs.GetComponentsInChildren<Health>(true) : new Health[0];

                if (targets.Length > 0)
                {
                    Undo.RecordObject(objective, DialogTitle);
                    objective.SetTargets(targets);
                    MarkChanged(objective);
                    report.AppendLine($"Assigned {targets.Length} training constructs as the objective's targets.");
                }
                else
                {
                    report.AppendLine($"WARNING: no training constructs were found under '{CombatSetup.ConstructsName}', " +
                                      "so the objective has no targets and cannot be completed.");
                }
            }

            EnsureHud(scene, objective, session, report);
        }

        private static void EnsureHud(Scene scene, DefeatTargetsObjective objective, PrototypeSession session,
            StringBuilder report)
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

            Text heading = CreateText("Heading", panel, "Training complete", 26, AccentColor);
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
