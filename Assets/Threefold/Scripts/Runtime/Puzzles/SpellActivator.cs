using Threefold.Combat;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// A magical object that is switched on by striking it with a spell of the right kind: a crystal
    /// that answers Arcane Bolt, later a brazier that answers fire. It needs a collider that spells
    /// can hit (not a trigger) on this object or a child.
    ///
    /// It can be made receptive only while another element is active. Until then, a matching spell
    /// fizzles, which tells the player that the crystal is right but something else is missing.
    /// While receptive and unlit, aim assist helps to hit it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpellActivator : PuzzleElement, IMagicEffectReceiver, IAimAssistTarget
    {
        [Header("Activator")]
        [Tooltip("The kinds of magic that switch it on.")]
        [SerializeField] private SpellElement acceptedElements = SpellElement.Arcane;

        [Tooltip("Optional. The activator responds only while this element is active.")]
        [SerializeField] private PuzzleElement receptiveWhile;

        [SerializeField] private ActivatorMode mode = ActivatorMode.Latch;

        [Header("Aim assist")]
        [Tooltip("While receptive and unlit, aim assist may pull the aim onto it.")]
        [SerializeField] private bool aimAssist = true;

        [Tooltip("Point that aim assist aims at, in this object's space.")]
        [SerializeField] private Vector3 aimPointOffset = Vector3.zero;

        private readonly EffectEventLog effectLog = new EffectEventLog();
        private SpellResponse lastResponse;
        private int answeredEventId;
        private int refusedEventId;

        public SpellElement AcceptedElements
        {
            get => acceptedElements;
            set => acceptedElements = value;
        }

        public PuzzleElement ReceptiveWhile
        {
            get => receptiveWhile;
            set => receptiveWhile = value;
        }

        public ActivatorMode Mode
        {
            get => mode;
            set => mode = value;
        }

        /// <summary>How the most recent spell was answered. Feedback can tell a wrong spell from a dormant crystal.</summary>
        public SpellResponse LastResponse => lastResponse;

        public override bool IsReady => !IsLocked && (receptiveWhile == null || receptiveWhile.IsActive);

        public bool IsAimAssistAvailable => aimAssist && isActiveAndEnabled && IsReady && !IsActive;

        public Vector3 AimPoint => transform.TransformPoint(aimPointOffset);

        /// <summary>A spell's effect is momentary, so only a locked (solved) state is remembered.</summary>
        protected override bool PersistsUnlockedState => false;

        /// <summary>
        /// A magical effect arrives. The activator answers the kind of magic the effect stands for:
        /// arcane charge is arcane, heat is fire, force is force. How strong the effect is does not
        /// matter here; an object that accumulates charge uses a <see cref="MagicalObject"/> instead.
        /// </summary>
        public EffectResponse ReceiveEffect(in MagicEffect effect)
        {
            if ((effect.Targets & EffectTargets.Objects) == 0)
            {
                return EffectResponse.Filtered;
            }

            SpellElement element = ElementOf(effect.Kind);
            if (element == SpellElement.None || effect.Magnitude <= 0f)
            {
                return EffectResponse.NoProperty;
            }

            // One attack is one spell: a swing that carries two kinds of effect must not switch a
            // toggle on and off again, or be refused twice.
            bool accepted = SpellElements.Matches(acceptedElements, element);
            if (SpellActivatorRules.PassesOver(effect.EventId, answeredEventId, refusedEventId, accepted) ||
                effectLog.CheckAndRecord(effect.EventId, effect.Kind))
            {
                return EffectResponse.Duplicate;
            }

            ReceiveSpell(new SpellImpact(element, effect.Point, effect.Direction, effect.Source));

            if (lastResponse == SpellResponse.RejectedElement)
            {
                refusedEventId = effect.EventId;
                return EffectResponse.NoProperty;
            }

            answeredEventId = effect.EventId;
            return lastResponse == SpellResponse.Activated || lastResponse == SpellResponse.Deactivated
                ? EffectResponse.Applied
                : EffectResponse.Resisted;
        }

        /// <summary>The kind of magic an effect stands for. Bracing is not a spell an activator answers.</summary>
        public static SpellElement ElementOf(MagicEffectKind kind)
        {
            switch (kind)
            {
                case MagicEffectKind.ArcaneCharge: return SpellElement.Arcane;
                case MagicEffectKind.Heat: return SpellElement.Fire;
                case MagicEffectKind.Force: return SpellElement.Force;
                default: return SpellElement.None;
            }
        }

        public void ReceiveSpell(in SpellImpact impact)
        {
            lastResponse = SpellActivatorRules.Decide(acceptedElements, impact.Element, IsReady, IsActive, IsLocked, mode);

            switch (lastResponse)
            {
                case SpellResponse.Activated:
                    SetElementActive(true);
                    break;
                case SpellResponse.Deactivated:
                    SetElementActive(false);
                    break;
                case SpellResponse.RejectedElement:
                case SpellResponse.RejectedDormant:
                    RaiseRejected();
                    break;
            }
        }

        private void OnEnable()
        {
            AimAssistTargets.Register(this);
        }

        private void OnDisable()
        {
            AimAssistTargets.Unregister(this);
        }
    }
}
