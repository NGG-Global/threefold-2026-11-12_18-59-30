using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Cameras
{
    /// <summary>
    /// Orbiting third-person follow camera. It follows a pivot above the target, turns with look
    /// input from a <see cref="CharacterInputSource"/>, and moves closer when geometry blocks the view.
    /// It reads no devices directly, so touch look can be added by supplying another input source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform to follow, normally the character root.")]
        [SerializeField] private Transform target;

        [Tooltip("Where look input comes from.")]
        [SerializeField] private CharacterInputSource inputSource;

        [Header("Framing")]
        [Tooltip("Point the camera orbits, measured from the target's position in world axes.")]
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Shifts the camera sideways (X) and up (Y) from the orbit point, in metres, so the character " +
                 "stands off-centre and the middle of the screen is clear for aiming.")]
        [SerializeField] private Vector2 shoulderOffset = new Vector2(0.55f, 0.15f);

        [Tooltip("Preferred distance from the orbit point, in metres.")]
        [Min(0f)]
        [SerializeField] private float distance = 5f;

        [Min(0f)]
        [SerializeField] private float minDistance = 1.5f;

        [Min(0f)]
        [SerializeField] private float maxDistance = 9f;

        [Header("Orbit")]
        [Tooltip("Degrees of horizontal turn per look unit (one unit is one pixel of mouse travel).")]
        [Min(0f)]
        [SerializeField] private float horizontalSensitivity = 0.15f;

        [Tooltip("Degrees of vertical turn per look unit.")]
        [Min(0f)]
        [SerializeField] private float verticalSensitivity = 0.12f;

        [SerializeField] private bool invertVertical;

        [Tooltip("Lowest camera angle in degrees. Negative values place the camera below the orbit point.")]
        [Range(-89f, 89f)]
        [SerializeField] private float minPitch = -25f;

        [Tooltip("Highest camera angle in degrees.")]
        [Range(-89f, 89f)]
        [SerializeField] private float maxPitch = 65f;

        [Tooltip("Camera angle used when the camera is first placed behind the target.")]
        [Range(-89f, 89f)]
        [SerializeField] private float startPitch = 15f;

        [Header("Smoothing")]
        [Tooltip("Approximate time for the camera to catch up with the target's position, in seconds.")]
        [Min(0f)]
        [SerializeField] private float followSmoothTime = 0.06f;

        [Tooltip("Approximate time for the camera to catch up with look input, in seconds. 0 is instant.")]
        [Min(0f)]
        [SerializeField] private float rotationSmoothTime = 0.04f;

        [Header("Collision")]
        [Tooltip("Move the camera closer when geometry is between it and the orbit point.")]
        [SerializeField] private bool avoidClipping = true;

        [SerializeField] private LayerMask collisionLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Clearance kept between the camera and geometry, in metres.")]
        [Min(0.01f)]
        [SerializeField] private float collisionRadius = 0.25f;

        [Tooltip("How quickly the camera returns to its preferred distance once the view is clear, in metres per second.")]
        [Min(0f)]
        [SerializeField] private float distanceRecoverSpeed = 6f;

        [Header("Shake")]
        [Tooltip("Largest rotation, in degrees, that camera shake can add.")]
        [Min(0f)]
        [SerializeField] private float maxShakeAngle = 1.6f;

        [Tooltip("How quickly shake dies away, per second.")]
        [Min(0.01f)]
        [SerializeField] private float shakeDecay = 2.6f;

        [Tooltip("Speed of the shake movement.")]
        [Min(0f)]
        [SerializeField] private float shakeFrequency = 22f;

        private const float MinimumClipDistance = 0.3f;

        private readonly RaycastHit[] collisionHits = new RaycastHit[8];

        private Vector3 pivot;
        private Vector3 framingPivot;
        private float trauma;
        private Vector3 pivotVelocity;
        private float targetYaw;
        private float targetPitch;
        private float yaw;
        private float pitch;
        private float yawVelocity;
        private float pitchVelocity;
        private float currentDistance;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public CharacterInputSource InputSource
        {
            get => inputSource;
            set => inputSource = value;
        }

        /// <summary>Preferred follow distance in metres, kept within the configured limits.</summary>
        public float Distance
        {
            get => distance;
            set => distance = Mathf.Clamp(value, minDistance, maxDistance);
        }

        private void OnEnable()
        {
            SnapToTarget();
        }

        private void OnValidate()
        {
            if (maxDistance < minDistance)
            {
                maxDistance = minDistance;
            }

            if (maxPitch < minPitch)
            {
                maxPitch = minPitch;
            }

            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            startPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (target == null || deltaTime <= 0f)
            {
                return;
            }

            ApplyLookInput();

            if (rotationSmoothTime > 0f)
            {
                yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, rotationSmoothTime, Mathf.Infinity, deltaTime);
                pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime, Mathf.Infinity, deltaTime);
            }
            else
            {
                yaw = targetYaw;
                pitch = targetPitch;
            }

            Vector3 desiredPivot = target.position + targetOffset;
            pivot = followSmoothTime > 0f
                ? Vector3.SmoothDamp(pivot, desiredPivot, ref pivotVelocity, followSmoothTime, Mathf.Infinity, deltaTime)
                : desiredPivot;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            framingPivot = GetFramingPivot(rotation);
            float allowedDistance = GetUnobstructedDistance(rotation);
            trauma = Mathf.Max(0f, trauma - shakeDecay * Time.unscaledDeltaTime);

            // Close in immediately so geometry never hides the character; ease back out to avoid a pop.
            currentDistance = allowedDistance < currentDistance
                ? allowedDistance
                : Mathf.MoveTowards(currentDistance, allowedDistance, distanceRecoverSpeed * deltaTime);

            ApplyTransform(rotation);
        }

        /// <summary>Places the camera directly behind the target, without smoothing.</summary>
        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            targetYaw = target.eulerAngles.y;
            targetPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);
            yaw = targetYaw;
            pitch = targetPitch;
            yawVelocity = 0f;
            pitchVelocity = 0f;

            pivot = target.position + targetOffset;
            pivotVelocity = Vector3.zero;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            framingPivot = GetFramingPivot(rotation);
            trauma = 0f;
            currentDistance = GetUnobstructedDistance(rotation);
            ApplyTransform(rotation);
        }

        private void ApplyLookInput()
        {
            if (inputSource == null)
            {
                return;
            }

            Vector2 look = inputSource.LookDelta;
            float verticalDirection = invertVertical ? 1f : -1f;

            targetYaw += look.x * horizontalSensitivity;
            targetPitch = Mathf.Clamp(targetPitch + look.y * verticalSensitivity * verticalDirection, minPitch, maxPitch);
        }

        /// <summary>
        /// Adds camera shake. Amounts accumulate up to 1 and die away on their own. Typical values
        /// are 0.1 for a light kick and 0.4 for a heavy impact.
        /// </summary>
        public void AddTrauma(float amount)
        {
            trauma = Mathf.Clamp01(trauma + Mathf.Max(0f, amount));
        }

        private void ApplyTransform(Quaternion rotation)
        {
            Vector3 position = framingPivot - rotation * Vector3.forward * currentDistance;

            if (trauma > 0f && maxShakeAngle > 0f)
            {
                // Squaring makes small amounts subtle and keeps strong hits pronounced.
                float strength = trauma * trauma * maxShakeAngle;
                float time = Time.unscaledTime * shakeFrequency;
                float shakePitch = (Mathf.PerlinNoise(time, 0.37f) - 0.5f) * 2f * strength;
                float shakeYaw = (Mathf.PerlinNoise(0.71f, time) - 0.5f) * 2f * strength;
                rotation *= Quaternion.Euler(shakePitch, shakeYaw, 0f);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// The point the camera pulls back from: the orbit point moved to the shoulder. The sideways
        /// shift is shortened when a wall is in the way, so the camera never starts inside geometry.
        /// </summary>
        private Vector3 GetFramingPivot(Quaternion rotation)
        {
            Vector3 raised = pivot + Vector3.up * shoulderOffset.y;
            float side = Mathf.Abs(shoulderOffset.x);
            if (side <= 0.001f)
            {
                return raised;
            }

            Vector3 direction = rotation * (shoulderOffset.x > 0f ? Vector3.right : Vector3.left);

            if (avoidClipping)
            {
                int hitCount = Physics.SphereCastNonAlloc(raised, collisionRadius, direction, collisionHits, side,
                    collisionLayers, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = collisionHits[i];
                    if (hit.transform == null || hit.transform.IsChildOf(target) || hit.distance <= 0f)
                    {
                        continue;
                    }

                    side = Mathf.Min(side, hit.distance);
                }
            }

            return raised + direction * side;
        }

        private float GetUnobstructedDistance(Quaternion rotation)
        {
            float preferred = Mathf.Clamp(distance, minDistance, maxDistance);
            if (!avoidClipping || preferred <= 0f)
            {
                return preferred;
            }

            Vector3 direction = rotation * Vector3.back;
            int hitCount = Physics.SphereCastNonAlloc(framingPivot, collisionRadius, direction, collisionHits, preferred,
                collisionLayers, QueryTriggerInteraction.Ignore);

            float nearest = preferred;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = collisionHits[i];

                // The character must never push its own camera in.
                if (hit.transform == null || hit.transform.IsChildOf(target))
                {
                    continue;
                }

                // A zero distance means the sweep started inside the collider; there is no usable surface point.
                if (hit.distance <= 0f)
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                }
            }

            return Mathf.Max(nearest, Mathf.Min(MinimumClipDistance, preferred));
        }
    }
}
