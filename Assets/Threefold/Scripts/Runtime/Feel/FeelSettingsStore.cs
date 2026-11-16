using UnityEngine;

namespace Threefold.Feel
{
    /// <summary>
    /// The feel settings in force, read once from the device's saved preferences and written back when they
    /// are changed. The camera and the touch controls read <see cref="Current"/> every frame, which is one
    /// static field. Nothing is saved until something is changed, so a device that never opens the Feel
    /// panel plays with the defaults, which are the game as it was.
    /// </summary>
    public static class FeelSettingsStore
    {
        private const string Prefix = "Threefold.Feel.";

        private static FeelSettings current = FeelSettings.Default;
        private static bool loaded;

        public static FeelSettings Current
        {
            get
            {
                if (!loaded)
                {
                    Load();
                }

                return current;
            }
        }

        /// <summary>Puts new settings in force and saves them.</summary>
        public static void Set(FeelSettings settings)
        {
            current = settings.Clamped();
            loaded = true;

            PlayerPrefs.SetFloat(Prefix + "LookSpeed", current.LookSpeed);
            PlayerPrefs.SetFloat(Prefix + "Shake", current.Shake);
            PlayerPrefs.SetInt(Prefix + "AutoRecenter", current.AutoRecenter ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "RecenterDelay", current.RecenterDelay);
            PlayerPrefs.SetFloat(Prefix + "RecenterSpeed", current.RecenterSpeed);
            PlayerPrefs.Save();
        }

        /// <summary>Back to the game as it was, and saved so.</summary>
        public static void ResetToDefaults()
        {
            Set(FeelSettings.Default);
        }

        private static void Load()
        {
            FeelSettings defaults = FeelSettings.Default;
            current = new FeelSettings
            {
                LookSpeed = PlayerPrefs.GetFloat(Prefix + "LookSpeed", defaults.LookSpeed),
                Shake = PlayerPrefs.GetFloat(Prefix + "Shake", defaults.Shake),
                AutoRecenter = PlayerPrefs.GetInt(Prefix + "AutoRecenter", defaults.AutoRecenter ? 1 : 0) != 0,
                RecenterDelay = PlayerPrefs.GetFloat(Prefix + "RecenterDelay", defaults.RecenterDelay),
                RecenterSpeed = PlayerPrefs.GetFloat(Prefix + "RecenterSpeed", defaults.RecenterSpeed),
            }.Clamped();
            loaded = true;
        }

        // Play mode in the Editor can keep statics between runs; start each run from the saved values.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ForgetBetweenRuns()
        {
            loaded = false;
        }
    }
}
