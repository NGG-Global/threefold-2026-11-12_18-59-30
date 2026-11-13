using UnityEngine;

namespace Threefold.Interface
{
    /// <summary>
    /// Supplies a font for simple interface text without adding a font file to the project. It
    /// uses Unity's built-in font and falls back to a font installed on the device.
    /// </summary>
    public static class UiFont
    {
        private const string BuiltInFontName = "LegacyRuntime.ttf";
        private static Font cached;

        public static Font Default
        {
            get
            {
                if (cached != null)
                {
                    return cached;
                }

                cached = Resources.GetBuiltinResource<Font>(BuiltInFontName);
                if (cached != null)
                {
                    return cached;
                }

                string[] installed = Font.GetOSInstalledFontNames();
                if (installed != null && installed.Length > 0)
                {
                    cached = Font.CreateDynamicFontFromOSFont(installed[0], 32);
                }

                if (cached == null)
                {
                    Debug.LogWarning("No interface font is available, so text such as damage numbers will not be shown.");
                }

                return cached;
            }
        }
    }
}
