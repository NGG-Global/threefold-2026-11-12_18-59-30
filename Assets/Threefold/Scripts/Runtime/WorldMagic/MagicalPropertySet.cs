using System;
using Threefold.Combat;
using Threefold.Persistence;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Which magical properties a kind of object has, and their values. This is the whole
    /// description of how something responds to magic; a definition asset holds one.
    /// </summary>
    [Serializable]
    public sealed class MagicalPropertySettings
    {
        [Tooltip("Heats up, cools down, and may catch fire.")]
        public bool thermal;
        public HeatSettings thermalSettings = new HeatSettings();

        [Tooltip("Has integrity that force and fire wear down until it breaks.")]
        public bool structural;
        public StructuralSettings structuralSettings = new StructuralSettings();

        [Tooltip("Can be moved by force, within limits.")]
        public bool kinetic;
        public KineticSettings kineticSettings = new KineticSettings();

        [Tooltip("Holds magical charge and activates at a threshold.")]
        public bool arcane;
        public ArcaneSettings arcaneSettings = new ArcaneSettings();

        [Tooltip("Can be unstable, braced, or brought down.")]
        public bool stability;
        public StabilitySettings stabilitySettings = new StabilitySettings();

        [Header("Persistence")]
        [Tooltip("What is saved for an object of this kind that has a Persistent Id in the scene. Everything else is " +
                 "reset when the world is reloaded. Heat and Charge are temporary by nature; tick them only for a " +
                 "puzzle that depends on them.")]
        public PersistedState persisted = PersistedState.Destruction | PersistedState.Activation | PersistedState.Collapse;

        [Tooltip("What happens to heat, charge and braces while the object is switched off (out of range, unloaded).")]
        public InactiveBehaviour whenInactive = InactiveBehaviour.Pause;

        [Tooltip("A puzzle depends on this object. It is always simulated, also when the budget for active objects is used up.")]
        public bool critical;

        [Header("Reactions")]
        [Tooltip("Effects the object sends out because of the state it is in: how one action sets off a chain.")]
        public ReactionEmission[] emissions = new ReactionEmission[0];

        public bool Has(WorldPropertyKind kind)
        {
            switch (kind)
            {
                case WorldPropertyKind.Thermal: return thermal;
                case WorldPropertyKind.Structural: return structural;
                case WorldPropertyKind.Kinetic: return kinetic;
                case WorldPropertyKind.Arcane: return arcane;
                case WorldPropertyKind.Stability: return stability;
                default: return false;
            }
        }

        public int PropertyCount =>
            (thermal ? 1 : 0) + (structural ? 1 : 0) + (kinetic ? 1 : 0) + (arcane ? 1 : 0) + (stability ? 1 : 0);
    }

    /// <summary>
    /// Conditions on an object's properties that something can wait for or show: a door that opens
    /// when its lock is activated or its structure is fractured, embers while it burns. Several may
    /// be combined; the combination holds when any one of them does.
    /// </summary>
    [Flags]
    public enum PropertyCondition
    {
        None = 0,
        Heated = 1 << 0,
        Burning = 1 << 1,
        Weakened = 1 << 2,
        Fractured = 1 << 3,
        Destroyed = 1 << 4,
        Charged = 1 << 5,
        Activated = 1 << 6,
        Moving = 1 << 7,
        AtTravelLimit = 1 << 8,
        Unstable = 1 << 9,
        Braced = 1 << 10,
        Collapsed = 1 << 11,
    }

    /// <summary>
    /// The magical properties of one object and the rules between them. It takes effects, advances
    /// time, and announces every change of state. It is the single place where an effect becomes a
    /// change in the world, for a crystal as for a door:
    ///
    /// - Heat goes to the thermal property; charge to the arcane; a brace to the stability.
    /// - Force goes to the stability (it may bring an unstable object down), to the structure (it
    ///   may crack it) and to the kinetic property (it may move it), in that order.
    /// - While the object burns, its structure loses integrity. While it is heated, its structure
    ///   may be more brittle. When its structure fails, an unstable object comes down. While it is
    ///   braced, it does not move.
    ///
    /// An effect that no property answers is refused, not ignored silently: the caller is told.
    /// Everything is deterministic and uses no Unity object, so it is tested outside Unity.
    /// </summary>
    public sealed class MagicalPropertySet
    {
        private readonly EffectEventLog log = new EffectEventLog();
        private readonly int[] lastStates = new int[5];

        // Reactions: per property, how far from the player's action the effect was that last changed it;
        // per rule, whether its condition held at the last look and when its next repeat is due.
        private readonly int[] causeDepth = new int[5];
        private readonly ReactionEmission[] emissions;
        private readonly bool[] emissionHeld;
        private readonly float[] emissionTimer;
        private readonly EmissionRequest[] pendingEmissions;
        private int pendingEmissionCount;
        private bool silent;

        private Vector3 kineticAxis = Vector3.forward;

        public MagicalPropertySet(MagicalPropertySettings settings, EffectTargets targetClass = EffectTargets.Objects)
        {
            Settings = settings ?? new MagicalPropertySettings();
            TargetClass = targetClass;

            if (Settings.thermal) Thermal = new HeatMeter(Settings.thermalSettings);
            if (Settings.structural) Structural = new StructuralProperty(Settings.structuralSettings);
            if (Settings.kinetic) Kinetic = new KineticProperty(Settings.kineticSettings);
            if (Settings.arcane) Arcane = new ArcaneProperty(Settings.arcaneSettings);
            if (Settings.stability) Stability = new StabilityProperty(Settings.stabilitySettings);

            emissions = Settings.emissions ?? new ReactionEmission[0];
            emissionHeld = new bool[emissions.Length];
            emissionTimer = new float[emissions.Length];
            pendingEmissions = new EmissionRequest[emissions.Length * 2];

            RememberStates();
            RememberEmissions();
        }

        public MagicalPropertySettings Settings { get; }

        /// <summary>Whether this is a creature or an object, for effects that are meant for only one of them.</summary>
        public EffectTargets TargetClass { get; }

        /// <summary>The properties. Null where the object does not have one.</summary>
        public HeatMeter Thermal { get; }
        public StructuralProperty Structural { get; }
        public KineticProperty Kinetic { get; }
        public ArcaneProperty Arcane { get; }
        public StabilityProperty Stability { get; }

        /// <summary>Raised for every change of state: the property, the state it left and the state it entered.</summary>
        public event Action<WorldPropertyKind, int, int> StateChanged;

        /// <summary>Axis mode: the world direction the object travels along. Set once by whoever places the object.</summary>
        public Vector3 KineticAxis
        {
            get => kineticAxis;
            set => kineticAxis = value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.forward;
        }

        /// <summary>True once the structure is destroyed. A destroyed object takes no more effects.</summary>
        public bool IsDestroyed => Structural != null && Structural.State == StructuralState.Destroyed;

        /// <summary>
        /// Optional. Asked, when a push sets a new glide, how far the object may go before something
        /// is in its way: it is given the offset the glide starts from and the offset it aims for, and
        /// returns the distance allowed. Whoever owns the object in the world answers (one physics
        /// sweep); without it nothing but the object's own limits stops a glide. Asked before the
        /// change is announced, so a push that is blocked outright announces nothing.
        /// </summary>
        public Func<Vector3, Vector3, float> GlideLimit { get; set; }

        /// <summary>False while the object is braced, has come down or is destroyed: then nothing moves it.</summary>
        private bool CanSlide =>
            !IsDestroyed && (Stability == null ||
                             (!Stability.ResistsDisplacement && Stability.State != StabilityState.Collapsed));

        /// <summary>True while nothing will change without a new effect, so time need not be advanced.</summary>
        public bool IsAtRest
        {
            get
            {
                if (IsDestroyed)
                {
                    return true;
                }

                return (Thermal == null || Thermal.IsAtRest) &&
                       (Arcane == null || Arcane.IsAtRest) &&
                       (Kinetic == null || Kinetic.State == KineticState.Resting) &&
                       (Stability == null || Stability.IsAtRest) &&
                       !HasRepeatingEmission;
            }
        }

        /// <summary>True while a repeating reaction is running, which needs time to keep passing.</summary>
        private bool HasRepeatingEmission
        {
            get
            {
                for (int i = 0; i < emissions.Length; i++)
                {
                    if (emissionHeld[i] && emissions[i] != null && emissions[i].timing == EmissionTiming.WhileHolding)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Reactions that have fallen due and wait to be sent to their targets. The owner sends and clears them.</summary>
        public int PendingEmissionCount => pendingEmissionCount;

        /// <summary>Reactions that fell due while the list was full and were lost. Stays 0 if the owner clears the list after every step.</summary>
        public int LostEmissionCount { get; private set; }

        public EmissionRequest GetPendingEmission(int index)
        {
            return pendingEmissions[index];
        }

        public void ClearPendingEmissions()
        {
            pendingEmissionCount = 0;
        }

        /// <summary>The rule a pending reaction belongs to.</summary>
        public ReactionEmission GetEmissionRule(int index)
        {
            return index >= 0 && index < emissions.Length ? emissions[index] : null;
        }

        /// <summary>How far from the player's action the effect was that last changed the given property. 0 for an attack.</summary>
        public int GetCauseDepth(WorldPropertyKind kind)
        {
            return causeDepth[(int)kind];
        }

        /// <summary>The current state of a property as a number, or -1 if the object does not have it.</summary>
        public int GetState(WorldPropertyKind kind)
        {
            switch (kind)
            {
                case WorldPropertyKind.Thermal: return Thermal != null ? (int)Thermal.State : -1;
                case WorldPropertyKind.Structural: return Structural != null ? (int)Structural.State : -1;
                case WorldPropertyKind.Kinetic: return Kinetic != null ? (int)Kinetic.State : -1;
                case WorldPropertyKind.Arcane: return Arcane != null ? (int)Arcane.State : -1;
                case WorldPropertyKind.Stability: return Stability != null ? (int)Stability.State : -1;
                default: return -1;
            }
        }

        /// <summary>True if any of the given conditions holds.</summary>
        public bool Meets(PropertyCondition conditions)
        {
            if (conditions == PropertyCondition.None)
            {
                return false;
            }

            if (Thermal != null)
            {
                ThermalState thermal = Thermal.State;
                if ((conditions & PropertyCondition.Heated) != 0 && thermal >= ThermalState.Heated) return true;
                if ((conditions & PropertyCondition.Burning) != 0 && thermal == ThermalState.Burning) return true;
            }

            if (Structural != null)
            {
                StructuralState structure = Structural.State;
                if ((conditions & PropertyCondition.Weakened) != 0 && structure >= StructuralState.Weakened) return true;
                if ((conditions & PropertyCondition.Fractured) != 0 && structure >= StructuralState.Fractured) return true;
                if ((conditions & PropertyCondition.Destroyed) != 0 && structure == StructuralState.Destroyed) return true;
            }

            if (Arcane != null)
            {
                ArcaneState arcane = Arcane.State;
                if ((conditions & PropertyCondition.Charged) != 0 && arcane >= ArcaneState.Charged) return true;
                if ((conditions & PropertyCondition.Activated) != 0 && arcane == ArcaneState.Activated) return true;
            }

            if (Kinetic != null)
            {
                if ((conditions & PropertyCondition.Moving) != 0 && Kinetic.State == KineticState.Moving) return true;
                if ((conditions & PropertyCondition.AtTravelLimit) != 0 && Kinetic.IsAtTravelLimit(kineticAxis)) return true;
            }

            if (Stability != null)
            {
                StabilityState stability = Stability.State;
                if ((conditions & PropertyCondition.Unstable) != 0 && stability == StabilityState.Unstable) return true;
                if ((conditions & PropertyCondition.Braced) != 0 && stability == StabilityState.Braced) return true;
                if ((conditions & PropertyCondition.Collapsed) != 0 && stability == StabilityState.Collapsed) return true;
            }

            return false;
        }

        /// <summary>Takes one effect and says what became of it.</summary>
        public EffectResponse Apply(in MagicEffect effect)
        {
            if (IsDestroyed)
            {
                return EffectResponse.Spent;
            }

            if ((effect.Targets & TargetClass) == 0)
            {
                return EffectResponse.Filtered;
            }

            if (effect.Magnitude <= 0f)
            {
                return EffectResponse.Resisted;
            }

            if (!Answers(effect.Kind))
            {
                return EffectResponse.NoProperty;
            }

            if (log.CheckAndRecord(effect.EventId, effect.Kind))
            {
                return EffectResponse.Duplicate;
            }

            bool applied;
            switch (effect.Kind)
            {
                case MagicEffectKind.Heat:
                    applied = ApplyHeat(effect.Magnitude);
                    break;

                case MagicEffectKind.ArcaneCharge:
                    applied = Arcane.AddCharge(effect.Magnitude);
                    break;

                case MagicEffectKind.Force:
                    applied = ApplyForce(effect.Direction, effect.Magnitude);
                    break;

                case MagicEffectKind.Stabilize:
                    applied = Stability.Brace(effect.Magnitude, effect.Duration);
                    break;

                default:
                    applied = false;
                    break;
            }

            if (applied)
            {
                RecordCause(effect.Kind, effect.Depth);
            }

            ApplyCouplings();
            AnnounceChanges();
            CollectEmissions(effect.EventId);
            return applied ? EffectResponse.Applied : EffectResponse.Resisted;
        }

        /// <summary>Advances time: cooling, burning, charge draining, gliding, braces running out.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || IsDestroyed)
            {
                return;
            }

            if (Thermal != null)
            {
                bool burning = Thermal.IsBurning;
                Thermal.Tick(deltaTime);

                // Fire consumes the structure for as long as it burns. What the fire then does to the
                // structure is as far from the player's action as the fire is.
                if (burning && Structural != null && Structural.Burn(deltaTime) > 0f)
                {
                    InheritCause(WorldPropertyKind.Structural, WorldPropertyKind.Thermal);
                }
            }

            Arcane?.Tick(deltaTime);
            Stability?.Tick(deltaTime);

            if (Kinetic != null && CanSlide)
            {
                Kinetic.Tick(deltaTime);
            }

            ApplyCouplings();
            AnnounceChanges();
            AdvanceRepeatingEmissions(deltaTime);
            CollectEmissions(0);
        }

        /// <summary>Puts every property back to how it started, without announcing it.</summary>
        public void Restore()
        {
            Thermal?.Clear();
            Structural?.Restore();
            Kinetic?.Restore();
            Arcane?.Restore();
            Stability?.Restore();
            log.Clear();
            Array.Clear(causeDepth, 0, causeDepth.Length);
            pendingEmissionCount = 0;
            RememberStates();
            RememberEmissions();
        }

        // ---------------------------------------------------------- persistence

        /// <summary>
        /// What of the present state is worth saving, for the parts named. A record with nothing in it
        /// means the object is as it starts and needs no entry.
        /// </summary>
        public ObjectRecord Capture(PersistedState parts)
        {
            var record = new ObjectRecord();

            if (Structural != null)
            {
                // Both are noted when both apply, so a destroyed barrier stays destroyed even if the
                // definition is later changed to keep only the one or the other.
                if ((parts & PersistedState.Destruction) != 0 && IsDestroyed)
                {
                    record.Saved |= PersistedState.Destruction;
                }

                if ((parts & PersistedState.Damage) != 0 && Structural.Integrity < Structural.Settings.integrity)
                {
                    record.Saved |= PersistedState.Damage;
                    record.Integrity = Structural.Integrity;
                }
            }

            if (Arcane != null)
            {
                bool activated = Arcane.State == ArcaneState.Activated;
                ArcaneActivation mode = Arcane.Settings.activation;

                if ((parts & PersistedState.Charge) != 0 && Arcane.Charge > 0f)
                {
                    // A timed activation is temporary by nature; what is kept of it is the charge.
                    record.Saved |= PersistedState.Charge;
                    record.Charge = Arcane.Charge;
                    record.Activated = activated && mode != ArcaneActivation.Timed;
                }

                if ((parts & PersistedState.Activation) != 0 && activated && mode == ArcaneActivation.Latch)
                {
                    record.Saved |= PersistedState.Activation;
                    record.Activated = true;
                }
            }

            if (Stability != null && (parts & PersistedState.Collapse) != 0 && Stability.State == StabilityState.Collapsed)
            {
                record.Saved |= PersistedState.Collapse;
                record.Collapsed = true;
            }

            if (Kinetic != null && (parts & PersistedState.Position) != 0)
            {
                // Where it will come to rest: a state saved in the middle of a glide is the glide's end.
                Vector3 rest = Kinetic.TargetOffset;
                if (rest.sqrMagnitude > 0.000001f)
                {
                    record.Saved |= PersistedState.Position;
                    record.OffsetX = rest.x;
                    record.OffsetY = rest.y;
                    record.OffsetZ = rest.z;
                }
            }

            if (Thermal != null && (parts & PersistedState.Heat) != 0 && !Thermal.IsBurning && Thermal.Heat > 0f)
            {
                record.Saved |= PersistedState.Heat;
                record.Heat = Thermal.Heat;
            }

            return record;
        }

        /// <summary>
        /// Takes the state from a saved record: everything goes back to how it starts, and then the
        /// parts that are both in the record and still named in <paramref name="parts"/> are applied.
        /// Everything temporary is therefore reset. Nothing is announced and no reaction is sent: what
        /// already happened does not happen again. An empty record gives the starting state.
        /// </summary>
        public void Load(in ObjectRecord record, PersistedState parts)
        {
            Restore();
            PersistedState use = record.Saved & parts;

            if (Structural != null)
            {
                if ((use & PersistedState.Damage) != 0)
                {
                    Structural.SetIntegrity(record.Integrity);
                }
                else if ((use & PersistedState.Destruction) != 0)
                {
                    Structural.SetIntegrity(0f);
                }
            }

            if (Arcane != null && (use & (PersistedState.Activation | PersistedState.Charge)) != 0)
            {
                Arcane.Load((use & PersistedState.Charge) != 0 ? record.Charge : 0f, record.Activated);
            }

            if (Stability != null && (use & PersistedState.Collapse) != 0 && record.Collapsed)
            {
                Stability.SetCollapsed();
            }

            if (Kinetic != null && (use & PersistedState.Position) != 0)
            {
                Kinetic.Place(new Vector3(record.OffsetX, record.OffsetY, record.OffsetZ), kineticAxis);
            }

            if (Thermal != null && (use & PersistedState.Heat) != 0)
            {
                Thermal.SetHeat(record.Heat);
            }

            // The rules between properties hold for a loaded state as for any other: a structure that
            // was saved as broken brings an unstable object down again, and so on.
            silent = true;
            ApplyCouplings();
            silent = false;

            RememberStates();
            RememberEmissions();
        }

        /// <summary>
        /// Drops everything temporary and keeps the rest: heat, fire, charge, a brace and stress go; a
        /// glide is completed; damage, a latched activation, a collapse and the position stay. Nothing
        /// is announced.
        /// </summary>
        public void ClearTemporary()
        {
            Thermal?.Clear();
            Arcane?.ClearTemporary();
            Stability?.ClearTemporary();
            Kinetic?.Finish();

            silent = true;
            ApplyCouplings();
            silent = false;

            pendingEmissionCount = 0;
            RememberStates();
            RememberEmissions();
        }

        /// <summary>
        /// Makes up time the object was away for: it cools, drains, burns down and finishes its glide as
        /// if the time had passed, in fixed steps so the result does not depend on how long a frame was.
        /// Nothing is announced and no reaction is sent, because nobody was there. After two minutes
        /// whatever is still temporary is simply dropped.
        /// </summary>
        public void CatchUp(float seconds)
        {
            if (seconds <= 0f || IsDestroyed)
            {
                return;
            }

            const float Step = 0.25f;
            const int MaxSteps = 480;

            silent = true;
            int steps = 0;
            float remaining = seconds;
            while (remaining > 0.0001f && steps < MaxSteps && !IsAtRest)
            {
                float step = Mathf.Min(Step, remaining);
                Tick(step);
                remaining -= step;
                steps++;
            }

            silent = false;
            pendingEmissionCount = 0;

            if (remaining > 0.0001f && !IsAtRest)
            {
                ClearTemporary();
                return;
            }

            RememberStates();
            RememberEmissions();
        }

        /// <summary>True if the object has a property that this kind of effect acts on.</summary>
        public bool Answers(MagicEffectKind kind)
        {
            switch (kind)
            {
                case MagicEffectKind.Heat: return Thermal != null;
                case MagicEffectKind.ArcaneCharge: return Arcane != null;
                case MagicEffectKind.Force: return Structural != null || Kinetic != null || Stability != null;
                case MagicEffectKind.Stabilize: return Stability != null;
                default: return false;
            }
        }

        private bool ApplyHeat(float magnitude)
        {
            if (!Thermal.CanHeat)
            {
                return false;
            }

            if (Thermal.IsBurning)
            {
                // More heat on something alight does nothing, unless its burn is set to renew.
                if (!Thermal.RenewsWhileBurning)
                {
                    return false;
                }

                Thermal.Add(magnitude);
                return true;
            }

            float before = Thermal.Heat;
            Thermal.Add(magnitude);
            return Thermal.IsBurning || Thermal.Heat > before;
        }

        private bool ApplyForce(Vector3 direction, float magnitude)
        {
            bool changed = false;

            if (Stability != null)
            {
                changed |= Stability.ApplyForce(magnitude);
            }

            if (Structural != null)
            {
                bool heated = Thermal != null && Thermal.State >= ThermalState.Heated;
                changed |= Structural.ApplyForce(magnitude, heated) > 0f;
            }

            // Judged after the force has acted on stability and structure: a blow that brings the object
            // down or destroys it does not also send it sliding, and a braced object holds its place.
            if (Kinetic != null && CanSlide)
            {
                Vector3 before = Kinetic.TargetOffset;
                if (Kinetic.Push(direction, magnitude, kineticAxis) > 0f)
                {
                    if (GlideLimit != null)
                    {
                        Kinetic.LimitGlide(GlideLimit(Kinetic.Offset, Kinetic.TargetOffset));
                    }

                    // A push that is blocked outright has changed nothing.
                    changed |= (Kinetic.TargetOffset - before).sqrMagnitude > 0.000001f;
                }
            }

            return changed;
        }

        /// <summary>The rules between properties, applied after every change in a fixed order.</summary>
        private void ApplyCouplings()
        {
            if (Structural != null && Stability != null)
            {
                StabilityState before = Stability.State;
                Stability.ObserveStructure(Structural.State);
                if (Stability.State != before)
                {
                    InheritCause(WorldPropertyKind.Stability, WorldPropertyKind.Structural);
                }
            }

            // What is braced, down or destroyed does not move: a glide under way stops where it is. This
            // also covers a structure that failed, and so collapsed, under the very push that moved it.
            if (Kinetic != null && !CanSlide)
            {
                Kinetic.Halt();
            }

            // What is destroyed no longer burns, holds no charge and needs no brace: every temporary
            // state ends with the object, and nothing keeps ticking.
            if (IsDestroyed)
            {
                if (Thermal != null && !Thermal.IsAtRest)
                {
                    Thermal.Clear();
                }

                Arcane?.ClearTemporary();
                Stability?.ClearTemporary();
            }
        }

        // ------------------------------------------------------------ reactions

        /// <summary>Notes how far from the player's action the effect was that has just changed something.</summary>
        private void RecordCause(MagicEffectKind kind, int depth)
        {
            switch (kind)
            {
                case MagicEffectKind.Heat:
                    causeDepth[(int)WorldPropertyKind.Thermal] = depth;
                    break;

                case MagicEffectKind.ArcaneCharge:
                    causeDepth[(int)WorldPropertyKind.Arcane] = depth;
                    break;

                case MagicEffectKind.Force:
                    causeDepth[(int)WorldPropertyKind.Structural] = depth;
                    causeDepth[(int)WorldPropertyKind.Kinetic] = depth;
                    causeDepth[(int)WorldPropertyKind.Stability] = depth;
                    break;

                case MagicEffectKind.Stabilize:
                    causeDepth[(int)WorldPropertyKind.Stability] = depth;
                    break;
            }
        }

        /// <summary>One property changed because of another: it is at least as far from the player's action.</summary>
        private void InheritCause(WorldPropertyKind changed, WorldPropertyKind from)
        {
            causeDepth[(int)changed] = Mathf.Max(causeDepth[(int)changed], causeDepth[(int)from]);
        }

        /// <summary>The depth of the cause behind a condition that holds: the deepest of the properties it names.</summary>
        private int CauseDepthOf(PropertyCondition when)
        {
            int depth = 0;
            const PropertyCondition Thermals = PropertyCondition.Heated | PropertyCondition.Burning;
            const PropertyCondition Structurals = PropertyCondition.Weakened | PropertyCondition.Fractured | PropertyCondition.Destroyed;
            const PropertyCondition Arcanes = PropertyCondition.Charged | PropertyCondition.Activated;
            const PropertyCondition Kinetics = PropertyCondition.Moving | PropertyCondition.AtTravelLimit;
            const PropertyCondition Stabilities = PropertyCondition.Unstable | PropertyCondition.Braced | PropertyCondition.Collapsed;

            if ((when & Thermals) != 0 && Meets(when & Thermals)) depth = Mathf.Max(depth, causeDepth[(int)WorldPropertyKind.Thermal]);
            if ((when & Structurals) != 0 && Meets(when & Structurals)) depth = Mathf.Max(depth, causeDepth[(int)WorldPropertyKind.Structural]);
            if ((when & Arcanes) != 0 && Meets(when & Arcanes)) depth = Mathf.Max(depth, causeDepth[(int)WorldPropertyKind.Arcane]);
            if ((when & Kinetics) != 0 && Meets(when & Kinetics)) depth = Mathf.Max(depth, causeDepth[(int)WorldPropertyKind.Kinetic]);
            if ((when & Stabilities) != 0 && Meets(when & Stabilities)) depth = Mathf.Max(depth, causeDepth[(int)WorldPropertyKind.Stability]);
            return depth;
        }

        /// <summary>
        /// Looks at every reaction rule after a change. A rule whose condition has just started to hold
        /// fires once (On Enter) or starts its interval (While Holding).
        /// </summary>
        /// <param name="causeEventId">The identity of the effect that caused the change, or 0 if time did.</param>
        private void CollectEmissions(int causeEventId)
        {
            for (int i = 0; i < emissions.Length; i++)
            {
                ReactionEmission rule = emissions[i];
                bool holds = rule != null && Meets(rule.when);
                if (holds && !emissionHeld[i])
                {
                    if (rule.timing == EmissionTiming.OnEnter)
                    {
                        // A reaction to an attack keeps the attack's identity: a target the same attack
                        // reaches by two paths takes it once, and it cannot come back to where it started.
                        AddEmission(i, rule, causeEventId != 0 ? causeEventId : MagicEffectEvents.NextId());
                    }
                    else
                    {
                        emissionTimer[i] = Mathf.Max(0.1f, rule.interval);
                    }
                }

                emissionHeld[i] = holds;
            }
        }

        private void AdvanceRepeatingEmissions(float deltaTime)
        {
            for (int i = 0; i < emissions.Length; i++)
            {
                ReactionEmission rule = emissions[i];
                if (rule == null || rule.timing != EmissionTiming.WhileHolding || !emissionHeld[i] || !Meets(rule.when))
                {
                    continue;
                }

                emissionTimer[i] -= deltaTime;
                if (emissionTimer[i] <= 0f)
                {
                    // At most one per step, however long the step: a slow frame must not turn into a burst.
                    float interval = Mathf.Max(0.1f, rule.interval);
                    emissionTimer[i] = emissionTimer[i] + interval > 0f ? emissionTimer[i] + interval : interval;
                    AddEmission(i, rule, MagicEffectEvents.NextId());
                }
            }
        }

        private void AddEmission(int index, ReactionEmission rule, int eventId)
        {
            if (silent || !rule.ReachesAnything)
            {
                return;
            }

            if (pendingEmissionCount >= pendingEmissions.Length)
            {
                LostEmissionCount++;
                return;
            }

            pendingEmissions[pendingEmissionCount++] = new EmissionRequest(index, CauseDepthOf(rule.when) + 1, eventId);
        }

        /// <summary>Takes the present state as the starting point for the rules, without firing any of them.</summary>
        private void RememberEmissions()
        {
            for (int i = 0; i < emissions.Length; i++)
            {
                ReactionEmission rule = emissions[i];
                emissionHeld[i] = rule != null && Meets(rule.when);
                emissionTimer[i] = rule != null ? Mathf.Max(0.1f, rule.interval) : 0f;
            }
        }

        private void RememberStates()
        {
            for (int i = 0; i < lastStates.Length; i++)
            {
                lastStates[i] = GetState((WorldPropertyKind)i);
            }
        }

        /// <summary>Compares every property with its last announced state, in a fixed order.</summary>
        private void AnnounceChanges()
        {
            // While loading or making up lost time nothing is announced; the owner looks at the result.
            if (silent)
            {
                return;
            }

            for (int i = 0; i < lastStates.Length; i++)
            {
                int now = GetState((WorldPropertyKind)i);
                int before = lastStates[i];
                if (now == before)
                {
                    continue;
                }

                lastStates[i] = now;
                StateChanged?.Invoke((WorldPropertyKind)i, before, now);
            }
        }
    }
}
