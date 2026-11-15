namespace Threefold.MainMenu
{
    /// <summary>
    /// The measurements of the main menu's interface, in canvas units. One canvas unit is one Android
    /// density-independent pixel (see DensityCanvasScaler), so these are physical sizes under the thumb.
    /// The setup command builds the interface from these numbers and the tests check them: change them
    /// here, not in the generated scene. Plain C#.
    ///
    /// The logo and the entries form one column at the top left of the safe area. Positions are measured
    /// from that corner: x to the right, y downwards.
    /// </summary>
    public static class MainMenuLayout
    {
        /// <summary>
        /// The smallest height the canvas can have (DensityCanvasScaler's minimum). Everything must fit in it.
        /// </summary>
        public const float SmallestCanvasHeight = 340f;

        /// <summary>
        /// The smallest touch target of the Android accessibility guidance, 48 dp. Verify against the current
        /// Android documentation before relying on it as a requirement.
        /// </summary>
        public const float SmallestTouchTarget = 48f;

        public const float ColumnLeft = 28f;
        public const float ColumnWidth = 232f;

        public const float LogoTop = 20f;
        public const float LogoHeight = 44f;
        public const int LogoFontSize = 36;

        /// <summary>The thin rule with three marks under the logo.</summary>
        public const float RuleTop = 66f;
        public const float RuleHeight = 10f;

        public const float FirstEntryTop = 88f;
        public const float EntryHeight = 48f;
        public const float EntryGap = 8f;
        public const int EntryFontSize = 16;

        /// <summary>The question shown in place of the entries before a saved game is replaced.</summary>
        public const float ConfirmTextHeight = 96f;
        public const int ConfirmFontSize = 14;

        /// <summary>The name plate under the character: centred on him, its top this far below his feet.</summary>
        public const float NamePlateWidth = 176f;
        public const float NamePlateHeight = 44f;
        public const float NamePlateGap = 6f;
        public const int NameFontSize = 19;
        public const int TitleFontSize = 12;

        public static float ColumnRight => ColumnLeft + ColumnWidth;

        /// <summary>Distance from the top of the safe area to the top of an entry.</summary>
        public static float EntryTop(int index)
        {
            return FirstEntryTop + index * (EntryHeight + EntryGap);
        }

        /// <summary>The lower edge of the last entry.</summary>
        public static float EntriesBottom => EntryTop(MainMenuModel.EntryCount - 1) + EntryHeight;

        /// <summary>The two answers under the question, in the places of the last two entries.</summary>
        public static float ConfirmAnswerTop(int index)
        {
            return EntryTop(MainMenuModel.EntryCount - 2 + index);
        }
    }
}
