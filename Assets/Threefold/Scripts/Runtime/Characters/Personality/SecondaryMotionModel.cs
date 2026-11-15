using System;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>What a secondary bone carries, which decides how it swings.</summary>
    public enum SecondaryKind
    {
        Default,
        Cloak,
        Hair,
        Sash,
    }

    /// <summary>How one kind of secondary bone swings.</summary>
    [Serializable]
    public sealed class SecondaryChainTuning
    {
        [Tooltip("How strongly a bone is pulled back to its animated direction. Higher is stiffer and quicker.")]
        [Range(5f, 400f)] public float stiffness = 70f;

        [Tooltip("How quickly a swing dies down. Higher is heavier.")]
        [Range(0.5f, 30f)] public float damping = 7f;

        [Tooltip("How far the bone's resting direction is pulled from where the animation points it towards hanging " +
                 "straight down, 0 (rigid to the body) to 1 (always hangs). Within the swing limit.")]
        [Range(0f, 1f)] public float gravity = 0.5f;

        [Tooltip("Share of steady movement the bone feels as wind, 0 to 1: how far it trails behind while running.")]
        [Range(0f, 1f)] public float trail = 0.1f;

        [Tooltip("The furthest a bone may swing from its animated direction, in degrees.")]
        [Range(0f, 80f)] public float maxAngleDegrees = 30f;

        public static SecondaryChainTuning Cloak()
        {
            return new SecondaryChainTuning { stiffness = 40f, damping = 5f, gravity = 0.85f, trail = 0.15f, maxAngleDegrees = 75f };
        }

        public static SecondaryChainTuning Sash()
        {
            return new SecondaryChainTuning { stiffness = 55f, damping = 6f, gravity = 0.85f, trail = 0.12f, maxAngleDegrees = 75f };
        }

        public static SecondaryChainTuning Hair()
        {
            return new SecondaryChainTuning { stiffness = 110f, damping = 9f, gravity = 0.5f, trail = 0.1f, maxAngleDegrees = 50f };
        }
    }

    /// <summary>The tuning of every kind of secondary bone.</summary>
    [Serializable]
    public sealed class SecondaryMotionTuning
    {
        public SecondaryChainTuning cloak = SecondaryChainTuning.Cloak();
        public SecondaryChainTuning hair = SecondaryChainTuning.Hair();
        public SecondaryChainTuning sash = SecondaryChainTuning.Sash();
        public SecondaryChainTuning other = new SecondaryChainTuning();

        public SecondaryChainTuning Get(SecondaryKind kind)
        {
            SecondaryChainTuning chosen = kind == SecondaryKind.Cloak ? cloak : kind == SecondaryKind.Hair ? hair
                : kind == SecondaryKind.Sash ? sash : other;
            return chosen ?? other ?? FallbackTuning;
        }

        private static readonly SecondaryChainTuning FallbackTuning = new SecondaryChainTuning();
    }

    /// <summary>
    /// Follow-through for a few hair and cloak bones: each bone's tip is a point on a damped spring that is pulled
    /// to where it would hang (the direction the animation gives it, pulled towards straight down by its share of
    /// gravity, so a cloak hangs when the chest pitches forward), is left behind for a moment when the body moves, trails a little while it runs, and may not swing
    /// past a limit from the animated direction. This is not cloth simulation: bones do not collide with the body or with each other, and
    /// nothing connects one chain to another. Plain C#, a fixed number of bones, no allocation after construction.
    ///
    /// The caller steps the bones parent first: a bone's head is given after its parent has been turned.
    /// </summary>
    public sealed class SecondaryMotionModel
    {
        public const int Capacity = 16;

        /// <summary>A head that moves further than this in one step has been teleported: the bone starts again at rest.</summary>
        public const float TeleportDistance = 2f;

        private const float MaxStep = 0.1f;
        private const float SubStep = 1f / 60f;

        private readonly SecondaryMotionTuning tuning;
        private readonly SecondaryKind[] kinds = new SecondaryKind[Capacity];
        private readonly float[] lengths = new float[Capacity];
        private readonly Vector3[] tips = new Vector3[Capacity];
        private readonly Vector3[] velocities = new Vector3[Capacity];
        private readonly Vector3[] heads = new Vector3[Capacity];
        private readonly bool[] started = new bool[Capacity];
        private int count;

        public SecondaryMotionModel(SecondaryMotionTuning tuning)
        {
            this.tuning = tuning ?? new SecondaryMotionTuning();
        }

        public int Count => count;

        /// <summary>The kind named by a bone ("sec_cloak_l_01" gives Cloak). Bones are named sec_&lt;kind&gt;_...</summary>
        public static SecondaryKind KindOf(string boneName)
        {
            if (string.IsNullOrEmpty(boneName) || !IsSecondary(boneName))
            {
                return SecondaryKind.Default;
            }

            int start = BonePrefix.Length;
            int end = boneName.IndexOf('_', start);
            int length = (end < 0 ? boneName.Length : end) - start;
            if (length == 5 && string.Compare(boneName, start, "cloak", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return SecondaryKind.Cloak;
            }

            if (length == 4 && string.Compare(boneName, start, "hair", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return SecondaryKind.Hair;
            }

            if (length == 4 && string.Compare(boneName, start, "sash", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return SecondaryKind.Sash;
            }

            return SecondaryKind.Default;
        }

        public const string BonePrefix = "sec_";

        public static bool IsSecondary(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) && boneName.StartsWith(BonePrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adds a bone of a length in metres. Returns its index, or -1 when the model is full.</summary>
        public int Add(SecondaryKind kind, float length)
        {
            if (count >= Capacity)
            {
                return -1;
            }

            kinds[count] = kind;
            lengths[count] = Mathf.Max(0.01f, length);
            started[count] = false;
            return count++;
        }

        public void Clear()
        {
            count = 0;
        }

        /// <summary>Every bone starts again at rest on its next step: after a teleport, or when the character is shown again.</summary>
        public void ResetAll()
        {
            for (int i = 0; i < count; i++)
            {
                started[i] = false;
            }
        }

        /// <summary>
        /// Advances one bone and returns the direction its tip should point in (unit length, world space).
        /// </summary>
        /// <param name="head">Where the bone starts now, in world space.</param>
        /// <param name="restDirection">Where the animation points it now, in world space.</param>
        public Vector3 Step(int index, Vector3 head, Vector3 restDirection, float deltaTime)
        {
            if (index < 0 || index >= count)
            {
                return restDirection;
            }

            float restLength = restDirection.magnitude;
            if (restLength < 1e-6f || float.IsNaN(restLength) || float.IsNaN(head.x + head.y + head.z))
            {
                started[index] = false;
                return restDirection;
            }

            restDirection /= restLength;
            float length = lengths[index];
            SecondaryChainTuning chain = tuning.Get(kinds[index]);
            float cosLimit = Mathf.Cos(Mathf.Clamp(chain.maxAngleDegrees, 0f, 89f) * Mathf.Deg2Rad);
            float sinLimit = Mathf.Sqrt(Mathf.Max(0f, 1f - cosLimit * cosLimit));
            Vector3 hangDirection = HangDirection(restDirection, Mathf.Clamp01(chain.gravity), cosLimit, sinLimit);

            if (!started[index] || (head - heads[index]).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                started[index] = true;
                heads[index] = head;
                tips[index] = head + hangDirection * length;
                velocities[index] = Vector3.zero;
                return hangDirection;
            }

            float dt = float.IsNaN(deltaTime) ? 0f : Mathf.Clamp(deltaTime, 0f, MaxStep);
            if (dt <= 0f)
            {
                // No time has passed: the bone keeps its swing, moved with its head and kept within its limit.
                Vector3 kept = WithinLimit(Direction(tips[index] - heads[index], restDirection), restDirection, cosLimit, sinLimit);
                heads[index] = head;
                tips[index] = head + kept * length;
                return kept;
            }

            float stiffness = Mathf.Clamp(chain.stiffness, 1f, 400f);
            float damping = Mathf.Clamp(chain.damping, 0.1f, 30f);

            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / SubStep - 1e-4f));
            float h = dt / steps;
            Vector3 previousHead = heads[index];
            Vector3 headStep = (head - previousHead) / steps;
            Vector3 carried = headStep / h * (1f - Mathf.Clamp01(chain.trail)); // the air moves with the body, mostly
            Vector3 tip = tips[index];
            Vector3 velocity = velocities[index];

            for (int i = 0; i < steps; i++)
            {
                // The pull is measured from where the head was when the step began and the bone is then fitted to
                // where the head is at its end; measuring both at the end would make the trail depend on the step.
                Vector3 stepHead = previousHead + headStep * (i + 1);
                Vector3 stepRest = previousHead + headStep * i + hangDirection * length;

                velocity += (stiffness * (stepRest - tip) - damping * (velocity - carried)) * h;
                Vector3 before = tip;
                tip += velocity * h;

                // Keep the bone's length and its limit, then take the velocity the point really had.
                Vector3 direction = WithinLimit(Direction(tip - stepHead, restDirection), restDirection, cosLimit, sinLimit);

                tip = stepHead + direction * length;
                velocity = (tip - before) / h;
            }

            if (float.IsNaN(tip.x + tip.y + tip.z) || float.IsNaN(velocity.x + velocity.y + velocity.z))
            {
                tip = head + hangDirection * length;
                velocity = Vector3.zero;
            }

            heads[index] = head;
            tips[index] = tip;
            velocities[index] = velocity;
            return Direction(tip - head, restDirection);
        }

        /// <summary>
        /// Where a bone comes to rest: the animated direction pulled towards straight down by its share of gravity,
        /// no further from the animated direction than its limit.
        /// </summary>
        public static Vector3 HangDirection(Vector3 restDirection, float gravity, float maxAngleDegrees)
        {
            float cosLimit = Mathf.Cos(Mathf.Clamp(maxAngleDegrees, 0f, 89f) * Mathf.Deg2Rad);
            return HangDirection(Direction(restDirection, Vector3.down), Mathf.Clamp01(gravity), cosLimit,
                Mathf.Sqrt(Mathf.Max(0f, 1f - cosLimit * cosLimit)));
        }

        private static Vector3 HangDirection(Vector3 restDirection, float gravity, float cosLimit, float sinLimit)
        {
            Vector3 pulled = restDirection * (1f - gravity) + Vector3.down * gravity;
            return WithinLimit(Direction(pulled, restDirection), restDirection, cosLimit, sinLimit);
        }

        /// <summary>A direction no further from the rest direction than the limit.</summary>
        private static Vector3 WithinLimit(Vector3 direction, Vector3 restDirection, float cosLimit, float sinLimit)
        {
            float along = Vector3.Dot(direction, restDirection);
            if (along >= cosLimit)
            {
                return direction;
            }

            Vector3 across = direction - restDirection * along;
            float acrossLength = across.magnitude;
            return acrossLength > 1e-6f ? restDirection * cosLimit + across * (sinLimit / acrossLength) : restDirection;
        }

        private static Vector3 Direction(Vector3 offset, Vector3 fallback)
        {
            float length = offset.magnitude;
            return length > 1e-6f ? offset / length : fallback;
        }
    }
}
