using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Turns the state of a magical object into a puzzle element, so that everything puzzles already
    /// offer can be driven by properties without code: a door that opens while a crystal is
    /// activated, a gate that needs two things to be burning, a timer that starts when a barrier is
    /// destroyed. The element is active while any of the chosen conditions holds for the object.
    /// </summary>
    public sealed class PropertyStateTrigger : PuzzleElement
    {
        [Header("Property trigger")]
        [Tooltip("The object whose properties are watched. The MagicalObject on this object if empty.")]
        [SerializeField] private MagicalObject source;

        [Tooltip("Active while any of these holds for the object.")]
        [SerializeField] private PropertyCondition activeWhen = PropertyCondition.Activated;

        [Tooltip("Once active, stay active even if the condition ends: a lock that stays open.")]
        [SerializeField] private bool latch;

        private MagicalObject subscribed;
        private bool started;

        public MagicalObject Source => source;
        public PropertyCondition ActiveWhen => activeWhen;

        /// <summary>A trigger follows its object; only a latched one is worth remembering.</summary>
        protected override bool PersistsUnlockedState => latch;

        /// <summary>Connects the trigger. Called by the setup tooling.</summary>
        public void Configure(MagicalObject watched, PropertyCondition conditions, bool stayActive)
        {
            source = watched;
            activeWhen = conditions;
            latch = stayActive;
        }

        /// <summary>Whether the trigger should be active for an object in the given state. The rule, apart from the component.</summary>
        public static bool Decide(bool conditionHolds, bool isActive, bool latched)
        {
            return conditionHolds || (latched && isActive);
        }

        protected override void Awake()
        {
            base.Awake();
            if (source == null)
            {
                source = GetComponent<MagicalObject>();
            }
        }

        private void OnEnable()
        {
            subscribed = source;
            if (subscribed != null)
            {
                subscribed.StateChanged += HandleStateChanged;
                subscribed.Restored += HandleRestored;
            }

            if (started)
            {
                Evaluate();
            }
            else
            {
                // The state the scene starts in, perhaps a saved one: a lock opened in an earlier session
                // is simply open. Nothing is announced, so nothing rumbles or chimes again.
                bool holds = source != null && source.Meets(activeWhen);
                SetElementActiveSilently(Decide(holds, IsActive, latch));
            }
        }

        private void Start()
        {
            started = true;
        }

        private void OnDisable()
        {
            if (subscribed != null)
            {
                subscribed.StateChanged -= HandleStateChanged;
                subscribed.Restored -= HandleRestored;
                subscribed = null;
            }
        }

        private void HandleStateChanged(MagicalObject changed, WorldPropertyKind kind, int from, int to)
        {
            Evaluate();
        }

        private void HandleRestored(MagicalObject restored)
        {
            // Back to the start: a latch is released as well.
            SetElementActive(source != null && source.Meets(activeWhen));
        }

        private void Evaluate()
        {
            bool holds = source != null && source.Meets(activeWhen);
            SetElementActive(Decide(holds, IsActive, latch));
        }
    }
}
