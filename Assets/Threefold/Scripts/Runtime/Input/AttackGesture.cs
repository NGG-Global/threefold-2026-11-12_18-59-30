using System;
using UnityEngine;

namespace Threefold.InputHandling
{
    /// <summary>Where an attack press is in its life. Released and Cancelled last for one frame.</summary>
    public enum AttackGesturePhase
    {
        /// <summary>The attack control is not held.</summary>
        Idle,

        /// <summary>Held for less than the hold threshold. Letting go now is a tap.</summary>
        Pressed,

        /// <summary>Held past the threshold with no aim direction. Letting go fires the charged attack.</summary>
        Charging,

        /// <summary>Charging, with an aim direction given by dragging.</summary>
        Aiming,

        /// <summary>Let go this frame: either a tap or a charged release.</summary>
        Released,

        /// <summary>Abandoned this frame without firing.</summary>
        Cancelled,
    }

    /// <summary>What a release produced.</summary>
    public enum AttackReleaseKind
    {
        None,

        /// <summary>A short press: the standard (melee) attack.</summary>
        Tap,

        /// <summary>A release after charging: the charged attack.</summary>
        Charged,
    }

    /// <summary>Tuning for <see cref="AttackGestureRecognizer"/>. Distances are in canvas units (dp on touch).</summary>
    [Serializable]
    public sealed class AttackGestureSettings
    {
        [Tooltip("Seconds the attack control must be held before a charge begins. A shorter press is a tap.")]
        [Range(0.08f, 0.6f)]
        public float holdThreshold = 0.22f;

        [Tooltip("Finger travel allowed during a tap, in canvas units. Smaller movement is treated as jitter.")]
        [Min(0f)]
        public float tapSlop = 14f;

        [Tooltip("Dragging further than the tap slop before the hold threshold starts the charge at once, " +
                 "so a quick drag to aim is never mistaken for a tap.")]
        public bool dragStartsCharge = true;

        [Tooltip("Seconds of charging after the threshold to reach full charge.")]
        [Min(0.01f)]
        public float fullChargeSeconds = 0.9f;

        [Tooltip("Finger travel from the press point that gives full aim deflection, in canvas units.")]
        [Min(1f)]
        public float aimRadius = 64f;

        [Tooltip("Share of the aim travel around the press point that counts as no aim.")]
        [Range(0f, 0.9f)]
        public float aimDeadZone = 0.2f;

        [Tooltip("Multiplies the aim deflection. Higher values reach full aim with less travel.")]
        [Range(0.1f, 4f)]
        public float aimSensitivity = 1f;

        [Tooltip("Largest aim deflection reported, from 0 to 1.")]
        [Range(0.1f, 1f)]
        public float maxAimMagnitude = 1f;
    }

