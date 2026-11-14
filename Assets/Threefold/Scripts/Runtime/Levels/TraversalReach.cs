using UnityEngine;

namespace Threefold.Levels
{
    /// <summary>
    /// What the Mage can climb, catch and jump, worked out from the default values of
    /// <c>ThirdPersonMotor</c> and <c>TraversalController</c>, so a level's layout can be checked
    /// against them outside Unity. The jump is followed in small steps with the motor's own gravity
    /// rules for a held jump (lighter near the top, heavier when falling); it is an approximation of
    /// the motor, not the motor itself, and the level keeps wide margins for that reason. If the
    /// component defaults change, change these with them; a test that needs Unity compares the two.
    /// </summary>
    public sealed class TraversalReach
    {
        // ThirdPersonMotor
        public float WalkSpeed = 4f;
        public float SprintSpeed = 7f;
        public float JumpHeight = 1.5f;
        public float Gravity = 28f;
        public float FallGravityMultiplier = 1.35f;
        public float ApexGravityMultiplier = 0.6f;
        public float ApexSpeedThreshold = 1.6f;
        public float StepOffset = 0.3f;
        public float SlopeLimit = 45f;

        // TraversalController
        public float GrabMinHeight = 1.25f;
        public float GrabMaxHeight = 2.25f;
        public float GrabMaxRiseSpeed = 3f;
        public float MantleMaxHeight = 1.25f;
        public float VaultMaxHeight = 1.15f;
        public float EdgeGuardDepth = 1.8f;

        // The character controller the setup gives the Mage.
        public float CapsuleHeight = 1.8f;
        public float CapsuleRadius = 0.35f;

        public static TraversalReach Defaults => new TraversalReach();

        /// <summary>How high the feet rise in a full, held jump.</summary>
        public float ApexHeight
        {
            get
            {
                Simulate(0f, out float apex, out _);
                return apex;
            }
        }

        /// <summary>
        /// The highest ledge, above the surface the Mage jumps from, that it can catch: at the top of a
        /// full jump a ledge up to the grab's maximum above the feet is caught.
        /// </summary>
        public float HighestCatchableLedge => ApexHeight + GrabMaxHeight;

        /// <summary>Whether a ledge this far above the surface stood on can be reached at all, by climbing onto it or catching it.</summary>
        public bool CanReachLedge(float heightAboveStand)
        {
            return heightAboveStand <= HighestCatchableLedge;
        }

        /// <summary>
        /// The horizontal distance covered by a full jump at a given running speed, from leaving a surface
        /// to coming back down to its height. Air control is ignored: the speed is kept, which is the most
        /// the motor allows.
        /// </summary>
        public float JumpDistance(float runSpeed, float landingDrop = 0f)
        {
            float time = Simulate(landingDrop, out _, out _);
            return runSpeed * time;
        }

        /// <summary>The longest gap a sprinting jump clears, landing at the height it left from.</summary>
        public float LongestJump => JumpDistance(SprintSpeed);

        /// <summary>The speed of landing after a fall of this height, with the motor's falling gravity.</summary>
        public float LandingSpeed(float dropHeight)
        {
            return Mathf.Sqrt(2f * Gravity * FallGravityMultiplier * Mathf.Max(0f, dropHeight));
        }

        /// <summary>Whether the edge guard stops the Mage before a drop of this depth (it does for deeper drops).</summary>
        public bool EdgeGuardStops(float dropDepth)
        {
            return dropDepth > EdgeGuardDepth;
        }

        /// <summary>Whether a vertical opening of this height lets the Mage walk through.</summary>
        public bool FitsUnder(float clearHeight)
        {
            return clearHeight >= CapsuleHeight + 0.2f;
        }

        /// <summary>Whether an opening of this width lets the Mage walk through.</summary>
        public bool FitsBetween(float clearWidth)
        {
            return clearWidth >= CapsuleRadius * 2f + 0.3f;
        }

        /// <summary>Whether a flat top of these dimensions has room to stand on.</summary>
        public bool CanStandOn(float width, float depth)
        {
            float needed = CapsuleRadius * 2f;
            return width >= needed && depth >= needed;
        }

        /// <summary>
        /// Follows a full held jump that leaves a surface and ends <paramref name="landingDrop"/> below it.
        /// Returns the time in the air.
        /// </summary>
        private float Simulate(float landingDrop, out float apex, out float risingSlowlyFrom)
        {
            const float dt = 1f / 480f;
            float height = 0f;
            float speed = Mathf.Sqrt(2f * Gravity * JumpHeight);
            float time = 0f;
            apex = 0f;
            risingSlowlyFrom = float.NaN;

            for (int i = 0; i < 20000; i++)
            {
                float applied = Gravity;
                if (speed < 0f)
                {
                    applied *= FallGravityMultiplier;
                }
                else if (speed < ApexSpeedThreshold)
                {
                    applied *= ApexGravityMultiplier;
                }

                if (speed < 0f && speed > -ApexSpeedThreshold)
                {
                    applied = Gravity * ApexGravityMultiplier;
                }

                if (speed > 0f && speed <= GrabMaxRiseSpeed && float.IsNaN(risingSlowlyFrom))
                {
                    risingSlowlyFrom = height;
                }

                speed -= applied * dt;
                height += speed * dt;
                time += dt;
                apex = Mathf.Max(apex, height);

                if (speed < 0f && height <= -landingDrop)
                {
                    break;
                }
            }

            return time;
        }
    }
}
