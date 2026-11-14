using System;
using Threefold.Combat;
using Threefold.Magic;
using Threefold.Pooling;
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
    /// - A few times a second it asks physics for the magical objects around the player and keeps
    ///   the nearest few.
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
        private const int MaxCandidates = 32;
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

        [Tooltip("Take the richness of answers from the project's quality level: the lowest level is Low, the highest High.")]
        [SerializeField] private bool followQualityLevel = true;

        [SerializeField] private DiscoveryQuality quality = DiscoveryQuality.Medium;

        [Header("Nearby objects")]
        [Tooltip("How far around the player magical objects are looked for.")]
        [Min(1f)]
        [SerializeField] private float scanRadius = 12f;

        [Tooltip("Seconds between looks. Nothing is looked for in between.")]
        [Min(0.05f)]
        [SerializeField] private float scanInterval = 0.25f;

        [Tooltip("How many objects are attended to at once: the nearest. Clues and resonance are shown for these only.")]
        [Range(1, MaxTrackedLimit)]
        [SerializeField] private int maxTracked = 8;

        [SerializeField] private LayerMask layers = Physics.DefaultRaycastLayers;

        [Header("Answers")]
        [Tooltip("The same small answer for the same kind of effect is not repeated within this many seconds.")]
        [Min(0f)]
        [SerializeField] private float repeatQuietSeconds = 1.5f;

        [Tooltip("The kinds of effect for which 'incompatible' is shown. Stabilise is left out by default: a guard reaches " +
                 "everything nearby several times a second, and things it means nothing to should stay still.")]
        [SerializeField] private EffectKinds incompatibleAnswersFor = EffectKinds.Heat | EffectKinds.ArcaneCharge | EffectKinds.Force;

        [Header("Hooks")]
        [SerializeField] private UnityEvent onResonance = new UnityEvent();
        [SerializeField] private UnityEvent onAnswer = new UnityEvent();

        private readonly Collider[] overlaps = new Collider[64];
        private readonly DiscoveryClues[] candidates = new DiscoveryClues[MaxCandidates];
        private readonly float[] candidateDistances = new float[MaxCandidates];
        private readonly int[] chosen = new int[MaxTrackedLimit];

        private readonly DiscoveryClues[] tracked = new DiscoveryClues[MaxTrackedLimit];
        private readonly float[] shown = new float[MaxTrackedLimit];
        private readonly AttentionState[] attentionStates = new AttentionState[MaxTrackedLimit];
        private readonly DiscoveryClues[] previousTracked = new DiscoveryClues[MaxTrackedLimit];
        private readonly float[] previousShown = new float[MaxTrackedLimit];
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

        /// <summary>How rich answers are on this device.</summary>
        public DiscoveryQuality Quality
        {
            get
            {
                if (!followQualityLevel)
                {
                    return quality;
                }

                int levels = QualitySettings.names.Length;
                int level = QualitySettings.GetQualityLevel();
                return level <= 0 ? DiscoveryQuality.Low : level >= levels - 1 ? DiscoveryQuality.High : DiscoveryQuality.Medium;
            }
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

            // The one particle option that concerns the objects' own effects is passed on to them.
            PropertyStateVisuals.ReducedParticles = settings.reducedParticles;

            float deltaTime = Time.deltaTime;
            scanTimer -= deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = scanInterval;
                Scan();
            }

            ShowTracked(deltaTime);
            AdvanceAnswers(deltaTime);
        }

        // ---------------------------------------------------------- who is near

        /// <summary>One physics query: the magical objects around the player, of which the nearest few are kept.</summary>
        private void Scan()
        {
            Vector3 origin = player.position;
            int found = Physics.OverlapSphereNonAlloc(origin, scanRadius, overlaps, layers, QueryTriggerInteraction.Ignore);
            int count = 0;

            for (int i = 0; i < found; i++)
            {
                Collider other = overlaps[i];
                overlaps[i] = null;
                if (other == null || count >= MaxCandidates)
                {
                    continue;
                }

                DiscoveryClues clues = other.GetComponentInParent<DiscoveryClues>();
                if (clues == null || !clues.isActiveAndEnabled || clues.Source == null)
                {
                    continue;
                }

                float distance = (other.bounds.ClosestPoint(origin) - origin).sqrMagnitude;
                int known = Array.IndexOf(candidates, clues, 0, count);
                if (known >= 0)
                {
                    candidateDistances[known] = Mathf.Min(candidateDistances[known], distance);
                    continue;
                }

                candidates[count] = clues;
                candidateDistances[count] = distance;
                count++;
            }

            // Keep what was known about those that stay, and let go of those that left.
            Array.Copy(tracked, previousTracked, trackedCount);
            Array.Copy(shown, previousShown, trackedCount);
            Array.Copy(attentionStates, previousAttention, trackedCount);
            int previousCount = trackedCount;

            int limit = Mathf.Clamp(maxTracked, 1, MaxTrackedLimit);
            int selected = Mathf.Min(NearbySelection.Select(candidateDistances, count, chosen), limit);

            for (int i = 0; i < selected; i++)
            {
                DiscoveryClues clues = candidates[chosen[i]];
                tracked[i] = clues;
                int before = Array.IndexOf(previousTracked, clues, 0, previousCount);
                shown[i] = before >= 0 ? previousShown[before] : 0f;
                attentionStates[i] = before >= 0 ? previousAttention[before] : default;
            }

            for (int i = selected; i < MaxTrackedLimit; i++)
            {
                tracked[i] = null;
            }

            trackedCount = selected;

            for (int i = 0; i < previousCount; i++)
            {
                DiscoveryClues left = previousTracked[i];
                previousTracked[i] = null;
                if (left != null && Array.IndexOf(tracked, left, 0, trackedCount) < 0 && !IsAnswering(left))
                {
                    left.ClearPresentation();
                }
            }

            Array.Clear(candidates, 0, count);
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
                    clues.ShowAccent(disciplineColor, resonanceGlow);
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
                    clues.Visuals.SetSwayScale(1f + boost);
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
            if (clues == null || !FeedbackGate.Allow(ref clues.Gate, result, Time.time, repeatQuietSeconds, incompatibleAnswersFor))
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

            if (answers[oldest].Clues != null)
            {
                answers[oldest].Clues.ShowPose(Vector3.zero, 0f);
            }

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
                    if (clues != null)
                    {
                        clues.ShowPose(Vector3.zero, 0f);
                        clues.ShowAccent(clues.AccentColor, 0f);
                    }

                    // Close the gap; the order of the rest does not matter.
                    answers[i] = answers[answerCount - 1];
                    answers[answerCount - 1] = default;
                    answerCount--;
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
