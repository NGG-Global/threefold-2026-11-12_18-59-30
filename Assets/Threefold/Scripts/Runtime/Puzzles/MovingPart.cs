using System;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A part that moves between its rest pose and a moved pose: a door leaf, a sinking seal, a
    /// lever handle, a chest lid. The moved pose is an offset from wherever the part stands in the
    /// scene, so the part can be placed and tuned freely.
    /// </summary>
    [Serializable]
    public sealed class MovingPart
    {
        [Tooltip("The part that moves. Its colliders move with it.")]
        public Transform target;

        [Tooltip("Change of local position when fully moved, in metres.")]
        public Vector3 positionOffset;

        [Tooltip("Change of local rotation when fully moved, in degrees.")]
        public Vector3 rotationOffset;

        [NonSerialized] private Vector3 restPosition;
        [NonSerialized] private Quaternion restRotation = Quaternion.identity;
        [NonSerialized] private bool restKnown;

        public MovingPart()
        {
        }

        public MovingPart(Transform part, Vector3 moveBy, Vector3 turnBy)
        {
            target = part;
            positionOffset = moveBy;
            rotationOffset = turnBy;
        }

        /// <summary>Remembers the current pose as the rest pose, once.</summary>
        public void CaptureRest()
        {
            if (restKnown || target == null)
            {
                return;
            }

            restPosition = target.localPosition;
            restRotation = target.localRotation;
            restKnown = true;
        }

        /// <summary>Places the part. 0 is the rest pose, 1 fully moved; the movement is eased.</summary>
        public void Apply(float amount)
        {
            if (target == null)
            {
                return;
            }

            CaptureRest();
            float eased = DoorMotion.Ease(amount);
            target.localPosition = restPosition + positionOffset * eased;
            target.localRotation = restRotation * Quaternion.Euler(rotationOffset * eased);
        }
    }
}
