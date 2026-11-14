using System;
using System.Collections.Generic;
using Threefold.Pooling;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.Discovery
{
    /// <summary>
    /// What one magical object contributes to discovery: the part that makes its small movements,
    /// how strongly its own clue shows when nothing is happening, and when there is nothing left
    /// to find out about it. It listens to its object's events and works out what each attempt came
    /// to (<see cref="ExperimentFeedback"/>); the <see cref="DiscoveryDirector"/> decides what is
    /// shown and animates it.
    ///
    /// It has no update and changes no rule. Without a director in the scene it does nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiscoveryClues : MonoBehaviour
    {
        [Tooltip("The object whose properties are hinted at. The MagicalObject on this object if empty.")]
        [SerializeField] private MagicalObject source;

        [Tooltip("Optional. Colour accents go through it, so the object's state colours and its clues do not fight.")]
        [SerializeField] private PropertyStateVisuals visuals;

        [Header("Movement")]
        [Tooltip("The part that makes the small movements of clues and answers. It must not be a part that something else " +
                 "moves or resizes: use the visible mesh group, not the sliding block or the door leaf itself.")]
        [SerializeField] private Transform motionTarget;

        [Tooltip("Optional. The part that moves instead once the object is destroyed: its remains. The usual part is " +
                 "hidden then, and the answer to the blow that destroyed it would not be seen.")]
        [SerializeField] private Transform motionTargetWhenDestroyed;

        [Header("Level 1: its own clue")]
        [Tooltip("How strongly its clue shows when nothing is happening: a slow, faint breathing of its accent. Keep it low; " +
                 "for objects whose clue is a built feature (a rail, straw, vents) a trace is enough.")]
        [Range(0f, 1f)]
        [SerializeField] private float ambientStrength = 0.2f;

        [Tooltip("The colour of that breathing. A pale tint of the object's own colour reads as part of it.")]
        [SerializeField] private Color accentColor = new Color(0.8f, 0.84f, 1f, 1f);

        [Tooltip("There is nothing left to find out about the object while any of these holds: no clue, no emphasis.")]
        [SerializeField] private PropertyCondition resolvedWhen =
            PropertyCondition.Destroyed | PropertyCondition.Collapsed | PropertyCondition.Activated;

        [Header("Level 3: answers")]
        [Tooltip("Optional. A few motes for a small answer (strain, progress).")]
        [SerializeField] private PooledObject smallBurst;

        [Header("Hooks")]
        [SerializeField] private UnityEvent onResonance = new UnityEvent();
        [SerializeField] private UnityEvent onAnswer = new UnityEvent();

        // Every clue component that is switched on. The director looks through this list a few at a
        // time; nothing is asked of physics, and no object does anything by itself.
        private static readonly List<DiscoveryClues> ActiveClues = new List<DiscoveryClues>(64);

        private MagicalObject subscribed;
        private Transform posedTarget;
        private Vector3 restPosition;
        private Vector3 restScale = Vector3.one;
        private int registryIndex = -1;
        private bool significant;
        private bool attempted;
        private bool progressed;
        private bool posed;
        private FeedbackGateState gate;

        public MagicalObject Source => source;
        public PropertyStateVisuals Visuals => visuals;
        public Transform MotionTarget => motionTarget;
        public Transform MotionTargetWhenDestroyed => motionTargetWhenDestroyed;

        /// <summary>Every clue component that is switched on, in no particular order.</summary>
        internal static List<DiscoveryClues> Active => ActiveClues;
        public float AmbientStrength => ambientStrength;
        public Color AccentColor => accentColor;
        public PooledObject SmallBurst => smallBurst;

        /// <summary>Where the object is, for distance and aim.</summary>
        public Vector3 Position => source != null ? source.AimPoint : transform.position;

        /// <summary>True while there is nothing left to find out about the object.</summary>
        public bool IsResolved => source == null || source.Meets(resolvedWhen);

        /// <summary>The clues its properties call for.</summary>
        public ClueKinds Clues => source != null && source.Definition != null
            ? Compatibility.CluesFor(source.Definition.Properties)
            : ClueKinds.None;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(MagicalObject magicalObject, PropertyStateVisuals stateVisuals, Transform moving)
        {
            source = magicalObject;
            visuals = stateVisuals;
            motionTarget = moving;
        }

        /// <summary>Sets the part that moves once the object is destroyed, if none is set. Returns true if it changed.</summary>
        public bool FillDestroyedMotionTarget(Transform remains)
        {
            if (motionTargetWhenDestroyed != null || remains == null)
            {
                return false;
            }

            motionTargetWhenDestroyed = remains;
            return true;
        }

        /// <summary>Sets how its own clue shows. Called by the setup tooling.</summary>
        public void Configure(float idleStrength, Color accent, PropertyCondition resolved, PooledObject motes)
        {
            ambientStrength = Mathf.Clamp01(idleStrength);
            accentColor = accent;
            resolvedWhen = resolved;
            smallBurst = motes;
        }

        /// <summary>The gate that keeps the same small answer from repeating. The director passes it on.</summary>
        internal ref FeedbackGateState Gate => ref gate;

        /// <summary>Whether the player tried anything on the object since the last time this was asked, and whether it took.</summary>
        internal bool ConsumeAttempt(out bool tookEffect)
        {
            bool tried = attempted;
            tookEffect = progressed;
            attempted = false;
            progressed = false;
            return tried;
        }

        /// <summary>Shows the colour accent of a clue or of resonance. 0 removes it.</summary>
        internal void ShowAccent(Color color, float strength)
        {
            if (visuals != null)
            {
                visuals.SetAccent(color, strength);
            }
        }

        /// <summary>Places the moving part: a movement of an answer, and the slight change of size of a breath.</summary>
        internal void ShowPose(Vector3 worldOffset, float scale)
        {
            bool atRest = worldOffset.sqrMagnitude < 0.0000001f && Mathf.Abs(scale) < 0.00005f;
            if (atRest && !posed)
            {
                return;
            }

            // What is left of a destroyed object moves in its place: the usual part is hidden by then.
            Transform target = motionTargetWhenDestroyed != null && source != null && source.IsDestroyed
                ? motionTargetWhenDestroyed
                : motionTarget;

            if (target != posedTarget)
            {
                RestorePose();
                posedTarget = target;
                if (target != null)
                {
                    restPosition = target.localPosition;
                    restScale = target.localScale;
                }
            }

            if (target == null)
            {
                return;
            }

            Transform parent = target.parent;
            Vector3 offset = parent != null ? parent.InverseTransformVector(worldOffset) : worldOffset;
            target.localPosition = restPosition + offset;
            target.localScale = restScale * (1f + scale);
            posed = !atRest;
        }

        /// <summary>Puts the part that was moved back exactly where and how it was.</summary>
        private void RestorePose()
        {
            if (posedTarget != null)
            {
                posedTarget.localPosition = restPosition;
                posedTarget.localScale = restScale;
            }

            posedTarget = null;
            posed = false;
        }

        /// <summary>Removes everything the director has shown on the object.</summary>
        internal void ClearPresentation()
        {
            ShowAccent(accentColor, 0f);
            RestorePose();
            if (visuals != null)
            {
                visuals.SetSwayScale(1f);
            }
        }

        internal void RaiseResonance()
        {
            onResonance.Invoke();
        }

        internal void RaiseAnswer()
        {
            onAnswer.Invoke();
        }

        private void Awake()
        {
            if (source == null)
            {
                source = GetComponent<MagicalObject>();
            }

            if (visuals == null)
            {
                visuals = GetComponent<PropertyStateVisuals>();
            }

            subscribed = source;
            if (subscribed != null)
            {
                subscribed.StateChanged += HandleStateChanged;
                subscribed.EffectReceived += HandleEffectReceived;
            }
        }

        private void OnDestroy()
        {
            if (subscribed != null)
            {
                subscribed.StateChanged -= HandleStateChanged;
                subscribed.EffectReceived -= HandleEffectReceived;
                subscribed = null;
            }
        }

        private void OnEnable()
        {
            if (registryIndex < 0)
            {
                registryIndex = ActiveClues.Count;
                ActiveClues.Add(this);
            }
        }

        private void OnDisable()
        {
            // Out of the list by swapping the last entry into its place: no gap, no shifting.
            if (registryIndex >= 0 && registryIndex < ActiveClues.Count && ActiveClues[registryIndex] == this)
            {
                int last = ActiveClues.Count - 1;
                DiscoveryClues moved = ActiveClues[last];
                ActiveClues[registryIndex] = moved;
                moved.registryIndex = registryIndex;
                ActiveClues.RemoveAt(last);
            }

            registryIndex = -1;
            DiscoveryDirector.Forget(this);
            RestorePose();
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveClues.Clear();
        }

        // Changes announced while an effect is being applied tell whether that effect made something
        // give. Changes that come with time (a fire burning down, a brace running out) are not answers.
        private void HandleStateChanged(MagicalObject changed, WorldPropertyKind property, int from, int to)
        {
            if (changed.IsApplyingEffect)
            {
                significant |= ExperimentFeedback.IsSignificant(property, from, to);
            }
        }

        private void HandleEffectReceived(MagicalObject target, MagicEffect effect, EffectResponse response)
        {
            bool gave = significant;
            significant = false;

            if (target.Properties == null)
            {
                return;
            }

            FeedbackResult result = ExperimentFeedback.Classify(target.Properties, effect, response, gave);

            // Only what the player did counts as trying something. An enemy's stray bolt is, to the
            // player, something that happened in the world: answered like any other object's reaction,
            // which is to say only if something really changed.
            bool byPlayer = effect.Depth <= 0 && DiscoveryDirector.IsPlayer(effect.Source);
            if (byPlayer && result.Category != FeedbackCategory.None)
            {
                attempted = true;
                progressed |= response == EffectResponse.Applied;
            }

            if (ExperimentFeedback.ShouldPresent(result, byPlayer ? 0 : Mathf.Max(1, effect.Depth)))
            {
                DiscoveryDirector.Answer(this, result, effect);
            }
        }
    }
}
