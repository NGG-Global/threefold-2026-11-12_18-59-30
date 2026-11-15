namespace Threefold.MainMenu
{
    /// <summary>The entries of the main menu, from top to bottom.</summary>
    public enum MainMenuEntry
    {
        Continue = 0,
        NewGame = 1,
        Settings = 2,
        Quit = 3,
    }

    public enum MainMenuState
    {
        /// <summary>The four entries are shown.</summary>
        Main = 0,

        /// <summary>The player is asked whether a new game may replace the saved one.</summary>
        ConfirmNewGame = 1,

        /// <summary>The settings panel is open.</summary>
        Settings = 2,

        /// <summary>A level is being loaded; nothing is accepted any more.</summary>
        Leaving = 3,
    }

    /// <summary>What the screen has to carry out after an input.</summary>
    public enum MainMenuCommand
    {
        None = 0,
        ContinueGame = 1,
        StartNewGame = 2,
        OpenSettings = 3,
        QuitGame = 4,
    }

    /// <summary>
    /// The rules of the main menu: which entry can be chosen, which one is highlighted, when the player is
    /// asked to confirm, and what each input leads to. Plain C# that never touches a Unity object, so it is
    /// tested outside Unity; <see cref="MainMenuScreen"/> only carries out its commands.
    ///
    /// Buttons of this project read the pointer themselves and do not block one another. A single touch could
    /// therefore press an entry and, in the same frame, whatever appears in its place. To rule that out, the
    /// model accepts nothing after a change of state until <see cref="Tick"/> has seen the pointer released.
    /// </summary>
    public sealed class MainMenuModel
    {
        public const int EntryCount = 4;

        private readonly bool hasSavedGame;
        private readonly bool settingsAvailable;
        private bool armed;

        public MainMenuModel(bool hasSavedGame, bool settingsAvailable)
        {
            this.hasSavedGame = hasSavedGame;
            this.settingsAvailable = settingsAvailable;
            Selected = hasSavedGame ? MainMenuEntry.Continue : MainMenuEntry.NewGame;
        }

        public MainMenuState State { get; private set; } = MainMenuState.Main;

        /// <summary>The highlighted entry. Always one that can be chosen.</summary>
        public MainMenuEntry Selected { get; private set; }

        /// <summary>
        /// The highlighted answer while confirming: true is "begin a new game". It starts on false, so a
        /// second press of the same key keeps the saved game.
        /// </summary>
        public bool ConfirmSelected { get; private set; }

        /// <summary>Counts every change the screen has to show.</summary>
        public int Revision { get; private set; }

        public bool HasSavedGame => hasSavedGame;

        /// <summary>Whether input is accepted now (see the class summary).</summary>
        public bool Armed => armed;

        public bool IsEnabled(MainMenuEntry entry)
        {
            switch (entry)
            {
                case MainMenuEntry.Continue: return hasSavedGame;
                case MainMenuEntry.NewGame: return true;
                case MainMenuEntry.Settings: return settingsAvailable;
                case MainMenuEntry.Quit: return true;
                default: return false;
            }
        }

        /// <summary>
        /// Once per frame, before any input of that frame is passed on.
        /// </summary>
        /// <param name="settingsPanelOpen">Whether the settings panel is showing.</param>
        /// <param name="pointerDown">Whether a finger is on the screen or the mouse button is held.</param>
        public void Tick(bool settingsPanelOpen, bool pointerDown)
        {
            if (State == MainMenuState.Settings && !settingsPanelOpen)
            {
                Enter(MainMenuState.Main);
            }

            if (!armed && !pointerDown)
            {
                armed = true;
            }
        }

        /// <summary>An entry was tapped or clicked.</summary>
        public MainMenuCommand Activate(MainMenuEntry entry)
        {
            if (State != MainMenuState.Main || !armed || !IsEnabled(entry))
            {
                return MainMenuCommand.None;
            }

            if (Selected != entry)
            {
                Selected = entry;
                Revision++;
            }

            switch (entry)
            {
                case MainMenuEntry.Continue:
                    Enter(MainMenuState.Leaving);
                    return MainMenuCommand.ContinueGame;

                case MainMenuEntry.NewGame:
                    if (hasSavedGame)
                    {
                        ConfirmSelected = false;
                        Enter(MainMenuState.ConfirmNewGame);
                        return MainMenuCommand.None;
                    }

                    Enter(MainMenuState.Leaving);
                    return MainMenuCommand.StartNewGame;

                case MainMenuEntry.Settings:
                    Enter(MainMenuState.Settings);
                    return MainMenuCommand.OpenSettings;

                case MainMenuEntry.Quit:
                    // The state stays as it is: where quitting does nothing (the Editor), the menu goes on working.
                    return MainMenuCommand.QuitGame;

                default:
                    return MainMenuCommand.None;
            }
        }

        /// <summary>The confirm key was pressed: chooses what is highlighted.</summary>
        public MainMenuCommand ActivateSelected()
        {
            if (State == MainMenuState.ConfirmNewGame)
            {
                return AnswerConfirm(ConfirmSelected);
            }

            return Activate(Selected);
        }

        /// <summary>Moves the highlight up (-1) or down (+1), past entries that cannot be chosen, wrapping round.</summary>
        public void MoveSelection(int direction)
        {
            if (direction == 0)
            {
                return;
            }

            if (State == MainMenuState.ConfirmNewGame)
            {
                ConfirmSelected = !ConfirmSelected;
                Revision++;
                return;
            }

            if (State != MainMenuState.Main)
            {
                return;
            }

            int step = direction > 0 ? 1 : -1;
            int index = (int)Selected;
            for (int i = 0; i < EntryCount; i++)
            {
                index = (index + step + EntryCount) % EntryCount;
                if (IsEnabled((MainMenuEntry)index))
                {
                    break;
                }
            }

            if (index != (int)Selected)
            {
                Selected = (MainMenuEntry)index;
                Revision++;
            }
        }

        /// <summary>The player answered the question about replacing the saved game.</summary>
        public MainMenuCommand AnswerConfirm(bool beginNewGame)
        {
            if (State != MainMenuState.ConfirmNewGame || !armed)
            {
                return MainMenuCommand.None;
            }

            if (beginNewGame)
            {
                Enter(MainMenuState.Leaving);
                return MainMenuCommand.StartNewGame;
            }

            Enter(MainMenuState.Main);
            return MainMenuCommand.None;
        }

        /// <summary>The Back button or Escape. Returns true if it did something.</summary>
        public bool Back()
        {
            if (State != MainMenuState.ConfirmNewGame)
            {
                return false;
            }

            Enter(MainMenuState.Main);
            return true;
        }

        /// <summary>The level could not be loaded: the menu is usable again.</summary>
        public void LoadFailed()
        {
            if (State == MainMenuState.Leaving)
            {
                Enter(MainMenuState.Main);
            }
        }

        private void Enter(MainMenuState next)
        {
            State = next;
            armed = false;
            Revision++;
        }
    }
}
