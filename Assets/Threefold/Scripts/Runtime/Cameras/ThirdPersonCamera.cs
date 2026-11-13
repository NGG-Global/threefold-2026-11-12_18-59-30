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

        [Tooltip("Approximate time for the camera to follow the target up and down, in seconds. Higher than the " +
                 "sideways value, so jumps, steps and climbs do not jolt the view.")]
        [Min(0f)]
        [SerializeField] private float verticalFollowSmoothTime = 0.18f;

        [Tooltip("The camera never trails the target vertically by more than this, in metres, so long falls stay in view.")]
        [Min(0f)]
        [SerializeField] private float maxVerticalLag = 1.2f;

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

        // Generous on purpose: hits beyond the buffer are dropped in no particular order.
        private readonly RaycastHit[] collisionHits = new RaycastHit[32];

        private Vector3 pivot;
        private Vector3 framingPivot;
        private float trauma;
        private Vector2 pivotVelocityXZ;
        private float pivotVelocityY;
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

        /// <summary>The heading the camera is turning towards, in degrees. 0 looks along +Z, 90 along +X.</summary>
        public float Yaw => targetYaw;

        /// <summary>The point the camera orbits, in world space.</summary>
        public Vector3 Pivot => pivot;

        /// <summary>
        /// Turns the view by a small angle, as if the player had dragged. Used for mild assistance;
        /// the usual smoothing applies, so it never jumps.
        /// </summary>
        public void AddYaw(float degrees)
        {
            targetYaw += degrees;
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
            pivot = FollowPivot(pivot, desiredPivot, deltaTime);

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

        /// <summary>
        /// Follows the target sideways closely and vertically more softly, within a maximum lag.
        /// </summary>
        private Vector3 FollowPivot(Vector3 current, Vector3 desired, float deltaTime)
        {
            Vector2 flat = followSmoothTime > 0f
                ? Vector2.SmoothDamp(new Vector2(current.x, current.z), new Vector2(desired.x, desired.z), ref pivotVelocityXZ,
                    followSmoothTime, Mathf.Infinity, deltaTime)
                : new Vector2(desired.x, desired.z);

            float height = verticalFollowSmoothTime > 0f
                ? Mathf.SmoothDamp(current.y, desired.y, ref pivotVelocityY, verticalFollowSmoothTime, Mathf.Infinity, deltaTime)
                : desired.y;

            if (maxVerticalLag > 0f)
            {
                height = Mathf.Clamp(height, desired.y - maxVerticalLag, desired.y + maxVerticalLag);
            }

            return new Vector3(flat.x, height, flat.y);
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
            pivotVelocityXZ = Vector2.zero;
            pivotVelocityY = 0f;

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
