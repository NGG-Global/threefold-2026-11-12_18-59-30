using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>What the enemy currently perceives of its target.</summary>
    public readonly struct TargetObservation
    {
        /// <summary>A living target exists. False when there is no player, or the player is defeated.</summary>
        public readonly bool Exists;

        /// <summary>The target is within sight: in range, in the field of view, and not behind cover.</summary>
        public readonly bool Visible;

        /// <summary>Where the target is. The brain only relies on it while the target is visible, or just after.</summary>
        public readonly Vector3 Position;

        public TargetObservation(bool exists, bool visible, Vector3 position)
        {
            Exists = exists;
            Visible = exists && visible;
            Position = position;
        }

        public static TargetObservation None => default;
    }

    /// <summary>
    /// The enemy's body, as seen by its decision making: movement, facing, navigation queries,
    /// attacks and presentation. The Unity component implements it with a NavMeshAgent and an
    /// Ability Caster; the tests implement it with a simple fake. Nothing here decides anything.
    /// </summary>
    public interface IEnemyBody
    {
        /// <summary>Position on the ground.</summary>
        Vector3 Position { get; }

        /// <summary>Facing direction along the ground.</summary>
        Vector3 Forward { get; }

        /// <summary>Where the enemy started, and returns to.</summary>
        Vector3 HomePosition { get; }

        /// <summary>True while the body is in the player's view. Off-screen enemies wait longer to attack.</summary>
        bool IsOnScreen { get; }

        int PatrolPointCount { get; }

        Vector3 GetPatrolPoint(int index);

        /// <summary>Starts moving towards a point. Returns false if no path can be found.</summary>
        bool MoveTo(Vector3 destination, bool run);

        void Stop();

        /// <summary>True once the last destination has been reached, or cannot be got any closer to.</summary>
        bool HasArrived { get; }

        /// <summary>Keeps facing a point, also while moving. Until cleared, movement does not turn the body.</summary>
        void LookAt(Vector3 point);

        /// <summary>Lets the body face the way it moves again, or hold its current facing when still.</summary>
        void ClearLook();

        bool IsFacing(Vector3 point, float toleranceDegrees);

        /// <summary>Pushes the body back along the ground, as far as navigation allows.</summary>
        void Knockback(Vector3 direction, float distance);

        /// <summary>
        /// Finds a reachable place to stand between <paramref name="minDistance"/> and
        /// <paramref name="maxDistance"/> from the target, preferring the side the enemy is on.
        /// With <paramref name="needsSight"/>, the place must have a clear view of the target.
        /// </summary>
        bool TryFindStandPoint(Vector3 target, float minDistance, float maxDistance, bool needsSight, out Vector3 point);

        /// <summary>The wind-up of an attack starts: show its telegraph.</summary>
        void BeginAttack(int attackIndex, float windupSeconds);

        /// <summary>The attack stops following the target: from now on it can be dodged.</summary>
        void LockAttack(int attackIndex);

        /// <summary>The attack lands. Returns false if the ability could not be used.</summary>
        bool PerformAttack(int attackIndex);

        /// <summary>The attack has ended, either by landing or by being interrupted.</summary>
        void EndAttack(int attackIndex, bool interrupted);

        /// <summary>The enemy has been defeated: stop, stop blocking, and play the defeat.</summary>
        void OnDefeated();
    }
}
