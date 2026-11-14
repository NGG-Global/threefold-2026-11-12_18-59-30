using Threefold.Persistence;

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
    /// Remembers puzzle state: a gateway that was opened stays open when the player leaves the scene
    /// and comes back, or when the level is restarted. Since Sprint LM-04 the records live in the
    /// world state (<see cref="WorldState"/>), together with the persistent state of magical
    /// objects, so there is one store and one save. Whether that store is also written to disk is
    /// decided there; this class only reads and writes puzzle records.
    ///
    /// Elements are identified by their persistence key, so keys must be unique across scenes.
    /// </summary>
    public static class PuzzleSessionState
    {
        public static int Count => WorldState.Current.PuzzleCount;

        public static bool TryGet(string key, out PuzzleRecord record)
        {
            return WorldState.Current.TryGetPuzzle(key, out record);
        }

        /// <summary>Stores a record. An empty key is ignored.</summary>
        public static void Set(string key, PuzzleRecord record)
        {
            WorldState.Current.SetPuzzle(key, record);
        }

        public static bool Remove(string key)
        {
            return WorldState.Current.RemovePuzzle(key);
        }

        /// <summary>Forgets every puzzle record. The state of magical objects is left alone.</summary>
        public static void Clear()
        {
            WorldState.Current.ClearPuzzles();
        }
    }
}
