using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Threefold > Validate > Android Performance Settings: shows the Android player settings that
    /// affect frame pacing and the development diagnostics, what each is now and what is proposed, and
    /// changes them only after the owner confirms (CLAUDE.md: project settings are not changed by
    /// tooling without showing what was found and what would change). Nothing is changed in batch mode,
    /// so a CI build never changes them by itself.
    ///
    /// Proposed:
    ///   Optimized Frame Pacing (Android Frame Pacing library, "Swappy") on: it times frame presentation
    ///   to the display, so a 30 or 60 target is held more evenly.
    ///   Frame Timing Stats on: lets the development diagnostics show CPU and GPU time per frame.
    /// Both are Unity player settings; neither needs a new package. Their effect on this game has not
    /// been measured on a device.
    /// </summary>
    internal static class AndroidPerformanceSettings
    {
        private const string MenuPath = "Threefold/Validate/Android Performance Settings";
        private const string DialogTitle = "Android Performance Settings";

        private sealed class Setting
        {
            public string Name;
            public string Why;
            public bool Current;
            public bool Proposed;
            public System.Action<bool> Apply;
        }

        [MenuItem(MenuPath, false, 30)]
        private static void Run()
        {
            List<Setting> settings = Inspect();
            string description = Describe(settings, out int changes);
            if (changes == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, description + "\n\nNothing to change.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(DialogTitle, description + $"\n\nMake the {changes} proposed change(s) to the project's player settings?",
                    "Change them", "Leave them"))
            {
                Debug.Log("[Threefold] Android performance settings: nothing was changed.\n" + description);
                return;
            }

            var report = new StringBuilder();
            foreach (Setting setting in settings)
            {
                if (setting.Current != setting.Proposed)
                {
                    setting.Apply(setting.Proposed);
                    report.AppendLine($"{setting.Name}: {OnOff(setting.Current)} -> {OnOff(setting.Proposed)}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Threefold] Android performance settings changed. Commit ProjectSettings/ProjectSettings.asset from this " +
                      "computer so the build uses them.\n" + report);
        }

        private static List<Setting> Inspect()
        {
            return new List<Setting>
            {
                new Setting
                {
                    Name = "Optimized Frame Pacing (Android)",
                    Why = "times each frame to the display, for an even 30 or 60",
                    Current = PlayerSettings.Android.optimizedFramePacing,
                    Proposed = true,
                    Apply = value => PlayerSettings.Android.optimizedFramePacing = value,
                },
                new Setting
                {
                    Name = "Frame Timing Stats",
                    Why = "CPU and GPU time per frame in the development diagnostics",
                    Current = PlayerSettings.enableFrameTimingStats,
                    Proposed = true,
                    Apply = value => PlayerSettings.enableFrameTimingStats = value,
                },
            };
        }

        private static string Describe(List<Setting> settings, out int changes)
        {
            changes = 0;
            var text = new StringBuilder();
            foreach (Setting setting in settings)
            {
                bool change = setting.Current != setting.Proposed;
                if (change)
                {
                    changes++;
                }

                text.Append(setting.Name).Append(": ").Append(OnOff(setting.Current));
                text.Append(change ? $", proposed {OnOff(setting.Proposed)}" : " (as proposed)");
                text.Append(" - ").Append(setting.Why).AppendLine();
            }

            text.AppendLine();
            text.Append("Vertical sync count (quality level): ").Append(QualitySettings.vSyncCount)
                .Append(". The graphics presets set the frame-rate target at runtime; nothing here changes that.");
            return text.ToString();
        }

        private static string OnOff(bool value) => value ? "on" : "off";

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !Application.isBatchMode;
        }
    }
}
