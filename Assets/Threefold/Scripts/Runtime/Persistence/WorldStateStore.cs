using System;
using System.Collections.Generic;
using Threefold.Puzzles;

namespace Threefold.Persistence
{
    /// <summary>
    /// How long a change to the world lasts.
    ///
    /// Immediate: an impact, a hit flash, a short shove, a burst. It happens and is gone. Nothing is
    /// stored and nothing is replayed when the world is loaded.
    ///
    /// Temporary: burning, heat, arcane charge, a brace, a stagger. It runs out by itself and is
    /// reset when the world is reloaded, unless a definition names it as an exception.
    ///
    /// Persistent: an opened gate, a destroyed barrier, an activated mechanism, a solved puzzle, a
    /// platform left where it matters. It is saved under the object's identifier and restored.
    /// </summary>
    public enum PersistenceTier
    {
        Immediate = 0,
        Temporary = 1,
        Persistent = 2,
    }

    /// <summary>
    /// The parts of a magical object's state that are saved. An object keeps only what its
    /// definition names here, and only if it has an identifier. Heat and charge are temporary by
    /// nature; naming them is the explicit exception for a puzzle that depends on them.
    /// </summary>
    [Flags]
    public enum PersistedState
    {
        None = 0,

        /// <summary>That the structure was destroyed.</summary>
        Destruction = 1 << 0,

        /// <summary>How much integrity is left, so partial damage is kept as well.</summary>
        Damage = 1 << 1,

        /// <summary>That an arcane mechanism was activated for good (latched).</summary>
        Activation = 1 << 2,

        /// <summary>Where a pushed object came to rest.</summary>
        Position = 1 << 3,

        /// <summary>That the object has collapsed.</summary>
        Collapse = 1 << 4,

        /// <summary>Exception: the heat it holds. The fire itself never survives a reload.</summary>
        Heat = 1 << 5,

        /// <summary>Exception: the arcane charge it holds.</summary>
        Charge = 1 << 6,
    }

    /// <summary>The saved state of one magical object. Only the parts named in <see cref="Saved"/> mean anything.</summary>
    public struct ObjectRecord
    {
        public PersistedState Saved;
        public float Integrity;
        public bool Activated;
        public float Charge;
        public bool Collapsed;
        public float OffsetX;
        public float OffsetY;
        public float OffsetZ;
        public float Heat;

        public bool IsEmpty => Saved == PersistedState.None;

        public bool Matches(in ObjectRecord other)
        {
            return Saved == other.Saved && Integrity == other.Integrity && Activated == other.Activated &&
                   Charge == other.Charge && Collapsed == other.Collapsed && OffsetX == other.OffsetX &&
                   OffsetY == other.OffsetY && OffsetZ == other.OffsetZ && Heat == other.Heat;
        }
    }

    /// <summary>A copy of the whole world state at one moment: a checkpoint.</summary>
    public sealed class WorldStateSnapshot
    {
        internal readonly Dictionary<string, PuzzleRecord> Puzzles;
        internal readonly Dictionary<string, ObjectRecord> Objects;

        internal WorldStateSnapshot(Dictionary<string, PuzzleRecord> puzzles, Dictionary<string, ObjectRecord> objects)
        {
            Puzzles = new Dictionary<string, PuzzleRecord>(puzzles, StringComparer.Ordinal);
            Objects = new Dictionary<string, ObjectRecord>(objects, StringComparer.Ordinal);
        }

        public int Count => Puzzles.Count + Objects.Count;
    }

    /// <summary>
    /// Everything the game remembers about the world: the state of puzzle elements and the
    /// persistent state of magical objects, each under a stable identifier. It is the one store the
    /// session memory of puzzles (<see cref="PuzzleSessionState"/>) and the magical objects share;
    /// there is no second save framework beside it. Plain C#: it knows nothing about files or Unity.
    ///
    /// Puzzle keys and object identifiers are separate name spaces, so the two cannot overwrite
    /// each other. <see cref="Revision"/> rises with every real change, which is how the saving
    /// code knows there is something to write.
    /// </summary>
    public sealed class WorldStateStore
    {
        private readonly Dictionary<string, PuzzleRecord> puzzles = new Dictionary<string, PuzzleRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectRecord> objects = new Dictionary<string, ObjectRecord>(StringComparer.Ordinal);

        /// <summary>Rises by one with every change. Setting a record to what it already holds is not a change.</summary>
        public int Revision { get; private set; }

