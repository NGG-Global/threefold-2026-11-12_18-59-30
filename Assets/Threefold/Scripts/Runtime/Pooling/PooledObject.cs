using UnityEngine;

namespace Threefold.Pooling
{
    /// <summary>
    /// Marks an object as reusable. Instead of being destroyed, a pooled object is switched off and
    /// handed out again later, which avoids the memory churn of creating and destroying objects
    /// during play. Spawn instances through <see cref="Pools"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        internal Pool Owner;

        /// <summary>
        /// Counts the times this instance has been handed out. Whoever keeps a reference to a spawned instance
        /// keeps this number too: when it has changed, the instance was reused for something else, and the
        /// old owner must leave it alone.
        /// </summary>
        public int Generation { get; private set; }

        internal void MarkSpawned()
        {
            Generation++;
        }

        /// <summary>True while this instance is still the one handed out with <paramref name="generation"/>.</summary>
        public bool IsSpawn(int generation)
        {
            return Generation == generation && isActiveAndEnabled;
        }

        /// <summary>Switches the object off and makes it available for reuse.</summary>
        public void ReturnToPool()
        {
            if (Owner != null)
            {
                Owner.Release(this);
            }
            else
            {
                // Not spawned through a pool, for example placed in a scene by hand.
                gameObject.SetActive(false);
            }
        }
    }
}
