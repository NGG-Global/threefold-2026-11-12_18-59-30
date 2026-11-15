using System;
using Threefold.Interface;
using Threefold.Persistence;
using Threefold.VisualStyle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Threefold.MainMenu
{
    /// <summary>
    /// The main menu on screen. It passes taps and keys to <see cref="MainMenuModel"/>, shows what the model
    /// says (the highlighted entry, the dimmed entry, the question before a saved game is replaced) and
    /// carries out its commands: load the game, forget the saved world, open the graphics settings, quit.
    /// It decides nothing itself.
    ///
    /// "Continue" is offered when the world state holds anything (<see cref="WorldState.Current"/>). Both
    /// "Continue" and "New Game" load the same first level; where the player was is not remembered yet.
    ///
    /// Keyboard: Up and Down move the highlight, Enter or Space chooses, Escape (the Android Back button)
    /// leaves the question.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuScreen : MonoBehaviour
    {
        [Tooltip("Name of the scene that Continue and New Game load, as listed in Build Settings.")]
        [SerializeField] private string gameScene = "FantasyPlayground";

        [Header("Entries, from top to bottom")]
        [SerializeField] private GameObject entriesRoot;
        [SerializeField] private TapButton[] entryButtons = new TapButton[0];
        [SerializeField] private Image[] entryHighlights = new Image[0];
        [SerializeField] private Text[] entryLabels = new Text[0];

        [Header("Question before a saved game is replaced")]
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private TapButton confirmBegin;
        [SerializeField] private TapButton confirmKeep;
        [SerializeField] private Image confirmBeginHighlight;
        [SerializeField] private Image confirmKeepHighlight;

        [Header("Name plate")]
        [Tooltip("Kept under the character's feet, whatever the shape of the screen.")]
        [SerializeField] private RectTransform namePlate;

        [Tooltip("Optional. Opened by the Settings entry.")]
        [SerializeField] private GraphicsMenu graphicsMenu;

        [Tooltip("Optional. Shown when the game scene cannot be loaded.")]
        [SerializeField] private Text notice;

        private MainMenuModel model;
        private Action[] entryHandlers = new Action[0];
        private int shownRevision = -1;
        private float shownAspect = -1f;
        private float shownAt;
        private bool enhancedTouchEnabledHere;

        /// <summary>The rules, for diagnostics and tests. Null until the menu has started.</summary>
        public MainMenuModel Model => model;

        public string GameScene => gameScene;

        /// <summary>Connects the menu. Called by the setup tooling; empty arguments leave what is set.</summary>
        public void BindMissing(GameObject entries, TapButton[] buttons, Image[] highlights, Text[] labels,
            GameObject confirm, TapButton begin, TapButton keep, Image beginHighlight, Image keepHighlight, RectTransform plate,
            GraphicsMenu settings, Text noticeText)
        {
            entriesRoot = entriesRoot != null ? entriesRoot : entries;
            if ((entryButtons == null || entryButtons.Length == 0) && buttons != null) entryButtons = buttons;
            if ((entryHighlights == null || entryHighlights.Length == 0) && highlights != null) entryHighlights = highlights;
            if ((entryLabels == null || entryLabels.Length == 0) && labels != null) entryLabels = labels;
            confirmRoot = confirmRoot != null ? confirmRoot : confirm;
            confirmBegin = confirmBegin != null ? confirmBegin : begin;
            confirmKeep = confirmKeep != null ? confirmKeep : keep;
            confirmBeginHighlight = confirmBeginHighlight != null ? confirmBeginHighlight : beginHighlight;
            confirmKeepHighlight = confirmKeepHighlight != null ? confirmKeepHighlight : keepHighlight;
            namePlate = namePlate != null ? namePlate : plate;
            graphicsMenu = graphicsMenu != null ? graphicsMenu : settings;
            notice = notice != null ? notice : noticeText;
        }

        /// <summary>Sets the scene the menu leads to. Called by the setup tooling.</summary>
        public void SetGameScene(string sceneName)
        {
            gameScene = sceneName ?? string.Empty;
        }

        private void Awake()
        {
            // The world state was read before the first scene loaded (WorldStateBootstrap).
            model = new MainMenuModel(WorldState.Current.Count > 0, graphicsMenu != null);

            int count = entryButtons != null ? Mathf.Min(entryButtons.Length, MainMenuModel.EntryCount) : 0;
            entryHandlers = new Action[count];
            for (int i = 0; i < count; i++)
            {
                var entry = (MainMenuEntry)i;
                entryHandlers[i] = () => Execute(model.Activate(entry));
            }

            if (notice != null)
            {
                notice.text = string.Empty;
            }
        }

        private void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
                enhancedTouchEnabledHere = true;
            }

            for (int i = 0; i < entryHandlers.Length; i++)
            {
                if (entryButtons[i] != null) entryButtons[i].Pressed += entryHandlers[i];
            }

            if (confirmBegin != null) confirmBegin.Pressed += AnswerBegin;
            if (confirmKeep != null) confirmKeep.Pressed += AnswerKeep;

            // A level may have captured the mouse for its camera.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            shownAt = Time.unscaledTime;
            shownRevision = -1;
            shownAspect = -1f;
        }

        private void OnDisable()
        {
            for (int i = 0; i < entryHandlers.Length; i++)
            {
                if (entryButtons[i] != null) entryButtons[i].Pressed -= entryHandlers[i];
            }

            if (confirmBegin != null) confirmBegin.Pressed -= AnswerBegin;
            if (confirmKeep != null) confirmKeep.Pressed -= AnswerKeep;

            if (enhancedTouchEnabledHere)
            {
                EnhancedTouchSupport.Disable();
                enhancedTouchEnabledHere = false;
            }
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            bool pointerDown = Touch.activeTouches.Count > 0 || (mouse != null && mouse.leftButton.isPressed);
            model.Tick(graphicsMenu != null && graphicsMenu.IsOpen, pointerDown);

            ReadKeyboard();

            if (model.Revision != shownRevision)
            {
                Show();
            }

            PulseHighlights();
            PlaceNamePlate();
        }

        private void ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || model.State == MainMenuState.Settings)
            {
                return;
            }

            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                model.MoveSelection(-1);
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                model.MoveSelection(1);
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame)
            {
                Execute(model.ActivateSelected());
            }
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                model.Back();
            }
        }

        private void AnswerBegin()
        {
            Execute(model.AnswerConfirm(true));
        }

        private void AnswerKeep()
        {
            Execute(model.AnswerConfirm(false));
        }

        private void Execute(MainMenuCommand command)
        {
            switch (command)
            {
                case MainMenuCommand.ContinueGame:
                    LoadGame();
                    break;

                case MainMenuCommand.StartNewGame:
                    WorldState.NewGame();
                    LoadGame();
                    break;

                case MainMenuCommand.OpenSettings:
                    if (graphicsMenu != null)
                    {
                        graphicsMenu.Open();
                    }

                    break;

                case MainMenuCommand.QuitGame:
                    if (Application.isEditor)
                    {
                        Debug.Log("[Threefold] Quit was chosen. In the Editor the game keeps running; a build closes.");
                    }

                    Application.Quit();
                    break;
            }
        }

        private void LoadGame()
        {
            if (string.IsNullOrEmpty(gameScene) || !Application.CanStreamedLevelBeLoaded(gameScene))
            {
                Debug.LogWarning($"[Threefold] The main menu leads to the scene '{gameScene}', which is not in Build Settings. " +
                                 "Run its setup command (Threefold > Setup), then try again.", this);
                if (notice != null)
                {
                    notice.text = "The adventure could not be opened.";
                }

                model.LoadFailed();
                return;
            }

            SceneManager.LoadScene(gameScene);
        }

        private void Show()
        {
            shownRevision = model.Revision;
            MainMenuState state = model.State;

            // While a level loads, the entries stay as they were.
            bool entries = state == MainMenuState.Main || state == MainMenuState.Leaving;
            SetActive(entriesRoot, entries);
            SetActive(confirmRoot, state == MainMenuState.ConfirmNewGame);

            int count = entryLabels != null ? Mathf.Min(entryLabels.Length, MainMenuModel.EntryCount) : 0;
            for (int i = 0; i < count; i++)
            {
                if (entryLabels[i] != null)
                {
                    entryLabels[i].color = model.IsEnabled((MainMenuEntry)i) ? MainMenuLook.Parchment : MainMenuLook.ParchmentDim;
                }
            }

            count = entryHighlights != null ? Mathf.Min(entryHighlights.Length, MainMenuModel.EntryCount) : 0;
            for (int i = 0; i < count; i++)
            {
                if (entryHighlights[i] != null)
                {
                    entryHighlights[i].enabled = i == (int)model.Selected;
                }
            }

            if (confirmBeginHighlight != null) confirmBeginHighlight.enabled = model.ConfirmSelected;
            if (confirmKeepHighlight != null) confirmKeepHighlight.enabled = !model.ConfirmSelected;
        }

        private void PulseHighlights()
        {
            float alpha = MainMenuLook.HighlightAlpha(Time.unscaledTime - shownAt);
            Color color = MainMenuLook.Highlight;
            color.a = alpha;

            int selected = (int)model.Selected;
            if (entryHighlights != null && selected < entryHighlights.Length && entryHighlights[selected] != null)
            {
                entryHighlights[selected].color = color;
            }

            if (model.State != MainMenuState.ConfirmNewGame)
            {
                return;
            }

            Image answer = model.ConfirmSelected ? confirmBeginHighlight : confirmKeepHighlight;
            if (answer != null)
            {
                answer.color = color;
            }
        }

        /// <summary>
        /// Keeps the name plate centred under the character's feet. Where he appears depends on the shape of
        /// the screen, so the place is worked out again whenever that changes.
        /// </summary>
        private void PlaceNamePlate()
        {
            if (namePlate == null || Screen.height <= 0)
            {
                return;
            }

            float aspect = (float)Screen.width / Screen.height;
            if (Mathf.Approximately(aspect, shownAspect))
            {
                return;
            }

            shownAspect = aspect;
            Vector2 feet = MainMenuStage.FeetOnScreen(aspect);
            namePlate.anchorMin = feet;
            namePlate.anchorMax = feet;
            namePlate.pivot = new Vector2(0.5f, 1f);
            namePlate.anchoredPosition = new Vector2(0f, -MainMenuLayout.NamePlateGap);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
