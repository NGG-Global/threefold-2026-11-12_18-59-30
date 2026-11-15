using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>
    /// The vertical arithmetic of a jump in the air, shared by <see cref="ThirdPersonMotor"/> and the plain
    /// simulations that check the aerial abilities and the test course: one step of gravity, with heavier
    /// gravity while falling and lighter gravity near the top of a held jump. Plain C#, no Unity objects.
    /// </summary>
    public static class JumpPhysics
    {
        /// <summary>Upward speed that rises exactly <paramref name="height"/> metres against <paramref name="gravity"/>.</summary>
        public static float LaunchSpeed(float gravity, float height)
        {
            return Mathf.Sqrt(2f * Mathf.Max(0f, gravity) * Mathf.Max(0f, height));
        }

        /// <summary>
        /// Upward speed that rises <paramref name="height"/> metres when set in a frame of <paramref name="deltaTime"/>
        /// seconds and followed by the motor's frame-by-frame gravity. The motor moves by the speed after each step,
        /// which rises about speed × deltaTime / 2 higher than the exact curve; this takes that out, so the height
        /// is the same at 15 or 120 frames per second.
        /// </summary>
        public static float LaunchSpeedForFrames(float gravity, float height, float deltaTime)
        {
            float g = Mathf.Max(0f, gravity);
            float halfStep = g * Mathf.Max(0f, deltaTime) * 0.5f;
            return Mathf.Sqrt(halfStep * halfStep + 2f * g * Mathf.Max(0f, height)) - halfStep;
        }

        /// <summary>
        /// One step of gravity in the air, after any jump cut. The arithmetic of the motor's airborne update,
        /// unchanged: while falling gravity is multiplied by <paramref name="fallMultiplier"/>; near the top of a
        /// jump that is still held it is multiplied by <paramref name="apexMultiplier"/>.
        /// </summary>
        public static float AirborneStep(float verticalSpeed, bool risingFromJump, bool jumpHeld, float gravity,
            float fallMultiplier, float apexMultiplier, float apexSpeedThreshold, float terminalFallSpeed, float deltaTime)
        {
            float appliedGravity = gravity;
            if (verticalSpeed < 0f)
            {
                appliedGravity *= fallMultiplier;
            }
            else if (risingFromJump && verticalSpeed < apexSpeedThreshold && jumpHeld)
            {
                appliedGravity *= apexMultiplier;
            }

            // Near the top of a held jump, falling also starts gently.
            if (verticalSpeed < 0f && verticalSpeed > -apexSpeedThreshold && jumpHeld)
            {
                appliedGravity = gravity * apexMultiplier;
            }

            return Mathf.Max(verticalSpeed - appliedGravity * deltaTime, -terminalFallSpeed);
        }
    }
}
