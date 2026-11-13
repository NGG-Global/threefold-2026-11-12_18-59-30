using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Aims a non-player character at a target. It can lead a moving target, scatter its shots by
    /// a few degrees, and lock its aim a moment before firing, so a player who sees the shot
    /// coming can step out of its path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TargetAimProvider : AimProvider
    {
        [Tooltip("What to aim at. Normally assigned at runtime by the enemy.")]
        [SerializeField] private Health target;

        [Tooltip("Share of the target's movement that is anticipated. 0 aims where the target is; 1 aims where it will be.")]
        [Range(0f, 1f)]
        [SerializeField] private float lead = 0.5f;

        [Tooltip("Random scatter of each shot, in degrees.")]
        [Range(0f, 15f)]
        [SerializeField] private float spreadDegrees = 2f;

        [Tooltip("Speed of the projectile, in metres per second, used to work out how far ahead to aim.")]
        [Min(0.1f)]
        [SerializeField] private float projectileSpeed = 14f;

        private Vector3 lastTargetPosition;
        private Vector3 targetVelocity;
        private bool sampled;
        private bool locked;
        private Vector3 lockedPoint;

        public Health Target
        {
            get => target;
            set
            {
                if (target != value)
                {
                    target = value;
                    sampled = false;
                    targetVelocity = Vector3.zero;
                }
            }
        }

        public bool IsLocked => locked;

        /// <summary>Sets accuracy. Called by the enemy when difficulty is applied.</summary>
        public void Configure(float leadShare, float spread, float speed)
        {
            lead = Mathf.Clamp01(leadShare);
            spreadDegrees = Mathf.Clamp(spread, 0f, 15f);
            projectileSpeed = Mathf.Max(0.1f, speed);
        }

        /// <summary>Fixes the aim point where it is now. Shots go there until <see cref="Unlock"/>.</summary>
        public void Lock(Vector3 origin)
        {
            lockedPoint = PredictPoint(origin);
            locked = true;
        }

        public void Unlock()
        {
            locked = false;
        }

        /// <summary>The point the next shot would go to, without scatter.</summary>
        public Vector3 CurrentAimPoint(Vector3 origin)
        {
            return locked ? lockedPoint : PredictPoint(origin);
        }

        public override AimSolution GetAim(Vector3 origin)
        {
            Vector3 point = CurrentAimPoint(origin);
            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;

            if (distance < 0.01f)
            {
                Vector3 forward = transform.forward;
                return new AimSolution(origin + forward * 10f, forward);
            }

            Vector3 direction = toPoint / distance;

            if (spreadDegrees > 0f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, direction);
                if (right.sqrMagnitude < 0.0001f)
                {
                    right = transform.right;
                }

                Quaternion scatter = Quaternion.AngleAxis(Random.Range(-spreadDegrees, spreadDegrees), Vector3.up) *
                                     Quaternion.AngleAxis(Random.Range(-spreadDegrees, spreadDegrees) * 0.5f, right.normalized);
                direction = scatter * direction;
            }

            return new AimSolution(origin + direction * distance, direction);
        }

        private Vector3 PredictPoint(Vector3 origin)
        {
            if (target == null)
            {
                return origin + transform.forward * 10f;
            }

            Vector3 point = target.AimPoint;
            if (lead > 0f && sampled)
            {
                float flightTime = Vector3.Distance(origin, point) / projectileSpeed;
                Vector3 drift = targetVelocity * (flightTime * lead);
                drift.y = 0f;
                point += drift;
            }

            return point;
        }

        // Only the target's position is sampled, so this works for any target without knowing how it moves.
        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 position = target.transform.position;

            if (sampled && deltaTime > 0f)
            {
                Vector3 velocity = (position - lastTargetPosition) / deltaTime;

                // A teleport (restart) must not be read as a burst of speed.
                if (velocity.sqrMagnitude > 400f)
                {
                    velocity = Vector3.zero;
                }

                targetVelocity = Vector3.Lerp(targetVelocity, velocity, 1f - Mathf.Exp(-10f * deltaTime));
            }

            lastTargetPosition = position;
            sampled = true;
        }
    }
}
