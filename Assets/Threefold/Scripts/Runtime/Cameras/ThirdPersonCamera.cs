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

        private const float MinimumClipDistance = 0.3f;

        private readonly RaycastHit[] collisionHits = new RaycastHit[8];

        private Vector3 pivot;
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
            float allowedDistance = GetUnobstructedDistance(rotation);

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

        private void ApplyTransform(Quaternion rotation)
        {
            Vector3 position = pivot - rotation * Vector3.forward * currentDistance;
            transform.SetPositionAndRotation(position, rotation);
        }

        private float GetUnobstructedDistance(Quaternion rotation)
        {
            float preferred = Mathf.Clamp(distance, minDistance, maxDistance);
            if (!avoidClipping || preferred <= 0f)
            {
                return preferred;
            }

            Vector3 direction = rotation * Vector3.back;
            int hitCount = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, collisionHits, preferred,
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
