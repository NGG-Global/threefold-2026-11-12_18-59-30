using System.Collections.Generic;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Tells a magical object how many creatures stand on it, so that weight can bear on an unstable
    /// walkway (<see cref="StabilitySettings.loadStressPerSecond"/>). It sits on a trigger collider over
    /// the walking surface and counts creatures (anything with <see cref="Health"/>) entering and
    /// leaving. It has no update: it answers physics callbacks, and the object asks it to recount while
    /// it is being simulated, in case a creature left without the physics engine saying so.
    ///
    /// Only colliders that physics reports to a trigger count: the player's character controller does;
    /// an enemy moved by its navigation agent without a Rigidbody does not.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class LoadZone : MonoBehaviour
    {
        private const int MaxTracked = 8;

        [Tooltip("The object whose weight this zone measures.")]
        [SerializeField] private MagicalObject target;

        private readonly List<Collider> inside = new List<Collider>(MaxTracked);
        private readonly List<Health> counted = new List<Health>(MaxTracked);
        private Collider zone;
        private int reported;

        public MagicalObject Target => target;

        /// <summary>How many creatures stand in the zone, as last reported.</summary>
        public int Occupants => reported;

        /// <summary>Connects the zone. Called by the setup tooling.</summary>
        public void Bind(MagicalObject magicalObject)
        {
            target = magicalObject;
        }

        /// <summary>
        /// Forgets what has gone: a creature that was defeated, switched off or moved away without a
        /// callback. Called by the object while it is simulated; cheap with a handful of entries.
        /// </summary>
        public void Recount()
        {
            Bounds bounds = Zone != null ? Zone.bounds : default;
            for (int i = inside.Count - 1; i >= 0; i--)
            {
                Collider other = inside[i];
                if (other == null || !other.enabled || !other.gameObject.activeInHierarchy ||
                    (Zone != null && !bounds.Intersects(other.bounds)))
                {
                    inside.RemoveAt(i);
                }
            }

            Report();
        }

        private Collider Zone => zone != null ? zone : zone = GetComponent<Collider>();

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || inside.Contains(other) || inside.Count >= MaxTracked || other.GetComponentInParent<Health>() == null)
            {
                return;
            }

            inside.Add(other);
            Report();
        }

        private void OnTriggerExit(Collider other)
        {
            if (inside.Remove(other))
            {
                Report();
            }
        }

        private void OnDisable()
        {
            inside.Clear();
            Report();
        }

        /// <summary>Counts each living creature once, however many of its colliders are inside.</summary>
        private void Report()
        {
            counted.Clear();
            for (int i = 0; i < inside.Count; i++)
            {
                Health health = inside[i] != null ? inside[i].GetComponentInParent<Health>() : null;
                if (health != null && health.IsAlive && !counted.Contains(health))
                {
                    counted.Add(health);
                }
            }

            int count = counted.Count;
            counted.Clear();
            if (count == reported)
            {
                return;
            }

            reported = count;
            if (target != null)
            {
                target.SetOccupants(count);
            }
        }
    }
}
