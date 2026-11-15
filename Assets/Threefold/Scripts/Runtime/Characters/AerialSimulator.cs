using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>The motor's numbers that shape a jump, as set on <see cref="ThirdPersonMotor"/>.</summary>
    public struct JumpFigures
    {
        public float Gravity;
        public float FallMultiplier;
        public float ApexMultiplier;
        public float ApexSpeedThreshold;
        public float TerminalFallSpeed;
        public float JumpHeight;
        public float JumpCutMultiplier;
        public float RunSpeed;
        public float Acceleration;
        public float AirControl;

        /// <summary>The motor's defaults (sprinting): gravity 28, falling ×1.35, jump 1.5 m, sprint 7 m/s.</summary>
        public static JumpFigures Defaults => new JumpFigures
        {
            Gravity = 28f,
            FallMultiplier = 1.35f,
            ApexMultiplier = 0.6f,
            ApexSpeedThreshold = 1.6f,
            TerminalFallSpeed = 45f,
            JumpHeight = 1.5f,
            JumpCutMultiplier = 0.5f,
            RunSpeed = 7f,
            Acceleration = 28f,
            AirControl = 0.45f,
        };
    }

    /// <summary>One run-up jump to simulate: when Jump is pressed and held, and when the aerial ability is used.</summary>
    public struct AerialRunScript
    {
        public AerialAbilityKind Ability;

        /// <summary>Seconds Jump stays held from the first jump.</summary>
        public float FirstHoldSeconds;

        /// <summary>Use the ability at the top of the first jump, instead of at <see cref="AbilitySeconds"/>.</summary>
        public bool AbilityAtApex;

        /// <summary>Seconds after the first jump at which Jump is pressed again; negative for never.</summary>
        public float AbilitySeconds;

        /// <summary>Seconds the second press is held.</summary>
        public float AbilityHoldSeconds;

        /// <summary>Height of the ground the run starts from.</summary>
        public float StartHeight;

        /// <summary>The run ends when the feet come down to this height.</summary>
        public float LandingHeight;

        /// <summary>A wall across the path at this distance from the take-off, or 0 for none.</summary>
        public float WallDistance;

        public float FramesPerSecond;
    }

    /// <summary>What a simulated jump did.</summary>
    public struct AerialRunResult
    {
        /// <summary>Highest point of the feet above the take-off ground.</summary>
        public float PeakHeight;

        /// <summary>Horizontal distance from the take-off to where the feet came down to the landing height.</summary>
        public float Distance;

        public float AirSeconds;
        public int Activations;
        public bool StoppedByWall;
        public float FastestHorizontalSpeed;
    }

    /// <summary>
    /// Runs a jump along a straight line with the motor's own gravity step (<see cref="JumpPhysics"/>) and the real
    /// <see cref="AerialAbilityModel"/>, frame by frame, in the order the game runs them: the aerial rules first,
    /// then the motor. It stands in for the character controller with a flat ground and an optional wall, so the
    /// abilities and the aerial course can be checked outside Unity. Plain C#.
    /// </summary>
    public static class AerialSimulator
    {
        private const float MaxSeconds = 12f;

        public static AerialRunResult Run(in JumpFigures motor, AerialAbilityModel model, in AerialRunScript script,
            List<AerialEvent> events = null)
        {
            events = events ?? new List<AerialEvent>();
            float dt = 1f / Mathf.Max(5f, script.FramesPerSecond);
            var result = new AerialRunResult();
            model.Reset();

            float x = 0f;
            float y = script.StartHeight;
            float horizontal = motor.RunSpeed;
            float vertical = 0f;
            bool grounded = true;
            bool rising = false;
            bool jumpedLastFrame = false;
            bool hitWall = false;
            bool airborneOnce = false;
            float sinceJump = -1f;
            bool secondPressed = false;
            float secondPressAt = -1f;
            bool apexSeen = false;
            var motion = new AerialMotion();

            for (float t = 0f; t < MaxSeconds; t += dt)
            {
                // ---- input
                bool firstPress = t == 0f;
                if (sinceJump >= 0f)
                {
                    sinceJump += dt;
                }

                bool wantSecond = script.Ability != AerialAbilityKind.None && !secondPressed && airborneOnce &&
                                  (script.AbilityAtApex ? vertical <= 0f && !apexSeen : script.AbilitySeconds >= 0f &&
                                      sinceJump >= script.AbilitySeconds);
                if (vertical <= 0f && airborneOnce)
                {
                    apexSeen = true;
                }

                bool secondPress = false;
                if (wantSecond)
                {
                    secondPressed = true;
                    secondPress = true;
                    secondPressAt = t;
                }

                bool held = (sinceJump >= 0f && sinceJump <= script.FirstHoldSeconds) ||
                            (secondPressed && t - secondPressAt <= script.AbilityHoldSeconds) || firstPress;

                // ---- aerial rules (before the motor, as in the game)
                var input = new AerialFrameInput
                {
                    DeltaTime = dt,
                    Grounded = grounded,
                    VerticalSpeed = vertical,
                    Gravity = motor.Gravity,
                    JumpedFromGround = jumpedLastFrame,
                    JumpPressed = firstPress || secondPress,
                    JumpHeld = held,
                    Selected = script.Ability,
                    Move = new Vector3(1f, 0f, 0f),
                    Facing = new Vector3(1f, 0f, 0f),
                    HitWall = hitWall,
                    WallNormal = new Vector3(-1f, 0f, 0f),
                };

                int before = events.Count;
                model.Tick(input, ref motion, events);
                for (int i = before; i < events.Count; i++)
                {
                    if (events[i].Kind == AerialEventKind.Activated)
                    {
                        result.Activations++;
                    }

                    if (events[i].Reason == AerialEndReason.Wall)
                    {
                        result.StoppedByWall = true;
                    }
                }

                // ---- motor
                jumpedLastFrame = false;
                bool pressReachesMotor = (firstPress || secondPress) && !motion.ConsumeJumpPress;

                // Horizontal: the stick is held forward at full sprint.
                float top = motor.RunSpeed;
                float rate = motor.Acceleration * (grounded ? 1f : motor.AirControl);
                if (motion.ScalesHorizontalVelocity)
                {
                    horizontal *= motion.HorizontalScale;
                }

                if (motion.HasStoredHorizontal)
                {
                    horizontal = motion.StoredHorizontal.x;
                }
                else
                {
                    if (motion.OverridesAirMovement && !grounded)
                    {
                        top = motion.AirTopSpeed;
                        rate = motion.AirAcceleration;
                    }

                    horizontal = Mathf.MoveTowards(horizontal, top, rate * dt);
                }

                // Vertical.
                if (pressReachesMotor && grounded)
                {
                    vertical = JumpPhysics.LaunchSpeed(motor.Gravity, motor.JumpHeight);
                    rising = true;
                    grounded = false;
                    jumpedLastFrame = true;
                    sinceJump = 0f;
                }
                else if (!grounded)
                {
                    if (motion.SetsVerticalSpeed)
                    {
                        vertical = Mathf.Max(motion.VerticalSpeed, -motor.TerminalFallSpeed);
                        rising = false;
                    }
                    else
                    {
                        if (rising && vertical > 0f && !held)
                        {
                            vertical *= motor.JumpCutMultiplier;
                            rising = false;
                        }

                        vertical = JumpPhysics.AirborneStep(vertical, rising, held, motor.Gravity, motor.FallMultiplier,
                            motor.ApexMultiplier, motor.ApexSpeedThreshold, motor.TerminalFallSpeed, dt);
                    }
                }

                float moveX = motion.HasDashVelocity ? motion.DashVelocity.x : horizontal;
                result.FastestHorizontalSpeed = Mathf.Max(result.FastestHorizontalSpeed, Mathf.Abs(moveX));
                float nextX = x + moveX * dt;
                hitWall = false;
                if (script.WallDistance > 0f && nextX > script.WallDistance)
                {
                    nextX = script.WallDistance;
                    hitWall = true;
                }

                x = nextX;
                y += vertical * dt;

                if (!grounded)
                {
                    airborneOnce = true;
                    result.AirSeconds += dt;
                    result.PeakHeight = Mathf.Max(result.PeakHeight, y - script.StartHeight);
                }

                if (airborneOnce && vertical <= 0f && y <= script.LandingHeight)
                {
                    result.Distance = x;
                    return result;
                }
            }

            result.Distance = x;
            return result;
        }
    }
}
