using System.IO;
using Threefold.Objectives;
using Threefold.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>
    /// The saved world state from the Editor's side: whether Play sessions keep it, and commands to
    /// look at it and to clear it.
    ///
    /// By default a Play session in the Editor starts with nothing saved, as it always has, so a
    /// puzzle can be tried again simply by pressing Play. Ticking Keep Saved State Between Play
    /// Sessions makes the Editor behave like a build: what was opened, destroyed or solved is read
    /// from disk at the start and written back as it changes.
    /// </summary>
    [InitializeOnLoad]
    public static class WorldStateMenu
    {
        private const string KeepMenuPath = "Threefold/World State/Keep Saved State Between Play Sessions";
        private const string NewGameMenuPath = "Threefold/World State/New Game (Forget Saved State)";
        private const string ShowMenuPath = "Threefold/World State/Show What Is Saved";
        private const string KeepPreference = "Threefold.WorldState.KeepInEditor";
        private const string DialogTitle = "World State";

        // Runs each time the Editor loads its scripts, which is also just before Play mode starts.
        static WorldStateMenu()
        {
            WorldStateBootstrap.KeepSavedStateInEditor = EditorPrefs.GetBool(KeepPreference, false);
        }

        [MenuItem(KeepMenuPath, false, 80)]
        private static void ToggleKeep()
        {
            bool keep = !EditorPrefs.GetBool(KeepPreference, false);
            EditorPrefs.SetBool(KeepPreference, keep);
            WorldStateBootstrap.KeepSavedStateInEditor = keep;

            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(DialogTitle, "The change applies from the next time Play is pressed.", "OK");
            }
        }

        [MenuItem(KeepMenuPath, true)]
        private static bool ToggleKeepValidate()
        {
            Menu.SetChecked(KeepMenuPath, EditorPrefs.GetBool(KeepPreference, false));
            return true;
        }

        /// <summary>
        /// Forgets everything saved about the world. In Play mode the level also restarts, so the
        /// result is seen at once; outside Play mode the file on disk is removed.
        /// </summary>
        [MenuItem(NewGameMenuPath, false, 81)]
        private static void NewGame()
        {
            if (!EditorUtility.DisplayDialog(DialogTitle,
                    "Forget everything that was saved about the world? Solved puzzles become unsolved, opened gates " +
                    "closed and destroyed barriers whole. This cannot be undone.", "Forget", "Cancel"))
            {
                return;
            }

            if (EditorApplication.isPlaying)
            {
                PrototypeSession session = SceneSetupUtility.FindInScene<PrototypeSession>(SceneManager.GetActiveScene());
                if (session != null)
                {
                    session.NewGame();
                }
                else
                {
                    WorldState.NewGame();
                }

                Debug.Log("[Threefold] World state: everything saved was forgotten and the world was put back to its start.");
                return;
            }

            string path = WorldStateBootstrap.DefaultFilePath;
            bool existed = File.Exists(path) || File.Exists(path + WorldStateFile.BackupSuffix);
            WorldStateFile.Delete(path);
            EditorUtility.DisplayDialog(DialogTitle,
                existed ? "The saved world state was removed." : "There was no saved world state on disk.", "OK");
        }

        [MenuItem(ShowMenuPath, false, 82)]
        private static void Show()
        {
            string path = WorldStateBootstrap.DefaultFilePath;
            string message;

            if (EditorApplication.isPlaying)
            {
                WorldStateStore store = WorldState.Current;
                message = $"In this Play session: {store.PuzzleCount} puzzle record(s) and {store.ObjectCount} object record(s).\n" +
                          (WorldState.SavesToDisk
                              ? $"They are written to:\n{WorldState.FilePath}"
                              : "They are kept in memory only (Keep Saved State Between Play Sessions is not ticked).") +
                          "\n\n" + WorldStateText.Write(store);
            }
            else if (File.Exists(path))
            {
                message = $"Saved at:\n{path}\n\n{File.ReadAllText(path)}";
            }
            else
            {
                message = $"Nothing is saved. A save would be written to:\n{path}";
            }

            Debug.Log($"[Threefold] World state.\n{message}");
            EditorUtility.DisplayDialog(DialogTitle, message.Length > 1500 ? message.Substring(0, 1500) + "\n(more in the Console)" : message, "OK");
        }
    }
}
