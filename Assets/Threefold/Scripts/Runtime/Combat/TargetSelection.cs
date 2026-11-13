using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// A flat sector on the ground: everything within <see cref="Reach"/> of the origin, inside an
    /// arc centred on <see cref="Forward"/>, and no more than <see cref="VerticalReach"/> above or
    /// below. An arc of 360 degrees is a full circle. Melee strikes, the Flame Wave and the Force
    /// Pulse all hit a sector.
    /// </summary>
    public readonly struct AreaShape
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Forward;
        public readonly float Reach;
        public readonly float ArcDegrees;
        public readonly float VerticalReach;

        public AreaShape(Vector3 origin, Vector3 forward, float reach, float arcDegrees, float verticalReach)
        {
            Origin = origin;
            Forward = forward;
            Reach = reach;
            ArcDegrees = arcDegrees;
            VerticalReach = verticalReach;
        }
    }

    /// <summary>What target selection needs to know about one possible target.</summary>
    public struct TargetCandidate
    {
        /// <summary>The nearest point of the target's body to the attacker, at the height of the target's feet.</summary>
        public Vector3 Position;

        public bool Alive;

        /// <summary>On the attacker's side. Allies are never hit.</summary>
        public bool Allied;

        /// <summary>Part of the attacker itself.</summary>
        public bool IsSelf;
    }

    /// <summary>
    /// Decides which candidates an area attack hits, and which target a melee swing should turn
    /// towards. Geometry and rules only, with no physics, so it is tested outside Unity and gives
    /// melee, wave and pulse the same answer for the same situation.
    /// </summary>
    public static class TargetSelection
    {
        /// <summary>A target that may be hit at all: alive, not an ally and not the attacker.</summary>
        public static bool IsValid(in TargetCandidate candidate)
        {
            return candidate.Alive && !candidate.Allied && !candidate.IsSelf;
        }

        /// <summary>True when the point lies inside the sector.</summary>
        public static bool IsInside(in AreaShape shape, Vector3 point)
        {
            Vector3 offset = point - shape.Origin;
            if (Mathf.Abs(offset.y) > shape.VerticalReach)
            {
                return false;
            }

            offset.y = 0f;
            if (offset.sqrMagnitude > shape.Reach * shape.Reach)
            {
                return false;
            }

            if (shape.ArcDegrees >= 359.9f || offset.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            Vector3 forward = shape.Forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(forward, offset) <= shape.ArcDegrees * 0.5f;
        }

        /// <summary>
        /// Picks the valid candidates inside the sector, nearest first, up to <paramref name="maxTargets"/>.
        /// </summary>
        /// <param name="selected">Receives the indices of the chosen candidates. Its length also caps the result.</param>
        /// <returns>How many were chosen.</returns>
        public static int Select(in AreaShape shape, TargetCandidate[] candidates, int count, int maxTargets, int[] selected)
        {
            int limit = Mathf.Min(Mathf.Max(0, maxTargets), selected.Length);
            int chosen = 0;
            if (limit == 0)
            {
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                if (!IsValid(candidates[i]) || !IsInside(shape, candidates[i].Position))
                {
                    continue;
                }

                float distance = FlatSqrDistance(shape.Origin, candidates[i].Position);

                // Insertion into the nearest-first list; the lists are short.
                int slot = chosen < limit ? chosen : -1;
                for (int j = 0; j < chosen; j++)
                {
                    if (distance < FlatSqrDistance(shape.Origin, candidates[selected[j]].Position))
                    {
                        slot = j;
                        break;
                    }
                }

                if (slot < 0)
                {
                    continue;
                }

                int last = Mathf.Min(chosen, limit - 1);
                for (int j = last; j > slot; j--)
                {
                    selected[j] = selected[j - 1];
                }

                selected[slot] = i;
                chosen = Mathf.Min(chosen + 1, limit);
            }

            return chosen;
        }

        /// <summary>
        /// The target a swing should turn towards: a valid candidate within range and within
        /// <paramref name="maxAngleDegrees"/> of the wanted direction. Smaller angles win, with a small
        /// preference for nearer targets. Returns -1 if there is none, so the swing keeps its direction.
        /// </summary>
        public static int FindFacingTarget(Vector3 origin, Vector3 wantedDirection, TargetCandidate[] candidates, int count,
            float range, float maxAngleDegrees)
        {
            wantedDirection.y = 0f;
            if (wantedDirection.sqrMagnitude < 0.0001f || range <= 0f)
            {
                return -1;
            }

            int best = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (!IsValid(candidates[i]))
                {
                    continue;
                }

                Vector3 offset = candidates[i].Position - origin;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > range)
                {
                    continue;
                }

                // A target standing on the attacker is always in front.
                float angle = distance < 0.05f ? 0f : Vector3.Angle(wantedDirection, offset);
                if (angle > maxAngleDegrees)
                {
                    continue;
                }

                float score = angle + distance / range * 10f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return x * x + z * z;
        }
    }

    /// <summary>Whether a raised guard stops a hit, and how much of it.</summary>
    public static class BlockRules
    {
        /// <summary>
        /// True when the hit arrives from within the guarded arc in front of the defender.
        /// <paramref name="damageDirection"/> is the direction the damage travels in, so a hit from the
        /// front travels against the facing. A hit with no direction (a burn, a fall) is never covered.
        /// </summary>
        public static bool Covers(Vector3 facing, Vector3 damageDirection, float arcDegrees)
        {
            facing.y = 0f;
            damageDirection.y = 0f;
            if (facing.sqrMagnitude < 0.0001f || damageDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            if (arcDegrees >= 359.9f)
            {
                return true;
            }

            return Vector3.Angle(facing, -damageDirection) <= Mathf.Max(0f, arcDegrees) * 0.5f;
        }

        /// <summary>The damage that still gets through a guard that removes the given share (0 to 1).</summary>
        public static float Mitigate(float amount, float reduction)
        {
            return Mathf.Max(0f, amount) * (1f - Mathf.Clamp01(reduction));
        }
    }

    /// <summary>How a charged attack grows with the time it was held.</summary>
    public static class ChargeScaling
    {
        /// <summary>0 at the moment the charge starts, 1 once it has been held for the full charge time.</summary>
        public static float Power(float heldSeconds, float fullChargeSeconds)
        {
            if (fullChargeSeconds <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(heldSeconds / fullChargeSeconds);
        }

        /// <summary>A value between its uncharged and fully charged amounts. Power beyond 1 is capped.</summary>
        public static float Scale(float atNoCharge, float atFullCharge, float power)
        {
            return Mathf.Lerp(atNoCharge, atFullCharge, Mathf.Clamp01(power));
        }
    }
}
