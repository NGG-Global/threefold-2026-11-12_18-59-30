namespace Threefold.DevTools
{
    /// <summary>What a touch gesture on the development diagnostics asks for.</summary>
    public enum DevGesture
    {
        None = 0,

        /// <summary>Three fingers: show or hide the diagnostics.</summary>
        ToggleDiagnostics = 1,

        /// <summary>Four fingers or more: load the next scene of the build.</summary>
        NextScene = 2,
    }

    /// <summary>
    /// The development-only way between the scenes of a build, and the finger gestures of the diagnostics.
    /// Plain C#: no Unity calls. Only a development build has the diagnostics, so a release build has neither.
    ///
    /// A gesture is a tap: counted from the first finger down to the last finger up, by the most fingers that
    /// were down at once, and only if it was over within <see cref="TapSeconds"/>. Three fingers show or hide
    /// the diagnostics. Four or more load the next scene, and only while the diagnostics are shown: playing
    /// can put four fingers on the screen (move, look, attack, block), and that must never change the scene.
    /// A tap is decided when the fingers leave, because a hand laying four fingers down passes through three.
    /// The scene gesture does not depend on the diagnostics' buttons, which are drawn with Unity's
    /// immediate-mode GUI and have not been tried with touch on a device.
    /// </summary>
    public sealed class DevSceneSwitcher
    {
        /// <summary>The longest a tap may last, from the first finger down to the last finger up.</summary>
        public const float TapSeconds = 0.7f;

        private int most;
        private float held;

        /// <summary>Call once a frame with the number of fingers on the screen and the time since the last call.</summary>
        public DevGesture Update(int fingers, float deltaSeconds, bool diagnosticsShown)
        {
            if (fingers > 0)
            {
                held += most > 0 && deltaSeconds > 0f ? deltaSeconds : 0f;
                if (fingers > most)
                {
                    most = fingers;
                }

                return DevGesture.None;
            }

            int counted = most;
            bool tap = held <= TapSeconds;
            most = 0;
            held = 0f;

            if (!tap)
            {
                return DevGesture.None;
            }

            if (counted >= 4)
            {
                return diagnosticsShown ? DevGesture.NextScene : DevGesture.None;
            }

            return counted == 3 ? DevGesture.ToggleDiagnostics : DevGesture.None;
        }

        /// <summary>The scene after <paramref name="current"/> in a build of <paramref name="count"/> scenes, going round.</summary>
        public static int NextIndex(int current, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return ((current < 0 ? -1 : current) + 1) % count;
        }

        /// <summary>
        /// A scene's name for a button, from its path: "Assets/Scenes/ReferenceCourtyard.unity" gives
        /// "Reference Courtyard".
        /// </summary>
        public static string DisplayName(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return "(unnamed scene)";
            }

            int slash = scenePath.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? scenePath.Substring(slash + 1) : scenePath;
            int dot = name.LastIndexOf('.');
            if (dot > 0)
            {
                name = name.Substring(0, dot);
            }

            var spaced = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char character = name[i];
                if (i > 0 && char.IsUpper(character) && !char.IsUpper(name[i - 1]) && name[i - 1] != ' ')
                {
                    spaced.Append(' ');
                }

                spaced.Append(character);
            }

            return spaced.Length > 0 ? spaced.ToString() : "(unnamed scene)";
        }
    }
}
