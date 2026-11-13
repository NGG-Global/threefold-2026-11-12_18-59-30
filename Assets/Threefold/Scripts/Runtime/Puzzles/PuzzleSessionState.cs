using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>The remembered state of one puzzle element.</summary>
    public readonly struct PuzzleRecord
    {
        public readonly bool Active;
        public readonly bool Locked;

        public PuzzleRecord(bool active, bool locked)
        {
            Active = active;
            Locked = locked;
        }
    }

    /// <summary>
    /// Remembers puzzle state for the rest of the play session: a gateway that was opened stays
    /// open when the player leaves the scene and comes back, or when the level is restarted. It is
    /// kept in memory only. Closing the game, or leaving Play mode in the Editor, forgets it;
    /// saving to disk belongs to a save system, which does not exist yet.
    ///
    /// Elements are identified by their persistence key, so keys must be unique across scenes.
    /// </summary>
    public static class PuzzleSessionState
    {
        private static readonly Dictionary<string, PuzzleRecord> Records = new Dictionary<string, PuzzleRecord>();

        public static int Count => Records.Count;

        public static bool TryGet(string key, out PuzzleRecord record)
        {
            if (string.IsNullOrEmpty(key))
            {
                record = default;
                return false;
            }

            return Records.TryGetValue(key, out record);
        }

        /// <summary>Stores a record. An empty key is ignored.</summary>
        public static void Set(string key, PuzzleRecord record)
        {
            if (!string.IsNullOrEmpty(key))
            {
                Records[key] = record;
            }
        }

        public static bool Remove(string key)
        {
            return !string.IsNullOrEmpty(key) && Records.Remove(key);
        }

        /// <summary>Forgets everything, as at the start of a new game.</summary>
        public static void Clear()
        {
            Records.Clear();
        }

        // Each Play session in the Editor starts with nothing remembered, also when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Records.Clear();
        }
    }
}
