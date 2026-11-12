using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Pooling
{
    /// <summary>Hands out pooled instances of prefabs. One pool is kept per prefab.</summary>
    public static class Pools
    {
        private static readonly Dictionary<PooledObject, Pool> ByPrefab = new Dictionary<PooledObject, Pool>();

        /// <summary>Spawns an instance of the prefab from its pool, creating the pool on first use.</summary>
        /// <param name="maxInstances">Most instances of this prefab that may exist at once.</param>
        public static PooledObject Spawn(PooledObject prefab, Vector3 position, Quaternion rotation, int maxInstances)
        {
            if (prefab == null)
            {
                return null;
            }

            if (!ByPrefab.TryGetValue(prefab, out Pool pool) || !pool.IsValid)
            {
                pool = new Pool(prefab, maxInstances);
                ByPrefab[prefab] = pool;
            }
            else
            {
                pool.MaxInstances = maxInstances;
            }

            return pool.Spawn(position, rotation);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByPrefab.Clear();
        }
    }
}
