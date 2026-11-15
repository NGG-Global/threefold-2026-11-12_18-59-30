using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>What an aerial ability does to the movement for one motor update. Empty by default: no change.</summary>
    public struct AerialMotion
    {
        /// <summary>The jump press of this frame was used by the ability: no jump, no jump on landing.</summary>
        public bool ConsumeJumpPress;

        public bool SetsVerticalSpeed;
        public float VerticalSpeed;

        /// <summary>Horizontal velocity stored for the next frames (set outright: nothing adds to it).</summary>
        public bool HasStoredHorizontal;
        public Vector3 StoredHorizontal;

        /// <summary>A dash's own velocity for this frame's move only.</summary>
        public bool HasDashVelocity;
        public Vector3 DashVelocity;

        /// <summary>Different top speed and steering in the air for this frame.</summary>
        public bool OverridesAirMovement;
        public float AirTopSpeed;
        public float AirAcceleration;

        /// <summary>The horizontal velocity is multiplied by <see cref="HorizontalScale"/> once.</summary>
        public bool ScalesHorizontalVelocity;
        public float HorizontalScale;
    }

    /// <summary>Everything the aerial rules read in one frame.</summary>
    public struct AerialFrameInput
    {
        public float DeltaTime;

        /// <summary>Standing on walkable ground this frame, as the motor sees it. Walls never count.</summary>
        public bool Grounded;

        public float VerticalSpeed;

        /// <summary>Downward acceleration while rising (the motor's gravity).</summary>
        public float Gravity;

        /// <summary>A jump from the ground happened since the last frame.</summary>
        public bool JumpedFromGround;

        /// <summary>A jump started from traversal (off a ledge) happened since the last frame.</summary>
        public bool LaunchedFromLedge;

        public bool JumpPressed;
        public bool JumpHeld;

        /// <summary>
        /// Falling, the ground will be reached within the motor's jump-buffer time: a press now is left to the motor,
        /// which jumps on landing as it always has.
        /// </summary>
        public bool LandingImminent;

        /// <summary>The ability of the selected discipline.</summary>
        public AerialAbilityKind Selected;

        /// <summary>Where the player steers, flat, length 0 to 1.</summary>
        public Vector3 Move;

        /// <summary>Where the Mage faces, flat.</summary>
        public Vector3 Facing;

        public bool Charging;
        public bool Blocking;
        public bool Staggered;
        public bool Defeated;

        /// <summary>Hanging, climbing or vaulting.</summary>
        public bool Traversing;

        /// <summary>The last move ran into a wall, with that wall's normal.</summary>
        public bool HitWall;
        public Vector3 WallNormal;
    }

    public enum AerialEventKind
    {
        /// <summary>An ability began. It uses the airborne sequence's one activation.</summary>
        Activated,

        /// <summary>The glide propels again after a release, with the fuel left. Not a new activation.</summary>
        Resumed,

        /// <summary>The glide stopped propelling with fuel left: Jump released, or a charge, block or hit.</summary>
        Paused,

        /// <summary>The ability is over.</summary>
        Ended,
    }

    public enum AerialEndReason
    {
        None,
        Finished,
        Released,
        FuelSpent,
        Landed,
        Interrupted,

        /// <summary>A dash stopped at a wall.</summary>
        Wall,
    }

    public readonly struct AerialEvent
    {
        public readonly AerialEventKind Kind;
        public readonly AerialAbilityKind Ability;
        public readonly AerialEndReason Reason;

        /// <summary>Activated: the ability's direction (up, or the dash). Ended at a wall: the wall's normal.</summary>
        public readonly Vector3 Direction;

        public AerialEvent(AerialEventKind kind, AerialAbilityKind ability, AerialEndReason reason, Vector3 direction)
        {
            Kind = kind;
            Ability = ability;
            Reason = reason;
            Direction = direction;
        }
    }

    public enum AerialState
    {
        /// <summary>No aerial ability in progress (on the ground or in the air).</summary>
        Idle = 0,
        Ascending = 1,
        Gliding = 2,
        Dashing = 3,
    }

    /// <summary>
    /// The rules of the discipline aerial abilities, as an explicit state machine. It reads one frame of input and
    /// answers with a change to the movement and with events; it never touches a Unity object, so it is tested
    /// outside Unity. <c>AerialAbilities</c> feeds it and hands the movement to <see cref="ThirdPersonMotor"/>.
    ///
    /// Rules:
    /// - The first jump is the motor's own and is unchanged. An ability becomes usable only after the character
    ///   left the ground by a jump (falling off an edge does not count), and only with a new press of Jump in the
    ///   air: holding the press of the first jump starts nothing. A press on the frame of a jump (from the ground
    ///   or off a ledge) is that jump's, never the ability's, and a press just before landing is left to the motor's
    ///   jump buffer.
    /// - One activation per airborne sequence, whichever discipline is selected: switching discipline in the air
    ///   never gives another. An ability in progress finishes as the one it began as.
    /// - A confirmed landing (ground for some frames in a row) gives it back. A one-frame touch of ground, a wall,
    ///   or a ledge does not. A new jump from the ground starts a new sequence.
    /// - The glide propels only while Jump is held. A release keeps the fuel; a new press in the same sequence
    ///   resumes it while Flame is selected and fuel is left. Propulsion never adds height.
    /// - No ability starts while charging, blocking, staggered, defeated or hanging, climbing or vaulting. A charge,
    ///   block or hit pauses a glide; a dash or an ascension already under way finishes.
    /// - While an ability uses the hands (the ascension's first moments, propulsion, a dash) the hands are busy:
    ///   the combat rules abandon a swing that has not landed and start no attack.
    /// Allocation-free once its event list has grown.
    /// </summary>
    public sealed class AerialAbilityModel
    {
        private readonly AerialRulesTuning rules;
        private readonly AscensionTuning ascension;
        private readonly GlideTuning glide;
        private readonly DashTuning dash;

        private AerialState state;
        private AerialAbilityKind active;
        private bool armed;
        private int usesLeft = 1;
        private float fuel;
        private bool glideStarted;
        private int groundedFrames;
        private float groundedSeconds;
        private float sinceStart;
        private float gatherLeft;
        private bool impulseGiven;
        private float dashLeft;
        private float dashSpeed;
        private Vector3 dashDirection;
        private bool exitPending;
        private Vector3 exitVelocity;

        public AerialAbilityModel(AerialRulesTuning rules, AscensionTuning ascension, GlideTuning glide, DashTuning dash)
        {
            this.rules = rules ?? AerialAbilityDefaults.Rules();
            this.ascension = ascension ?? AerialAbilityDefaults.Ascension();
            this.glide = glide ?? AerialAbilityDefaults.Glide();
            this.dash = dash ?? AerialAbilityDefaults.Dash();
            fuel = this.glide.fuelSeconds;
        }

        public AerialState State => state;

        /// <summary>The ability in progress, or None.</summary>
        public AerialAbilityKind Active => active;

        /// <summary>True once the character has left the ground by a jump in this airborne sequence.</summary>
        public bool IsArmed => armed;

        /// <summary>Activations left in this airborne sequence: 1 or 0.</summary>
        public int UsesLeft => usesLeft;

        /// <summary>Seconds of glide propulsion left in this airborne sequence.</summary>
        public float FuelSeconds => fuel;

        public float FuelFraction => glide.fuelSeconds > 0f ? Mathf.Clamp01(fuel / glide.fuelSeconds) : 0f;

        /// <summary>True when a new press would resume a glide: it was started in this sequence and has fuel left.</summary>
        public bool CanResumeGlide => glideStarted && state != AerialState.Gliding && fuel >= glide.minimumToResume;

        /// <summary>The hands are used by the ability: no attack starts, and a swing that has not landed is abandoned.</summary>
        public bool HandsBusy =>
            (state == AerialState.Ascending && sinceStart < ascension.handsBusySeconds) ||
            state == AerialState.Gliding || state == AerialState.Dashing;

        /// <summary>For the animation: 0 none, 1 ascension, 2 glide propulsion, 3 dash.</summary>
        public int AnimationMove => (int)state;

        /// <summary>Back to the start: on the ground, nothing in progress, the ability available after the next jump.</summary>
        public void Reset()
        {
            state = AerialState.Idle;
            active = AerialAbilityKind.None;
            armed = false;
            usesLeft = 1;
            fuel = glide.fuelSeconds;
            glideStarted = false;
            groundedFrames = 0;
            groundedSeconds = 0f;
            exitPending = false;
        }

        /// <summary>Advances one frame. <paramref name="motion"/> is cleared and filled; events are appended.</summary>
        public void Tick(in AerialFrameInput input, ref AerialMotion motion, List<AerialEvent> events)
        {
            motion = default;
            float deltaTime = Mathf.Max(0f, input.DeltaTime);

            if (exitPending)
            {
                // The frame after a dash: the speed it leaves is set outright, so nothing carries over.
                exitPending = false;
                motion.HasStoredHorizontal = true;
                motion.StoredHorizontal = exitVelocity;
            }

            TrackGround(input, deltaTime, events);

            bool interrupted = input.Defeated || input.Traversing || input.Staggered;
            bool restrained = input.Charging || input.Blocking;

            switch (state)
            {
                case AerialState.Ascending:
                    UpdateAscension(input, deltaTime, interrupted, ref motion, events);
                    break;
                case AerialState.Gliding:
                    UpdateGlide(input, deltaTime, interrupted || restrained, ref motion, events);
                    break;
                case AerialState.Dashing:
                    UpdateDash(input, deltaTime, ref motion, events);
                    break;
            }

            // The press of a jump made this frame (from the ground or off a ledge) belongs to that jump; a press just
            // before landing belongs to the motor's jump buffer.
            bool pressIsFree = !input.JumpedFromGround && !input.LaunchedFromLedge && !input.LandingImminent;
            if (state == AerialState.Idle && input.JumpPressed && pressIsFree && !input.Grounded && armed && !interrupted &&
                !restrained)
            {
                TryStart(input, deltaTime, ref motion, events);
            }
        }

        /// <summary>
        /// One step of the glide's vertical speed: while rising, at least normal gravity (no lift); a fall faster than
        /// the cap is braked to it; a slower one falls with reduced gravity up to the cap.
        /// </summary>
        public static float GlideVerticalStep(float verticalSpeed, float gravity, float deltaTime, GlideTuning tuning)
        {
            if (verticalSpeed > 0f)
            {
                return verticalSpeed - gravity * tuning.riseGravityScale * deltaTime;
            }

            if (verticalSpeed < -tuning.maxFallSpeed)
            {
                return Mathf.Min(-tuning.maxFallSpeed, verticalSpeed + tuning.brakeAcceleration * deltaTime);
            }

            return Mathf.Max(-tuning.maxFallSpeed, verticalSpeed - gravity * tuning.fallGravityScale * deltaTime);
        }

        /// <summary>
        /// Which way a dash goes: where the joystick points if it is pushed past the threshold, otherwise where the
        /// Mage faces. Always flat and of length 1.
        /// </summary>
        public static Vector3 DashDirection(Vector3 move, Vector3 facing, float threshold)
        {
            move.y = 0f;
            facing.y = 0f;
            Vector3 chosen = move.magnitude >= Mathf.Max(0.0001f, threshold) ? move : facing;
            return chosen.sqrMagnitude > 0.0001f ? chosen.normalized : new Vector3(0f, 0f, 1f);
        }

        /// <summary>
        /// True if a wall with this normal stops a dash going this way: met more head-on than the stop angle from
        /// grazing allows.
        /// </summary>
        public static bool WallStopsDash(Vector3 direction, Vector3 wallNormal, float stopAngle)
        {
            wallNormal.y = 0f;
            if (wallNormal.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float into = -Vector3.Dot(direction.normalized, wallNormal.normalized);
            return into >= Mathf.Sin(Mathf.Clamp(stopAngle, 0f, 90f) * Mathf.Deg2Rad);
        }

        // ------------------------------------------------------------------ steps

        private void TrackGround(in AerialFrameInput input, float deltaTime, List<AerialEvent> events)
        {
            if (input.Grounded)
            {
                groundedFrames++;
                groundedSeconds += deltaTime;
            }
            else
            {
                groundedFrames = 0;
                groundedSeconds = 0f;
            }

            if (input.JumpedFromGround)
            {
                // A jump from the ground is a new airborne sequence, with its ability.
                End(AerialEndReason.Landed, events);
                Restore();
                armed = true;
                return;
            }

            if (input.LaunchedFromLedge)
            {
                if (rules.ledgeJumpRestoresAbility)
                {
                    Restore();
                }

                armed = true;
                return;
            }

            bool landed = groundedFrames >= rules.landingConfirmFrames && groundedSeconds >= rules.landingConfirmSeconds;
            if (landed)
            {
                End(AerialEndReason.Landed, events);
                Restore();
                armed = false;
            }
        }

        private void Restore()
        {
            usesLeft = 1;
            fuel = glide.fuelSeconds;
            glideStarted = false;
        }

        private void TryStart(in AerialFrameInput input, float deltaTime, ref AerialMotion motion, List<AerialEvent> events)
        {
            if (input.Selected == AerialAbilityKind.Glide && CanResumeGlide)
            {
                motion.ConsumeJumpPress = true;
                state = AerialState.Gliding;
                active = AerialAbilityKind.Glide;
                events.Add(new AerialEvent(AerialEventKind.Resumed, AerialAbilityKind.Glide, AerialEndReason.None, Vector3.down));
                Propel(input, deltaTime, ref motion, events);
                return;
            }

            if (usesLeft <= 0 || input.Selected == AerialAbilityKind.None)
            {
                return;
            }

            usesLeft--;
            motion.ConsumeJumpPress = true;
            active = input.Selected;
            sinceStart = 0f;

            switch (input.Selected)
            {
                case AerialAbilityKind.Ascension:
                    state = AerialState.Ascending;
                    gatherLeft = ascension.gatherSeconds;
                    impulseGiven = false;
                    motion.ScalesHorizontalVelocity = true;
                    motion.HorizontalScale = ascension.horizontalSpeedKept;
                    events.Add(new AerialEvent(AerialEventKind.Activated, active, AerialEndReason.None, Vector3.up));
                    Ascend(input, 0f, ref motion);
                    break;

                case AerialAbilityKind.Glide:
                    state = AerialState.Gliding;
                    glideStarted = true;
                    events.Add(new AerialEvent(AerialEventKind.Activated, active, AerialEndReason.None, Vector3.down));
                    Propel(input, deltaTime, ref motion, events);
                    break;

                case AerialAbilityKind.Dash:
                    state = AerialState.Dashing;
                    dashDirection = DashDirection(input.Move, input.Facing, dash.inputThreshold);
                    dashSpeed = dash.Speed;
                    dashLeft = dash.seconds;
                    events.Add(new AerialEvent(AerialEventKind.Activated, active, AerialEndReason.None, dashDirection));
                    DashStep(deltaTime, ref motion, events);
                    break;
            }
        }

        private void UpdateAscension(in AerialFrameInput input, float deltaTime, bool interrupted, ref AerialMotion motion,
            List<AerialEvent> events)
        {
            sinceStart += deltaTime;

            if (interrupted && !impulseGiven)
            {
                // Hit while gathering: the use is spent, the impulse never comes.
                End(AerialEndReason.Interrupted, events);
                return;
            }

            if (impulseGiven && (input.VerticalSpeed <= 0f || input.Grounded))
            {
                End(AerialEndReason.Finished, events);
                return;
            }

            Ascend(input, deltaTime, ref motion);
        }

        private void Ascend(in AerialFrameInput input, float deltaTime, ref AerialMotion motion)
        {
            if (impulseGiven)
            {
                return;
            }

            gatherLeft -= deltaTime;
            if (gatherLeft > 0f)
            {
                // The gathering pose: held in the air for a moment, not sinking.
                motion.SetsVerticalSpeed = true;
                motion.VerticalSpeed = 0f;
                return;
            }

            impulseGiven = true;
            motion.SetsVerticalSpeed = true;
            motion.VerticalSpeed = JumpPhysics.LaunchSpeedForFrames(input.Gravity, ascension.height, input.DeltaTime);
        }

        private void UpdateGlide(in AerialFrameInput input, float deltaTime, bool stopPropelling, ref AerialMotion motion,
            List<AerialEvent> events)
        {
            if (input.Defeated)
            {
                End(AerialEndReason.Interrupted, events);
                return;
            }

            if (stopPropelling)
            {
                Pause(AerialEndReason.Interrupted, events);
                return;
            }

            if (!input.JumpHeld)
            {
                Pause(AerialEndReason.Released, events);
                return;
            }

            if (input.Grounded)
            {
                Pause(AerialEndReason.Landed, events);
                return;
            }

            Propel(input, deltaTime, ref motion, events);
        }

        private void Propel(in AerialFrameInput input, float deltaTime, ref AerialMotion motion, List<AerialEvent> events)
        {
            float step = Mathf.Min(deltaTime, fuel);
            fuel -= step;

            motion.SetsVerticalSpeed = true;
            motion.VerticalSpeed = GlideVerticalStep(input.VerticalSpeed, input.Gravity, deltaTime, glide);
            motion.OverridesAirMovement = true;
            motion.AirTopSpeed = glide.speed;
            motion.AirAcceleration = glide.steerAcceleration;

            if (fuel <= 0f)
            {
                fuel = 0f;
                End(AerialEndReason.FuelSpent, events);
            }
        }

        private void Pause(AerialEndReason reason, List<AerialEvent> events)
        {
            state = AerialState.Idle;
            active = AerialAbilityKind.None;
            events.Add(new AerialEvent(AerialEventKind.Paused, AerialAbilityKind.Glide, reason, Vector3.down));
        }

        private void UpdateDash(in AerialFrameInput input, float deltaTime, ref AerialMotion motion, List<AerialEvent> events)
        {
            if (input.Defeated || input.Traversing)
            {
                ExitDash(dash.exitSpeed);
                End(AerialEndReason.Interrupted, events);
                return;
            }

            if (input.Grounded)
            {
                // A dash is an aerial move: on the ground it ends at once and keeps only its exit speed, from this frame.
                ExitDash(dash.exitSpeed);
                motion.HasStoredHorizontal = true;
                motion.StoredHorizontal = exitVelocity;
                End(AerialEndReason.Landed, events);
                return;
            }

            if (input.HitWall)
            {
                if (WallStopsDash(dashDirection, input.WallNormal, dash.stopAngle))
                {
                    // Stopped by the wall: no speed is kept into it.
                    motion.HasStoredHorizontal = true;
                    motion.StoredHorizontal = Vector3.zero;
                    state = AerialState.Idle;
                    active = AerialAbilityKind.None;
                    events.Add(new AerialEvent(AerialEventKind.Ended, AerialAbilityKind.Dash, AerialEndReason.Wall,
                        new Vector3(input.WallNormal.x, 0f, input.WallNormal.z).normalized));
                    return;
                }

                // A glancing wall turns the dash along it, as the controller slides along walls; speed into it is lost.
                Vector3 normal = new Vector3(input.WallNormal.x, 0f, input.WallNormal.z).normalized;
                Vector3 along = dashDirection - normal * Vector3.Dot(dashDirection, normal);
                float kept = along.magnitude;
                if (kept > 0.0001f)
                {
                    dashDirection = along / kept;
                    dashSpeed *= kept;
                }
            }

            DashStep(deltaTime, ref motion, events);
        }

        private void DashStep(float deltaTime, ref AerialMotion motion, List<AerialEvent> events)
        {
            sinceStart += deltaTime;
            Vector3 velocity = dashDirection * dashSpeed;

            // The last frame moves only for the time left, so the distance is the same at any frame rate.
            float share = deltaTime > 0f ? Mathf.Min(1f, dashLeft / deltaTime) : 1f;
            dashLeft -= deltaTime;

            motion.SetsVerticalSpeed = true;
            motion.VerticalSpeed = dash.verticalSpeed;
            motion.HasDashVelocity = true;
            motion.DashVelocity = velocity * share;
            motion.HasStoredHorizontal = true;
            motion.StoredHorizontal = velocity;

            if (dashLeft <= 0f)
            {
                ExitDash(dash.exitSpeed);
                motion.StoredHorizontal = exitVelocity;
                exitPending = false;
                End(AerialEndReason.Finished, events);
            }
        }

        private void ExitDash(float speed)
        {
            exitVelocity = dashDirection * Mathf.Min(speed, dashSpeed);
            exitPending = true;
        }

        private void End(AerialEndReason reason, List<AerialEvent> events)
        {
            if (state == AerialState.Idle)
            {
                return;
            }

            if (state == AerialState.Dashing && reason == AerialEndReason.Landed)
            {
                // Landing, or a jump from the ground, during a dash: the dash's speed is not carried along the ground.
                ExitDash(dash.exitSpeed);
            }

            AerialAbilityKind ended = active;
            state = AerialState.Idle;
            active = AerialAbilityKind.None;
            events.Add(new AerialEvent(AerialEventKind.Ended, ended, reason, Vector3.zero));
        }
    }
}
