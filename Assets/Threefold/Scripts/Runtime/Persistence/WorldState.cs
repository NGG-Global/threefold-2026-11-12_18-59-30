using System;
using UnityEngine;

namespace Threefold.Persistence
{
    /// <summary>Why the world is told to take its state from the store again.</summary>
    public enum WorldReload
    {
        /// <summary>A restart: the store is unchanged. Temporary state is reset, persistent state stays.</summary>
        Restart = 0,

        /// <summary>The store itself was replaced: a checkpoint was restored, a new game begun, or a save loaded.</summary>
        StateReplaced = 1,
    }

    /// <summary>
    /// The world state of the running game: the one <see cref="WorldStateStore"/>, where it is saved,
    /// and the moments at which the world is told to take its state from it again.
    ///
    /// Changes are kept as they happen. A gate that was opened is open in the store at once, so it
    /// is still open if the player is defeated a moment later. Three things can happen to the world
    /// as a whole:
    ///
    /// - <see cref="ReloadWorld"/>: a restart or a return to a checkpoint. Everything persistent is
    ///   kept, everything temporary (heat, charge, braces) is reset.
    /// - <see cref="RevertToCheckpoint"/>: the store itself goes back to the snapshot taken by
    ///   <see cref="SaveCheckpoint"/>, and the world follows.
    /// - <see cref="NewGame"/>: everything is forgotten, also on disk.
    ///
    /// Each raises <see cref="Reloaded"/>, which the objects in the scene answer by taking their
    /// state from the store. This class holds no Unity object and touches the disk only through
    /// <see cref="WorldStateFile"/>; where the file lives is set from outside (see WorldStateBootstrap).
    /// </summary>
    public static class WorldState
    {
        private static WorldStateSnapshot checkpoint;
        private static int savedRevision;
        private static bool writeSoon;

        /// <summary>The store everything shares.</summary>
        public static WorldStateStore Current { get; } = new WorldStateStore();

        /// <summary>Where the state is saved. Empty means it is kept in memory only, for this session.</summary>
        public static string FilePath { get; set; } = string.Empty;

        /// <summary>What the last load from disk found.</summary>
        public static WorldStateLoadReport LastLoad { get; private set; }

        /// <summary>The problem of the last failed save, or empty.</summary>
        public static string LastSaveProblem { get; private set; } = string.Empty;

        public static bool SavesToDisk => !string.IsNullOrEmpty(FilePath);

        /// <summary>True if the store holds changes that have not been written to disk.</summary>
        public static bool IsDirty => SavesToDisk && (Current.Revision != savedRevision || writeSoon);

        public static bool HasCheckpoint => checkpoint != null;

        /// <summary>
        /// Raised when the world must take its state from the store again. Objects reset what is
        /// temporary and apply what is saved for them, silently: nothing that already happened is
        /// played again.
        /// </summary>
        public static event Action<WorldReload> Reloaded;

        /// <summary>
        /// Raised just before the store is written, copied or reloaded from. Objects answer by putting
        /// what is meant to last into the store as it is at this moment. Most of it is there already,
        /// written when it changed; this catches what changes continuously, such as damage from a fire
        /// that is still burning or a charge that is draining.
        /// </summary>
        public static event Action Capturing;

        /// <summary>A restart or a return to a checkpoint: persistent state stays, temporary state is reset.</summary>
        public static void ReloadWorld()
        {
            Capturing?.Invoke();
            Reloaded?.Invoke(WorldReload.Restart);
        }

        /// <summary>Remembers the world state as it is now.</summary>
        public static void SaveCheckpoint()
        {
            Capturing?.Invoke();
            checkpoint = Current.Capture();
        }

        /// <summary>Puts the world state back to the last checkpoint. Returns false if there is none.</summary>
        public static bool RevertToCheckpoint()
        {
            if (checkpoint == null)
            {
                return false;
            }

            Current.Restore(checkpoint);
            Reloaded?.Invoke(WorldReload.StateReplaced);
            return true;
        }

        /// <summary>Forgets everything, in memory and on disk, and puts the world back to how it starts.</summary>
        public static void NewGame()
        {
            Current.Clear();
            checkpoint = null;
            if (SavesToDisk)
            {
                WorldStateFile.Delete(FilePath);
                savedRevision = Current.Revision;
                writeSoon = false;
            }

            Reloaded?.Invoke(WorldReload.StateReplaced);
        }

        /// <summary>
        /// Forgets what is saved under keys and identifiers that start with <paramref name="prefix"/>, such
        /// as everything of one level, keeps everything else, and tells the world, which puts what was
        /// forgotten back to how it starts. The checkpoint is dropped as well, because it may hold what was
        /// just forgotten. Returns how many records were forgotten.
        /// </summary>
        public static int ForgetScope(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return 0;
            }

            Capturing?.Invoke();
            int removed = Current.RemoveWithPrefix(prefix);
            checkpoint = null;
            Reloaded?.Invoke(WorldReload.StateReplaced);
            return removed;
        }

        /// <summary>Writes the store to disk if it has changed. Returns false only if a write was tried and failed.</summary>
        public static bool Save()
        {
            if (SavesToDisk)
            {
                Capturing?.Invoke();
            }

            if (!IsDirty)
            {
                return true;
            }

            int revision = Current.Revision;
            if (!WorldStateFile.TrySave(FilePath, Current, out string problem))
            {
                LastSaveProblem = problem;
                return false;
            }

            LastSaveProblem = string.Empty;
            savedRevision = revision;
            writeSoon = false;
            return true;
        }

        /// <summary>
        /// Reads the saved state from disk into the store and tells the world. With nothing saved, or
        /// nothing readable, the store is left as it is. A file that could not be used is never
        /// overwritten: it is set aside first, and if that fails, this session keeps its state in
        /// memory only.
        /// </summary>
        public static WorldStateLoadReport Load()
        {
            LastLoad = WorldStateFile.Load(FilePath, Current);
            if (LastLoad.SaveBlocked)
            {
                FilePath = string.Empty;
            }

            if (LastLoad.Loaded)
            {
                savedRevision = Current.Revision;

                // Taken from a backup, a pending save or part of a damaged file: written at once as a sound
                // main file, because a damaged one may just have been set aside. If that fails, the saver retries.
                writeSoon = LastLoad.NeedsSave;
                Reloaded?.Invoke(WorldReload.StateReplaced);
                if (writeSoon)
                {
                    Save();
                }
            }

            return LastLoad;
        }

        /// <summary>Back to an empty world state that is kept in memory only. For the start of a session and for tests.</summary>
        public static void ResetSession()
        {
            Current.Clear();
            checkpoint = null;
            FilePath = string.Empty;
            savedRevision = Current.Revision;
            writeSoon = false;
            LastLoad = default;
            LastSaveProblem = string.Empty;
        }

        // Each Play session in the Editor starts clean, also when domain reload is disabled. What is on
        // disk is read afterwards, before the first scene loads, if saving to disk is switched on.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetSession();
            Reloaded = null;
            Capturing = null;
        }
    }
}