    /// <summary>
    /// One frame of attack input, as a device-independent snapshot. The flags are true only on the
    /// frame the event happened.
    /// </summary>
    public readonly struct AttackGestureFrame
    {
        public readonly AttackGesturePhase Phase;

        /// <summary>A short press was released: perform the standard attack.</summary>
        public readonly bool Tapped;

        /// <summary>The hold threshold was crossed (or a drag started the charge).</summary>
        public readonly bool ChargeStarted;

        /// <summary>A charge was released outside the cancel zone: fire the charged attack.</summary>
        public readonly bool ChargeReleased;

        /// <summary>The press or charge was abandoned without firing.</summary>
        public readonly bool Cancelled;

        /// <summary>Aim deflection, length 0 to 1. X is right, Y is up on the screen. Zero means default aim.</summary>
        public readonly Vector2 Aim;

        /// <summary>0 when the charge starts, 1 at full charge. On the release frame, the value at release.</summary>
        public readonly float ChargeProgress;

        /// <summary>True while the finger or button is over the cancel zone during a charge.</summary>
        public readonly bool CancelArmed;

        public AttackGestureFrame(AttackGesturePhase phase, bool tapped, bool chargeStarted, bool chargeReleased,
            bool cancelled, Vector2 aim, float chargeProgress, bool cancelArmed)
        {
            Phase = phase;
            Tapped = tapped;
            ChargeStarted = chargeStarted;
            ChargeReleased = chargeReleased;
            Cancelled = cancelled;
            Aim = aim;
            ChargeProgress = chargeProgress;
            CancelArmed = cancelArmed;
        }

        public bool IsCharging => Phase == AttackGesturePhase.Charging || Phase == AttackGesturePhase.Aiming;

        public bool IsActive => Phase == AttackGesturePhase.Pressed || IsCharging;

        /// <summary>
        /// Combines two sources, such as touch and keyboard. Events are combined with OR; the phase,
        /// aim and charge come from whichever source is further along.
        /// </summary>
        public static AttackGestureFrame Merge(in AttackGestureFrame a, in AttackGestureFrame b)
        {
            bool takeB = Rank(b.Phase) > Rank(a.Phase);
            AttackGestureFrame lead = takeB ? b : a;
            return new AttackGestureFrame(lead.Phase, a.Tapped || b.Tapped, a.ChargeStarted || b.ChargeStarted,
                a.ChargeReleased || b.ChargeReleased, a.Cancelled || b.Cancelled, lead.Aim, lead.ChargeProgress,
                lead.CancelArmed);
        }

        private static int Rank(AttackGesturePhase phase)
        {
            switch (phase)
            {
                case AttackGesturePhase.Pressed: return 1;
                case AttackGesturePhase.Charging: return 2;
                case AttackGesturePhase.Aiming: return 3;
                case AttackGesturePhase.Cancelled: return 4;
                case AttackGesturePhase.Released: return 5;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// Turns presses, drags and releases of one attack control into a tap, a charged release or a
    /// cancellation. Plain C# with the time passed in, so it is tested outside Unity, and shared by
    /// the touch and desktop input sources.
    ///
    /// Rules:
    /// - A release before the hold threshold, without dragging past the tap slop, is a tap.
    /// - Holding past the threshold starts a charge. A charge never produces a tap.
    /// - Dragging during a charge aims. Releasing fires the charged attack, unless the release is
    ///   over the cancel zone, the control was cancelled by the system, or <see cref="Cancel"/> was called.
    ///
    /// Call <see cref="BeginFrame"/> once per frame before feeding events, then <see cref="Update"/>.
    /// </summary>
    public sealed class AttackGestureRecognizer
    {
        private readonly AttackGestureSettings settings;

        private float pressTime;
        private float chargeStartTime;
        private float now;
        private Vector2 pressPosition;
        private Vector2 aim;
        private bool cancelArmed;
        private float releasedProgress;

        public AttackGestureRecognizer(AttackGestureSettings settings)
        {
            this.settings = settings ?? new AttackGestureSettings();
        }

        public AttackGestureSettings Settings => settings;

        public AttackGesturePhase Phase { get; private set; } = AttackGesturePhase.Idle;

        /// <summary>What the most recent release produced. Kept until the next press.</summary>
        public AttackReleaseKind LastRelease { get; private set; } = AttackReleaseKind.None;

        public bool Tapped { get; private set; }
        public bool ChargeStarted { get; private set; }
        public bool ChargeReleased { get; private set; }
        public bool Cancelled { get; private set; }

        /// <summary>Aim deflection, length 0 to 1. Kept on the release frame so it can be read with the event.</summary>
        public Vector2 Aim => aim;

        public bool CancelArmed => cancelArmed && IsCharging;

        public bool IsActive => Phase == AttackGesturePhase.Pressed || IsCharging;

        public bool IsCharging => Phase == AttackGesturePhase.Charging || Phase == AttackGesturePhase.Aiming;

        /// <summary>Seconds the current press has lasted, or 0 when idle.</summary>
        public float HeldSeconds => IsActive ? Mathf.Max(0f, now - pressTime) : 0f;

        /// <summary>0 at the start of the charge, 1 at full charge.</summary>
        public float ChargeProgress
        {
            get
            {
                if (Phase == AttackGesturePhase.Released)
                {
                    return releasedProgress;
                }

                return IsCharging ? Mathf.Clamp01((now - chargeStartTime) / Mathf.Max(settings.fullChargeSeconds, 0.01f)) : 0f;
            }
        }

        /// <summary>Clears the one-frame events and ends a Released or Cancelled phase.</summary>
        public void BeginFrame()
        {
            Tapped = false;
            ChargeStarted = false;
            ChargeReleased = false;
            Cancelled = false;

            if (Phase == AttackGesturePhase.Released || Phase == AttackGesturePhase.Cancelled)
            {
                Phase = AttackGesturePhase.Idle;
                aim = Vector2.zero;
                cancelArmed = false;
            }
        }

        /// <summary>Starts a press. Ignored while a press is already in progress.</summary>
        /// <returns>True if the press started.</returns>
        public bool Press(float time, Vector2 position)
        {
            if (IsActive)
            {
                return false;
            }

            now = time;
            pressTime = time;
            pressPosition = position;
            aim = Vector2.zero;
            cancelArmed = false;
            releasedProgress = 0f;
            LastRelease = AttackReleaseKind.None;
            Phase = AttackGesturePhase.Pressed;
            return true;
        }

        /// <summary>Reports where the finger is now, and whether it is over the cancel zone.</summary>
        public void Move(float time, Vector2 position, bool overCancelZone)
        {
            if (!IsActive)
            {
                return;
            }

            now = Mathf.Max(now, time);
            Vector2 offset = position - pressPosition;

            if (Phase == AttackGesturePhase.Pressed)
            {
                if (!settings.dragStartsCharge || offset.magnitude <= settings.tapSlop)
                {
                    return;
                }

                StartCharge(time);
            }

            aim = ComputeAim(offset);
            cancelArmed = overCancelZone;
            Phase = aim.sqrMagnitude > 0f ? AttackGesturePhase.Aiming : AttackGesturePhase.Charging;
        }

        /// <summary>Advances time: a press held past the threshold becomes a charge.</summary>
        public void Update(float time)
        {
            now = Mathf.Max(now, time);

            if (Phase == AttackGesturePhase.Pressed && now - pressTime >= settings.holdThreshold)
            {
                StartCharge(pressTime + settings.holdThreshold);
            }
        }

        /// <summary>Ends the press normally (the finger lifted or the button was let go).</summary>
        public void Release(float time, bool overCancelZone)
        {
            if (!IsActive)
            {
                return;
            }

            now = Mathf.Max(now, time);

            if (Phase == AttackGesturePhase.Pressed)
            {
                if (now - pressTime < settings.holdThreshold)
                {
                    Phase = AttackGesturePhase.Released;
                    LastRelease = AttackReleaseKind.Tap;
                    Tapped = true;
                    return;
                }

                // The threshold passed between two updates: the press was long enough to be a charge.
                StartCharge(pressTime + settings.holdThreshold);
            }

            if (overCancelZone)
            {
                Cancel();
                return;
            }

            releasedProgress = ChargeProgress;
            Phase = AttackGesturePhase.Released;
            LastRelease = AttackReleaseKind.Charged;
            ChargeReleased = true;
            cancelArmed = false;
        }

        /// <summary>
        /// Abandons the press or charge without firing: the system cancelled the touch, the controls
        /// were hidden, or another action (such as blocking) took priority.
        /// </summary>
        public void Cancel()
        {
            if (!IsActive)
            {
                return;
            }

            Phase = AttackGesturePhase.Cancelled;
            LastRelease = AttackReleaseKind.None;
            Cancelled = true;
            cancelArmed = false;
        }

        /// <summary>Returns to Idle at once, without raising any event. Used when input is switched off.</summary>
        public void Reset()
        {
            Phase = AttackGesturePhase.Idle;
            LastRelease = AttackReleaseKind.None;
            Tapped = false;
            ChargeStarted = false;
            ChargeReleased = false;
            Cancelled = false;
            aim = Vector2.zero;
            cancelArmed = false;
        }

        public AttackGestureFrame Snapshot()
        {
            return new AttackGestureFrame(Phase, Tapped, ChargeStarted, ChargeReleased, Cancelled, aim, ChargeProgress,
                CancelArmed);
        }

        /// <summary>
        /// Converts a drag offset into an aim deflection: a dead zone around the press point, a linear
        /// rise to full deflection at the aim radius, and a cap.
        /// </summary>
        public static Vector2 ComputeAim(Vector2 offset, AttackGestureSettings settings)
        {
            Vector2 raw = offset / Mathf.Max(settings.aimRadius, 1f) * Mathf.Max(settings.aimSensitivity, 0.01f);
            float magnitude = raw.magnitude;
            float deadZone = Mathf.Clamp(settings.aimDeadZone, 0f, 0.95f);

            if (magnitude <= deadZone || magnitude <= 0f)
            {
                return Vector2.zero;
            }

            float normalized = (Mathf.Min(magnitude, 1f) - deadZone) / (1f - deadZone);
            float length = normalized * Mathf.Clamp(settings.maxAimMagnitude, 0f, 1f);
            return raw * (length / magnitude);
        }

        private Vector2 ComputeAim(Vector2 offset)
        {
            return ComputeAim(offset, settings);
        }

        private void StartCharge(float startTime)
        {
            chargeStartTime = startTime;
            Phase = AttackGesturePhase.Charging;
            ChargeStarted = true;
        }
    }

    /// <summary>
    /// Rules that keep attack and block from contradicting each other. Each input source applies
    /// them once per frame after reading its controls.
    /// </summary>
    public static class CombatInputRules
    {
        /// <summary>
        /// Blocking takes priority over attacking: pressing or holding block abandons an attack press
        /// or charge without firing it.
        /// </summary>
        /// <returns>True if an attack was cancelled.</returns>
        public static bool ApplyBlock(AttackGestureRecognizer attack, bool blockHeld, bool blockPressedThisFrame)
        {
            if (attack == null || !attack.IsActive || !(blockHeld || blockPressedThisFrame))
            {
                return false;
            }

            attack.Cancel();
            return true;
        }

        /// <summary>A new attack press is ignored while block is held; it needs a fresh press afterwards.</summary>
        public static bool AttackPressAllowed(bool blockHeld)
        {
            return !blockHeld;
        }
    }
}
