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

        [Tooltip("Gravity is multiplied by this while falling, so jumps rise smoothly and come down with weight.")]
        [Min(1f)]
        [SerializeField] private float fallGravityMultiplier = 1.35f;

        [Tooltip("Gravity is multiplied by this near the top of a jump while jump is held, for a moment of control.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float apexGravityMultiplier = 0.6f;

        [Tooltip("Vertical speed, in metres per second, below which the character counts as near the top of a jump.")]
        [Min(0f)]
        [SerializeField] private float apexSpeedThreshold = 1.6f;

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

        [Header("Landing")]
        [Tooltip("Landing faster than this, in metres per second, counts as a hard landing.")]
        [Min(0f)]
        [SerializeField] private float hardLandingSpeed = 13f;

        [Tooltip("After a hard landing, top speed is reduced for this many seconds.")]
        [Min(0f)]
        [SerializeField] private float hardLandingRecovery = 0.25f;

        [Tooltip("Share of top speed available during hard-landing recovery.")]
        [Range(0f, 1f)]
        [SerializeField] private float hardLandingSpeedFactor = 0.35f;

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
        private const float FacingOverrideSmoothTime = 0.05f;
        private const float InputDeadZoneSqr = 0.0001f;
        private const float TimerExpired = 1000f;

        private CharacterController controller;
        private Vector3 horizontalVelocity;
        private float verticalSpeed;
        private float yawVelocity;
        private float facingOverrideYaw;
        private float facingOverrideTime;
        private float timeSinceGrounded = TimerExpired;
        private float timeSinceJumpPressed = TimerExpired;
        private bool isGrounded;
        private bool isSprinting;
        private bool risingFromJump;
        private bool groundStateKnown;
        private bool externalControl;
        private float landingRecoveryRemaining;
        private Vector3 blockedDirection;
        private float speedLimit = 1f;
        private bool sprintSuppressed;
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

        public float HardLandingSpeed => hardLandingSpeed;

        /// <summary>Downward acceleration while rising, in metres per second squared.</summary>
        public float Gravity => gravity;

        /// <summary>
        /// True while another component, such as traversal, moves the character. The motor then
        /// neither moves it nor applies gravity, and its collider is switched off.
        /// </summary>
        public bool IsExternallyControlled => externalControl;

        public CharacterController Controller
        {
            get
            {
                if (controller == null)
                {
                    controller = GetComponent<CharacterController>();
                }

                return controller;
            }
        }

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
            if (deltaTime <= 0f || externalControl)
            {
                blockedDirection = Vector3.zero;
                return;
            }

            if (landingRecoveryRemaining > 0f)
            {
                landingRecoveryRemaining -= deltaTime;
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

        /// <summary>
        /// Turns the character towards a world direction and holds that facing for a short time,
        /// overriding the usual turn towards the movement direction. Used when attacking, so the
        /// character faces its target while it keeps moving.
        /// </summary>
        public void FaceDirection(Vector3 worldDirection, float holdSeconds)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f || holdSeconds <= 0f)
            {
                return;
            }

            facingOverrideYaw = Mathf.Atan2(worldDirection.x, worldDirection.z) * Mathf.Rad2Deg;
            facingOverrideTime = holdSeconds;
        }

        /// <summary>
        /// Converts stick or keyboard input into a world direction on the ground, relative to the
        /// camera. Length 0 to 1.
        /// </summary>
        public Vector3 GetCameraRelativeMoveDirection(Vector2 moveInput)
        {
            return GetCameraRelativeDirection(moveInput);
        }

        /// <summary>
        /// Stops movement in a direction for the current frame only, for example at the edge of a
        /// high drop. Call it before the motor updates.
        /// </summary>
        public void BlockMovementThisFrame(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            blockedDirection = worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.zero;
        }

        /// <summary>
        /// Limits the top speed to a share of normal for the next motor update only, and optionally
        /// rules out sprinting. Combat calls it every frame while a swing, a charge or a block slows
        /// the character; when it stops calling, full speed returns by itself. Several callers may
        /// limit in one frame: the lowest limit applies.
        /// </summary>
        public void LimitSpeedThisFrame(float multiplier, bool allowSprint)
        {
            speedLimit = Mathf.Min(speedLimit, Mathf.Clamp01(multiplier));
            sprintSuppressed |= !allowSprint;
        }

        /// <summary>
        /// Hands the character over to another component, which then positions it with
        /// <see cref="SetExternalPose"/>. The collider is switched off and momentum cleared.
        /// </summary>
        public void BeginExternalControl()
        {
            externalControl = true;
            horizontalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            yawVelocity = 0f;
            risingFromJump = false;
            Controller.enabled = false;
        }

        /// <summary>Places the character during external control.</summary>
        public void SetExternalPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Gives control back to the motor with the given velocity. A character that ends on the
        /// ground can jump at once; one that ends in the air gets no extra coyote-time jump.
        /// </summary>
        public void EndExternalControl(Vector3 velocity, bool grounded)
        {
            Controller.enabled = true;
            externalControl = false;
            horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            verticalSpeed = grounded ? -groundedStickSpeed : velocity.y;
            isGrounded = grounded;
            timeSinceGrounded = grounded ? 0f : TimerExpired;
            timeSinceJumpPressed = TimerExpired;
            risingFromJump = false;
        }

        /// <summary>Starts a jump with a given velocity, for jumps that begin from traversal.</summary>
        public void Launch(Vector3 velocity)
        {
            EndExternalControl(velocity, false);
            Jumped?.Invoke();
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
                    float impact = Mathf.Max(0f, -verticalSpeed);
                    if (impact >= hardLandingSpeed)
                    {
                        landingRecoveryRemaining = hardLandingRecovery;
                    }

                    Landed?.Invoke(impact);
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
            isSprinting = hasMoveInput && sprintHeld && !sprintSuppressed;

            float topSpeed = isSprinting ? sprintSpeed : walkSpeed;
            if (landingRecoveryRemaining > 0f)
            {
                topSpeed *= hardLandingSpeedFactor;
            }

            // Limits last one update; whoever wants one asks again every frame.
            topSpeed *= speedLimit;
            speedLimit = 1f;
            sprintSuppressed = false;

            Vector3 targetVelocity = moveDirection * topSpeed;

            // A blocked direction (the edge of a drop) cannot be moved into this frame.
            if (blockedDirection != Vector3.zero)
            {
                float into = Vector3.Dot(targetVelocity, blockedDirection);
                if (into > 0f)
                {
                    targetVelocity -= blockedDirection * into;
                }

                float current = Vector3.Dot(horizontalVelocity, blockedDirection);
                if (current > 0f)
                {
                    horizontalVelocity -= blockedDirection * current;
                }

                blockedDirection = Vector3.zero;
            }

            float rate = hasMoveInput ? acceleration : deceleration;
            if (!isGrounded)
            {
                rate *= airControl;
            }

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
        }

        private void UpdateRotation(Vector3 moveDirection, bool hasMoveInput, float deltaTime)
        {
            float targetYaw;
            float smoothTime = rotationSmoothTime;

            if (facingOverrideTime > 0f)
            {
                // An ability asked the character to face a direction; movement continues as strafing.
                facingOverrideTime -= deltaTime;
                targetYaw = facingOverrideYaw;
                smoothTime = Mathf.Min(rotationSmoothTime, FacingOverrideSmoothTime);
            }
            else if (hasMoveInput)
            {
                targetYaw = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            }
            else
            {
                yawVelocity = 0f;
                return;
            }

            float yaw = smoothTime > 0f
                ? Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref yawVelocity, smoothTime,
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

            float appliedGravity = gravity;
            if (verticalSpeed < 0f)
            {
                appliedGravity *= fallGravityMultiplier;
            }
            else if (risingFromJump && verticalSpeed < apexSpeedThreshold && readInput && inputSource.JumpHeld)
            {
                appliedGravity *= apexGravityMultiplier;
            }

            // Near the top of a held jump, falling also starts gently.
            if (verticalSpeed < 0f && verticalSpeed > -apexSpeedThreshold && readInput && inputSource.JumpHeld)
            {
                appliedGravity = gravity * apexGravityMultiplier;
            }

            verticalSpeed = Mathf.Max(verticalSpeed - appliedGravity * deltaTime, -terminalFallSpeed);
        }
    }
}
