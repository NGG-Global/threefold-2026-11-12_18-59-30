using System;
using Threefold.Combat;
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

            RememberStates();
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
                       (Stability == null || Stability.IsAtRest);
            }
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

            ApplyCouplings();
            AnnounceChanges();
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

                // Fire consumes the structure for as long as it burns.
                if (burning && Structural != null)
                {
                    Structural.Burn(deltaTime);
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
            RememberStates();
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
            if (!Thermal.CanHeat || Thermal.IsBurning)
            {
                return false;
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
                Stability.ObserveStructure(Structural.State);
            }

            // What is braced, down or destroyed does not move: a glide under way stops where it is. This
            // also covers a structure that failed, and so collapsed, under the very push that moved it.
            if (Kinetic != null && !CanSlide)
            {
                Kinetic.Halt();
            }

            // What is destroyed no longer burns: nothing is left alight, and nothing keeps ticking.
            if (IsDestroyed && Thermal != null && !Thermal.IsAtRest)
            {
                Thermal.Clear();
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
