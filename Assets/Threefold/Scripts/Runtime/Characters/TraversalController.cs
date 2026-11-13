using System;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>A traversal movement, announced for animation and sound.</summary>
    public enum TraversalMove
    {
        Grab,
        ClimbUp,
        Mantle,
        Vault,
        DropToHang,
        LetGo,
        BackJump,
    }

    /// <summary>
    /// Context-sensitive traversal on top of <see cref="ThirdPersonMotor"/>: catching and hanging
    /// from ledges, shimmying along them, climbing up, vaulting low obstacles, climbing onto
    /// waist-high ones, and stopping at high drops so the player can lower themselves to hang.
    ///
    /// Every movement starts only when the player is pushing towards it, and every path is
    /// checked for space before it starts, so the character never passes through geometry. While a
    /// movement plays, the motor is paused and this component places the character along the path.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ThirdPersonMotor))]
    [DefaultExecutionOrder(-50)] // Before the motor, so a movement can take over in the same frame.
    public sealed class TraversalController : MonoBehaviour
    {
        private enum State
        {
            Free,
            Hanging,
            Moving,
        }

        private enum Finish
        {
            Ground,
            Hang,
        }

        [Header("Surfaces")]
        [Tooltip("Layers that can be climbed and vaulted. Ignore Raycast is excluded by default, so invisible walls cannot be climbed.")]
        [SerializeField] private LayerMask climbableLayers = Physics.DefaultRaycastLayers;

        [Tooltip("How far from vertical a surface may lean and still count as a wall, in degrees.")]
        [Range(0f, 45f)]
        [SerializeField] private float maxWallTilt = 25f;

        [Tooltip("Steepest ledge top that can be stood on, in degrees.")]
        [Range(0f, 60f)]
        [SerializeField] private float maxLedgeSlope = 35f;

        [Tooltip("Share of the stick that must point at a wall before anything happens. 0.5 is about 60 degrees off-centre.")]
        [Range(0f, 1f)]
        [SerializeField] private float minInputTowardWall = 0.5f;

        [Header("Ledge grab")]
        [Tooltip("Lowest ledge, above the feet, that is caught and hung from. Lower ledges are climbed onto directly.")]
        [Min(0.3f)]
        [SerializeField] private float grabMinHeight = 1.25f;

        [Tooltip("Highest ledge, above the feet, that can be caught.")]
        [Min(0.5f)]
        [SerializeField] private float grabMaxHeight = 2.25f;

        [Tooltip("Ledges are only caught while rising slower than this, in metres per second, or falling.")]
        [Min(0f)]
        [SerializeField] private float grabMaxRiseSpeed = 3f;

        [Tooltip("How far ahead of the body a wall is looked for, in metres.")]
        [Min(0.05f)]
        [SerializeField] private float reach = 0.45f;

        [Header("Hanging")]
        [Tooltip("Height from the feet to the ledge while hanging, in metres.")]
        [Min(0.5f)]
        [SerializeField] private float hangDepth = 2.0f;

        [Tooltip("Gap between the body and the wall while hanging, in metres.")]
        [Min(0f)]
        [SerializeField] private float hangWallGap = 0.06f;

        [Tooltip("Sideways speed along a ledge, in metres per second.")]
        [Min(0f)]
        [SerializeField] private float shimmySpeed = 1.3f;

        [Tooltip("Seconds that must pass after catching a ledge before pushing towards it climbs. Jump climbs at once.")]
        [Min(0f)]
        [SerializeField] private float climbInputDelay = 0.25f;

        [Tooltip("Height of a jump away from the wall, in metres.")]
        [Min(0f)]
        [SerializeField] private float backJumpHeight = 1.0f;

        [Tooltip("Speed away from the wall when jumping off it, in metres per second.")]
        [Min(0f)]
        [SerializeField] private float backJumpSpeed = 4f;

        [Tooltip("Seconds after letting go during which no ledge is caught, so the same one is not caught again.")]
        [Min(0f)]
        [SerializeField] private float regrabDelay = 0.4f;

        [Header("Vault and mantle")]
        [Tooltip("Highest obstacle that is vaulted, above the feet.")]
        [Min(0.3f)]
        [SerializeField] private float vaultMaxHeight = 1.15f;

        [Tooltip("Thickest obstacle that is vaulted. Deeper ones are climbed onto.")]
        [Min(0.1f)]
        [SerializeField] private float vaultMaxThickness = 1.0f;

        [Tooltip("Slowest speed, in metres per second, at which a thin obstacle is vaulted.")]
        [Min(0f)]
        [SerializeField] private float vaultMinSpeed = 2.5f;

        [Tooltip("Highest obstacle that is climbed onto from the ground, above the feet.")]
        [Min(0.3f)]
        [SerializeField] private float mantleMaxHeight = 1.25f;

        [Header("Edges")]
        [Tooltip("Drops deeper than this, in metres, stop the character at the edge unless it is sprinting or jumping.")]
        [Min(0.5f)]
        [SerializeField] private float edgeGuardDepth = 1.8f;

        [Tooltip("Seconds of pushing into a guarded edge before the character lowers itself to hang from it.")]
        [Min(0f)]
        [SerializeField] private float edgeHoldTime = 0.35f;

        [Header("Timing")]
        [Min(0.05f)]
        [SerializeField] private float grabDuration = 0.14f;

        [Min(0.1f)]
        [SerializeField] private float climbDuration = 0.75f;

        [Min(0.1f)]
        [SerializeField] private float mantleDuration = 0.45f;

        [Min(0.1f)]
        [SerializeField] private float vaultDuration = 0.45f;

        [Min(0.1f)]
        [SerializeField] private float dropToHangDuration = 0.4f;

        private const int MaxPathPoints = 4;
        private const float ProbeRadius = 0.12f;
        private const float StandInset = 0.12f;

        private readonly Collider[] overlaps = new Collider[16];
        private readonly Vector3[] path = new Vector3[MaxPathPoints];

        private ThirdPersonMotor motor;
        private State state = State.Free;

        private int pathCount;
        private float pathDuration;
        private float pathTime;
        private Quaternion pathStartRotation;
        private Quaternion pathEndRotation;
        private Finish pathFinish;
        private Vector3 pathExitVelocity;

        private Vector3 ledgeCorner;
        private Vector3 ledgeNormal;
        private float hangTime;
        private float regrabBlockedUntil;
        private float edgeHeldFor;

        /// <summary>Raised when a traversal movement starts.</summary>
        public event Action<TraversalMove> MoveStarted;

        /// <summary>True while hanging from a ledge, or moving into a hang.</summary>
        public bool IsHanging => state == State.Hanging || (state == State.Moving && pathFinish == Finish.Hang);

        /// <summary>True while a scripted movement such as a climb or a vault plays.</summary>
        public bool IsMoving => state == State.Moving;

        /// <summary>Sideways shimmy speed this frame, from -1 (left) to 1 (right), for animation.</summary>
        public float ShimmyAmount { get; private set; }

        private float Radius => motor.Controller.radius * Mathf.Abs(transform.lossyScale.x);
        private float Height => motor.Controller.height * Mathf.Abs(transform.lossyScale.y);
        private float StepHeight => motor.Controller.stepOffset;

        private void Awake()
        {
            motor = GetComponent<ThirdPersonMotor>();
        }

        private void OnDisable()
        {
            if (state != State.Free && motor != null && motor.IsExternallyControlled)
            {
                motor.EndExternalControl(Vector3.zero, false);
            }

            state = State.Free;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            CharacterInputSource input = motor.InputSource;
            bool inputAllowed = input != null && motor.MovementInputEnabled;
            Vector2 stick = inputAllowed ? input.Move : Vector2.zero;
            Vector3 moveDirection = motor.GetCameraRelativeMoveDirection(stick);
            bool jumpPressed = inputAllowed && input.JumpPressedThisFrame;
            bool sprinting = inputAllowed && input.SprintHeld;

            ShimmyAmount = 0f;

            switch (state)
            {
                case State.Moving:
                    UpdatePath(deltaTime);
                    break;

                case State.Hanging:
                    UpdateHanging(moveDirection, jumpPressed, deltaTime);
                    break;

                default:
                    UpdateFree(moveDirection, jumpPressed, sprinting, deltaTime);
                    break;
            }
        }

        // ------------------------------------------------------------------ free

        private void UpdateFree(Vector3 moveDirection, bool jumpPressed, bool sprinting, float deltaTime)
        {
            if (motor.IsExternallyControlled)
            {
                return;
            }

            bool hasInput = moveDirection.sqrMagnitude > 0.04f;
            Vector3 feet = transform.position;

            if (!motor.IsGrounded)
            {
                edgeHeldFor = 0f;
                if (Time.time >= regrabBlockedUntil && motor.VerticalSpeed <= grabMaxRiseSpeed)
                {
                    TryAirLedge(feet, moveDirection, hasInput);
                }

                return;
            }

            if (!hasInput || jumpPressed)
            {
                edgeHeldFor = 0f;
                return;
            }

            Vector3 direction = moveDirection.normalized;
            if (TryGroundObstacle(feet, direction))
            {
                edgeHeldFor = 0f;
                return;
            }

            if (!sprinting)
            {
                GuardEdge(feet, direction, deltaTime);
            }
            else
            {
                edgeHeldFor = 0f;
            }
        }

        /// <summary>In the air: catch a ledge within reach of the hands, or climb straight onto a lower one.</summary>
        private void TryAirLedge(Vector3 feet, Vector3 moveDirection, bool hasInput)
        {
            Vector3 facing = transform.forward;
            if (!FindLedge(feet, facing, out Vector3 corner, out Vector3 normal))
            {
                return;
            }

            if (TraversalRules.Toward(hasInput ? moveDirection : Vector3.zero, normal) < minInputTowardWall)
            {
                return;
            }

            float height = corner.y - feet.y;
            ObstacleAction action = TraversalRules.ClassifyAirLedge(height, StepHeight, grabMinHeight, grabMaxHeight);

            if (action == ObstacleAction.Hang)
            {
                Vector3 hang = TraversalRules.HangPosition(corner, normal, Radius, hangWallGap, hangDepth);
                if (IsSpaceFree(hang))
                {
                    StartPath(MoveTo(feet, hang), grabDuration, Quaternion.LookRotation(-normal), Finish.Hang,
                        Vector3.zero, corner, normal, TraversalMove.Grab);
                }
            }
            else if (action == ObstacleAction.Mantle)
            {
                TryClimbOnto(feet, corner, normal, mantleDuration, TraversalMove.Mantle);
            }
        }

        /// <summary>On the ground: vault a thin obstacle at speed, or climb onto a waist-high one.</summary>
        private bool TryGroundObstacle(Vector3 feet, Vector3 direction)
        {
            float speed = motor.HorizontalSpeed;
            Vector3 origin = feet + Vector3.up * (StepHeight + ProbeRadius + 0.02f);
            float distance = Radius + reach + speed * 0.08f;

            if (!SphereCast(origin, direction, distance, out RaycastHit wall) || !TraversalRules.IsWall(wall.normal, maxWallTilt))
            {
                return false;
            }

            Vector3 normal = Flat(wall.normal);
            if (TraversalRules.Toward(direction, normal) < minInputTowardWall)
            {
                return false;
            }

            // The face must reach down to the ground. Stairs have a nearer riser at the bottom, so
            // walking up them is never mistaken for an obstacle to climb.
            float faceDistance = wall.distance + ProbeRadius;
            if (!Raycast(feet + Vector3.up * 0.08f, direction, faceDistance + 0.3f, out RaycastHit low)
                || Mathf.Abs(low.distance - faceDistance) > 0.25f)
            {
                return false;
            }

            if (!FindTop(wall.point, normal, feet.y + mantleMaxHeight + 0.4f, mantleMaxHeight + 0.6f, out RaycastHit top))
            {
                return false;
            }

            Vector3 corner = Corner(wall.point, normal, top.point.y);
            float height = corner.y - feet.y;
            float thickness = MeasureThickness(corner, normal);

            ObstacleAction action = TraversalRules.ClassifyObstacle(height, thickness, speed, StepHeight, vaultMaxHeight,
                mantleMaxHeight, vaultMaxThickness, vaultMinSpeed);

            switch (action)
            {
                case ObstacleAction.Vault:
                    return TryVault(feet, corner, normal, thickness, direction, speed);
                case ObstacleAction.Mantle:
                    return TryClimbOnto(feet, corner, normal, mantleDuration, TraversalMove.Mantle);
                default:
                    return false;
            }
        }

        private bool TryVault(Vector3 feet, Vector3 corner, Vector3 normal, float thickness, Vector3 direction, float speed)
        {
            // Land just beyond the far side, on ground near the starting height: never into a drop.
            Vector3 beyond = corner - normal * (thickness + Radius + 0.3f);
            if (!Raycast(new Vector3(beyond.x, corner.y + 0.3f, beyond.z), Vector3.down, corner.y - feet.y + 1.0f, out RaycastHit ground)
                || !TraversalRules.IsWalkable(ground.normal, maxLedgeSlope)
                || Mathf.Abs(ground.point.y - feet.y) > 0.6f)
            {
                return false;
            }

            Vector3 over = corner - normal * (thickness * 0.5f) + Vector3.up * 0.12f;
            Vector3 land = ground.point + Vector3.up * 0.02f;

            Vector3 approach = corner + normal * (Radius + 0.05f);
            approach.y = Mathf.Max(feet.y, corner.y - 0.35f);

            if (!IsSpaceFree(approach) || !IsSpaceFree(over) || !IsSpaceFree(land))
            {
                return false;
            }

            int count = 0;
            path[count++] = feet;
            path[count++] = approach;
            path[count++] = over;
            path[count++] = land;

            Vector3 exit = direction * Mathf.Max(speed, motor.WalkSpeed);
            StartPath(count, vaultDuration, Quaternion.LookRotation(Flat(direction)), Finish.Ground, exit, corner, normal,
                TraversalMove.Vault);
            return true;
        }

        private bool TryClimbOnto(Vector3 feet, Vector3 corner, Vector3 normal, float duration, TraversalMove move)
        {
            Vector3 lift = corner + normal * (Radius + 0.04f) + Vector3.up * 0.05f;
            Vector3 stand = TraversalRules.StandPosition(corner, normal, Radius, StandInset);

            if (!IsSpaceFree(lift) || !IsSpaceFree(stand))
            {
                return false;
            }

            int count = 0;
            path[count++] = feet;
            path[count++] = lift;
            path[count++] = stand;

            // Taller climbs take a little longer.
            float scaled = duration * Mathf.Lerp(0.8f, 1.2f, Mathf.InverseLerp(0.4f, grabMaxHeight, corner.y - feet.y));
            StartPath(count, scaled, Quaternion.LookRotation(-normal), Finish.Ground, Vector3.zero, corner, normal, move);
            return true;
        }

        /// <summary>
        /// Stops the character at the edge of a deep drop. Pushing into the edge for a moment lowers
        /// the character to hang from it; jumping or sprinting goes over as usual.
        /// </summary>
        private void GuardEdge(Vector3 feet, Vector3 direction, float deltaTime)
        {
            Vector3 ahead = feet + direction * (Radius + 0.25f) + Vector3.up * 0.4f;
            if (Raycast(ahead, Vector3.down, 0.4f + edgeGuardDepth, out _))
            {
                edgeHeldFor = 0f;
                return;
            }

            // The wall below the edge, seen from outside: its normal points away from the character.
            Vector3 outside = feet + direction * (Radius + 0.7f) + Vector3.down * 0.45f;
            bool hasFace = Raycast(outside, -direction, 1.0f, out RaycastHit face) && TraversalRules.IsWall(face.normal, maxWallTilt);
            Vector3 outward = hasFace ? Flat(face.normal) : direction;

            motor.BlockMovementThisFrame(outward);
            edgeHeldFor += deltaTime;

            if (!hasFace || edgeHeldFor < edgeHoldTime)
            {
                return;
            }

            if (!FindTop(face.point, outward, feet.y + 0.5f, 1.0f, out RaycastHit top))
            {
                return;
            }

            Vector3 corner = Corner(face.point, outward, top.point.y);
            Vector3 overEdge = corner + outward * (Radius + hangWallGap) + Vector3.up * 0.05f;
            Vector3 hang = TraversalRules.HangPosition(corner, outward, Radius, hangWallGap, hangDepth);

            if (!IsSpaceFree(overEdge) || !IsSpaceFree(hang))
            {
                return;
            }

            edgeHeldFor = 0f;
            int count = 0;
            path[count++] = feet;
            path[count++] = overEdge;
            path[count++] = hang;
            StartPath(count, dropToHangDuration, Quaternion.LookRotation(-outward), Finish.Hang, Vector3.zero, corner, outward,
                TraversalMove.DropToHang);
        }

        // --------------------------------------------------------------- hanging

        private void UpdateHanging(Vector3 moveDirection, bool jumpPressed, float deltaTime)
        {
            hangTime += deltaTime;

            Vector3 facing = -ledgeNormal;
            Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
            float toward = TraversalRules.Toward(moveDirection, ledgeNormal) * moveDirection.magnitude;
            float along = Vector3.Dot(moveDirection, right);

            if (jumpPressed && toward < -0.4f)
            {
                BackJump();
                return;
            }

            if (jumpPressed || (toward > 0.6f && hangTime >= climbInputDelay))
            {
                if (ClimbFromHang())
                {
                    return;
                }
            }

            if (toward < -0.6f && !jumpPressed)
            {
                LetGo();
                return;
            }

            if (Mathf.Abs(along) > 0.3f && shimmySpeed > 0f)
            {
                Shimmy(right * Mathf.Sign(along), Mathf.Abs(along), deltaTime);
            }
        }

        private void Shimmy(Vector3 side, float amount, float deltaTime)
        {
            Vector3 candidate = ledgeCorner + side * (shimmySpeed * amount * deltaTime);

            // The ledge must continue: wall below the new hand position, top level with the current one.
            Vector3 front = candidate + ledgeNormal * 0.4f + Vector3.down * 0.25f;
            if (!Raycast(front, -ledgeNormal, 0.7f, out RaycastHit wall) || !TraversalRules.IsWall(wall.normal, maxWallTilt))
            {
                return;
            }

            Vector3 normal = Flat(wall.normal);
            if (!FindTop(wall.point, normal, ledgeCorner.y + 0.4f, 0.8f, out RaycastHit top)
                || Mathf.Abs(top.point.y - ledgeCorner.y) > 0.25f)
            {
                return;
            }

            Vector3 corner = Corner(wall.point, normal, top.point.y);
            Vector3 hang = TraversalRules.HangPosition(corner, normal, Radius, hangWallGap, hangDepth);
            if (!IsSpaceFree(hang))
            {
                return;
            }

            ledgeCorner = corner;
            ledgeNormal = normal;
            ShimmyAmount = Vector3.Dot(side, Vector3.Cross(Vector3.up, -normal).normalized) * amount;
            motor.SetExternalPose(hang, Quaternion.LookRotation(-normal));
        }

        private bool ClimbFromHang()
        {
            Vector3 feet = transform.position;
            Vector3 lift = ledgeCorner + ledgeNormal * (Radius + hangWallGap * 0.5f) + Vector3.up * 0.05f;
            Vector3 stand = TraversalRules.StandPosition(ledgeCorner, ledgeNormal, Radius, StandInset);

            if (!IsSpaceFree(lift) || !IsSpaceFree(stand))
            {
                return false;
            }

            int count = 0;
            path[count++] = feet;
            path[count++] = lift;
            path[count++] = stand;
            StartPath(count, climbDuration, transform.rotation, Finish.Ground, Vector3.zero, ledgeCorner, ledgeNormal,
                TraversalMove.ClimbUp);
            return true;
        }

        private void LetGo()
        {
            state = State.Free;
            regrabBlockedUntil = Time.time + regrabDelay;
            motor.EndExternalControl(ledgeNormal * 0.8f, false);
            MoveStarted?.Invoke(TraversalMove.LetGo);
        }

        private void BackJump()
        {
            state = State.Free;
            regrabBlockedUntil = Time.time + regrabDelay;
            float upSpeed = Mathf.Sqrt(2f * motor.Gravity * backJumpHeight);
            Vector3 velocity = ledgeNormal * backJumpSpeed + Vector3.up * upSpeed;

            transform.rotation = Quaternion.LookRotation(ledgeNormal);
            motor.Launch(velocity);
            MoveStarted?.Invoke(TraversalMove.BackJump);
        }

        // ------------------------------------------------------------------ paths

        private int MoveTo(Vector3 from, Vector3 to)
        {
            path[0] = from;
            path[1] = to;
            return 2;
        }

        private void StartPath(int count, float duration, Quaternion endRotation, Finish finish, Vector3 exitVelocity,
            Vector3 corner, Vector3 normal, TraversalMove move)
        {
            if (!motor.IsExternallyControlled)
            {
                motor.BeginExternalControl();
            }

            pathCount = count;
            pathDuration = Mathf.Max(0.05f, duration);
            pathTime = 0f;
            pathStartRotation = transform.rotation;
            pathEndRotation = endRotation;
            pathFinish = finish;
            pathExitVelocity = exitVelocity;
            ledgeCorner = corner;
            ledgeNormal = normal;
            edgeHeldFor = 0f;
            state = State.Moving;

            MoveStarted?.Invoke(move);
        }

        private void UpdatePath(float deltaTime)
        {
            pathTime += deltaTime;
            float t = Mathf.Clamp01(pathTime / pathDuration);
            float eased = TraversalRules.Ease(t);

            Vector3 position = TraversalRules.AlongPath(path, pathCount, eased);
            Quaternion rotation = Quaternion.Slerp(pathStartRotation, pathEndRotation, TraversalRules.Ease(t * 2.5f));
            motor.SetExternalPose(position, rotation);

            if (t < 1f)
            {
                return;
            }

            if (pathFinish == Finish.Hang)
            {
                state = State.Hanging;
                hangTime = 0f;
            }
            else
            {
                state = State.Free;
                motor.EndExternalControl(pathExitVelocity, true);
            }
        }

        // ---------------------------------------------------------------- queries

        /// <summary>A wall ahead with a standable top: returns the corner where wall and top meet.</summary>
        private bool FindLedge(Vector3 feet, Vector3 direction, out Vector3 corner, out Vector3 normal)
        {
            corner = Vector3.zero;
            normal = Vector3.zero;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            direction.Normalize();

            // Two heights, so both low and high ledges are found while the feet rise and fall.
            for (int i = 0; i < 2; i++)
            {
                float probeHeight = i == 0 ? StepHeight + 0.15f : 1.0f;
                Vector3 origin = feet + Vector3.up * probeHeight;
                if (!SphereCast(origin, direction, Radius + reach, out RaycastHit wall) || !TraversalRules.IsWall(wall.normal, maxWallTilt))
                {
                    continue;
                }

                normal = Flat(wall.normal);
                if (!FindTop(wall.point, normal, feet.y + grabMaxHeight + 0.3f, grabMaxHeight + 0.3f - probeHeight + 0.05f,
                        out RaycastHit top))
                {
                    continue;
                }

                corner = Corner(wall.point, normal, top.point.y);
                return true;
            }

            return false;
        }

        /// <summary>Looks down onto the top of a wall, just behind its face.</summary>
        private bool FindTop(Vector3 wallPoint, Vector3 normal, float fromHeight, float distance, out RaycastHit top)
        {
            Vector3 origin = wallPoint - normal * 0.12f;
            origin.y = fromHeight;

            // Something directly above the ledge (an overhang or ceiling) means there is nothing to climb onto.
            if (Physics.CheckSphere(origin, 0.05f, climbableLayers, QueryTriggerInteraction.Ignore))
            {
                top = default;
                return false;
            }

            return Raycast(origin, Vector3.down, distance, out top) && TraversalRules.IsWalkable(top.normal, maxLedgeSlope);
        }

        /// <summary>How deep an obstacle is behind its front face, or -1 if it continues beyond vaulting depth.</summary>
        private float MeasureThickness(Vector3 corner, Vector3 normal)
        {
            const float step = 0.15f;
            for (float depth = 0.12f; depth <= vaultMaxThickness + 0.15f; depth += step)
            {
                Vector3 origin = corner - normal * depth + Vector3.up * 0.3f;
                bool solid = Raycast(origin, Vector3.down, 0.5f, out RaycastHit hit) && hit.point.y > corner.y - 0.2f;
                if (!solid)
                {
                    return depth;
                }
            }

            return -1f;
        }

        /// <summary>The point on the wall's face at the height of its top.</summary>
        private static Vector3 Corner(Vector3 wallPoint, Vector3 normal, float topHeight)
        {
            return new Vector3(wallPoint.x, topHeight, wallPoint.z);
        }

        /// <summary>True if the character's standing capsule fits with its feet at the given point.</summary>
        private bool IsSpaceFree(Vector3 feet)
        {
            float radius = Radius * 0.95f;
            Vector3 bottom = feet + Vector3.up * (radius + 0.04f);
            Vector3 top = feet + Vector3.up * Mathf.Max(Height - radius, radius + 0.05f);

            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, climbableLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (overlaps[i] != null && !overlaps[i].transform.IsChildOf(transform))
                {
                    return false;
                }
            }

            return true;
        }

        private bool SphereCast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            return Physics.SphereCast(origin, ProbeRadius, direction, out hit, distance, climbableLayers,
                       QueryTriggerInteraction.Ignore)
                   && !hit.transform.IsChildOf(transform);
        }

        private bool Raycast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            return Physics.Raycast(origin, direction, out hit, distance, climbableLayers, QueryTriggerInteraction.Ignore)
                   && !hit.transform.IsChildOf(transform);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector.sqrMagnitude > 1e-6f ? vector.normalized : Vector3.forward;
        }
    }
}
