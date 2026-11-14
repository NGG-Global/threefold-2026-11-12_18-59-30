using System;
using Threefold.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// An object in the world that responds to magic according to its properties: a crystal that
    /// charges, a barricade that burns, a platform that slides, a structure that can be braced. What
    /// it has and how it behaves comes from its <see cref="MagicalObjectDefinition"/>; the rules are
    /// in <see cref="MagicalPropertySet"/>. This component receives effects from attacks, advances
    /// time while something is changing, moves the object if it is pushed, and switches it off when
    /// it is destroyed.
    ///
    /// It needs no Rigidbody and no health. It is asleep (switched off, so no update at all) until an
    /// effect arrives and again once everything has settled. Attacks find it through its colliders; it must have at
    /// least one collider that is not a trigger, on this object or a child.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MagicalObject : MonoBehaviour, IMagicEffectReceiver, IRestorable, IAimAssistTarget
    {
        [SerializeField] private MagicalObjectDefinition definition;

        [Header("Parts")]
        [Tooltip("Kinetic objects: the part that moves. This object if empty.")]
        [SerializeField] private Transform moveTarget;

        [Tooltip("Colliders switched off when the object is destroyed, so that attacks and characters pass. " +
                 "All colliders under this object if empty.")]
        [SerializeField] private Collider[] colliders = new Collider[0];

        [Header("Aim assist")]
        [Tooltip("Arcane-reactive objects: while not yet activated, aim assist may pull the aim onto them.")]
        [SerializeField] private bool aimAssist = true;

        [Tooltip("Point that attacks aim at, in the space of the moving part (this object if there is none).")]
        [SerializeField] private Vector3 aimPointOffset = new Vector3(0f, 1f, 0f);

        [Header("Movement")]
        [Tooltip("Layers that stop a pushed object.")]
        [SerializeField] private LayerMask obstacleLayers = Physics.DefaultRaycastLayers;

        [Header("Feedback hooks")]
        [SerializeField] private UnityEvent onDestroyed = new UnityEvent();

        // How much smaller than the object the swept box is across its direction of travel.
        private const float SweepMargin = 0.1f;

        private readonly RaycastHit[] sweepHits = new RaycastHit[8];
        private Func<Vector3, Vector3, float> glideLimit;
        private MagicalPropertySet properties;
        private Vector3 restPosition;
        private bool restKnown;
        private bool spent;
        private bool registeredForAim;

        /// <summary>Raised for every change of state: the property, the state it left and the state it entered.</summary>
        public event Action<MagicalObject, WorldPropertyKind, int, int> StateChanged;

        /// <summary>Raised for every effect that arrives, with what became of it. For feedback.</summary>
        public event Action<MagicalObject, MagicEffect, EffectResponse> EffectReceived;

        /// <summary>Raised when the object is put back to how it started.</summary>
        public event Action<MagicalObject> Restored;

        public MagicalObjectDefinition Definition => definition;

        /// <summary>The object's properties and their rules. Null without a definition.</summary>
        public MagicalPropertySet Properties
        {
            get
            {
                EnsureProperties();
                return properties;
            }
        }

        public bool IsDestroyed => properties != null && properties.IsDestroyed;

        public Transform MoveTarget => moveTarget != null ? moveTarget : transform;

        /// <summary>How many colliders are listed to be switched off on destruction. 0 means all under the object.</summary>
        public int ListedColliderCount => colliders != null ? colliders.Length : 0;

        /// <summary>Gives the object a definition if it has none. Returns true if it changed.</summary>
        public bool FillDefinition(MagicalObjectDefinition objectDefinition)
        {
            if (definition != null || objectDefinition == null)
            {
                return false;
            }

            definition = objectDefinition;
            properties = null;
            return true;
        }

        // The component is switched off while the object is at rest, so "active" is asked of the GameObject.
        public bool IsAimAssistAvailable =>
            aimAssist && gameObject.activeInHierarchy && properties != null && properties.Arcane != null &&
            !properties.IsDestroyed && properties.Arcane.State != ArcaneState.Activated;

        /// <summary>Where attacks aim and effects appear. It follows the part that moves.</summary>
        public Vector3 AimPoint => MoveTarget.TransformPoint(aimPointOffset);

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(MagicalObjectDefinition objectDefinition, Transform mover, Collider[] bodyColliders)
        {
            definition = objectDefinition;
            moveTarget = mover;
            colliders = bodyColliders ?? new Collider[0];
            properties = null;
        }

        /// <summary>Sets the point attacks aim at. Called by the setup tooling.</summary>
        public void SetAimPoint(Vector3 localOffset)
        {
            aimPointOffset = localOffset;
        }

        /// <summary>True if any of the given conditions holds for this object.</summary>
        public bool Meets(PropertyCondition conditions)
        {
            EnsureProperties();
            return properties != null && properties.Meets(conditions);
        }

        /// <summary>A magical effect arrives.</summary>
        public EffectResponse ReceiveEffect(in MagicEffect effect)
        {
            EnsureProperties();
            if (properties == null || !gameObject.activeInHierarchy)
            {
                return EffectResponse.NoProperty;
            }

            // A push asks AllowedGlide (below) how far the way is clear before anything is announced.
            EffectResponse response = properties.Apply(effect);
            EffectReceived?.Invoke(this, effect, response);

            // Wake up for as long as something is changing.
            if (!properties.IsAtRest)
            {
                enabled = true;
            }

            return response;
        }

        /// <summary>Puts the object back to how it started: whole, cold, uncharged, in its place.</summary>
        public void Restore()
        {
            EnsureProperties();
            if (properties == null)
            {
                return;
            }

            properties.Restore();
            spent = false;
            SetCollidersEnabled(true);

            if (restKnown)
            {
                MoveTarget.position = restPosition;
            }

            enabled = false;
            Restored?.Invoke(this);
        }

        private void Awake()
        {
            EnsureProperties();
            RegisterForAim();

            // Asleep until an effect arrives: a resting object costs nothing per frame.
            enabled = false;
        }

        private void OnDestroy()
        {
            if (registeredForAim)
            {
                AimAssistTargets.Unregister(this);
                registeredForAim = false;
            }

            if (properties != null)
            {
                properties.StateChanged -= HandleStateChanged;
            }
        }

        private void Update()
        {
            if (properties == null)
            {
                enabled = false;
                return;
            }

            properties.Tick(Time.deltaTime);

            if (properties.Kinetic != null && restKnown)
            {
                MoveTarget.position = restPosition + properties.Kinetic.Offset;
            }

            if (properties.IsAtRest)
            {
                enabled = false;
            }
        }

        private void EnsureProperties()
        {
            if (properties != null || definition == null)
            {
                return;
            }

            properties = new MagicalPropertySet(definition.Properties, EffectTargets.Objects);
            properties.StateChanged += HandleStateChanged;

            // The delegate is made once, so asking it on every push allocates nothing.
            if (glideLimit == null)
            {
                glideLimit = AllowedGlide;
            }

            properties.GlideLimit = glideLimit;

            Transform mover = MoveTarget;
            restPosition = mover.position;
            restKnown = true;

            if (properties.Kinetic != null)
            {
                properties.KineticAxis = mover.TransformDirection(definition.Properties.kineticSettings.localAxis);
            }

            if (colliders == null || colliders.Length == 0)
            {
                colliders = GetComponentsInChildren<Collider>(true);
            }
        }

        private void RegisterForAim()
        {
            if (!registeredForAim && properties != null && properties.Arcane != null)
            {
                AimAssistTargets.Register(this);
                registeredForAim = true;
            }
        }

        private void HandleStateChanged(WorldPropertyKind kind, int from, int to)
        {
            StateChanged?.Invoke(this, kind, from, to);

            if (kind == WorldPropertyKind.Structural && to == (int)StructuralState.Destroyed && !spent)
            {
                // Cleanup: nothing can hit it, stand on it or be blocked by it any more, and it stops updating.
                spent = true;
                SetCollidersEnabled(false);
                onDestroyed.Invoke();
            }
        }

        private void SetCollidersEnabled(bool value)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = value;
                }
            }
        }

        /// <summary>
        /// How far a glide may go before something is in the way. One box sweep along the glide, using
        /// the object's own bounds; its own colliders are ignored. The property set asks this when a
        /// push sets a new glide, before the change is announced.
        /// </summary>
        private float AllowedGlide(Vector3 fromOffset, Vector3 toOffset)
        {
            Vector3 glide = toOffset - fromOffset;
            float length = glide.magnitude;
            if (length < 0.01f || colliders == null || colliders.Length == 0)
            {
                return length;
            }

            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider part = colliders[i];
                if (part == null || !part.enabled || part.isTrigger)
                {
                    continue;
                }

                if (hasBounds)
                {
                    bounds.Encapsulate(part.bounds);
                }
                else
                {
                    bounds = part.bounds;
                    hasBounds = true;
                }
            }

            if (!hasBounds)
            {
                return length;
            }

            // Slightly smaller across the direction of travel, so resting on the floor or beside a rail
            // does not count. Full size along it, so the object stops at what it meets and not inside it.
            Vector3 direction = glide / length;
            Vector3 extents = bounds.extents;
            var halfExtents = new Vector3(
                extents.x * (1f - SweepMargin * (1f - Mathf.Abs(direction.x))),
                extents.y * (1f - SweepMargin * (1f - Mathf.Abs(direction.y))),
                extents.z * (1f - SweepMargin * (1f - Mathf.Abs(direction.z))));

            int count = Physics.BoxCastNonAlloc(bounds.center, halfExtents, direction, sweepHits, Quaternion.identity, length,
                obstacleLayers, QueryTriggerInteraction.Ignore);

            float allowed = length;
            Transform self = transform;
            Transform mover = MoveTarget;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweepHits[i];

                // A sweep reports distance 0 for what the box already touches (the floor it rests on, its
                // rail); only what lies ahead can stop it.
                if (hit.distance <= 0f || hit.transform.IsChildOf(self) || hit.transform.IsChildOf(mover))
                {
                    continue;
                }

                // Something a character stands in front of is in the way too; characters are obstacles here.
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.02f));
            }

            return allowed;
        }
    }
}
