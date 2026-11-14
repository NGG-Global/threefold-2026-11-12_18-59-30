using System;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>The kinds of magical property an object can have. An object may have several.</summary>
    public enum WorldPropertyKind
    {
        Thermal = 0,
        Structural = 1,
        Kinetic = 2,
        Arcane = 3,
        Stability = 4,
    }

    // ------------------------------------------------------------ structural

    /// <summary>How whole an object is. The order is the order of the transitions; there is no way back.</summary>
    public enum StructuralState
    {
        Intact = 0,
        Weakened = 1,
        Fractured = 2,
        Destroyed = 3,
    }

    [Serializable]
    public sealed class StructuralSettings
    {
        [Tooltip("How much damage to its structure the object takes before it is destroyed.")]
        [Min(1f)]
        public float integrity = 100f;

        [Tooltip("Share of the integrity at or below which the object is weakened.")]
        [Range(0f, 1f)]
        public float weakenedFraction = 0.6f;

        [Tooltip("Share of the integrity at or below which the object is fractured.")]
        [Range(0f, 1f)]
        public float fracturedFraction = 0.25f;

        [Header("Force")]
        [Tooltip("A force at or below this strength does the structure no harm. Reinforced things have a high value.")]
        [Min(0f)]
        public float forceThreshold = 0.5f;

        [Tooltip("Integrity lost for each unit of force above the threshold. 0 makes force harmless.")]
        [Min(0f)]
        public float forceDamage = 20f;

        [Tooltip("Multiplies the harm force does. Above 1 is brittle, below 1 is tough.")]
        [Min(0f)]
        public float brittleness = 1f;

        [Tooltip("If above 0, used in place of the brittleness while the object is heated or burning: " +
                 "stone that turns brittle in the heat.")]
        [Min(0f)]
        public float heatedBrittleness;

        [Header("Fire")]
        [Tooltip("Integrity lost per second while the object burns. 0 for things fire does not consume.")]
        [Min(0f)]
        public float burnDamagePerSecond;
    }

    /// <summary>
    /// The structure of an object: integrity that force and fire wear down, passing through
    /// Weakened and Fractured to Destroyed at fixed shares of the integrity. Plain C#.
    /// </summary>
    public sealed class StructuralProperty
    {
        private readonly StructuralSettings settings;

        public StructuralProperty(StructuralSettings settings)
        {
            this.settings = settings ?? new StructuralSettings();
            Integrity = this.settings.integrity;
        }

        public StructuralSettings Settings => settings;

        public float Integrity { get; private set; }

        public float Fraction => Mathf.Clamp01(Integrity / Mathf.Max(settings.integrity, 1f));

        public StructuralState State
        {
            get
            {
                if (Integrity <= 0f)
                {
                    return StructuralState.Destroyed;
                }

                float fraction = Fraction;
                if (fraction <= settings.fracturedFraction + 0.0001f)
                {
                    return StructuralState.Fractured;
                }

                return fraction <= settings.weakenedFraction + 0.0001f ? StructuralState.Weakened : StructuralState.Intact;
            }
        }

        /// <summary>The harm a force of the given strength would do. 0 if it is at or below the threshold.</summary>
        public float ForceDamage(float magnitude, bool heated)
        {
            float excess = magnitude - settings.forceThreshold;
            if (excess <= 0f || settings.forceDamage <= 0f)
            {
                return 0f;
            }

            float brittle = heated && settings.heatedBrittleness > 0f ? settings.heatedBrittleness : settings.brittleness;
            return excess * settings.forceDamage * brittle;
        }

        /// <summary>Applies a force. Returns the integrity it removed.</summary>
        public float ApplyForce(float magnitude, bool heated)
        {
            return Damage(ForceDamage(magnitude, heated));
        }

        /// <summary>Burns for the given time. Returns the integrity removed.</summary>
        public float Burn(float deltaTime)
        {
            return Damage(settings.burnDamagePerSecond * Mathf.Max(0f, deltaTime));
        }

        /// <summary>Removes integrity. Returns what was actually removed.</summary>
        public float Damage(float amount)
        {
            if (amount <= 0f || Integrity <= 0f)
            {
                return 0f;
            }

            float removed = Mathf.Min(amount, Integrity);
            Integrity -= removed;

            // Many small losses leave a rounding remainder; a sliver of integrity is none.
            if (Integrity < 0.0001f)
            {
                Integrity = 0f;
            }

            return removed;
        }

        /// <summary>Destroys the structure at once, for example when it collapses.</summary>
        public void Destroy()
        {
            Integrity = 0f;
        }

        public void Restore()
        {
            Integrity = settings.integrity;
        }

        /// <summary>Sets the integrity from a saved state, kept within what the structure can hold.</summary>
        public void SetIntegrity(float value)
        {
            Integrity = float.IsNaN(value) ? settings.integrity : Mathf.Clamp(value, 0f, settings.integrity);
            if (Integrity < 0.0001f)
            {
                Integrity = 0f;
            }
        }
    }

    // --------------------------------------------------------------- kinetic

    public enum KineticMode
    {
        /// <summary>Moves only along one axis, between two travel limits: a platform on a rail, a sliding block.</summary>
        Axis,

        /// <summary>Moves in any direction along the ground, up to a distance from where it started.</summary>
        GroundPlane,
    }

    public enum KineticState
    {
        Resting = 0,
        Moving = 1,
    }

    [Serializable]
    public sealed class KineticSettings
    {
        [Tooltip("Share of a force that the object resists. 0 moves the full distance; 1 cannot be moved.")]
        [Range(0f, 1f)]
        public float resistance = 0.2f;

        [Tooltip("A force below this strength does not move the object at all.")]
        [Min(0f)]
        public float minimumForce = 0.5f;

        public KineticMode mode = KineticMode.Axis;

        [Tooltip("Axis mode: the direction of travel, in the object's own space.")]
        public Vector3 localAxis = Vector3.forward;

        [Tooltip("Axis mode: how far back from its starting place the object may travel, in metres (0 or negative).")]
        public float minTravel;

        [Tooltip("Axis mode: how far forward from its starting place the object may travel, in metres.")]
        public float maxTravel = 4f;

        [Tooltip("Ground mode: the furthest the object may be from its starting place, in metres.")]
        [Min(0f)]
        public float maxRange = 5f;

        [Tooltip("The most one push can move the object, in metres. Keeps the movement controlled.")]
        [Min(0f)]
        public float maxStepPerPush = 4f;

        [Tooltip("How fast the object glides to where it was pushed, in metres per second.")]
        [Min(0.1f)]
        public float moveSpeed = 5f;

        [Tooltip("A part held by a mechanism: force moves it only while the object meets one of these conditions, " +
                 "such as Activated. None: force may always move it. Conditions of the movement itself are ignored.")]
        public PropertyCondition movableOnlyWhen = PropertyCondition.None;
    }

    /// <summary>The arithmetic of a push, shared by objects in the world and by enemies.</summary>
    public static class KineticRules
    {
        /// <summary>
        /// How much of a push must go along the axis for it to count. A push is horizontal, so an axis
        /// that points (nearly) straight up or down can never be moved along by one.
        /// </summary>
        public const float MinimumAlong = 0.2f;

        /// <summary>
        /// Whether some push could ever move an object of these settings, with its axis as it stands in
        /// the world. False when it resists everything, has no room to move, or runs along an axis no
        /// horizontal push has a share in. Used to keep the discovery system from promising movement.
        /// </summary>
        public static bool CanEverBePushed(KineticSettings settings, Vector3 axisWorld)
        {
            if (settings == null || settings.resistance >= 1f || settings.maxStepPerPush <= 0f)
            {
                return false;
            }

            if (settings.mode != KineticMode.Axis)
            {
                return settings.maxRange > 0f;
            }

            if (Mathf.Max(settings.maxTravel, 0f) <= Mathf.Min(settings.minTravel, 0f) || axisWorld.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            Vector3 axis = axisWorld.normalized;
            return new Vector2(axis.x, axis.z).magnitude >= MinimumAlong;
        }

        /// <summary>
        /// How far a force moves something: nothing below the minimum, otherwise the force less the
        /// share that is resisted.
        /// </summary>
        public static float Displacement(float magnitude, float resistance, float minimumForce = 0f)
        {
            if (magnitude <= 0f || magnitude < minimumForce)
            {
                return 0f;
            }

            return magnitude * (1f - Mathf.Clamp01(resistance));
        }

        /// <summary>
        /// Whether a new push takes over from a movement already under way, such as the knockback of a
        /// stagger. The larger of the two moves the target: never their sum, and never the smaller.
        /// </summary>
        /// <param name="distanceInProgress">The full distance of the movement under way, or 0 if there is none.</param>
        public static bool TakesOver(float pushDistance, float distanceInProgress)
        {
            return pushDistance > 0f && pushDistance > distanceInProgress;
        }
    }

    /// <summary>
    /// Controlled movement under force, without a physics body. A push sets a target offset from
    /// the starting place, limited by the axis or range the settings allow; the object then glides
    /// there at a fixed speed. Offsets are in world axes, relative to the starting place. Plain C#.
    /// </summary>
    public sealed class KineticProperty
    {
        private readonly KineticSettings settings;

        public KineticProperty(KineticSettings settings)
        {
            this.settings = settings ?? new KineticSettings();
        }

        public KineticSettings Settings => settings;

        /// <summary>Where the object is now, relative to its starting place.</summary>
        public Vector3 Offset { get; private set; }

        /// <summary>Where it is gliding to.</summary>
        public Vector3 TargetOffset { get; private set; }

        public KineticState State => (TargetOffset - Offset).sqrMagnitude > 0.000001f ? KineticState.Moving : KineticState.Resting;

        /// <summary>Axis mode: travel along the axis. Positive is forward.</summary>
        public float Travel(Vector3 axisWorld)
        {
            return Vector3.Dot(Offset, axisWorld.normalized);
        }

        /// <summary>Axis mode: true once the object rests at either end of its travel.</summary>
        public bool IsAtTravelLimit(Vector3 axisWorld)
        {
            if (settings.mode != KineticMode.Axis || State != KineticState.Resting)
            {
                return false;
            }

            float travel = Travel(axisWorld);
            return travel >= settings.maxTravel - 0.01f || (settings.minTravel < 0f && travel <= settings.minTravel + 0.01f);
        }

        /// <summary>
        /// Pushes the object. Returns the distance the target moved, 0 if the force was too weak, was
        /// aimed across the axis, or the object is already at its limit.
        /// </summary>
        /// <param name="axisWorld">Axis mode: the axis of travel in world space.</param>
        public float Push(Vector3 direction, float magnitude, Vector3 axisWorld)
        {
            float distance = Mathf.Min(KineticRules.Displacement(magnitude, settings.resistance, settings.minimumForce),
                settings.maxStepPerPush);
            direction.y = 0f;
            if (distance <= 0f || direction.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            direction.Normalize();
            Vector3 before = TargetOffset;

            if (settings.mode == KineticMode.Axis)
            {
                if (axisWorld.sqrMagnitude < 0.0001f)
                {
                    return 0f;
                }

                axisWorld.Normalize();

                // Only the part of the push along the axis moves it; a push across the rail does nothing.
                float along = Vector3.Dot(direction, axisWorld);
                if (Mathf.Abs(along) < KineticRules.MinimumAlong)
                {
                    return 0f;
                }

                float travel = Vector3.Dot(TargetOffset, axisWorld) + distance * along;
                travel = Mathf.Clamp(travel, Mathf.Min(settings.minTravel, 0f), Mathf.Max(settings.maxTravel, 0f));
                TargetOffset = axisWorld * travel;
            }
            else
            {
                Vector3 target = TargetOffset + direction * distance;
                target.y = 0f;
                TargetOffset = Vector3.ClampMagnitude(target, settings.maxRange);
            }

            return (TargetOffset - before).magnitude;
        }

        /// <summary>
        /// Shortens the glide in progress to the given distance from where the object is now, because
        /// something is in the way.
        /// </summary>
        public void LimitGlide(float allowedDistance)
        {
            Vector3 remaining = TargetOffset - Offset;
            float length = remaining.magnitude;
            if (length <= 0f || allowedDistance >= length)
            {
                return;
            }

            TargetOffset = Offset + remaining * (Mathf.Max(0f, allowedDistance) / length);
        }

        /// <summary>Stops where it is: what is left of a glide is given up.</summary>
        public void Halt()
        {
            TargetOffset = Offset;
        }

        /// <summary>Glides towards the target. Returns the current offset.</summary>
        public Vector3 Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                Offset = Vector3.MoveTowards(Offset, TargetOffset, settings.moveSpeed * deltaTime);
            }

            return Offset;
        }

        public void Restore()
        {
            Offset = Vector3.zero;
            TargetOffset = Vector3.zero;
        }

        /// <summary>Completes a glide at once: the object is where it was going.</summary>
        public void Finish()
        {
            Offset = TargetOffset;
        }

        /// <summary>
        /// Puts the object at rest at a saved offset, kept within the limits it has now: on its axis and
        /// between its stops, or within its range. Limits may have been retuned since the state was saved.
        /// </summary>
        public void Place(Vector3 offset, Vector3 axisWorld)
        {
            if (float.IsNaN(offset.x) || float.IsNaN(offset.y) || float.IsNaN(offset.z))
            {
                offset = Vector3.zero;
            }

            if (settings.mode == KineticMode.Axis)
            {
                if (axisWorld.sqrMagnitude < 0.0001f)
                {
                    offset = Vector3.zero;
                }
                else
                {
                    axisWorld.Normalize();
                    float travel = Mathf.Clamp(Vector3.Dot(offset, axisWorld), Mathf.Min(settings.minTravel, 0f),
                        Mathf.Max(settings.maxTravel, 0f));
                    offset = axisWorld * travel;
                }
            }
            else
            {
                offset.y = 0f;
                offset = Vector3.ClampMagnitude(offset, settings.maxRange);
            }

            Offset = offset;
            TargetOffset = offset;
        }
    }

    // ---------------------------------------------------------------- arcane

    public enum ArcaneState
    {
        Uncharged = 0,

        /// <summary>Holds some charge, not yet enough to activate.</summary>
        Charged = 1,

        /// <summary>Reached its threshold: whatever it controls is switched on.</summary>
        Activated = 2,
    }

    public enum ArcaneActivation
    {
        /// <summary>Once activated it stays activated.</summary>
        Latch,

        /// <summary>Stays activated for a set time, then goes dark.</summary>
        Timed,

        /// <summary>Stays activated until its charge has drained away, and can be topped up meanwhile.</summary>
        WhileCharged,
    }

    [Serializable]
    public sealed class ArcaneSettings
    {
        [Tooltip("Multiplies charge received. 0 makes the object inert.")]
        [Min(0f)]
        public float susceptibility = 1f;

        [Tooltip("Charge at which the object activates.")]
        [Min(1f)]
        public float activationThreshold = 100f;

        [Tooltip("Seconds after the last charge before it starts to drain.")]
        [Min(0f)]
        public float decayDelay = 2f;

        [Tooltip("Charge lost per second once it drains.")]
        [Min(0f)]
        public float decayPerSecond = 15f;

        public ArcaneActivation activation = ArcaneActivation.Latch;

        [Tooltip("Timed activation: seconds it stays activated.")]
        [Min(0.1f)]
        public float activeSeconds = 8f;

        [Tooltip("Timed activation: what more charge does while it is activated. Ignore: nothing. Refresh: the " +
                 "active time starts again. Extend: one active time is added, up to twice the active time in all.")]
        public StackPolicy activeStacking = StackPolicy.Ignore;
    }

    /// <summary>
    /// Magical charge on an object. Charge accumulates and drains after a pause; at the threshold the
    /// object activates, and stays so for good, for a time, or while charge remains. Plain C#.
    /// </summary>
    public sealed class ArcaneProperty
    {
        private readonly ArcaneSettings settings;
        private float sinceLastCharge;
        private float activeRemaining;
        private bool activated;

        public ArcaneProperty(ArcaneSettings settings)
        {
            this.settings = settings ?? new ArcaneSettings();
        }

        public ArcaneSettings Settings => settings;

        public float Charge { get; private set; }

        public float Fraction => Mathf.Clamp01(Charge / Mathf.Max(settings.activationThreshold, 1f));

        public ArcaneState State
        {
            get
            {
                if (activated)
                {
                    return ArcaneState.Activated;
                }

                return Charge > 0f ? ArcaneState.Charged : ArcaneState.Uncharged;
            }
        }

        /// <summary>True while nothing will change without new charge.</summary>
        public bool IsAtRest => activated ? settings.activation == ArcaneActivation.Latch : Charge <= 0f;

        /// <summary>Adds charge. Returns true if the object accepted any.</summary>
        public bool AddCharge(float amount)
        {
            if (amount <= 0f || settings.susceptibility <= 0f)
            {
                return false;
            }

            if (activated && settings.activation != ArcaneActivation.WhileCharged)
            {
                // Already switched on. A latch takes no more; a timed activation may be renewed, by policy.
                if (settings.activation != ArcaneActivation.Timed || settings.activeStacking == StackPolicy.Ignore)
                {
                    return false;
                }

                activeRemaining = settings.activeStacking == StackPolicy.Refresh
                    ? settings.activeSeconds
                    : Mathf.Min(activeRemaining + settings.activeSeconds, settings.activeSeconds * 2f);
                return true;
            }

            sinceLastCharge = 0f;
            Charge = Mathf.Min(Charge + amount * settings.susceptibility, settings.activationThreshold);

            if (!activated && Charge + 0.0001f >= settings.activationThreshold)
            {
                activated = true;
                activeRemaining = settings.activeSeconds;
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (activated)
            {
                switch (settings.activation)
                {
                    case ArcaneActivation.Latch:
                        return;

                    case ArcaneActivation.Timed:
                        activeRemaining -= deltaTime;
                        if (activeRemaining <= 0f)
                        {
                            Deactivate();
                        }

                        return;
                }
            }

            if (Charge <= 0f)
            {
                return;
            }

            sinceLastCharge += deltaTime;
            if (sinceLastCharge >= settings.decayDelay)
            {
                Charge = Mathf.Max(0f, Charge - settings.decayPerSecond * deltaTime);
                if (Charge <= 0f && activated)
                {
                    Deactivate();
                }
            }
        }

        public void Restore()
        {
            Deactivate();
        }

        /// <summary>
        /// Drops what is temporary: charge, and an activation that only lasts for a time or while
        /// charge remains. A latched activation is not temporary and stays.
        /// </summary>
        public void ClearTemporary()
        {
            if (activated && settings.activation == ArcaneActivation.Latch)
            {
                return;
            }

            Deactivate();
        }

        /// <summary>
        /// Sets the state from a saved one. A timed activation is never restored as running: it is
        /// temporary by nature, and comes back as the charge it held, if charge was saved at all.
        /// </summary>
        public void Load(float charge, bool wasActivated)
        {
            Deactivate();
            Charge = float.IsNaN(charge) ? 0f : Mathf.Clamp(charge, 0f, settings.activationThreshold);

            if (wasActivated && settings.activation == ArcaneActivation.Latch)
            {
                activated = true;
                Charge = settings.activationThreshold;
            }
            else if (wasActivated && settings.activation == ArcaneActivation.WhileCharged && Charge > 0f)
            {
                activated = true;
            }
        }

        private void Deactivate()
        {
            activated = false;
            Charge = 0f;
            sinceLastCharge = 0f;
            activeRemaining = 0f;
        }
    }

    // ------------------------------------------------------------- stability

    public enum StabilityState
    {
        /// <summary>Stands firm. Force does not bring it down.</summary>
        Stable = 0,

        /// <summary>Stands, but a strong push or enough repeated force brings it down.</summary>
        Unstable = 1,

        /// <summary>Held for a time by a stabilising effect: it does not collapse and does not move.</summary>
        Braced = 2,

        /// <summary>Has come down. There is no way back.</summary>
        Collapsed = 3,
    }

    /// <summary>A structural state at which something happens to the stability, or never.</summary>
    public enum StructuralTrigger
    {
        Never = 0,
        Weakened = 1,
        Fractured = 2,
        Destroyed = 3,
    }

    [Serializable]
    public sealed class StabilitySettings
    {
        [Tooltip("Whether the object starts out unstable. If not, it only becomes unstable through damage.")]
        public bool startsUnstable = true;

        [Header("Collapse")]
        [Tooltip("A single force of at least this strength brings an unstable object down.")]
        [Min(0f)]
        public float collapseForce = 2.5f;

        [Tooltip("Force adds up as stress; at this much the object comes down. 0 switches this off.")]
        [Min(0f)]
        public float stressLimit = 4f;

        [Tooltip("Stress that fades per second.")]
        [Min(0f)]
        public float stressRecoveryPerSecond = 0.5f;

        [Tooltip("A stable object becomes unstable when its structure reaches this state.")]
        public StructuralTrigger unstableWhenStructure = StructuralTrigger.Weakened;

        [Tooltip("The object comes down when its structure reaches this state, unless it is braced.")]
        public StructuralTrigger collapseWhenStructure = StructuralTrigger.Fractured;

        [Header("Bracing")]
        [Tooltip("A stabilising effect weaker than this does not brace the object.")]
        [Min(0f)]
        public float braceThreshold = 0.5f;

        [Tooltip("Seconds a brace lasts when the effect does not say.")]
        [Min(0.1f)]
        public float braceSeconds = 2f;

        [Tooltip("The longest a brace can last, however often it is renewed.")]
        [Min(0.1f)]
        public float maxBraceSeconds = 6f;

        [Tooltip("What a new brace does to one that is running. Refresh: the longer of the two counts. Extend: the " +
                 "new time is added, up to the longest a brace can last. Ignore: the first brace runs its course.")]
        public StackPolicy braceStacking = StackPolicy.Refresh;

        [Tooltip("A single force of at least this strength breaks a brace before its time. 0: a brace holds against anything.")]
        [Min(0f)]
        public float braceBreakForce;

        [Header("Weight")]
        [Tooltip("Stress added each second by each creature standing on the object while it is unstable and not " +
                 "braced: a walkway that gives way under weight. It adds up to the stress limit like force does. " +
                 "0: weight does nothing.")]
        [Min(0f)]
        public float loadStressPerSecond;

        [Header("Recovery")]
        [Tooltip("Seconds after it came down before it stands again, counted only while nobody stands on it: a " +
                 "gantry a hoist winds back up. 0: once down, it stays down until a restart or a load. A collapse " +
                 "that recovers is temporary, so it is never saved and a reload stands the object up again.")]
        [Min(0f)]
        public float recoverSeconds;
    }

    /// <summary>
    /// Whether an object stands. An unstable object comes down under a strong push, under enough
    /// accumulated force, or when its structure fails. A stabilising effect braces it for a time:
    /// while braced it neither collapses nor moves, and what would have brought it down waits until
    /// the brace ends. Plain C#.
    /// </summary>
    public sealed class StabilityProperty
    {
        private readonly StabilitySettings settings;
        private bool unstable;
        private bool collapsed;
        private bool collapsePending;
        private float braceRemaining;
        private float recoverRemaining;

        public StabilityProperty(StabilitySettings settings)
        {
            this.settings = settings ?? new StabilitySettings();
            unstable = this.settings.startsUnstable;
        }

        public StabilitySettings Settings => settings;

        /// <summary>Accumulated force, for the stress limit.</summary>
        public float Stress { get; private set; }

        public float BraceRemaining => braceRemaining;

        public StabilityState State
        {
            get
            {
                if (collapsed)
                {
                    return StabilityState.Collapsed;
                }

                if (braceRemaining > 0f)
                {
                    return StabilityState.Braced;
                }

                return unstable ? StabilityState.Unstable : StabilityState.Stable;
            }
        }

        /// <summary>True while a brace keeps the object from being moved.</summary>
        public bool ResistsDisplacement => !collapsed && braceRemaining > 0f;

        /// <summary>How many creatures stand on the object. A physical fact, not a state: nothing resets it but the creatures leaving.</summary>
        public int Occupants { get; private set; }

        /// <summary>
        /// True while weight is wearing the object down: someone stands on it, it is unstable, not
        /// braced and not down, and weight counts for it at all.
        /// </summary>
        public bool IsStraining =>
            Occupants > 0 && settings.loadStressPerSecond > 0f && unstable && !collapsed && braceRemaining <= 0f;

        public bool IsAtRest => collapsed ? recoverRemaining <= 0f : braceRemaining <= 0f && Stress <= 0f && !IsStraining;

        /// <summary>
        /// True while the object is down but will stand again by itself (see <see cref="StabilitySettings.recoverSeconds"/>).
        /// Such a collapse is temporary: it is not saved, and clearing temporary state ends it.
        /// </summary>
        public bool IsRecovering => collapsed && recoverRemaining > 0f;

        /// <summary>Seconds until a recovering object stands again, counted while nobody stands on it. 0 when not recovering.</summary>
        public float RecoverRemaining => collapsed ? recoverRemaining : 0f;

        /// <summary>
        /// Says how many creatures stand on the object. While it is unstable and not braced, each adds
        /// stress every second; at the stress limit it comes down, exactly as if force had added the stress.
        /// </summary>
        /// <returns>True if the number changed.</returns>
        public bool SetOccupants(int count)
        {
            count = count < 0 ? 0 : count;
            if (count == Occupants)
            {
                return false;
            }

            Occupants = count;
            return true;
        }

        /// <summary>Braces the object. Returns false if the effect was too weak or the object has collapsed.</summary>
        public bool Brace(float magnitude, float seconds)
        {
            if (collapsed || magnitude < settings.braceThreshold)
            {
                return false;
            }

            float duration = seconds > 0f ? seconds : settings.braceSeconds;
            if (braceRemaining > 0f)
            {
                switch (settings.braceStacking)
                {
                    case StackPolicy.Ignore:
                        return false;

                    case StackPolicy.Extend:
                        braceRemaining = Mathf.Min(braceRemaining + duration, settings.maxBraceSeconds);
                        return true;
                }
            }

            braceRemaining = Mathf.Min(Mathf.Max(braceRemaining, duration), settings.maxBraceSeconds);
            return true;
        }

        /// <summary>
        /// A force arrives. Returns true if it brought the object down or added stress; false if the
        /// object is stable, braced or already down.
        /// </summary>
        public bool ApplyForce(float magnitude)
        {
            if (collapsed || magnitude <= 0f)
            {
                return false;
            }

            if (braceRemaining > 0f)
            {
                // A brace normally holds against anything. One that can be broken ends here and now; the
                // blow that broke it is spent on the brace and does nothing more.
                if (settings.braceBreakForce > 0f && magnitude + 0.0001f >= settings.braceBreakForce)
                {
                    EndBrace();
                    return true;
                }

                return false;
            }

            if (!unstable)
            {
                return false;
            }

            Stress += magnitude;
            if (magnitude + 0.0001f >= settings.collapseForce ||
                (settings.stressLimit > 0f && Stress + 0.0001f >= settings.stressLimit))
            {
                Collapse();
            }

            return true;
        }

        /// <summary>Tells the stability what state the structure is in, so damage can unsettle or collapse it.</summary>
        public void ObserveStructure(StructuralState structure)
        {
            if (collapsed)
            {
                return;
            }

            if (Reached(settings.unstableWhenStructure, structure))
            {
                unstable = true;
            }

            if (Reached(settings.collapseWhenStructure, structure))
            {
                if (braceRemaining > 0f)
                {
                    collapsePending = true;
                }
                else
                {
                    Collapse();
                }
            }
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (collapsed)
            {
                // A structure that recovers stands again once its time has passed, counted only while nobody
                // stands on what is left of it, so it never rises under someone.
                if (recoverRemaining > 0f && Occupants == 0)
                {
                    recoverRemaining = Mathf.Max(0f, recoverRemaining - deltaTime);
                    if (recoverRemaining <= 0f)
                    {
                        StandAgain();
                    }
                }

                return;
            }

            if (braceRemaining > 0f)
            {
                braceRemaining = Mathf.Max(0f, braceRemaining - deltaTime);
                if (braceRemaining <= 0f)
                {
                    EndBrace();
                }

                return;
            }

            if (IsStraining)
            {
                // Weight is force spread over time: it adds stress as a push does, and nothing recovers
                // while it lasts.
                Stress += Occupants * settings.loadStressPerSecond * deltaTime;
                if (settings.stressLimit > 0f && Stress + 0.0001f >= settings.stressLimit)
                {
                    Collapse();
                }

                return;
            }

            if (Stress > 0f)
            {
                Stress = Mathf.Max(0f, Stress - settings.stressRecoveryPerSecond * deltaTime);
            }
        }

        public void Restore()
        {
            unstable = settings.startsUnstable;
            collapsed = false;
            collapsePending = false;
            braceRemaining = 0f;
            recoverRemaining = 0f;
            Stress = 0f;
        }

        /// <summary>
        /// Drops what is temporary: a brace and accumulated stress. A collapse that was only being held
        /// off by the brace happens now; it was a consequence, not a temporary state. A collapse that
        /// would recover by itself is temporary, so the object stands again at once.
        /// </summary>
        public void ClearTemporary()
        {
            if (collapsed)
            {
                if (recoverRemaining > 0f)
                {
                    StandAgain();
                }

                return;
            }

            EndBrace();
            Stress = 0f;
        }

        /// <summary>
        /// Keeps the object down for good: a recovery under way is given up. For an object whose structure was
        /// destroyed, which can never stand again.
        /// </summary>
        public void StopRecovering()
        {
            recoverRemaining = 0f;
        }

        /// <summary>Marks the object as having come down, from a saved state. A saved collapse is meant to last, so it does not recover.</summary>
        public void SetCollapsed()
        {
            collapsed = true;
            collapsePending = false;
            braceRemaining = 0f;
            recoverRemaining = 0f;
            Stress = 0f;
        }

        private void EndBrace()
        {
            braceRemaining = 0f;
            if (collapsePending)
            {
                collapsePending = false;
                Collapse();
            }
        }

        private void Collapse()
        {
            collapsed = true;
            recoverRemaining = settings.recoverSeconds;
        }

        /// <summary>Stands up again as it started: unstable if it started so, no stress, no brace.</summary>
        private void StandAgain()
        {
            collapsed = false;
            collapsePending = false;
            braceRemaining = 0f;
            recoverRemaining = 0f;
            Stress = 0f;
            unstable = settings.startsUnstable;
        }

        private static bool Reached(StructuralTrigger trigger, StructuralState structure)
        {
            return trigger != StructuralTrigger.Never && (int)structure >= (int)trigger;
        }
    }
}
