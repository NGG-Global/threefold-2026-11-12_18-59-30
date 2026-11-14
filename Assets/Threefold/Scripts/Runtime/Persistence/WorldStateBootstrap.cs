using System.IO;
using UnityEngine;

namespace Threefold.Persistence
{
    /// <summary>
    /// Decides, when the game starts, whether the world state is kept on disk, and reads what was
    /// saved before the first scene loads.
    ///
    /// In a build the state is always kept on disk, in the app's own data folder. In the Unity
    /// Editor it is kept in memory only, so every Play session starts fresh as before, unless
    /// Threefold > World State > Keep Saved State Between Play Sessions is ticked.
    ///
    /// This is the only place in the persistence code that asks Unity anything.
    /// </summary>
    public static class WorldStateBootstrap
    {
        public const string FileName = "world-state.txt";

        /// <summary>Set by the Editor from its preference before Play mode starts. Ignored in a build.</summary>
        public static bool KeepSavedStateInEditor { get; set; }

        /// <summary>Where the world state is saved on this device.</summary>
        public static string DefaultFilePath => Path.Combine(Application.persistentDataPath, FileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            // An identifier held by a Unity object that has been destroyed is free again.
            PersistentIdRegistry.IsOwnerAlive = owner => !(owner is Object unityObject) || unityObject != null;

            bool onDisk = !Application.isEditor || KeepSavedStateInEditor;
            if (!onDisk)
            {
                WorldState.FilePath = string.Empty;
                return;
            }

            WorldState.FilePath = DefaultFilePath;
            WorldStateLoadReport report = WorldState.Load();
            WorldStateSaver.Ensure();

            if (report.Loaded)
            {
                Debug.Log($"[Threefold] Loaded the saved world state: {report.Read.Puzzles} puzzle record(s) and " +
                          $"{report.Read.Objects} object record(s) from '{DefaultFilePath}'.");
            }

            if (!string.IsNullOrEmpty(report.Problem))
            {
                Debug.LogWarning($"[Threefold] World state: {report.Problem}");
            }
        }
    }
}
