using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Pooling
{
    /// <summary>
    /// A bounded pool of instances of one prefab. When every instance is in use and the limit has
    /// been reached, the oldest active instance is reused, so the number of live objects can never
    /// grow without bound.
    /// </summary>
    public sealed class Pool
    {
        private readonly PooledObject prefab;
        private readonly Transform root;
        private readonly Stack<PooledObject> inactive = new Stack<PooledObject>();
        private readonly List<PooledObject> active = new List<PooledObject>();
        private int maxInstances;

        internal Pool(PooledObject prefab, int maxInstances)
        {
            this.prefab = prefab;
            this.maxInstances = Mathf.Max(1, maxInstances);
            root = new GameObject("[Pool] " + prefab.name).transform;
        }

        /// <summary>False once the scene that held the pooled instances has been unloaded.</summary>
        public bool IsValid => root != null;

        public int ActiveCount => active.Count;

        public int MaxInstances
        {
            get => maxInstances;
            set => maxInstances = Mathf.Max(maxInstances, value);
        }

        public PooledObject Spawn(Vector3 position, Quaternion rotation)
        {
            PooledObject instance = TakeInactive();

            if (instance == null && active.Count >= maxInstances)
            {
                instance = active[0];
                active.RemoveAt(0);

                if (instance != null)
                {
                    instance.gameObject.SetActive(false);
                }
            }

            if (instance == null)
            {
                instance = Object.Instantiate(prefab, position, rotation, root);
                instance.Owner = this;
            }

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            active.Add(instance);
            return instance;
        }

        public void Release(PooledObject instance)
        {
            if (instance == null || !active.Remove(instance))
            {
                return;
            }

            instance.gameObject.SetActive(false);
            inactive.Push(instance);
        }

        private PooledObject TakeInactive()
        {
            while (inactive.Count > 0)
            {
                PooledObject candidate = inactive.Pop();
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
