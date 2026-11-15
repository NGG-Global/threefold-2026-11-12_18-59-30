using UnityEngine;

namespace Threefold.MainMenu
{
    /// <summary>
    /// The colours, words and small movements of the main menu's interface: dark stone plates in an aged
    /// bronze frame, muted gold marks, parchment lettering, and a warm highlight on the chosen entry that
    /// breathes slowly. Plain C#. The look is a proposal until the owner has reviewed it in Unity and on a
    /// device, and so are the character's name and title.
    /// </summary>
    public static class MainMenuLook
    {
        public const string GameTitle = "THREEFOLD";

        /// <summary>Proposed, not approved (Docs/MainMenu.md).</summary>
        public const string CharacterName = "ERYN";

        /// <summary>Proposed, not approved (Docs/MainMenu.md).</summary>
        public const string CharacterTitle = "The Young Mage";

        public const string ConfirmQuestion = "Begin a new adventure?\nThe saved adventure will be lost.";
        public const string ConfirmBegin = "BEGIN ANEW";
        public const string ConfirmKeep = "KEEP MY ADVENTURE";

        public static readonly Color Stone = new Color(0.085f, 0.09f, 0.105f, 0.9f);
        public static readonly Color Bronze = new Color(0.44f, 0.32f, 0.17f, 0.95f);
        public static readonly Color Gold = new Color(0.86f, 0.69f, 0.36f, 0.9f);
        public static readonly Color Parchment = new Color(0.94f, 0.88f, 0.74f, 1f);
        public static readonly Color ParchmentDim = new Color(0.94f, 0.88f, 0.74f, 0.38f);
        public static readonly Color Highlight = new Color(0.95f, 0.74f, 0.36f, 1f);

        /// <summary>Seconds for the highlight to brighten and dim once.</summary>
        public const float HighlightPeriod = 2.6f;

        public const float HighlightLow = 0.16f;
        public const float HighlightHigh = 0.3f;

        private static readonly string[] Captions = { "CONTINUE ADVENTURE", "NEW GAME", "SETTINGS", "QUIT" };

        public static string Caption(MainMenuEntry entry)
        {
            int index = (int)entry;
            return index >= 0 && index < Captions.Length ? Captions[index] : string.Empty;
        }

        /// <summary>Opacity of the warm highlight on the chosen entry, <paramref name="seconds"/> after the menu appeared.</summary>
        public static float HighlightAlpha(float seconds)
        {
            float wave = 0.5f - 0.5f * Mathf.Cos(seconds / HighlightPeriod * Mathf.PI * 2f);
            return Mathf.Lerp(HighlightLow, HighlightHigh, wave);
        }
    }
}
