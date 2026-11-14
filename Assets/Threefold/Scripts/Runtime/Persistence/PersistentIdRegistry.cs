using System;
using System.Collections.Generic;

namespace Threefold.Persistence
{
    /// <summary>
    /// Keeps track of which object in the running game owns which identifier. Two objects with the
    /// same identifier would overwrite each other's saved state, so the second to ask is refused and
    /// simply does not persist; the first is unaffected. The editor's validation finds duplicates
    /// before the game runs; this is the guard for the ones it could not see (two scenes loaded
    /// together, an object copied at run time). Plain C#.
    /// </summary>
    public static class PersistentIdRegistry
    {
        private static readonly Dictionary<string, object> Owners = new Dictionary<string, object>(StringComparer.Ordinal);

        public static int Count => Owners.Count;

        /// <summary>How often an identifier was asked for while another object held it.</summary>
        public static int DuplicateCount { get; private set; }

        /// <summary>
        /// Claims an identifier for an object. True if the object now owns it, or already did. False
        /// if the identifier is empty or another object owns it.
        /// </summary>
        public static bool TryRegister(string id, object owner)
        {
            if (string.IsNullOrEmpty(id) || owner == null)
            {
                return false;
            }

            if (Owners.TryGetValue(id, out object existing))
            {
                if (ReferenceEquals(existing, owner))
                {
                    return true;
                }

                DuplicateCount++;
                return false;
            }

            Owners.Add(id, owner);
            return true;
        }

        /// <summary>Gives an identifier up. Only its owner can; anyone else's call changes nothing.</summary>
        public static bool Unregister(string id, object owner)
        {
            if (string.IsNullOrEmpty(id) || !Owners.TryGetValue(id, out object existing) || !ReferenceEquals(existing, owner))
            {
                return false;
            }

            Owners.Remove(id);
            return true;
        }

        public static bool IsRegistered(string id)
        {
            return !string.IsNullOrEmpty(id) && Owners.ContainsKey(id);
        }

        /// <summary>Forgets every claim, as at the start of a session.</summary>
        public static void Clear()
        {
            Owners.Clear();
            DuplicateCount = 0;
        }

        /// <summary>
        /// Finds identifiers used more than once in a list, for validation before the game runs. Empty
        /// identifiers are not counted: an object without one is simply not persistent.
        /// </summary>
        /// <param name="duplicates">Receives each identifier that occurs more than once, once.</param>
        public static int FindDuplicates(IReadOnlyList<string> ids, List<string> duplicates)
        {
            duplicates.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!seen.Add(id) && reported.Add(id))
                {
                    duplicates.Add(id);
                }
            }

            return duplicates.Count;
        }

        // Claims made in one Play session must not survive into the next when domain reload is disabled.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
        }
    }
}
