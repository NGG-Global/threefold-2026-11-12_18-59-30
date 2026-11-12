using System;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>
    /// Third-person locomotion on a CharacterController: camera-relative movement with
    /// acceleration, turning towards the direction of travel, sprinting, jumping and gravity.
    /// It knows nothing about devices; all input arrives through a <see cref="CharacterInputSource"/>.
    /// State needed by animation and abilities is exposed through read-only properties and events.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonMotor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Where movement, sprint and jump input comes from.")]
        [SerializeField] private CharacterInputSource inputSource;

        [Tooltip("Movement is relative to this transform's facing. Normally the gameplay camera.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Speed")]
        [Tooltip("Top speed without sprinting, in metres per second.")]
        [Min(0f)]
        [SerializeField] private float walkSpeed = 4f;

        [Tooltip("Top speed while sprinting, in metres per second.")]
        [Min(0f)]
        [SerializeField] private float sprintSpeed = 7f;

        [Tooltip("How quickly the character gains speed, in metres per second squared.")]
        [Min(0f)]
        [SerializeField] private float acceleration = 28f;

        [Tooltip("How quickly the character stops when input is released, in metres per second squared.")]
        [Min(0f)]
        [SerializeField] private float deceleration = 34f;

        [Tooltip("Share of acceleration and deceleration available while airborne.")]
        [Range(0f, 1f)]
        [SerializeField] private float airControl = 0.45f;

        [Header("Rotation")]
        [Tooltip("Approximate time to turn towards the movement direction, in seconds. Lower is snappier.")]
        [Min(0f)]
        [SerializeField] private float rotationSmoothTime = 0.09f;

        [Header("Jump and gravity")]
        [Tooltip("Height of a full jump, in metres.")]
        [Min(0f)]
        [SerializeField] private float jumpHeight = 1.5f;

        [Tooltip("Downward acceleration, in metres per second squared.")]
        [Min(0.01f)]
        [SerializeField] private float gravity = 28f;

        [Tooltip("Maximum falling speed, in metres per second.")]
        [Min(0f)]
        [SerializeField] private float terminalFallSpeed = 45f;

        [Tooltip("Grace period after leaving the ground during which a jump is still allowed, in seconds.")]
        [Min(0f)]
        [SerializeField] private float coyoteTime = 0.12f;

        [Tooltip("A jump pressed this long before landing is still performed on landing, in seconds.")]
        [Min(0f)]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Tooltip("Upward speed kept when jump is released early. 1 disables variable jump height.")]
        [Range(0f, 1f)]
        [SerializeField] private float jumpCutMultiplier = 0.5f;

        [Header("Ground detection")]
        [Tooltip("Layers that count as ground.")]
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;

        [Tooltip("How far below the feet to look for ground, in metres.")]
        [Min(0f)]
        [SerializeField] private float groundProbeDistance = 0.12f;

        [Tooltip("Downward speed applied while grounded so the character stays in contact on slopes and steps.")]
        [Min(0f)]
        [SerializeField] private float groundedStickSpeed = 3f;

        private const float ProbeLift = 0.1f;
        private const float InputDeadZoneSqr = 0.0001f;
        private const float TimerExpired = 1000f;

        private CharacterController controller;
        private Vector3 horizontalVelocity;
        private float verticalSpeed;
        private float yawVelocity;
        private float timeSinceGrounded = TimerExpired;
        private float timeSinceJumpPressed = TimerExpired;
        private bool isGrounded;
        private bool isSprinting;
        private bool risingFromJump;
        private bool groundStateKnown;
        private bool missingCameraWarned;
        private bool missingInputWarned;

        /// <summary>Raised on the frame a jump starts.</summary>
        public event Action Jumped;

        /// <summary>Raised on landing. The argument is the downward speed at impact, in metres per second.</summary>
        public event Action<float> Landed;

        /// <summary>True while the character is standing on walkable ground.</summary>
        public bool IsGrounded => isGrounded;

        /// <summary>True while the character is moving with sprint held.</summary>
        public bool IsSprinting => isSprinting;

        /// <summary>Actual velocity after collisions, in metres per second.</summary>
        public Vector3 Velocity => controller != null ? controller.velocity : Vector3.zero;

        /// <summary>Actual speed along the ground plane, in metres per second.</summary>
        public float HorizontalSpeed
        {
            get
            {
                Vector3 velocity = Velocity;
                velocity.y = 0f;
                return velocity.magnitude;
            }
        }

        /// <summary>Vertical speed, in metres per second. Negative while falling.</summary>
        public float VerticalSpeed => verticalSpeed;

        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;

        /// <summary>
        /// When false the character ignores movement and jump input but still decelerates and falls.
        /// Intended for abilities that root the character, such as a channelled spell.
        /// </summary>
        public bool MovementInputEnabled { get; set; } = true;

        public CharacterInputSource InputSource
        {
            get => inputSource;
            set => inputSource = value;
        }

        public Transform CameraTransform
        {
            get => cameraTransform;
            set => cameraTransform = value;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            if (inputSource == null)
            {
                inputSource = GetComponent<CharacterInputSource>();
            }

            if (cameraTransform == null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    cameraTransform = mainCamera.transform;
                }
            }
        }

        private void OnValidate()
        {
            if (sprintSpeed < walkSpeed)
            {
                sprintSpeed = walkSpeed;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            UpdateGroundedState(deltaTime);

            bool readInput = MovementInputEnabled && HasInputSource();
            Vector3 moveDirection = readInput ? GetCameraRelativeDirection(inputSource.Move) : Vector3.zero;
            bool hasMoveInput = moveDirection.sqrMagnitude > InputDeadZoneSqr;

            UpdateHorizontalVelocity(moveDirection, hasMoveInput, readInput && inputSource.SprintHeld, deltaTime);
            UpdateRotation(moveDirection, hasMoveInput, deltaTime);
            UpdateVerticalSpeed(readInput, deltaTime);

            Vector3 displacement = (horizontalVelocity + Vector3.up * verticalSpeed) * deltaTime;
            CollisionFlags flags = controller.Move(displacement);

            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            {
                verticalSpeed = 0f;
            }
        }

        /// <summary>Moves the character instantly and clears its momentum.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterController>();
            }

            // A CharacterController overrides direct transform changes unless it is disabled first.
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = wasEnabled;

            horizontalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            yawVelocity = 0f;
        }

        private bool HasInputSource()
        {
            if (inputSource != null)
            {
                return true;
            }

            if (!missingInputWarned)
            {
                missingInputWarned = true;
                Debug.LogWarning(
                    $"{nameof(ThirdPersonMotor)} on '{name}' has no input source assigned, so the character " +
                    "cannot be controlled.", this);
            }

            return false;
        }

        private void UpdateGroundedState(float deltaTime)
        {
            bool wasGrounded = isGrounded;
            isGrounded = verticalSpeed <= 0.01f && (controller.isGrounded || ProbeGround());

            if (isGrounded)
            {
                timeSinceGrounded = 0f;
                risingFromJump = false;

                // The first evaluation only establishes the starting state; it is not a landing.
                if (!wasGrounded && groundStateKnown)
                {
                    Landed?.Invoke(Mathf.Max(0f, -verticalSpeed));
                }
            }
            else
            {
                timeSinceGrounded += deltaTime;
            }

            groundStateKnown = true;
        }

        /// <summary>
        /// Sweeps a sphere down from inside the capsule. This complements CharacterController.isGrounded,
        /// which only reports contact made during the previous Move call and flickers on slopes and steps.
        /// The sweep starts inside the character's own collider, which Unity's sweep queries do not report.
        /// </summary>
        private bool ProbeGround()
        {
            Vector3 scale = transform.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = Mathf.Max(controller.height * Mathf.Abs(scale.y) * 0.5f, radius);

            Vector3 centre = transform.TransformPoint(controller.center);
            Vector3 origin = centre + Vector3.down * (halfHeight - radius) + Vector3.up * ProbeLift;
            float distance = ProbeLift + controller.skinWidth + groundProbeDistance;

            if (!Physics.SphereCast(origin, radius * 0.95f, Vector3.down, out RaycastHit hit, distance,
                    groundLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit + 0.5f;
        }

        private Vector3 GetCameraRelativeDirection(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude <= InputDeadZoneSqr)
            {
                return Vector3.zero;
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);

                // Looking straight up or down leaves no usable forward; the camera's up vector still points ahead.
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                }

                forward.Normalize();
                right = Vector3.Cross(Vector3.up, forward);
            }
            else if (!missingCameraWarned)
            {
                missingCameraWarned = true;
                Debug.LogWarning(
                    $"{nameof(ThirdPersonMotor)} on '{name}' has no camera transform assigned. Movement uses world " +
                    "axes until one is set.", this);
            }

            return Vector3.ClampMagnitude(forward * moveInput.y + right * moveInput.x, 1f);
        }

        private void UpdateHorizontalVelocity(Vector3 moveDirection, bool hasMoveInput, bool sprintHeld, float deltaTime)
        {
            isSprinting = hasMoveInput && sprintHeld;

            float topSpeed = isSprinting ? sprintSpeed : walkSpeed;
            Vector3 targetVelocity = moveDirection * topSpeed;

            float rate = hasMoveInput ? acceleration : deceleration;
            if (!isGrounded)
            {
                rate *= airControl;
            }

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
        }

        private void UpdateRotation(Vector3 moveDirection, bool hasMoveInput, float deltaTime)
        {
            if (!hasMoveInput)
            {
                yawVelocity = 0f;
                return;
            }

            float targetYaw = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            float yaw = rotationSmoothTime > 0f
                ? Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref yawVelocity, rotationSmoothTime,
                    Mathf.Infinity, deltaTime)
                : targetYaw;

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void UpdateVerticalSpeed(bool readInput, float deltaTime)
        {
            if (readInput && inputSource.JumpPressedThisFrame)
            {
                timeSinceJumpPressed = 0f;
            }
            else
            {
                timeSinceJumpPressed += deltaTime;
            }

            bool jumpRequested = timeSinceJumpPressed <= jumpBufferTime;
            bool canJump = timeSinceGrounded <= coyoteTime;

            if (readInput && jumpRequested && canJump && jumpHeight > 0f)
            {
                verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
                timeSinceJumpPressed = TimerExpired;
                timeSinceGrounded = TimerExpired;
                isGrounded = false;
                risingFromJump = true;
                Jumped?.Invoke();
                return;
            }

            if (isGrounded)
            {
                // Keep a small downward push so the controller stays in contact on slopes and steps.
                verticalSpeed = -groundedStickSpeed;
                return;
            }

            // Releasing jump early shortens the jump once, which gives fine control over height.
            if (risingFromJump && verticalSpeed > 0f && !(readInput && inputSource.JumpHeld))
            {
                verticalSpeed *= jumpCutMultiplier;
                risingFromJump = false;
            }

            verticalSpeed = Mathf.Max(verticalSpeed - gravity * deltaTime, -terminalFallSpeed);
        }
    }
}
