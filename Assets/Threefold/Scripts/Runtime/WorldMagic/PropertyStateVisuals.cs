using System;
using Threefold.Pooling;
using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>A colour an object takes on while a condition holds.</summary>
    [Serializable]
    public sealed class ConditionTint
    {
        public PropertyCondition when;
        public Color color = Color.white;

        [Tooltip("How far the object's own colour moves towards this one.")]
        [Range(0f, 1f)]
        public float strength = 0.6f;
    }

    /// <summary>Objects that are shown only while a condition holds, or only while it does not.</summary>
    [Serializable]
    public sealed class ConditionObjects
    {
        public PropertyCondition when;

        [Tooltip("Show the objects while the condition does NOT hold: the intact mesh that disappears when destroyed.")]
        public bool invert;

        public GameObject[] objects = new GameObject[0];
    }

    /// <summary>An effect played once when a condition starts to hold.</summary>
    [Serializable]
    public sealed class ConditionEffect
    {
        public PropertyCondition onEnter;
        public PooledObject effect;
    }

    /// <summary>
    /// Shows the state of a <see cref="MagicalObject"/> without a script per object. Everything is
    /// configured against conditions on its properties:
    ///
    /// - Tints: the object's renderers move towards a colour while a condition holds (charred when
    ///   weakened, gold while braced). The last matching tint in the list wins.
    /// - Heat and charge: renderers warm towards a heat colour as the object heats, and others
    ///   brighten towards a charge colour as it charges.
    /// - Objects: parts shown or hidden by condition (the whole mesh, the rubble, a lamp).
    /// - Effects: a pooled burst when a condition starts, and a repeating one while it burns.
    /// - Motion: a slight sway while unstable, and a fall into a collapsed pose.
    ///
    /// It changes no rule. It updates only while the object is changing, apart from the sway.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropertyStateVisuals : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const int MaxEffects = 6;

        [Tooltip("The object whose state is shown. The MagicalObject on this object if empty.")]
        [SerializeField] private MagicalObject source;

        [Header("Tints")]
        [Tooltip("Renderers that take the tints and the heat colour.")]
        [SerializeField] private Renderer[] tintRenderers = new Renderer[0];

        [SerializeField] private ConditionTint[] tints = new ConditionTint[0];

        [Tooltip("Colour the tinted renderers move towards as the object heats up.")]
        [SerializeField] private Color heatColor = new Color(1f, 0.45f, 0.15f, 1f);

        [Range(0f, 1f)]
        [SerializeField] private float heatStrength = 0.65f;

        [Header("Charge")]
        [Tooltip("Renderers that brighten as the object charges.")]
        [SerializeField] private Renderer[] chargeRenderers = new Renderer[0];

        [SerializeField] private Color chargeColor = new Color(0.62f, 0.64f, 1f, 1f);
        [SerializeField] private Color activatedColor = new Color(0.9f, 0.92f, 1f, 1f);

        [Header("Parts and effects")]
        [SerializeField] private ConditionObjects[] objects = new ConditionObjects[0];
        [SerializeField] private ConditionEffect[] effects = new ConditionEffect[0];

        [Tooltip("Optional. Played again and again while the object burns.")]
        [SerializeField] private PooledObject burningEffect;

        [Min(0.1f)]
        [SerializeField] private float burningEffectSeconds = 0.5f;

        [Header("Motion")]
        [Tooltip("Optional. Sways slightly while the object is unstable.")]
        [SerializeField] private Transform swayTarget;

        [Range(0f, 10f)]
        [SerializeField] private float swayDegrees = 1.5f;

        [Min(0.1f)]
        [SerializeField] private float swaySpeed = 1.4f;

        [Tooltip("Optional. Moves from its rest pose to this offset when the object collapses.")]
        [SerializeField] private MovingPart collapsePart = new MovingPart();

        [Min(0.05f)]
        [SerializeField] private float collapseSeconds = 0.7f;

        [Header("Performance")]
        [Tooltip("While none of the listed renderers is drawn, colours, sway and bursts are not computed. The object's " +
                 "state is unaffected and is shown again the moment it is seen.")]
        [SerializeField] private bool suspendWhenUnseen = true;

        private MaterialPropertyBlock block;
        private Color[] tintBase = new Color[0];
        private Color[] chargeBase = new Color[0];
        private bool[] effectHeld = new bool[0];
        private MagicalObject subscribed;
        private Quaternion swayRest = Quaternion.identity;
        private bool swayRestKnown;
        private float collapseAmount;
        private float burningTimer;
        private bool unseen;
        private Color accentColor = Color.white;
        private float accentStrength;
        private float swayScale = 1f;
        private bool chargeAccented;

        /// <summary>Connects the object and the renderers. Called by the setup tooling.</summary>
        public void Bind(MagicalObject magicalObject, Renderer[] tinted, Renderer[] charged)
        {
            source = magicalObject;
            tintRenderers = tinted ?? new Renderer[0];
            chargeRenderers = charged ?? new Renderer[0];
        }

        /// <summary>Sets what is shown for which condition. Called by the setup tooling.</summary>
        public void Configure(ConditionTint[] stateTints, ConditionObjects[] stateObjects, ConditionEffect[] stateEffects,
            PooledObject whileBurning)
        {
            tints = stateTints ?? new ConditionTint[0];
            objects = stateObjects ?? new ConditionObjects[0];
            effects = stateEffects ?? new ConditionEffect[0];
            burningEffect = whileBurning;
        }

        /// <summary>Sets the motion. Called by the setup tooling.</summary>
        public void ConfigureMotion(Transform sway, float degrees, Transform collapsing, Vector3 collapseOffset,
            Vector3 collapseRotation)
        {
            swayTarget = sway;
            swayDegrees = Mathf.Max(0f, degrees);
            collapsePart = new MovingPart(collapsing, collapseOffset, collapseRotation);
        }

        /// <summary>
        /// A player option, set by the discovery director: with it, the embers that repeat while
        /// something burns are left out. The single burst at a change of state stays, and so does
        /// everything that is not a particle.
        /// </summary>
        public static bool ReducedParticles { get; set; }

        /// <summary>
        /// The share of its particles a burst at a change of state plays, from 1 (all) down: set by the
        /// discovery director from the quality level and the player's particle option. The burst is
        /// never left out; it marks the change.
        /// </summary>
        public static float BurstShare { get; set; } = 1f;

        public MagicalObject Source => source;
        public int TintRendererCount => tintRenderers.Length;

        /// <summary>
        /// A light wash of colour over everything this component colours, from outside: the discovery
        /// system uses it for clues and resonance. It goes through here because one renderer can only
        /// have one writer of its colour. It shows on top of the state and changes nothing about it.
        /// </summary>
        /// <param name="strength">0 for none, up to 1.</param>
        public void SetAccent(Color color, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (Mathf.Abs(strength - accentStrength) < 0.004f && (strength <= 0f || color == accentColor))
            {
                return;
            }

            accentColor = color;
            accentStrength = strength;
            if (source != null && source.Properties != null && block != null)
            {
                ApplyColors(source.Properties);
            }
        }

        /// <summary>Makes the sway of an unstable object larger or smaller: 1 is as authored.</summary>
        public void SetSwayScale(float scale)
        {
            swayScale = Mathf.Clamp(scale, 0f, 4f);
            if (swayScale > 1.001f && swayTarget != null && swayDegrees > 0f)
            {
                enabled = true;
            }
        }

        /// <summary>
        /// Every condition this component waits for. One the object can never reach (see
        /// <see cref="MagicalObjectRules.Reachable"/>) is a part that never shows; the validation reports it.
        /// </summary>
        public PropertyCondition WatchedConditions
        {
            get
            {
                PropertyCondition watched = PropertyCondition.None;
                for (int i = 0; i < tints.Length; i++)
                {
                    if (tints[i] != null) watched |= tints[i].when;
                }

                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i] != null) watched |= objects[i].when;
                }

                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i] != null) watched |= effects[i].onEnter;
                }

                if (burningEffect != null) watched |= PropertyCondition.Burning;
                if (swayTarget != null && swayDegrees > 0f) watched |= PropertyCondition.Unstable;
                if (collapsePart != null && collapsePart.target != null) watched |= PropertyCondition.Collapsed;
                return watched;
            }
        }

        private void Awake()
        {
            if (source == null)
            {
                source = GetComponent<MagicalObject>();
            }

            block = new MaterialPropertyBlock();
            tintBase = CaptureBaseColors(tintRenderers);
            chargeBase = CaptureBaseColors(chargeRenderers);
            collapsePart.CaptureRest();

            // Subscribed for the component's whole life, not only while it is enabled: it switches itself
            // off while nothing changes, and the object's events are what switch it on again.
            subscribed = source;
            if (subscribed != null)
            {
                subscribed.StateChanged += HandleStateChanged;
                subscribed.EffectReceived += HandleEffectReceived;
                subscribed.Restored += HandleRestored;
            }

            ShowCurrentState(false);

            // A state loaded from the saved world is shown as it is: a structure that had come down lies down.
            if (source != null && source.Properties != null)
            {
                SnapMotion(source.Properties);
            }
        }

        private void OnDestroy()
        {
            if (subscribed != null)
            {
                subscribed.StateChanged -= HandleStateChanged;
                subscribed.EffectReceived -= HandleEffectReceived;
                subscribed.Restored -= HandleRestored;
                subscribed = null;
            }
        }

        private void Update()
        {
            if (source == null || source.Properties == null)
            {
                return;
            }

            MagicalPropertySet properties = source.Properties;
            float deltaTime = Time.deltaTime;
            bool busy = false;

            // Out of sight, nothing here is worth computing. The object's state carries on regardless:
            // it lives in its properties, not in what is shown. What is shown is brought up to date once
            // if the object has settled, and again the moment it is seen.
            if (suspendWhenUnseen && !IsSeen())
            {
                if (!unseen)
                {
                    unseen = true;
                    ShowCurrentState(false);
                    SnapMotion(properties);
                }

                bool sways = swayTarget != null && swayDegrees > 0f && properties.Meets(PropertyCondition.Unstable);
                if (!source.IsAwake && !sways)
                {
                    // It has settled out of sight. What is shown is brought up to date one last time,
                    // because a component that is switched off cannot notice being looked at again.
                    ShowCurrentState(false);
                    SnapMotion(properties);
                    enabled = false;
                }

                return;
            }

            if (unseen)
            {
                unseen = false;
                ShowCurrentState(false);
                SnapMotion(properties);
            }

            // Heat and charge change without a change of state, so the colours follow them while the object is awake.
            if (source.IsAwake)
            {
                ApplyColors(properties);
                busy = true;
            }

            if (burningEffect != null && !ReducedParticles && properties.Meets(PropertyCondition.Burning))
            {
                burningTimer -= deltaTime;
                if (burningTimer <= 0f)
                {
                    burningTimer = burningEffectSeconds;
                    // The same pool may hold instances a state burst limited; the embers play in full.
                    BurstParticleShare.Apply(Pools.Spawn(burningEffect, source.AimPoint, Quaternion.identity, MaxEffects), 1f);
                }

                busy = true;
            }

            bool collapsed = properties.Meets(PropertyCondition.Collapsed);
            float collapseTarget = collapsed ? 1f : 0f;
            if (!Mathf.Approximately(collapseAmount, collapseTarget))
            {
                collapseAmount = Mathf.MoveTowards(collapseAmount, collapseTarget, deltaTime / collapseSeconds);
                collapsePart.Apply(collapseAmount);
                busy = true;
            }

            if (swayTarget != null && swayDegrees > 0f)
            {
                if (!swayRestKnown)
                {
                    swayRest = swayTarget.localRotation;
                    swayRestKnown = true;
                }

                if (!collapsed && properties.Meets(PropertyCondition.Unstable))
                {
                    float angle = Mathf.Sin(Time.time * swaySpeed) * swayDegrees * swayScale;
                    swayTarget.localRotation = swayRest * Quaternion.Euler(angle, 0f, angle * 0.6f);
                    busy = true;
                }
                else
                {
                    swayTarget.localRotation = swayRest;
                }
            }

            // Nothing is moving or changing: stop updating until the object says otherwise.
            if (!busy)
            {
                enabled = false;
            }
        }

        private void HandleStateChanged(MagicalObject changed, WorldPropertyKind kind, int from, int to)
        {
            // A burst for something nobody is looking at is not worth its particles.
            ShowCurrentState(!suspendWhenUnseen || IsSeen());
            enabled = true;
        }

        private void HandleEffectReceived(MagicalObject target, MagicEffect effect, EffectResponse response)
        {
            if (response == EffectResponse.Applied)
            {
                enabled = true;
            }
        }

        private void HandleRestored(MagicalObject restored, StateReset reason)
        {
            // The state was set as a whole: show it as it is, without playing how it came about.
            ShowCurrentState(false);
            if (source != null && source.Properties != null)
            {
                SnapMotion(source.Properties);
            }

            enabled = true;
        }

        /// <summary>Puts the moving parts where the state says they are, at once.</summary>
        private void SnapMotion(MagicalPropertySet properties)
        {
            collapseAmount = properties.Meets(PropertyCondition.Collapsed) ? 1f : 0f;
            collapsePart.Apply(collapseAmount);

            if (swayTarget != null && swayRestKnown)
            {
                swayTarget.localRotation = swayRest;
            }
        }

        /// <summary>True if any renderer this component colours is being drawn. With none listed it counts as seen.</summary>
        private bool IsSeen()
        {
            bool any = false;
            for (int i = 0; i < tintRenderers.Length; i++)
            {
                if (tintRenderers[i] != null)
                {
                    any = true;
                    if (tintRenderers[i].isVisible) return true;
                }
            }

            for (int i = 0; i < chargeRenderers.Length; i++)
            {
                if (chargeRenderers[i] != null)
                {
                    any = true;
                    if (chargeRenderers[i].isVisible) return true;
                }
            }

            return !any;
        }

        /// <summary>Shows and hides parts, plays the effects of conditions that have just started, and sets the colours.</summary>
        private void ShowCurrentState(bool playEffects)
        {
            if (source == null || source.Properties == null)
            {
                return;
            }

            MagicalPropertySet properties = source.Properties;

            for (int i = 0; i < objects.Length; i++)
            {
                ConditionObjects entry = objects[i];
                if (entry == null || entry.objects == null)
                {
                    continue;
                }

                bool show = properties.Meets(entry.when) != entry.invert;
                for (int j = 0; j < entry.objects.Length; j++)
                {
                    GameObject part = entry.objects[j];
                    if (part != null && part.activeSelf != show)
                    {
                        part.SetActive(show);
                    }
                }
            }

            // Remembered per entry, not per condition: two entries may share a condition (a small burst
            // on "fractured or destroyed", a large one on "destroyed"), and each plays when its own starts.
            if (effectHeld.Length != effects.Length)
            {
                effectHeld = new bool[effects.Length];
            }

            for (int i = 0; i < effects.Length; i++)
            {
                ConditionEffect entry = effects[i];
                bool holds = entry != null && properties.Meets(entry.onEnter);
                if (holds && !effectHeld[i] && playEffects && entry.effect != null)
                {
                    PooledObject burst = Pools.Spawn(entry.effect, source.AimPoint, Quaternion.identity, MaxEffects);
                    BurstParticleShare.Apply(burst, BurstShare);
                }

                effectHeld[i] = holds;
            }

            ApplyColors(properties);
        }

        private void ApplyColors(MagicalPropertySet properties)
        {
            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            // The last matching tint wins, so the list is ordered from the mildest state to the gravest.
            bool tinted = false;
            Color tint = Color.white;
            float strength = 0f;
            for (int i = 0; i < tints.Length; i++)
            {
                if (tints[i] != null && properties.Meets(tints[i].when))
                {
                    tint = tints[i].color;
                    strength = tints[i].strength;
                    tinted = true;
                }
            }

            float heat = properties.Thermal != null ? properties.Thermal.Fraction * heatStrength : 0f;

            for (int i = 0; i < tintRenderers.Length && i < tintBase.Length; i++)
            {
                if (tintRenderers[i] == null)
                {
                    continue;
                }

                Color color = tintBase[i];
                if (tinted)
                {
                    color = Color.Lerp(color, tint, strength);
                }

                if (heat > 0f)
                {
                    color = Color.Lerp(color, heatColor, heat);
                }

                if (accentStrength > 0f)
                {
                    color = Color.Lerp(color, accentColor, accentStrength * 0.5f);
                }

                SetColor(tintRenderers[i], color);
            }

            bool hasArcane = properties.Arcane != null;
            if (!hasArcane && accentStrength <= 0f && !chargeAccented)
            {
                return;
            }

            bool activated = hasArcane && properties.Arcane.State == ArcaneState.Activated;
            float charge = hasArcane ? properties.Arcane.Fraction : 0f;
            for (int i = 0; i < chargeRenderers.Length && i < chargeBase.Length; i++)
            {
                if (chargeRenderers[i] == null)
                {
                    continue;
                }

                Color color = activated ? activatedColor : Color.Lerp(chargeBase[i], chargeColor, charge);
                if (accentStrength > 0f)
                {
                    // The parts that show charge are where an accent reads best, so it is fuller here.
                    color = Color.Lerp(color, accentColor, accentStrength);
                }

                SetColor(chargeRenderers[i], color);
            }

            chargeAccented = accentStrength > 0f;
        }

        private void SetColor(Renderer target, Color color)
        {
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }

        private static Color[] CaptureBaseColors(Renderer[] renderers)
        {
            var colors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i] != null ? renderers[i].sharedMaterial : null;
                colors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
            }

            return colors;
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReducedParticles = false;
            BurstShare = 1f;
        }
    }
}
