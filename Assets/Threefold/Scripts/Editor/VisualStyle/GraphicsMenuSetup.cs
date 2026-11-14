using System.Text;
using Threefold.InputHandling;
using Threefold.Interface;
using Threefold.TouchControls;
using Threefold.VisualStyle;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Adds the player's graphics menu (<see cref="GraphicsMenu"/>) to a scene that has the visual style:
    /// a Graphics button at the top left, under the health and mana bars, and a panel with the three
    /// presets. Called when the visual style is applied, so every level that has the style gets it. A
    /// scene that already has the menu keeps it as it is; only empty references are filled.
    /// </summary>
    internal static class GraphicsMenuSetup
    {
        private const string DialogTitle = "Graphics Menu";
        internal const string MenuName = "GraphicsMenu";
        private const int UiLayer = 5;

        private static readonly Color PanelColor = new Color(0.03f, 0.035f, 0.05f, 0.9f);
        private static readonly Color ButtonColor = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        private static readonly Color TextColor = new Color(1f, 0.97f, 0.9f, 0.95f);
        private static readonly Color AccentColor = new Color(0.95f, 0.78f, 0.45f, 1f);

        internal static GraphicsMenu Ensure(Scene scene, StringBuilder report)
        {
            TouchControlsInput touch = FindInScene<TouchControlsInput>(scene);
            ControlSchemeSelector scheme = FindInScene<ControlSchemeSelector>(scene);

            GraphicsMenu menu = FindInScene<GraphicsMenu>(scene);
            if (menu != null)
            {
                Undo.RecordObject(menu, DialogTitle);
                menu.BindMissing(null, null, null, null, null, null, null, touch, scheme);
                MarkChanged(menu);
                return menu;
            }

            var root = new GameObject(MenuName, typeof(Canvas));
            root.layer = UiLayer;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 96;
            root.AddComponent<DensityCanvasScaler>();

            RectTransform safeArea = TouchControlsSetup.CreateRect("SafeArea", root.transform);
            TouchControlsSetup.Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            // The button: top left, under the health bar (24, -24) and the mana bar (24, -44).
            TapButton open = CreateButton(safeArea, "GraphicsButton", "Graphics", 15, new Vector2(0f, 1f), new Vector2(24f, -62f),
                new Vector2(120f, 40f), out _, out _);

            // The panel: centred, hidden until the button is pressed. Its own canvas, so opening it
            // rebuilds only its own geometry.
            RectTransform panel = TouchControlsSetup.CreateRect("Panel", safeArea);
            panel.gameObject.AddComponent<Canvas>();
            // 300 units tall: the canvas can be as short as 340 units on a dense screen (DensityCanvasScaler).
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(330f, 300f));
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;

            Text heading = CreateText("Heading", panel, "Graphics", 22, AccentColor);
            Anchor(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(300f, 30f));

            string[] names = { "Performance", "Balanced", "Quality" };
            var presets = new TapButton[names.Length];
            var backgrounds = new Image[names.Length];
            var labels = new Text[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                presets[i] = CreateButton(panel, names[i], names[i], 17, new Vector2(0.5f, 1f), new Vector2(0f, -48f - i * 52f),
                    new Vector2(270f, 44f), out backgrounds[i], out labels[i]);
            }

            Text note = CreateText("Note", panel, string.Empty, 12, TextColor);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            note.alignment = TextAnchor.UpperCenter;
            Anchor(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -206f), new Vector2(300f, 42f));

            TapButton close = CreateButton(panel, "CloseButton", "Close", 16, new Vector2(0.5f, 0f), new Vector2(0f, 8f),
                new Vector2(140f, 40f), out _, out _);

            panel.gameObject.SetActive(false);

            menu = root.AddComponent<GraphicsMenu>();
            menu.BindMissing(open, panel.gameObject, presets, backgrounds, labels, close, note, touch, scheme);
            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            MarkChanged(menu);
            report.AppendLine("Added the graphics menu (Graphics button at the top left): Performance, Balanced and Quality presets.");
            return menu;
        }

        private static TapButton CreateButton(RectTransform parent, string name, string caption, int size, Vector2 anchor, Vector2 position,
            Vector2 extent, out Image background, out Text label)
        {
            RectTransform rect = TouchControlsSetup.CreateRect(name, parent);
            Anchor(rect, anchor, position, extent);
            background = rect.gameObject.AddComponent<Image>();
            background.color = ButtonColor;
            background.raycastTarget = false;
            TapButton button = rect.gameObject.AddComponent<TapButton>();
            label = CreateText("Label", rect, caption, size, TextColor);
            TouchControlsSetup.Stretch(label.rectTransform);
            return button;
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
