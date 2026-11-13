using System.Collections.Generic;
using Threefold.Enemies;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// A stand-in for an enemy's body in the scene, so the brain and its state machine can be
    /// tested without Unity. It records what the brain asked for. Movement is instant when a test
    /// calls <see cref="ArriveAtDestination"/>; facing turns at once towards a look point.
    /// </summary>
    internal sealed class FakeEnemyBody : IEnemyBody
    {
        public readonly List<Vector3> Route = new List<Vector3>();
        public readonly List<string> Events = new List<string>();

        public Vector3 Position { get; set; }
        public Vector3 Forward { get; set; } = Vector3.forward;
        public Vector3 HomePosition { get; set; }
        public bool IsOnScreen { get; set; } = true;

        public bool CanReach { get; set; } = true;
        public bool StandPointsAvailable { get; set; } = true;
        public Vector3? Destination { get; private set; }
        public bool LastMoveRan { get; private set; }
        public int MoveCount { get; private set; }
        public Vector3? Look { get; private set; }
        public int KnockbackCount { get; private set; }
        public int PerformCount { get; private set; }
        public int CancelCount { get; private set; }
        public bool Defeated { get; private set; }

        public float LastStandMin { get; private set; }
        public float LastStandMax { get; private set; }
        public bool LastStandNeedsSight { get; private set; }
        public int StandPointRequests { get; private set; }

        public int PatrolPointCount => Route.Count;

        public Vector3 GetPatrolPoint(int index)
        {
            return Route[index];
        }

        public bool MoveTo(Vector3 destination, bool run)
        {
            if (!CanReach)
            {
                return false;
            }

            Destination = destination;
            LastMoveRan = run;
            MoveCount++;
            return true;
        }

        public void Stop()
        {
            Destination = null;
        }

        public bool HasArrived => Destination == null || EnemyRules.FlatDistance(Position, Destination.Value) < 0.2f;

        public void LookAt(Vector3 point)
        {
            Look = point;
            Vector3 direction = point - Position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Forward = direction.normalized;
            }
        }

        public void ClearLook()
        {
            Look = null;
        }

        public bool IsFacing(Vector3 point, float toleranceDegrees)
        {
            return EnemyRules.IsInsideViewCone(Forward, point - Position, toleranceDegrees * 2f);
        }

        public void Knockback(Vector3 direction, float distance)
        {
            KnockbackCount++;
        }

        public bool TryFindStandPoint(Vector3 target, float minDistance, float maxDistance, bool needsSight, out Vector3 point)
        {
            StandPointRequests++;
            LastStandMin = minDistance;
            LastStandMax = maxDistance;
            LastStandNeedsSight = needsSight;
            point = EnemyRules.PointAround(target, Position, EnemyRules.PreferredDistance(minDistance, maxDistance), 0f);
            return StandPointsAvailable;
        }

        public void BeginAttack(int attackIndex, float windupSeconds)
        {
            Events.Add("begin " + attackIndex);
        }

        public void LockAttack(int attackIndex)
        {
            Events.Add("lock " + attackIndex);
        }

        public bool PerformAttack(int attackIndex)
        {
            PerformCount++;
            Events.Add("perform " + attackIndex);
            return true;
        }

        public void EndAttack(int attackIndex, bool interrupted)
        {
            if (interrupted)
            {
                CancelCount++;
            }

            Events.Add((interrupted ? "cancel " : "end ") + attackIndex);
        }

        public void OnDefeated()
        {
            Defeated = true;
            Events.Add("defeated");
        }

        public void ArriveAtDestination()
        {
            if (Destination.HasValue)
            {
                Position = Destination.Value;
            }
        }
    }
}
