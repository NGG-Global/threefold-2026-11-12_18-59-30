using System;
using Threefold.Combat;
using Threefold.Magic;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Discovery
{
    /// <summary>A set of effect kinds: what a discipline can deliver, or what an object could respond to.</summary>
    [Flags]
    public enum EffectKinds
    {
        None = 0,
        Heat = 1 << 0,
        ArcaneCharge = 1 << 1,
        Force = 1 << 2,
        Stabilize = 1 << 3,
        All = Heat | ArcaneCharge | Force | Stabilize,
    }

    /// <summary>How an object answered something the player tried on it.</summary>
    public enum FeedbackCategory
    {
        /// <summary>Nothing to show.</summary>
        None = 0,

        /// <summary>The object has nothing this kind of magic acts on.</summary>
        Incompatible = 1,

        /// <summary>The right kind of magic, but not enough of it.</summary>
        Insufficient = 2,

        /// <summary>It took effect and moved the object towards a threshold, without crossing one.</summary>
        Progress = 3,

        /// <summary>A state changed.</summary>
        Transition = 4,

        /// <summary>The object reached a state from which there is nothing further to do.</summary>
        Completed = 5,

        /// <summary>The right kind of magic, but something holds the object for now: it is braced, alight, at its stop.</summary>
        Blocked = 6,
    }

    /// <summary>What became of one attempt, for whoever shows it.</summary>
    public readonly struct FeedbackResult
    {
        public readonly FeedbackCategory Category;
        public readonly MagicEffectKind Kind;

        /// <summary>
        /// 0 to 1: how far the object is towards its next threshold for this kind of effect. It sets
        /// how strongly the answer is shown. It is never shown as a number.
        /// </summary>
        public readonly float Progress;

        /// <summary>
        /// Whether the attempt changed anything on the object. "Completed" with a change is the moment
        /// something was finished; without one, it is an attempt on something already finished.
        /// </summary>
        public readonly bool Changed;

        public FeedbackResult(FeedbackCategory category, MagicEffectKind kind, float progress, bool changed = false)
        {
            Category = category;
            Kind = kind;
            Progress = Mathf.Clamp01(progress);
            Changed = changed;
        }
    }

    /// <summary>The features of an object that tell an attentive player what it might respond to, before anything is tried.</summary>
    [Flags]
    public enum ClueKinds
    {
        None = 0,

        /// <summary>A faint, slow pulse of light: it holds or takes magical charge.</summary>
        ArcanePulse = 1 << 0,

        /// <summary>A material that burns: wood, straw, cloth.</summary>
        Flammable = 1 << 1,

        /// <summary>Vents, discoloration, scorch marks: heat changes it without burning it.</summary>
        HeatMarks = 1 << 2,

        /// <summary>A rail, a groove, a hinge: it is made to move.</summary>
        Track = 1 << 3,

        /// <summary>Cracks, loose debris, a slight sway: it stands only just.</summary>
        Unsteady = 1 << 4,
    }

    /// <summary>
    /// Player-side options for how strongly the world hints at its magic. They change presentation
    /// only; no rule and no outcome depends on them.
    /// </summary>
    [Serializable]
    public sealed class DiscoverySettings
    {
        [Tooltip("Strength of clues before and while aiming: 0 switches them off, 1 is the default, 2 is twice as strong. " +
                 "The answer to an attempt is always shown.")]
        [Range(0f, 2f)]
        public float clueIntensity = 1f;

        [Tooltip("Fewer particles: none for small answers, a few for large ones.")]
        public bool reducedParticles;

        [Tooltip("Larger movements, for players who cannot rely on colour or brightness. Movement is always part of every cue.")]
        public bool emphasizeMotion;

        [Tooltip("Objects answer more strongly when a matching discipline is selected and they are aimed at.")]
        public bool strongResonance;

        public DiscoverySettings Clone()
        {
            return (DiscoverySettings)MemberwiseClone();
        }
    }

    /// <summary>How much the device can afford. Only the richness of a cue depends on it, never which cue is shown.</summary>
    public enum DiscoveryQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    [Serializable]
    public sealed class ResonanceSettings
    {
        [Tooltip("Within this distance an object may answer a matching discipline faintly.")]
        [Min(0f)]
        public float radius = 9f;

        [Tooltip("Strength when merely near, at close range. It fades with distance.")]
        [Range(0f, 1f)]
        public float nearStrength = 0.25f;

        [Tooltip("Strength when the object is aimed at.")]
        [Range(0f, 1f)]
        public float aimedStrength = 0.6f;

        [Tooltip("Strength when it is aimed at while an attack is being charged or the guard is held.")]
        [Range(0f, 1f)]
        public float preparedStrength = 1f;

        [Tooltip("An object counts as aimed at when it is within this angle of the view direction.")]
        [Range(1f, 45f)]
        public float aimAngleDegrees = 9f;

        [Tooltip("Seconds for resonance to rise or fall.")]
        [Min(0.05f)]
        public float fadeSeconds = 0.35f;
    }

    [Serializable]
    public sealed class AttentionSettings
    {
        [Tooltip("Whether lingering near something unresolved strengthens its clues at all.")]
        public bool enabled = true;

        [Tooltip("Seconds near an unresolved object, without trying anything, before its clues start to strengthen.")]
        [Min(1f)]
        public float delaySeconds = 25f;

        [Tooltip("Seconds over which the emphasis rises.")]
        [Min(0.5f)]
        public float rampSeconds = 8f;

        [Tooltip("Seconds the emphasis stays up.")]
        [Min(0f)]
        public float holdSeconds = 6f;

        [Tooltip("Seconds of rest before it rises again, so it does not go on without pause.")]
        [Min(1f)]
        public float restSeconds = 20f;

        [Tooltip("How much stronger the clues become at most: 1 doubles them.")]
        [Range(0f, 2f)]
        public float maxBoost = 1f;

        [Tooltip("Seconds after the last hit dealt or taken during which nothing is emphasised.")]
        [Min(0f)]
        public float combatQuietSeconds = 8f;

        [Tooltip("How near the player has to be for the time to count.")]
        [Min(0.5f)]
        public float nearRadius = 7f;
    }

    /// <summary>What the attention rule remembers about one object.</summary>
    public struct AttentionState
    {
        /// <summary>Seconds the player has lingered near the object without trying anything.</summary>
        public float Lingered;

        /// <summary>The present emphasis, 0 to 1.</summary>
        public float Emphasis;
    }

    /// <summary>The movement a cue makes. Each category of answer has its own, so no answer depends on colour.</summary>
    public enum CueMotion
    {
        None = 0,

        /// <summary>Two quick shakes from side to side: no.</summary>
        Shake = 1,

        /// <summary>A lean with the effect that holds for a moment and eases back: it strains and does not give.</summary>
        Strain = 2,

        /// <summary>One soft swell: something was taken in.</summary>
        Swell = 3,

        /// <summary>A sharp kick with the effect that settles: something gave.</summary>
        Recoil = 4,

        /// <summary>Two slow swells, the second smaller: done.</summary>
        Settle = 5,

        /// <summary>One short dip: held fast.</summary>
        Dip = 6,
    }

    /// <summary>The sound a cue asks for. Whoever plays sounds decides what each one is.</summary>
    public enum DiscoverySound
    {
        None = 0,
        Resonance = 1,
        Refusal = 2,
        Resistance = 3,
        Progress = 4,
        Transition = 5,
        Completion = 6,
        Held = 7,
    }

    /// <summary>Everything about how one answer is shown. Made by <see cref="FeedbackPresentation"/>.</summary>
    public struct FeedbackCue
    {
        public FeedbackCategory Category;
        public CueMotion Motion;

        /// <summary>Size of the movement, 0 to 1.</summary>
        public float Amplitude;

        public float Seconds;

        /// <summary>Strength of the colour accent, 0 to 1. Supplementary: the movement carries the meaning.</summary>
        public float Accent;

        /// <summary>
        /// How many particles the answer may spend. A budget, not a count: a small answer shows its
        /// few motes or none, and a change of state shows the burst its object already has.
        /// </summary>
        public int Particles;

        public DiscoverySound Sound;

        /// <summary>Strength of the haptic pulse, 0 to 1.</summary>
        public float Haptic;
    }

    /// <summary>One moment of a cue's movement, relative to the object at rest.</summary>
    public struct MotionSample
    {
        /// <summary>Metres along the direction of the effect.</summary>
        public float Along;

        /// <summary>Metres across it.</summary>
        public float Across;

        /// <summary>Change of size: 0.02 is two per cent larger.</summary>
        public float Scale;
    }

    /// <summary>
    /// What the disciplines offer and what objects could respond to. This is the whole basis of
    /// resonance: an object answers a discipline only if one of the effects that discipline can
    /// really deliver could change something on the object as it is now. Nothing is looked up in a
    /// table of pairs, so a retuned discipline or a new object is right without further work.
    /// Plain C#.
    /// </summary>
    public static class Compatibility
    {
        public static EffectKinds Of(MagicEffectKind kind)
        {
            return (EffectKinds)(1 << (int)kind);
        }

        public static EffectKinds Of(in EffectPayload payload)
        {
            EffectKinds kinds = EffectKinds.None;
            if (payload.heat > 0f) kinds |= EffectKinds.Heat;
            if (payload.arcaneCharge > 0f) kinds |= EffectKinds.ArcaneCharge;
            if (payload.force > 0f) kinds |= EffectKinds.Force;
            if (payload.stabilize > 0f) kinds |= EffectKinds.Stabilize;
            return kinds;
        }

        /// <summary>
        /// The effects a discipline can deliver with the values it has now: by its swings, by its
        /// charged attack at full charge, and by its guard.
        /// </summary>
        public static EffectKinds OfDiscipline(MeleeComboProfile melee, in EffectPayload chargedAtFull, GuardEffectProfile guard)
        {
            EffectKinds kinds = Of(chargedAtFull);

            if (melee != null && melee.steps != null)
            {
                for (int i = 0; i < melee.steps.Length; i++)
                {
                    if (melee.steps[i] != null)
                    {
                        kinds |= Of(melee.steps[i].Effects);
                    }
                }
            }

            if (guard != null && guard.HasEffect)
            {
                kinds |= Of(guard.Effects);
            }

            return kinds;
        }

        /// <summary>The default disciplines' effects, from their starting values.</summary>
        public static EffectKinds OfDefaultDiscipline(MagicDiscipline discipline)
        {
            ChargedAttackDefaults charged = DisciplineDefaults.Charged(discipline);
            EffectPayload payload = charged.Projectile != null ? charged.Projectile.Effects(1f) : charged.Area.Effects(1f);
            return OfDiscipline(DisciplineDefaults.Melee(discipline), payload, DisciplineDefaults.Guard(discipline));
        }

        /// <summary>Of the kinds offered, those the object could respond to as it is now.</summary>
        public static EffectKinds Matching(MagicalPropertySet set, EffectKinds offered)
        {
            if (set == null || offered == EffectKinds.None)
            {
                return EffectKinds.None;
            }

            EffectKinds matching = EffectKinds.None;
            for (int kind = 0; kind < 4; kind++)
            {
                EffectKinds flag = (EffectKinds)(1 << kind);
                if ((offered & flag) != 0 && set.CouldRespondTo((MagicEffectKind)kind))
                {
                    matching |= flag;
                }
            }

            return matching;
        }

        /// <summary>
        /// The clues an object of these settings should carry, from its properties alone. Whether a
        /// particular object in a scene shows them is a matter of how it was built.
        /// </summary>
        public static ClueKinds CluesFor(MagicalPropertySettings settings)
        {
            ClueKinds clues = ClueKinds.None;
            if (settings == null)
            {
                return clues;
            }

            if (settings.arcane && settings.arcaneSettings != null && settings.arcaneSettings.susceptibility > 0f)
            {
                clues |= ClueKinds.ArcanePulse;
            }

            bool heats = settings.thermal && settings.thermalSettings != null && settings.thermalSettings.susceptibility > 0f;
            if (heats && settings.thermalSettings.flammable && settings.thermalSettings.burnSeconds > 0f)
            {
                clues |= ClueKinds.Flammable;
            }

            if (heats && settings.structural && settings.structuralSettings != null && settings.structuralSettings.heatedBrittleness > 0f)
            {
                clues |= ClueKinds.HeatMarks;
            }

            if (settings.kinetic && settings.kineticSettings != null && settings.kineticSettings.resistance < 1f &&
                settings.kineticSettings.mode == KineticMode.Axis)
            {
                clues |= ClueKinds.Track;
            }

            if (settings.stability && settings.stabilitySettings != null &&
                (settings.stabilitySettings.startsUnstable ||
                 (settings.structural && settings.stabilitySettings.unstableWhenStructure != StructuralTrigger.Never)))
            {
                clues |= ClueKinds.Unsteady;
            }

            return clues;
        }
    }

    /// <summary>
    /// Level 2: how strongly an object answers the mere presence of a matching discipline. Resonance
    /// says "this might work", never "this will work": it depends on kind, not on strength, and it
    /// is zero for an object nothing offered could change. Plain C#.
    /// </summary>
    public static class ResonanceRules
    {
        /// <param name="compatible">Whether any effect of the selected discipline could change the object now.</param>
        /// <param name="distance">Metres from the player to the object.</param>
        /// <param name="aimedAt">Whether the view points at it.</param>
        /// <param name="preparing">Whether an attack is being charged or the guard is held.</param>
        /// <returns>0 to 1. Always 0 when not compatible.</returns>
        public static float Strength(bool compatible, float distance, bool aimedAt, bool preparing, ResonanceSettings resonance,
            DiscoverySettings settings)
        {
            if (!compatible || resonance == null || settings == null || settings.clueIntensity <= 0f ||
                distance > resonance.radius)
            {
                return 0f;
            }

            float strength;
            if (aimedAt)
            {
                strength = preparing ? resonance.preparedStrength : resonance.aimedStrength;
            }
            else
            {
                // Merely near: faint, and fainter with distance.
                float nearness = resonance.radius > 0f ? 1f - Mathf.Clamp01(distance / resonance.radius) : 0f;
                strength = resonance.nearStrength * nearness;
            }

            if (settings.strongResonance)
            {
                strength *= 1.6f;
            }

            return Mathf.Clamp01(strength * settings.clueIntensity);
        }

        /// <summary>Moves the shown strength towards its target, so resonance swells and fades instead of switching.</summary>
        public static float Approach(float current, float target, float fadeSeconds, float deltaTime)
        {
            return Mathf.MoveTowards(current, target, deltaTime / Mathf.Max(0.05f, fadeSeconds));
        }
    }

    /// <summary>
    /// Level 3: what an attempt came to. The answer follows from the object's real state and
    /// properties, read after the effect was applied, so what is shown is always what happened.
    /// Plain C#.
    /// </summary>
    public static class ExperimentFeedback
    {
        /// <param name="set">The object's properties, after the effect.</param>
        /// <param name="effect">The effect that arrived.</param>
        /// <param name="response">What the object said became of it.</param>
        /// <param name="transitioned">Whether a state of the object changed because of it in a way that counts (see <see cref="IsSignificant"/>).</param>
        public static FeedbackResult Classify(MagicalPropertySet set, in MagicEffect effect, EffectResponse response, bool transitioned)
        {
            MagicEffectKind kind = effect.Kind;
            if (set == null || effect.Magnitude <= 0f)
            {
                return new FeedbackResult(FeedbackCategory.None, kind, 0f);
            }

            switch (response)
            {
                case EffectResponse.NoProperty:
                    return new FeedbackResult(FeedbackCategory.Incompatible, kind, 0f);

                case EffectResponse.Applied:
                {
                    if (!transitioned)
                    {
                        // A brace that is merely being kept up brings the object nearer to nothing; the
                        // object already shows that it is braced.
                        return kind == MagicEffectKind.Stabilize
                            ? new FeedbackResult(FeedbackCategory.None, kind, 0f)
                            : new FeedbackResult(FeedbackCategory.Progress, kind, ProgressOf(set, kind), true);
                    }

                    return new FeedbackResult(IsComplete(set, kind) ? FeedbackCategory.Completed : FeedbackCategory.Transition, kind,
                        1f, true);
                }

                case EffectResponse.Resisted:
                    return new FeedbackResult(ClassifyResisted(set, effect), kind, ProgressOf(set, kind));

                default:
                    // Filtered, duplicate, or the object is already gone: nothing the player should be told.
                    return new FeedbackResult(FeedbackCategory.None, kind, 0f);
            }
        }

        /// <summary>
        /// Whether a change of state is one the player should be told about as "something gave". Taking
        /// a first trace of charge or heat is progress, not a transition; reaching a threshold is one.
        /// Changes that come with time alone (cooling, a brace running out, a glide ending) are not.
        /// </summary>
        public static bool IsSignificant(WorldPropertyKind property, int from, int to)
        {
            switch (property)
            {
                case WorldPropertyKind.Thermal:
                    return to > from && to >= (int)ThermalState.Heated;

                case WorldPropertyKind.Structural:
                    return to > from;

                case WorldPropertyKind.Kinetic:
                    return to == (int)KineticState.Moving;

                case WorldPropertyKind.Arcane:
                    return to == (int)ArcaneState.Activated && from != to;

                case WorldPropertyKind.Stability:
                    return to == (int)StabilityState.Braced || to == (int)StabilityState.Collapsed ||
                           (to == (int)StabilityState.Unstable && from == (int)StabilityState.Stable);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether an answer is worth showing at all. What the player did is always answered. What
        /// other objects set off is shown only when something really changed, or a burning barricade
        /// would make everything around it shake once a second.
        /// </summary>
        public static bool ShouldPresent(in FeedbackResult result, int reactionDepth)
        {
            if (result.Category == FeedbackCategory.None)
            {
                return false;
            }

            return reactionDepth <= 0 || result.Category == FeedbackCategory.Transition ||
                   result.Category == FeedbackCategory.Completed;
        }

        private static FeedbackCategory ClassifyResisted(MagicalPropertySet set, in MagicEffect effect)
        {
            switch (effect.Kind)
            {
                case MagicEffectKind.Heat:
                    if (set.Thermal == null || !set.Thermal.CanHeat)
                    {
                        return FeedbackCategory.Incompatible;
                    }

                    // Already alight, or as hot as it can get: nothing more to add for now.
                    return FeedbackCategory.Blocked;

                case MagicEffectKind.ArcaneCharge:
                    if (set.Arcane == null || set.Arcane.Settings.susceptibility <= 0f)
                    {
                        return FeedbackCategory.Incompatible;
                    }

                    return set.Arcane.State == ArcaneState.Activated && set.Arcane.Settings.activation == ArcaneActivation.Latch
                        ? FeedbackCategory.Completed
                        : FeedbackCategory.Blocked;

                case MagicEffectKind.Force:
                {
                    if (set.Stability != null && set.Stability.ResistsDisplacement)
                    {
                        return FeedbackCategory.Blocked;
                    }

                    bool collapsed = set.Stability != null && set.Stability.State == StabilityState.Collapsed;
                    bool structureTooStrong = set.Structural != null && set.Structural.Settings.forceDamage > 0f &&
                                              effect.Magnitude <= set.Structural.Settings.forceThreshold;
                    bool tooLightToMove = set.Kinetic != null && !collapsed &&
                                          KineticRules.Displacement(effect.Magnitude, set.Kinetic.Settings.resistance,
                                              set.Kinetic.Settings.minimumForce) <= 0f && set.Kinetic.Settings.resistance < 1f;
                    if (structureTooStrong || tooLightToMove)
                    {
                        return FeedbackCategory.Insufficient;
                    }

                    if (set.Kinetic != null && !collapsed)
                    {
                        // Strong enough, but at its stop, across its rail, or with something in the way.
                        return FeedbackCategory.Blocked;
                    }

                    return collapsed ? FeedbackCategory.Completed : FeedbackCategory.Incompatible;
                }

                case MagicEffectKind.Stabilize:
                    if (set.Stability == null)
                    {
                        return FeedbackCategory.Incompatible;
                    }

                    if (set.Stability.State == StabilityState.Collapsed)
                    {
                        return FeedbackCategory.Completed;
                    }

                    return effect.Magnitude < set.Stability.Settings.braceThreshold
                        ? FeedbackCategory.Insufficient
                        : FeedbackCategory.Blocked;

                default:
                    return FeedbackCategory.None;
            }
        }

        /// <summary>True if, for this kind of effect, the object has reached a state with nothing further to do.</summary>
        private static bool IsComplete(MagicalPropertySet set, MagicEffectKind kind)
        {
            if (set.IsDestroyed)
            {
                return true;
            }

            switch (kind)
            {
                case MagicEffectKind.ArcaneCharge:
                    return set.Arcane != null && set.Arcane.State == ArcaneState.Activated &&
                           set.Arcane.Settings.activation == ArcaneActivation.Latch;

                case MagicEffectKind.Force:
                    return set.Stability != null && set.Stability.State == StabilityState.Collapsed;

                default:
                    return false;
            }
        }

        /// <summary>How far the object is towards its next threshold for this kind of effect, 0 to 1.</summary>
        public static float ProgressOf(MagicalPropertySet set, MagicEffectKind kind)
        {
            switch (kind)
            {
                case MagicEffectKind.Heat:
                    return set.Thermal != null ? set.Thermal.Fraction : 0f;

                case MagicEffectKind.ArcaneCharge:
                    return set.Arcane != null ? set.Arcane.Fraction : 0f;

                case MagicEffectKind.Force:
                {
                    float progress = 0f;
                    if (set.Structural != null)
                    {
                        progress = Mathf.Max(progress, 1f - set.Structural.Fraction);
                    }

                    if (set.Stability != null && set.Stability.Settings.stressLimit > 0f)
                    {
                        progress = Mathf.Max(progress, set.Stability.Stress / set.Stability.Settings.stressLimit);
                    }

                    return Mathf.Clamp01(progress);
                }

                case MagicEffectKind.Stabilize:
                    return set.Stability != null && set.Stability.State == StabilityState.Braced ? 1f : 0f;

                default:
                    return 0f;
            }
        }
    }

    /// <summary>What the feedback gate remembers about one object.</summary>
    public struct FeedbackGateState
    {
        public FeedbackCategory LastCategory;
        public MagicEffectKind LastKind;
        public float LastTime;
        public bool HasLast;
    }

    /// <summary>
    /// Keeps the same small answer from being shown over and over: a guard held next to a wall sends
    /// its effect several times a second, and the wall should not twitch each time. A change of
    /// state is always shown. Plain C#.
    /// </summary>
    public static class FeedbackGate
    {
        /// <param name="quietSeconds">The same category for the same kind of effect is not repeated within this time.</param>
        /// <param name="unchangedAnswersFor">
        /// The kinds of effect for which an answer is shown although nothing changed (incompatible,
        /// insufficient, blocked, already finished). A kind that is not aimed, such as the brace of a
        /// held guard, is left out, so that things it does nothing to stay still.
        /// </param>
        public static bool Allow(ref FeedbackGateState state, in FeedbackResult result, float now, float quietSeconds,
            EffectKinds unchangedAnswersFor)
        {
            if (result.Category == FeedbackCategory.None)
            {
                return false;
            }

            if (!result.Changed && (Compatibility.Of(result.Kind) & unchangedAnswersFor) == 0)
            {
                return false;
            }

            // Only a real change is always shown. An attempt on something already finished is "completed"
            // too, but it changed nothing and must not be repeated without pause.
            bool significant = result.Changed &&
                               (result.Category == FeedbackCategory.Transition || result.Category == FeedbackCategory.Completed);
            if (!significant && state.HasLast && state.LastCategory == result.Category && state.LastKind == result.Kind &&
                now - state.LastTime < quietSeconds)
            {
                return false;
            }

            state.HasLast = true;
            state.LastCategory = result.Category;
            state.LastKind = result.Kind;
            state.LastTime = now;
            return true;
        }
    }

    /// <summary>
    /// Turns a category of answer into what is shown, heard and felt. The movement and the sound of a
    /// category are the same on every device and with every option; only how rich the answer is
    /// (its size, its particles) depends on the options and the quality level. Every category has a
    /// movement of its own, so none of them depends on colour. Plain C#.
    /// </summary>
    public static class FeedbackPresentation
    {
        /// <summary>The smallest movement an answer is shown with, however low the progress.</summary>
        public const float MinimumAmplitude = 0.2f;

        /// <summary>A small answer gets its few motes only if its plan allows at least this many particles.</summary>
        public const int MoteThreshold = 2;

        public static FeedbackCue Plan(in FeedbackResult result, DiscoverySettings settings, DiscoveryQuality quality)
        {
            var cue = new FeedbackCue { Category = result.Category };
            int particles;

            switch (result.Category)
            {
                case FeedbackCategory.Incompatible:
                    cue.Motion = CueMotion.Shake;
                    cue.Amplitude = 0.25f;
                    cue.Seconds = 0.3f;
                    cue.Accent = 0.1f;
                    cue.Sound = DiscoverySound.Refusal;
                    cue.Haptic = 0.1f;
                    particles = 0;
                    break;

                case FeedbackCategory.Insufficient:
                    cue.Motion = CueMotion.Strain;
                    cue.Amplitude = 0.5f;
                    cue.Seconds = 0.5f;
                    cue.Accent = 0.25f;
                    cue.Sound = DiscoverySound.Resistance;
                    cue.Haptic = 0.3f;
                    particles = 2;
                    break;

                case FeedbackCategory.Progress:
                    // The nearer the threshold, the fuller the answer: the only hint of "how much more".
                    cue.Motion = CueMotion.Swell;
                    cue.Amplitude = Mathf.Lerp(0.25f, 0.6f, result.Progress);
                    cue.Seconds = 0.35f;
                    cue.Accent = Mathf.Lerp(0.2f, 0.6f, result.Progress);
                    cue.Sound = DiscoverySound.Progress;
                    cue.Haptic = 0.15f;
                    particles = 2;
                    break;

                case FeedbackCategory.Transition:
                    cue.Motion = CueMotion.Recoil;
                    cue.Amplitude = 0.75f;
                    cue.Seconds = 0.5f;
                    cue.Accent = 0.8f;
                    cue.Sound = DiscoverySound.Transition;
                    cue.Haptic = 0.5f;
                    particles = 8;
                    break;

                case FeedbackCategory.Completed:
                    cue.Motion = CueMotion.Settle;
                    cue.Sound = DiscoverySound.Completion;
                    if (result.Changed)
                    {
                        // The moment something was finished.
                        cue.Amplitude = 1f;
                        cue.Seconds = 0.9f;
                        cue.Accent = 1f;
                        cue.Haptic = 0.8f;
                        particles = 12;
                    }
                    else
                    {
                        // An attempt on something already finished: the same movement, quietly.
                        cue.Amplitude = 0.35f;
                        cue.Seconds = 0.6f;
                        cue.Accent = 0.25f;
                        cue.Haptic = 0.15f;
                        particles = 0;
                    }

                    break;

                case FeedbackCategory.Blocked:
                    cue.Motion = CueMotion.Dip;
                    cue.Amplitude = 0.3f;
                    cue.Seconds = 0.3f;
                    cue.Accent = 0.15f;
                    cue.Sound = DiscoverySound.Held;
                    cue.Haptic = 0.2f;
                    particles = 0;
                    break;

                default:
                    return cue;
            }

            if (settings != null && settings.emphasizeMotion)
            {
                cue.Amplitude = Mathf.Min(1f, cue.Amplitude * 1.5f);
            }

            cue.Amplitude = Mathf.Max(MinimumAmplitude, cue.Amplitude);

            // Particles are the one thing that costs: fewer on a weaker device, fewer still if asked.
            float share = quality == DiscoveryQuality.Low ? 0.5f : quality == DiscoveryQuality.Medium ? 0.75f : 1f;
            particles = Mathf.RoundToInt(particles * share);
            if (settings != null && settings.reducedParticles)
            {
                bool significant = result.Changed &&
                                   (result.Category == FeedbackCategory.Transition || result.Category == FeedbackCategory.Completed);
                particles = significant ? Mathf.Min(particles, 3) : 0;
            }

            cue.Particles = particles;
            return cue;
        }

        /// <summary>
        /// Where a cue's movement is at a moment of its course. The same curve on every device.
        /// </summary>
        /// <param name="progress">0 at the start of the cue, 1 at its end.</param>
        /// <param name="amplitude">The cue's amplitude, 0 to 1.</param>
        public static MotionSample Sample(CueMotion motion, float progress, float amplitude)
        {
            var sample = new MotionSample();
            float t = Mathf.Clamp01(progress);
            float fade = 1f - t;
            const float Reach = 0.08f;
            const float Growth = 0.05f;

            switch (motion)
            {
                case CueMotion.Shake:
                    sample.Across = Mathf.Sin(t * Mathf.PI * 4f) * fade * Reach * amplitude;
                    break;

                case CueMotion.Strain:
                    // Out quickly, held, eased back.
                    sample.Along = (t < 0.2f ? t / 0.2f : t < 0.6f ? 1f : (1f - t) / 0.4f) * Reach * amplitude;
                    break;

                case CueMotion.Swell:
                    sample.Scale = Mathf.Sin(t * Mathf.PI) * Growth * amplitude;
                    break;

                case CueMotion.Recoil:
                    sample.Along = Mathf.Sin(t * Mathf.PI * 3f) * fade * fade * Reach * 1.5f * amplitude;
                    sample.Scale = Mathf.Sin(t * Mathf.PI) * Growth * 0.5f * amplitude;
                    break;

                case CueMotion.Settle:
                    sample.Scale = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * (t < 0.5f ? 1f : 0.5f) * Growth * amplitude;
                    break;

                case CueMotion.Dip:
                    sample.Scale = -Mathf.Sin(t * Mathf.PI) * Growth * 0.6f * amplitude;
                    break;
            }

            return sample;
        }

        /// <summary>The slow breathing of an ambient clue or of resonance: a change of size, and the strength of its glow.</summary>
        /// <param name="time">Seconds, from any clock.</param>
        /// <param name="strength">0 to 1.</param>
        /// <param name="quick">Resonance breathes faster than an idle clue.</param>
        public static float Breath(float time, float strength, bool quick)
        {
            // Half-cycles per second: an idle clue breathes about every two seconds, resonance in under one.
            float rate = quick ? 2.4f : 0.9f;
            return (0.5f + 0.5f * Mathf.Sin(time * rate * Mathf.PI)) * Mathf.Clamp01(strength);
        }
    }

    /// <summary>
    /// The optional rule that strengthens an object's own clues when the player lingers near it
    /// without resolving it. It only ever multiplies what the object already shows, so it cannot
    /// name a spell. It waits before it starts, rests between rises, stays out of the way during
    /// combat, and stops the moment the player tries something. Plain C#.
    /// </summary>
    public static class AttentionRules
    {
        /// <param name="near">Whether the player is near the object.</param>
        /// <param name="resolved">Whether the object has nothing left to discover.</param>
        /// <param name="inCombat">Whether the player is fighting.</param>
        /// <param name="attempted">Whether the player tried anything on the object since the last step.</param>
        /// <param name="progressed">Whether an attempt took effect.</param>
        /// <returns>How much the object's clues are strengthened, 0 to the maximum boost.</returns>
        public static float Advance(ref AttentionState state, bool near, bool resolved, bool inCombat, bool attempted,
            bool progressed, AttentionSettings settings, float deltaTime)
        {
            if (settings == null || !settings.enabled || resolved)
            {
                state = default;
                return 0f;
            }

            float fall = deltaTime / 1.5f;

            if (progressed)
            {
                // It worked: the hint has done its job, or was never needed.
                state = default;
                return 0f;
            }

            if (attempted)
            {
                // The player is experimenting. That is the point; the clock starts again and nothing nags.
                state.Lingered = 0f;
            }

            if (inCombat)
            {
                // Time spent fighting next to something is not time spent puzzling over it.
                state.Emphasis = Mathf.Max(0f, state.Emphasis - fall);
                return state.Emphasis * settings.maxBoost;
            }

            if (!near)
            {
                state.Lingered = Mathf.Max(0f, state.Lingered - deltaTime * 0.5f);
                state.Emphasis = Mathf.Max(0f, state.Emphasis - fall);
                return state.Emphasis * settings.maxBoost;
            }

            state.Lingered += deltaTime;

            float target = 0f;
            float since = state.Lingered - settings.delaySeconds;
            if (since >= 0f)
            {
                float cycle = settings.rampSeconds + settings.holdSeconds + settings.restSeconds;
                float phase = cycle > 0f ? since % cycle : 0f;
                target = phase < settings.rampSeconds
                    ? phase / settings.rampSeconds
                    : phase < settings.rampSeconds + settings.holdSeconds ? 1f : 0f;
            }

            state.Emphasis = target > state.Emphasis
                ? Mathf.Min(target, state.Emphasis + deltaTime / Mathf.Max(0.5f, settings.rampSeconds))
                : Mathf.Max(target, state.Emphasis - fall);
            return state.Emphasis * settings.maxBoost;
        }

        /// <summary>
        /// How much wider an unstable object sways under emphasis: 1 is as authored. The player's clue
        /// intensity applies to it as to every clue, so switching clues off switches this off too.
        /// </summary>
        public static float SwayScale(float boost, DiscoverySettings settings)
        {
            float intensity = settings != null ? Mathf.Clamp01(settings.clueIntensity) : 1f;
            return 1f + Mathf.Max(0f, boost) * intensity;
        }

        /// <summary>The strength an ambient clue is shown with: its base, the player's setting, and any emphasis.</summary>
        public static float ClueStrength(float baseStrength, float boost, DiscoverySettings settings)
        {
            float intensity = settings != null ? settings.clueIntensity : 1f;
            return Mathf.Clamp01(baseStrength * intensity * (1f + Mathf.Max(0f, boost)));
        }
    }

    /// <summary>The game's quality levels, as discovery sees them.</summary>
    public static class DiscoveryQualityRules
    {
        /// <summary>
        /// Maps a level of the game's own quality setting to a tier: the lowest level is Low, the
        /// highest High, anything between Medium. With a single level, or none, it is Medium.
        /// </summary>
        public static DiscoveryQuality FromLevel(int level, int levelCount)
        {
            if (levelCount <= 1)
            {
                return DiscoveryQuality.Medium;
            }

            return level <= 0 ? DiscoveryQuality.Low : level >= levelCount - 1 ? DiscoveryQuality.High : DiscoveryQuality.Medium;
        }
    }

    /// <summary>
    /// Keeps the nearest few of whatever it is offered, nearest first. Candidates can be offered a
    /// few at a time, over several frames, so that looking through a whole level costs the same per
    /// frame however large the level is; and however many are offered, only this many are kept and
    /// attended to. Plain C#, no allocation after it is made.
    /// </summary>
    public sealed class NearestFew<T> where T : class
    {
        private readonly T[] items;
        private readonly float[] distances;

        public NearestFew(int capacity)
        {
            items = new T[Mathf.Max(1, capacity)];
            distances = new float[items.Length];
        }

        public int Capacity => items.Length;

        public int Count { get; private set; }

        public T this[int index] => items[index];

        public float DistanceAt(int index)
        {
            return distances[index];
        }

        public void Clear()
        {
            Array.Clear(items, 0, Count);
            Count = 0;
        }

        /// <summary>
        /// Offers a candidate with its (squared) distance. It is kept if it is among the nearest so far.
        /// One that is already kept is not taken twice. Equal distances keep the order offered.
        /// </summary>
        /// <param name="limit">Keep at most this many, if smaller than the capacity.</param>
        public void Offer(T item, float sqrDistance, int limit = int.MaxValue)
        {
            if (item == null)
            {
                return;
            }

            int room = Mathf.Clamp(limit, 1, items.Length);
            for (int i = 0; i < Count; i++)
            {
                if (ReferenceEquals(items[i], item))
                {
                    return;
                }
            }

            int position = Count;
            while (position > 0 && distances[position - 1] > sqrDistance)
            {
                position--;
            }

            if (position >= room)
            {
                return;
            }

            int last = Mathf.Min(Count, room - 1);
            for (int i = last; i > position; i--)
            {
                items[i] = items[i - 1];
                distances[i] = distances[i - 1];
            }

            items[position] = item;
            distances[position] = sqrDistance;
            if (Count < room)
            {
                Count++;
            }
        }

        public bool Contains(T item)
        {
            for (int i = 0; i < Count; i++)
            {
                if (ReferenceEquals(items[i], item))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
