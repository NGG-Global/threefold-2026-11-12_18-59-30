using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>
    /// Behaviour of the stationary movement joystick, in the joystick's own coordinates (canvas
    /// units). It never moves: a touch activates it only within the activation radius around its
    /// centre, and touches anywhere else are left for other controls. Once active, the finger may
    /// travel anywhere until it lifts. Plain C#, so it is tested outside Unity.
    /// </summary>
    public sealed class FixedJoystickModel
    {
        private float activationRadius = 105f;
        private float radius = 55f;
        private float deadZone = 0.12f;
        private float responseExponent = 1.4f;
        private float sprintThreshold = 1.45f;
        private float sprintReleaseDeflection = 0.6f;

        /// <summary>Centre of the stick, in the same coordinates as the positions passed in.</summary>
        public Vector2 Centre { get; set; }

        /// <summary>Distance from the centre within which a touch takes the stick. Covers the ring and a margin.</summary>
        public float ActivationRadius
        {
            get => activationRadius;
            set => activationRadius = Mathf.Max(1f, value);
        }

        /// <summary>Finger travel from the centre that gives full speed.</summary>
        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(1f, value);
        }

        /// <summary>Share of the travel around the centre that gives no movement.</summary>
        public float DeadZone
        {
            get => deadZone;
            set => deadZone = Mathf.Clamp(value, 0f, 0.9f);
        }

        public float ResponseExponent
        {
            get => responseExponent;
            set => responseExponent = Mathf.Clamp(value, 0.25f, 4f);
        }

        /// <summary>
        /// Finger distance, as a multiple of <see cref="Radius"/>, that starts sprinting. Pushing past the
        /// ring sprints; 0 switches the feature off.
        /// </summary>
        public float SprintThreshold
        {
            get => sprintThreshold;
            set => sprintThreshold = value <= 0f ? 0f : Mathf.Max(1f, value);
        }

        /// <summary>Once sprinting, the sprint ends only when the deflection drops below this (0 to 1), or on release.</summary>
        public float SprintReleaseDeflection
        {
            get => sprintReleaseDeflection;
            set => sprintReleaseDeflection = Mathf.Clamp01(value);
        }

        public bool IsActive { get; private set; }

        /// <summary>Processed movement. Length 0 to 1.</summary>
        public Vector2 Value { get; private set; }

        /// <summary>Where the knob is drawn relative to the centre, never further than <see cref="Radius"/>.</summary>
        public Vector2 KnobOffset { get; private set; }

        public bool Sprinting { get; private set; }

        public bool CanActivate(Vector2 position)
        {
            return (position - Centre).sqrMagnitude <= activationRadius * activationRadius;
        }

        /// <summary>Takes the stick if the touch is inside the activation radius.</summary>
        public bool Begin(Vector2 position)
        {
            if (IsActive || !CanActivate(position))
            {
                return false;
            }

            IsActive = true;
            Sprinting = false;
            Move(position);
            return true;
        }

        public void Move(Vector2 position)
        {
            if (!IsActive)
            {
                return;
            }

            Vector2 offset = position - Centre;
            float distance = offset.magnitude;

            KnobOffset = Vector2.ClampMagnitude(offset, radius);
            Value = JoystickMath.Process(offset / radius, deadZone, responseExponent);

            if (sprintThreshold <= 0f)
            {
                Sprinting = false;
            }
            else if (distance >= radius * sprintThreshold)
            {
                Sprinting = true;
            }
            else if (Sprinting && distance < radius * sprintReleaseDeflection)
            {
                Sprinting = false;
            }
        }

        /// <summary>The finger lifted or was cancelled. The stick returns to rest at once.</summary>
        public void End()
        {
            IsActive = false;
            Value = Vector2.zero;
            KnobOffset = Vector2.zero;
            Sprinting = false;
        }
    }
}
