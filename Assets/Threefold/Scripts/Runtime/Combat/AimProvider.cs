using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>Where an ability should go.</summary>
    public readonly struct AimSolution
    {
        public readonly Vector3 Point;
        public readonly Vector3 Direction;

        public AimSolution(Vector3 point, Vector3 direction)
        {
            Point = point;
            Direction = direction;
        }
    }

    /// <summary>
    /// Decides what a character is aiming at. The player aims with the camera; an enemy would aim
    /// at its target. Abilities only see the result.
    /// </summary>
    public abstract class AimProvider : MonoBehaviour
    {
        /// <param name="origin">World position the ability starts from.</param>
        public abstract AimSolution GetAim(Vector3 origin);
    }
}
