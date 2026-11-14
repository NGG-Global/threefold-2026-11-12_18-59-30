using System;
using System.Collections.Generic;
using Threefold.Combat;
using Threefold.Magic;
using Threefold.Pooling;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.Discovery
{
    /// <summary>
    /// Shows how the world hints at its magic and answers what the player tries. It is the one
    /// component of the discovery system with an update, and the work it does per frame is for a
    /// handful of objects near the player, however many there are in the level:
    ///
    /// - It looks through the list of objects that carry clues, a fixed number per frame, and keeps
    ///   the nearest few around the player. Nothing is asked of physics.
    /// - For those it shows their own clue (level 1), strengthened a little if the player has
    ///   lingered without trying anything, and resonance (level 2) if the selected discipline
    ///   could change the object as it is now.
    /// - When an object reports what an attempt came to (level 3), it plays the answer: a movement
    ///   particular to that kind of answer, with an accent, a few motes, a sound and a haptic pulse
    ///   as supplements.
    ///
    /// It reads the state of objects and never changes it. Everything it decides is in plain C#
    /// (see DiscoveryRules.cs); this component looks things up and moves them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiscoveryDirector : MonoBehaviour
    {
        private const int MaxTrackedLimit = 16;
        private const int MaxAnswers = 6;
        private const float BreathScale = 0.012f;
        private const float ResonanceThreshold = 0.3f;

        private static DiscoveryDirector instance;

        [Header("Player")]
        [SerializeField] private Transform player;

        [Tooltip("The camera, for telling what the player is looking at.")]
        [SerializeField] private Transform view;

        [SerializeField] private PlayerDisciplines disciplines;
        [SerializeField] private DisciplineCombat combat;
        [SerializeField] private PlayerGuard guard;

        [Header("Options")]
        [SerializeField] private DiscoverySettings settings = new DiscoverySettings();
        [SerializeField] private ResonanceSettings resonance = new ResonanceSettings();
        [SerializeField] private AttentionSettings attention = new AttentionSettings();

        [Tooltip("Optional. The game's quality setting. With it, the richness of answers follows the chosen level: the " +
                 "lowest is Low, the highest High.")]
        [SerializeField] private VisualStyleController style;

        [Tooltip("The richness of answers when there is no quality setting to follow.")]
        [SerializeField] private DiscoveryQuality quality = DiscoveryQuality.Medium;

        [Header("Nearby objects")]
        [Tooltip("How far around the player objects with clues are attended to.")]
        [Min(1f)]
        [SerializeField] private float scanRadius = 12f;

        [Tooltip("Seconds between one look through the level's objects and the next.")]
        [Min(0.05f)]
        [SerializeField] private float scanInterval = 0.25f;

        [Tooltip("How many objects are looked at per frame during a look. A large level takes several frames; the cost per " +
                 "frame stays the same.")]
        [Range(8, 512)]
        [SerializeField] private int scanBudget = 64;

        [Tooltip("How many objects are attended to at once: the nearest. Clues and resonance are shown for these only.")]
        [Range(1, MaxTrackedLimit)]
        [SerializeField] private int maxTracked = 8;

        [Header("Answers")]
        [Tooltip("The same small answer for the same kind of effect is not repeated within this many seconds.")]
        [Min(0f)]
        [SerializeField] private float repeatQuietSeconds = 1.5f;

        [Tooltip("The kinds of effect that are answered although nothing changed (incompatible, insufficient, blocked, " +
                 "already finished). Stabilise is left out by default: a guard reaches everything nearby several times a " +
                 "second, and things it does nothing to should stay still.")]
        [SerializeField] private EffectKinds unchangedAnswersFor = EffectKinds.Heat | EffectKinds.ArcaneCharge | EffectKinds.Force;

        [Header("Hooks")]
        [SerializeField] private UnityEvent onResonance = new UnityEvent();
        [SerializeField] private UnityEvent onAnswer = new UnityEvent();

        private readonly NearestFew<DiscoveryClues> nearest = new NearestFew<DiscoveryClues>(MaxTrackedLimit);
        private Vector3 sweepOrigin;
        private int sweepIndex;
        private bool sweeping;

        private readonly DiscoveryClues[] tracked = new DiscoveryClues[MaxTrackedLimit];
        private readonly float[] shown = new float[MaxTrackedLimit];
        private readonly AttentionState[] attentionStates = new AttentionState[MaxTrackedLimit];
        private readonly DiscoveryClues[] previousTracked = new DiscoveryClues[MaxTrackedLimit];
        private readonly float[] previousShown = new float[MaxTrackedLimit];
        private readonly Color[] shownColour = new Color[MaxTrackedLimit];
        private readonly Color[] previousShownColour = new Color[MaxTrackedLimit];
        private readonly AttentionState[] previousAttention = new AttentionState[MaxTrackedLimit];
        private int trackedCount;

        private readonly PlayingAnswer[] answers = new PlayingAnswer[MaxAnswers];
        private int answerCount;

        private readonly EffectKinds[] kindsBySlot = new EffectKinds[8];
        private readonly bool[] kindsKnown = new bool[8];
        private float scanTimer;
        private float lastCombatTime = float.NegativeInfinity;

        private struct PlayingAnswer
        {
            public DiscoveryClues Clues;
            public FeedbackCue Cue;
            public Vector3 Along;
            public Vector3 Across;
            public float Elapsed;
        }

        /// <summary>Raised when an object's resonance becomes noticeable: the moment for a sound, if any.</summary>
        public event Action<DiscoveryClues, float> ResonanceBegan;

        /// <summary>Raised when an answer starts to play, with what it is and how it is shown.</summary>
        public event Action<DiscoveryClues, FeedbackResult, FeedbackCue> AnswerPresented;

        /// <summary>
        /// Raised once per frame with the real time that passed, for the optional hooks that have
        /// something to time (a haptic pulse to end). They subscribe only while they need it, so that
        /// this stays the one update of the discovery system.
        /// </summary>
        public event Action<float> Stepped;

        /// <summary>The director in the scene, or null.</summary>
        public static DiscoveryDirector Existing => instance;

        /// <summary>The player's options. Changes apply at once.</summary>
        public DiscoverySettings Settings => settings;

        public ResonanceSettings Resonance => resonance;
        public AttentionSettings Attention => attention;
        public int TrackedCount => trackedCount;
        public int MaxTracked => maxTracked;
        public Transform Player => player;
        public PlayerDisciplines Disciplines => disciplines;

        /// <summary>How rich answers are: from the game's quality setting if there is one, otherwise as set here.</summary>
        public DiscoveryQuality Quality => style != null && style.QualityLevelCount > 0
            ? DiscoveryQualityRules.FromLevel(style.QualityLevel, style.QualityLevelCount)
            : quality;

        public VisualStyleController Style => style;

        /// <summary>Connects the game's quality setting if none is connected. Returns true if it changed.</summary>
        public bool FillStyle(VisualStyleController controller)
        {
            if (style != null || controller == null)
            {
                return false;
            }

            style = controller;
            return true;
        }

        /// <summary>True if the given source of an effect is the player this director attends to.</summary>
        internal static bool IsPlayer(GameObject source)
        {
            return instance != null && instance.player != null && source == instance.player.gameObject;
        }

        /// <summary>Connects the director. Called by the setup tooling.</summary>
        public void Bind(Transform playerTransform, Transform viewTransform, PlayerDisciplines playerDisciplines,
            DisciplineCombat disciplineCombat, PlayerGuard playerGuard)
        {
            player = playerTransform;
            view = viewTransform;
            disciplines = playerDisciplines;
            combat = disciplineCombat;
            guard = playerGuard;
        }

        /// <summary>Fills in the connections that are still empty. Returns true if anything changed. Called by the setup tooling.</summary>
        public bool FillReferences(Transform playerTransform, Transform viewTransform, PlayerDisciplines playerDisciplines,
            DisciplineCombat disciplineCombat, PlayerGuard playerGuard)
        {
            bool changed = false;
            if (player == null && playerTransform != null) { player = playerTransform; changed = true; }
            if (view == null && viewTransform != null) { view = viewTransform; changed = true; }
            if (disciplines == null && playerDisciplines != null) { disciplines = playerDisciplines; changed = true; }
            if (combat == null && disciplineCombat != null) { combat = disciplineCombat; changed = true; }
            if (guard == null && playerGuard != null) { guard = playerGuard; changed = true; }
            return changed;
        }

        public Transform View => view;
        public DisciplineCombat Combat => combat;

        /// <summary>
        /// An object reports what an attempt on it came to. Called by <see cref="DiscoveryClues"/>.
        /// Works at any distance: an arrow that strikes a crystal across the hall is answered too.
        /// </summary>
        internal static void Answer(DiscoveryClues clues, in FeedbackResult result, in MagicEffect effect)
        {
            if (instance != null && instance.isActiveAndEnabled)
            {
                instance.Present(clues, result, effect);
            }
        }

        /// <summary>An object is switched off or gone: nothing more is shown on it.</summary>
        internal static void Forget(DiscoveryClues clues)
        {
            if (instance == null)
            {
                return;
            }

            for (int i = 0; i < instance.trackedCount; i++)
            {
                if (instance.tracked[i] == clues)
                {
                    instance.tracked[i] = null;
                }
            }

            for (int i = 0; i < instance.answerCount; i++)
            {
                if (instance.answers[i].Clues == clues)
                {
                    instance.answers[i].Clues = null;
                }
            }
        }

        private void Awake()
        {
            instance = this;
        }

        private void OnEnable()
        {
            instance = this;
            CombatEvents.DamageDealt += HandleDamage;
            if (disciplines != null)
            {
                disciplines.Changed += HandleDisciplineChanged;
            }
        }

        private void OnDisable()
        {
            CombatEvents.DamageDealt -= HandleDamage;
            if (disciplines != null)
            {
                disciplines.Changed -= HandleDisciplineChanged;
            }

            for (int i = 0; i < trackedCount; i++)
            {
                if (tracked[i] != null)
                {
                    tracked[i].ClearPresentation();
                }

                tracked[i] = null;
            }

            for (int i = 0; i < answerCount; i++)
            {
                if (answers[i].Clues != null)
                {
                    answers[i].Clues.ClearPresentation();
                }

                answers[i] = default;
            }

            trackedCount = 0;
            answerCount = 0;

            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            // The particle options that concern the objects' own effects are passed on to them: no
            // repeating embers with reduced particles, and fewer particles in a burst at a change of state.
            PropertyStateVisuals.ReducedParticles = settings.reducedParticles;
            PropertyStateVisuals.BurstShare = FeedbackPresentation.StateBurstShare(Quality, settings);

            float deltaTime = Time.deltaTime;
            scanTimer -= deltaTime;
            Sweep();

            ShowTracked(deltaTime);
            AdvanceAnswers(deltaTime);
            Stepped?.Invoke(Time.unscaledDeltaTime);
        }

        // ---------------------------------------------------------- who is near

        /// <summary>
        /// Looks through the objects that carry clues, a fixed number per frame, keeping the nearest
        /// few. When it has been through all of them, those become the ones attended to, and after a
        /// pause it starts again. No physics is involved, so what else stands around does not matter.
        /// </summary>
        private void Sweep()
        {
            List<DiscoveryClues> all = DiscoveryClues.Active;

            if (!sweeping)
            {
                if (scanTimer > 0f)
                {
                    return;
                }

                sweeping = true;
                sweepIndex = 0;
                sweepOrigin = player.position;
                nearest.Clear();
            }

            int limit = Mathf.Clamp(maxTracked, 1, MaxTrackedLimit);
            float reach = scanRadius * scanRadius;
            int budget = scanBudget;

            while (budget-- > 0 && sweepIndex < all.Count)
            {
                DiscoveryClues clues = all[sweepIndex++];
                if (clues == null || clues.Source == null)
                {
                    continue;
                }

                float distance = (clues.Position - sweepOrigin).sqrMagnitude;
                if (distance <= reach)
                {
                    nearest.Offer(clues, distance, limit);
                }
            }

            if (sweepIndex >= all.Count)
            {
                sweeping = false;
                scanTimer = scanInterval;
                Commit();
            }
        }

        /// <summary>The nearest few found by a look become the ones attended to. What was known about those that stay is kept.</summary>
        private void Commit()
        {
            Array.Copy(tracked, previousTracked, trackedCount);
            Array.Copy(shown, previousShown, trackedCount);
            Array.Copy(shownColour, previousShownColour, trackedCount);
            Array.Copy(attentionStates, previousAttention, trackedCount);
            int previousCount = trackedCount;

            int selected = 0;
            for (int i = 0; i < nearest.Count; i++)
            {
                DiscoveryClues clues = nearest[i];
                if (clues == null || !clues.isActiveAndEnabled)
                {
                    continue;
                }

                tracked[selected] = clues;
                int before = Array.IndexOf(previousTracked, clues, 0, previousCount);
                shown[selected] = before >= 0 ? previousShown[before] : 0f;
                shownColour[selected] = before >= 0 ? previousShownColour[before] : default;
                attentionStates[selected] = before >= 0 ? previousAttention[before] : default;
                selected++;
            }

            for (int i = selected; i < MaxTrackedLimit; i++)
            {
                tracked[i] = null;
            }

            trackedCount = selected;
            nearest.Clear();

            for (int i = 0; i < previousCount; i++)
            {
                DiscoveryClues left = previousTracked[i];
                previousTracked[i] = null;

                // One that is still playing an answer is tidied up when the answer ends.
                if (left != null && !IsTracked(left) && !IsAnswering(left))
                {
                    left.ClearPresentation();
                }
            }
        }

        private bool IsTracked(DiscoveryClues clues)
        {
            return Array.IndexOf(tracked, clues, 0, trackedCount) >= 0;
        }

        /// <summary>
        /// Ends what is shown on an object after an answer. If the object is among those attended to,
        /// its clue and resonance take over again; if not, everything shown on it is removed, so that
        /// nothing is left behind on an object nobody is attending to.
        /// </summary>
        private void Release(DiscoveryClues clues)
        {
            if (clues == null)
            {
                return;
            }

            if (IsTracked(clues))
            {
                clues.ShowPose(Vector3.zero, 0f);
                clues.ShowAccent(clues.AccentColor, 0f);
            }
            else
            {
                clues.ClearPresentation();
            }
        }

        // ------------------------------------------------- clues and resonance

        private void ShowTracked(float deltaTime)
        {
            if (trackedCount == 0)
            {
                return;
            }

            Vector3 origin = player.position;
            EffectKinds offered = OfferedKinds();
            bool preparing = (combat != null && combat.IsCharging) || (guard != null && guard.IsBlocking);
            bool inCombat = Time.time - lastCombatTime < attention.combatQuietSeconds;
            int aimed = FindAimed();
            Color disciplineColor = disciplines != null && disciplines.CurrentDefinition != null
                ? disciplines.CurrentDefinition.Color
                : Color.white;
            float time = Time.time;

            for (int i = 0; i < trackedCount; i++)
            {
                DiscoveryClues clues = tracked[i];
                if (clues == null || clues.Source == null || clues.Source.Properties == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, clues.Position);
                bool resolved = clues.IsResolved;

                // Level 2. Zero unless something the selected discipline carries could change the object now.
                bool compatible = Compatibility.Matching(clues.Source.Properties, offered) != EffectKinds.None;
                float target = ResonanceRules.Strength(compatible, distance, i == aimed, preparing, resonance, settings);
                float before = shown[i];
                shown[i] = ResonanceRules.Approach(before, target, resonance.fadeSeconds, deltaTime);
                shownColour[i] = ResonanceRules.ColourToShow(shownColour[i], disciplineColor, target);
                if (before < ResonanceThreshold && shown[i] >= ResonanceThreshold)
                {
                    clues.RaiseResonance();
                    onResonance.Invoke();
                    ResonanceBegan?.Invoke(clues, shown[i]);
                }

                // Level 1, with its emphasis. The emphasis multiplies the object's own clue and nothing else.
                bool tried = clues.ConsumeAttempt(out bool tookEffect);
                float boost = AttentionRules.Advance(ref attentionStates[i], distance <= attention.nearRadius, resolved, inCombat,
                    tried, tookEffect, attention, deltaTime);
                float ambient = resolved ? 0f : AttentionRules.ClueStrength(clues.AmbientStrength, boost, settings);

                if (IsAnswering(clues))
                {
                    // An answer is playing on it; that has the stage.
                    continue;
                }

                float ambientGlow = FeedbackPresentation.Breath(time, ambient, false);
                float resonanceGlow = FeedbackPresentation.Breath(time, shown[i], true);
                if (resonanceGlow >= ambientGlow)
                {
                    clues.ShowAccent(shownColour[i], resonanceGlow);
                }
                else
                {
                    clues.ShowAccent(clues.AccentColor, ambientGlow);
                }

                // The breath is also a slight change of size, so that it does not depend on colour.
                float breath = resonanceGlow * BreathScale * (settings.emphasizeMotion ? 2f : 1f);
                clues.ShowPose(Vector3.zero, breath);

                if (clues.Visuals != null)
                {
                    clues.Visuals.SetSwayScale(AttentionRules.SwayScale(boost, settings));
                }
            }
        }

        /// <summary>The tracked object nearest to the centre of the view, within the aiming angle, or -1.</summary>
        private int FindAimed()
        {
            if (view == null)
            {
                return -1;
            }

            Vector3 eye = view.position;
            Vector3 forward = view.forward;
            float best = Mathf.Cos(resonance.aimAngleDegrees * Mathf.Deg2Rad);
            int index = -1;

            for (int i = 0; i < trackedCount; i++)
            {
                DiscoveryClues clues = tracked[i];
                if (clues == null)
                {
                    continue;
                }

                Vector3 to = clues.Position - eye;
                float length = to.magnitude;
                if (length < 0.01f)
                {
                    continue;
                }

                float alignment = Vector3.Dot(forward, to) / length;
                if (alignment > best)
                {
                    best = alignment;
                    index = i;
                }
            }

            return index;
        }

        /// <summary>What the selected discipline can deliver, from its present values. Worked out once per discipline.</summary>
        private EffectKinds OfferedKinds()
        {
            if (disciplines == null)
            {
                return EffectKinds.None;
            }

            int slot = disciplines.CurrentSlot;
            if (slot < 0 || slot >= kindsBySlot.Length)
            {
                return EffectKinds.None;
            }

            if (!kindsKnown[slot])
            {
                DisciplineDefinition definition = disciplines.GetDefinition(slot);
                EffectPayload charged = default;
                if (definition != null && definition.ChargedAbility is ChargedProjectileDefinition projectile)
                {
                    charged = projectile.Projectile.Effects(1f);
                }
                else if (definition != null && definition.ChargedAbility is ChargedAreaDefinition area)
                {
                    charged = area.Area.Effects(1f);
                }

                kindsBySlot[slot] = definition != null
                    ? Compatibility.OfDiscipline(definition.Melee, charged, definition.GuardEffect)
                    : EffectKinds.None;
                kindsKnown[slot] = true;
            }

            return kindsBySlot[slot];
        }

        private void HandleDisciplineChanged(MagicDiscipline discipline)
        {
            // Values may have been retuned since the last time; they are read again when next needed.
            Array.Clear(kindsKnown, 0, kindsKnown.Length);
        }

        private void HandleDamage(DamageEvent damage)
        {
            if (player == null)
            {
                return;
            }

            GameObject self = player.gameObject;
            if (damage.Source == self || damage.Target == self)
            {
                lastCombatTime = Time.time;
            }
        }

        // -------------------------------------------------------------- answers

        private void Present(DiscoveryClues clues, in FeedbackResult result, in MagicEffect effect)
        {
            if (clues == null || !FeedbackGate.Allow(ref clues.Gate, result, Time.time, repeatQuietSeconds, unchangedAnswersFor))
            {
                return;
            }

            FeedbackCue cue = FeedbackPresentation.Plan(result, settings, Quality);
            if (cue.Motion == CueMotion.None)
            {
                return;
            }

            Vector3 along = effect.Direction;
            along.y = 0f;
            if (along.sqrMagnitude < 0.0001f && player != null)
            {
                // No direction of its own: away from the player.
                along = clues.Position - player.position;
                along.y = 0f;
            }

            along = along.sqrMagnitude > 0.0001f ? along.normalized : Vector3.forward;

            int slot = FindAnswerSlot(clues);
            answers[slot] = new PlayingAnswer
            {
                Clues = clues, Cue = cue, Along = along, Across = Vector3.Cross(Vector3.up, along), Elapsed = 0f,
            };

            // Motes only for the small answers, and only where the plan allows at least a couple: not on
            // the lowest quality level, not with reduced particles. Changes of state have their own
            // bursts from the object's visuals.
            bool small = result.Category == FeedbackCategory.Insufficient || result.Category == FeedbackCategory.Progress;
            if (cue.Particles >= FeedbackPresentation.MoteThreshold && small && clues.SmallBurst != null)
            {
                Pools.Spawn(clues.SmallBurst, effect.Point.sqrMagnitude > 0.0001f ? effect.Point : clues.Position, Quaternion.identity,
                    MaxAnswers);
            }

            clues.RaiseAnswer();
            onAnswer.Invoke();
            AnswerPresented?.Invoke(clues, result, cue);
        }

        /// <summary>The slot an answer for this object goes into: its own if one is playing, a free one, or the oldest.</summary>
        private int FindAnswerSlot(DiscoveryClues clues)
        {
            int oldest = 0;
            float oldestShare = -1f;
            for (int i = 0; i < answerCount; i++)
            {
                if (answers[i].Clues == clues)
                {
                    return i;
                }

                float share = answers[i].Cue.Seconds > 0f ? answers[i].Elapsed / answers[i].Cue.Seconds : 1f;
                if (share > oldestShare)
                {
                    oldestShare = share;
                    oldest = i;
                }
            }

            if (answerCount < MaxAnswers)
            {
                return answerCount++;
            }

            // The answer that gives way is ended properly, not left half shown.
            DiscoveryClues replaced = answers[oldest].Clues;
            answers[oldest].Clues = null;
            Release(replaced);
            return oldest;
        }

        private bool IsAnswering(DiscoveryClues clues)
        {
            for (int i = 0; i < answerCount; i++)
            {
                if (answers[i].Clues == clues)
                {
                    return true;
                }
            }

            return false;
        }

        private void AdvanceAnswers(float deltaTime)
        {
            for (int i = answerCount - 1; i >= 0; i--)
            {
                DiscoveryClues clues = answers[i].Clues;
                answers[i].Elapsed += deltaTime;
                float seconds = Mathf.Max(0.05f, answers[i].Cue.Seconds);
                float progress = answers[i].Elapsed / seconds;

                if (clues == null || progress >= 1f)
                {
                    // Close the gap first, so the object no longer counts as answering when it is released.
                    answers[i] = answers[answerCount - 1];
                    answers[answerCount - 1] = default;
                    answerCount--;
                    Release(clues);
                    continue;
                }

                MotionSample sample = FeedbackPresentation.Sample(answers[i].Cue.Motion, progress, answers[i].Cue.Amplitude);
                clues.ShowPose(answers[i].Along * sample.Along + answers[i].Across * sample.Across, sample.Scale);
                clues.ShowAccent(clues.AccentColor, answers[i].Cue.Accent * (1f - progress));
            }
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }
    }
}