        public int PuzzleCount => puzzles.Count;
        public int ObjectCount => objects.Count;
        public int Count => puzzles.Count + objects.Count;

        /// <summary>Raised after every change.</summary>
        public event Action Changed;

        // ------------------------------------------------------------ puzzles

        public bool TryGetPuzzle(string key, out PuzzleRecord record)
        {
            if (string.IsNullOrEmpty(key))
            {
                record = default;
                return false;
            }

            return puzzles.TryGetValue(key, out record);
        }

        /// <summary>Stores a puzzle record. An empty key is ignored.</summary>
        public void SetPuzzle(string key, PuzzleRecord record)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (puzzles.TryGetValue(key, out PuzzleRecord existing) && existing.Active == record.Active &&
                existing.Locked == record.Locked)
            {
                return;
            }

            puzzles[key] = record;
            Touch();
        }

        public bool RemovePuzzle(string key)
        {
            if (string.IsNullOrEmpty(key) || !puzzles.Remove(key))
            {
                return false;
            }

            Touch();
            return true;
        }

        /// <summary>Forgets every puzzle record and leaves the objects alone.</summary>
        public void ClearPuzzles()
        {
            if (puzzles.Count == 0)
            {
                return;
            }

            puzzles.Clear();
            Touch();
        }

        // ------------------------------------------------------------ objects

        public bool TryGetObject(string id, out ObjectRecord record)
        {
            if (string.IsNullOrEmpty(id))
            {
                record = default;
                return false;
            }

            return objects.TryGetValue(id, out record);
        }

        /// <summary>
        /// Stores an object's record. An empty identifier is ignored, and an empty record removes what
        /// was stored: an object in its starting state needs no entry.
        /// </summary>
        public void SetObject(string id, in ObjectRecord record)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (record.IsEmpty)
            {
                RemoveObject(id);
                return;
            }

            if (objects.TryGetValue(id, out ObjectRecord existing) && existing.Matches(record))
            {
                return;
            }

            objects[id] = record;
            Touch();
        }

        public bool RemoveObject(string id)
        {
            if (string.IsNullOrEmpty(id) || !objects.Remove(id))
            {
                return false;
            }

            Touch();
            return true;
        }

        // -------------------------------------------------------------- whole

        /// <summary>
        /// Forgets every puzzle record and object record whose key or identifier starts with the prefix,
        /// such as everything of one level, and keeps the rest. Returns how many records were forgotten.
        /// </summary>
        public int RemoveWithPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return 0;
            }

            int removed = RemoveKeys(puzzles, prefix) + RemoveKeys(objects, prefix);
            if (removed > 0)
            {
                Touch();
            }

            return removed;
        }

        private static int RemoveKeys<T>(Dictionary<string, T> records, string prefix)
        {
            List<string> doomed = null;
            foreach (string key in records.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    (doomed ?? (doomed = new List<string>())).Add(key);
                }
            }

            if (doomed == null)
            {
                return 0;
            }

            foreach (string key in doomed)
            {
                records.Remove(key);
            }

            return doomed.Count;
        }

        /// <summary>Forgets everything, as at the start of a new game.</summary>
        public void Clear()
        {
            if (puzzles.Count == 0 && objects.Count == 0)
            {
                return;
            }

            puzzles.Clear();
            objects.Clear();
            Touch();
        }

        /// <summary>Copies the present state, to return to later.</summary>
        public WorldStateSnapshot Capture()
        {
            return new WorldStateSnapshot(puzzles, objects);
        }

        /// <summary>Replaces the present state with a snapshot. The snapshot stays usable.</summary>
        public void Restore(WorldStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            puzzles.Clear();
            objects.Clear();

            foreach (KeyValuePair<string, PuzzleRecord> pair in snapshot.Puzzles)
            {
                puzzles[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<string, ObjectRecord> pair in snapshot.Objects)
            {
                objects[pair.Key] = pair.Value;
            }

            Touch();
        }

        /// <summary>Replaces the present state with another store's.</summary>
        public void CopyFrom(WorldStateStore other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            Restore(other.Capture());
        }

        /// <summary>The puzzle keys in a fixed (ordinal) order, so that what is written is the same every time.</summary>
        public List<string> GetSortedPuzzleKeys()
        {
            var keys = new List<string>(puzzles.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        public List<string> GetSortedObjectIds()
        {
            var ids = new List<string>(objects.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private void Touch()
        {
            Revision++;
            Changed?.Invoke();
        }
    }
}
