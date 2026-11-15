using System;
using Threefold.Combat;
using Threefold.Persistence;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// An object in the world that responds to magic according to its properties: a crystal that
    /// charges, a barricade that burns, a platform that slides, a structure that can be braced. What
    /// it has and how it behaves comes from its <see cref="MagicalObjectDefinition"/>; the rules are
    /// in <see cref="MagicalPropertySet"/>. This component receives effects from attacks and from
    /// other objects' reactions, moves the object if it is pushed, switches it off when it is
    /// destroyed, sends out its own reactions, and saves and restores what is meant to last.
    ///
    /// It needs no Rigidbody and no health, and it has no update of its own. While something on it
    /// is changing it is advanced by the <see cref="WorldSimulation"/>; at rest it costs nothing.
    /// Attacks find it through its colliders; it must have at least one that is not a trigger.
    ///
    /// With a Persistent Id, the parts of its state that its definition names are saved and come
    /// back after loading. Everything else is temporary and is reset when the world is reloaded.
    /// </summary>
    /// <summary>Why a magical object's state was set as a whole rather than changed step by step.</summary>
    public enum StateReset
    {
        /// <summary>The world was reloaded from an unchanged store (a restart): temporary state reset, lasting state kept.</summary>
        Reloaded = 0,

        /// <summary>The state was replaced: put back to the start, or taken from a store that was itself replaced.</summary>
        Replaced = 1,

        /// <summary>The object was switched off and its temporary state was brought up to date or dropped.</summary>
        Settled = 2,
    }

    [DisallowMultipleComponent]
    public sealed class MagicalObject : MonoBehaviour, IMagicEffectReceiver, IRestorable, IAimAssistTarget, ISimulated
    {
        private const int MaxReactionTargets = 16;

        // How much smaller than the object the swept box is across its direction of travel.
        private const float SweepMargin = 0.1f;

        // Shared by all objects: reactions are sent one object at a time, never nested.
        private static readonly Collider[] ReactionOverlaps = new Collider[48];
        private static readonly IMagicEffectReceiver[] ReactionTargets = new IMagicEffectReceiver[MaxReactionTargets];
        private static readonly Vector3[] ReactionPoints = new Vector3[MaxReactionTargets];
        private static readonly float[] ReactionDistances = new float[MaxReactionTargets];

        [SerializeField] private MagicalObjectDefinition definition;

        [Header("Persistence")]
        [Tooltip("The stable identifier under which this object's lasting state is saved. Empty: nothing is saved and " +
                 "the object is as new after every reload. Must be unique in the game. Threefold > World Magic > " +
                 "Assign Persistent Ids fills it in.")]
        [SerializeField] private string persistentId = string.Empty;

        [Header("Parts")]
        [Tooltip("Kinetic objects: the part that moves. This object if empty.")]
        [SerializeField] private Transform moveTarget;

        [Tooltip("Colliders switched off when the object is destroyed, so that attacks and characters pass. " +
                 "All colliders under this object if empty.")]
        [SerializeField] private Collider[] colliders = new Collider[0];

        [Header("Reactions")]
        [Tooltip("Objects that receive this object's reactions marked 'To Linked Targets': a mechanism a crystal feeds.")]
        [SerializeField] private GameObject[] linkedTargets = new GameObject[0];

        [Tooltip("Layers searched for what a reaction reaches by distance.")]
        [SerializeField] private LayerMask reactionLayers = Physics.DefaultRaycastLayers;

        [Header("Aim assist")]
        [Tooltip("Arcane-reactive objects: while not yet activated, aim assist may pull the aim onto them.")]
        [SerializeField] private bool aimAssist = true;

        [Tooltip("Point that attacks aim at, in the space of the moving part (this object if there is none).")]
        [SerializeField] private Vector3 aimPointOffset = new Vector3(0f, 1f, 0f);

        [Header("Movement")]
        [Tooltip("Layers that stop a pushed object.")]
        [SerializeField] private LayerMask obstacleLayers = Physics.DefaultRaycastLayers;

        [Header("Weight")]
        [Tooltip("Optional. Counts the creatures standing on the object, for an object that weight brings down.")]
        [SerializeField] private LoadZone loadZone;

        [Header("Feedback hooks")]
        [SerializeField] private UnityEvent onDestroyed = new UnityEvent();

        private readonly RaycastHit[] sweepHits = new RaycastHit[8];
        private Func<Vector3, Vector3, float> glideLimit;
        private MagicalPropertySet properties;
        private Vector3 restPosition;
        private bool restKnown;
        private bool spent;
        private bool registeredForAim;
        private bool awake;
        private bool persistent;
        private bool listensToWorld;
        private bool wasSwitchedOff;
        private float switchedOffAt;

        /// <summary>Raised for every change of state: the property, the state it left and the state it entered.</summary>
        public event Action<MagicalObject, WorldPropertyKind, int, int> StateChanged;

        /// <summary>Raised for every effect that arrives, with what became of it. For feedback.</summary>
        public event Action<MagicalObject, MagicEffect, EffectResponse> EffectReceived;

        /// <summary>
        /// Raised for every effect that arrives at any magical object, after <see cref="EffectReceived"/>. For
        /// feedback that is shown the same on every object (the magic arriving), so it needs no setup per object.
        /// It changes nothing: the effect has already been applied.
        /// </summary>
        public static event Action<MagicalObject, MagicEffect, EffectResponse> AnyEffectReceived;

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvents()
        {
            AnyEffectReceived = null;
        }

        /// <summary>
        /// Raised when the object's state was set as a whole rather than changed step by step: put back
        /// to how it started, loaded from the saved world, or brought up to date after being switched
        /// off. Whoever shows the state looks at it afresh; nothing that already happened is played again.
        /// </summary>
        public event Action<MagicalObject, StateReset> Restored;

        /// <summary>Raised when the number of creatures standing on the object changes. Whoever shows the state looks again.</summary>
        public event Action<MagicalObject> LoadChanged;

        public LoadZone LoadZone => loadZone;

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

        /// <summary>True while the simulation is advancing the object: something on it is changing with time.</summary>
        public bool IsAwake => awake;

        /// <summary>
        /// True while an effect is being applied. A change of state announced meanwhile was caused by
        /// that effect; one announced at any other time came with the passing of time.
        /// </summary>
        public bool IsApplyingEffect { get; private set; }

        public string PersistentId => persistentId;

        /// <summary>True if the object's lasting state is being saved: it has an identifier and nothing else claimed it first.</summary>
        public bool IsPersistent => persistent;

        public Transform MoveTarget => moveTarget != null ? moveTarget : transform;

        /// <summary>How many colliders are listed to be switched off on destruction. 0 means all under the object.</summary>
        public int ListedColliderCount => colliders != null ? colliders.Length : 0;

        public int LinkedTargetCount => linkedTargets != null ? linkedTargets.Length : 0;

        public bool IsSimulationAtRest => properties == null || properties.IsAtRest;

        // Asked of the GameObject and the component, which is also right outside Play mode.
        private bool IsSwitchedOn => enabled && gameObject.activeInHierarchy;

        /// <summary>What of this object is saved: the parts its definition names, if it has an identifier.</summary>
        private PersistedState SavedParts => persistent && definition != null ? definition.Properties.persisted : PersistedState.None;

        public bool IsSimulationCritical => definition != null && definition.Properties.critical;

        public bool IsAimAssistAvailable =>
            aimAssist && IsSwitchedOn && properties != null && properties.Arcane != null &&
            !properties.IsDestroyed && properties.Arcane.State != ArcaneState.Activated;

        /// <summary>Where attacks aim and effects appear. It follows the part that moves.</summary>
        public Vector3 AimPoint => MoveTarget.TransformPoint(aimPointOffset);

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

        /// <summary>Gives the object an identifier if it has none. Returns true if it changed. Called by the setup tooling.</summary>
        public bool FillPersistentId(string id)
        {
            if (!string.IsNullOrEmpty(persistentId) || string.IsNullOrEmpty(id))
            {
                return false;
            }

            persistentId = id;
            return true;
        }

        /// <summary>Sets the linked targets if none are listed. Returns true if it changed. Called by the setup tooling.</summary>
        public bool FillLinkedTargets(GameObject[] targets)
        {
            if ((linkedTargets != null && linkedTargets.Length > 0) || targets == null || targets.Length == 0)
            {
                return false;
            }

            linkedTargets = targets;
            return true;
        }

        public GameObject GetLinkedTarget(int index)
        {
            return linkedTargets != null && index >= 0 && index < linkedTargets.Length ? linkedTargets[index] : null;
        }

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

        /// <summary>A magical effect arrives, from an attack or from another object's reaction.</summary>
        public EffectResponse ReceiveEffect(in MagicEffect effect)
        {
            EnsureProperties();
            if (properties == null || !IsSwitchedOn)
            {
                return EffectResponse.NoProperty;
            }

            // A push asks AllowedGlide (below) how far the way is clear before anything is announced.
            IsApplyingEffect = true;
            EffectResponse response;
            try
            {
                response = properties.Apply(effect);
            }
            finally
            {
                IsApplyingEffect = false;
            }

            EffectReceived?.Invoke(this, effect, response);
            AnyEffectReceived?.Invoke(this, effect, response);

            SendReactions();

            if (response == EffectResponse.Applied)
            {
                Persist();
            }

            // Time has to pass for as long as something is changing.
            if (!properties.IsAtRest && !awake)
            {
                awake = WorldSimulation.Wake(this);
            }

            return response;
        }

        /// <summary>
        /// Says how many creatures stand on the object. Weight bears only on an unstable object whose
        /// stability says so; to anything else this means nothing. Called by its <see cref="LoadZone"/>.
        /// </summary>
        public void SetOccupants(int count)
        {
            EnsureProperties();

            // The count is kept even while the object is switched off, so it is right when it comes back.
            if (properties == null || !properties.SetOccupants(count) || !IsSwitchedOn)
            {
                return;
            }

            SendReactions();
            LoadChanged?.Invoke(this);

            if (!properties.IsAtRest && !awake)
            {
                awake = WorldSimulation.Wake(this);
            }
        }

        /// <summary>Connects the zone that measures weight on the object. Called by the setup tooling.</summary>
        public void BindLoadZone(LoadZone zone)
        {
            loadZone = zone;
        }

        /// <summary>
        /// One step of time, given by the world simulation while something on the object is changing.
        /// Not for anyone else to call.
        /// </summary>
        public void Simulate(float deltaTime)
        {
            if (properties == null)
            {
                awake = false;
                return;
            }

            // Someone may have left without the physics engine saying so; weight is counted afresh.
            if (loadZone != null && properties.Stability != null && properties.Stability.Occupants > 0)
            {
                loadZone.Recount();
            }

            awake = true;
            properties.Tick(deltaTime);
            ApplyOffset();
            SendReactions();

            if (properties.IsAtRest)
            {
                // The simulation lets the object go; what it has come to is what is kept.
                awake = false;
                Persist();
            }
        }

        /// <summary>
        /// Puts the object back to how it started: whole, cold, uncharged, in its place. What was saved
        /// for it is forgotten as well. To take the saved state instead, reload the world.
        /// </summary>
        public void Restore()
        {
            EnsureProperties();
            if (properties == null)
            {
                return;
            }

            properties.Restore();
            if (persistent)
            {
                WorldState.Current.RemoveObject(persistentId);
            }

            ShowLoadedState(StateReset.Replaced);
        }

        private void Awake()
        {
            EnsureProperties();
            RegisterForAim();
        }

        private void OnEnable()
        {
            if (properties == null)
            {
                return;
            }

            if (wasSwitchedOff)
            {
                wasSwitchedOff = false;

                // While it was off, time stood still for it, or is made up now, as its definition says.
                if (definition.Properties.whenInactive == InactiveBehaviour.CatchUp)
                {
                    properties.CatchUp(Time.time - switchedOffAt, SavedParts);
                    ShowLoadedState(StateReset.Settled);
                    Persist();
                }
            }

            if (!properties.IsAtRest && !awake)
            {
                awake = WorldSimulation.Wake(this);
            }
        }

        private void OnDisable()
        {
            // Nothing may call into an object that is switched off or about to be destroyed.
            WorldSimulation.Forget(this, this);
            awake = false;

            if (properties == null)
            {
                return;
            }

            wasSwitchedOff = true;
            switchedOffAt = Time.time;

            if (definition != null && definition.Properties.whenInactive == InactiveBehaviour.Reset)
            {
                // Heat or charge the definition saves as an exception is kept, or this would erase it.
                properties.ClearTemporary(SavedParts);
                ApplyOffset();

                // Whoever shows or follows the state is told, or a door would stay open for a crystal
                // that has gone dark. Not while the scene itself is being unloaded.
                if (gameObject.scene.isLoaded)
                {
                    Restored?.Invoke(this, StateReset.Settled);
                }
            }

            // Whatever is meant to last is noted now: the object may be unloaded without another chance.
            Persist();
        }

        private void OnDestroy()
        {
            if (registeredForAim)
            {
                AimAssistTargets.Unregister(this);
                registeredForAim = false;
            }

            if (listensToWorld)
            {
                WorldState.Reloaded -= HandleWorldReloaded;
                WorldState.Capturing -= Persist;
                listensToWorld = false;
            }

            if (persistent)
            {
                PersistentIdRegistry.Unregister(persistentId, this);
                persistent = false;
            }

            if (properties != null)
            {
                properties.StateChanged -= HandleStateChanged;
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

            // The saved world only exists while the game runs; in the Editor's edit mode an object is as authored.
            if (!Application.isPlaying)
            {
                return;
            }

            if (!listensToWorld)
            {
                WorldState.Reloaded += HandleWorldReloaded;
                WorldState.Capturing += Persist;
                listensToWorld = true;
            }

            if (!string.IsNullOrEmpty(persistentId))
            {
                persistent = PersistentIdRegistry.TryRegister(persistentId, this);
                if (!persistent)
                {
                    Debug.LogError($"[Threefold] '{name}' has the Persistent Id '{persistentId}', which another object " +
                                   "already uses. Its state is not saved. Give it an id of its own " +
                                   "(Threefold > World Magic > Assign Persistent Ids).", this);
                }
                else if (WorldState.Current.TryGetObject(persistentId, out ObjectRecord record))
                {
                    properties.Load(record, definition.Properties.persisted);
                    ApplyLoadedState();
                }
            }
        }

        /// <summary>The world was reloaded: take what is saved, reset what is temporary.</summary>
        private void HandleWorldReloaded(WorldReload reload)
        {
            if (LeftWithoutNotice())
            {
                return;
            }

            if (properties == null)
            {
                return;
            }

            ObjectRecord record = default;
            if (persistent)
            {
                WorldState.Current.TryGetObject(persistentId, out record);
            }

            properties.Load(record, definition.Properties.persisted);
            ShowLoadedState(reload == WorldReload.Restart ? StateReset.Reloaded : StateReset.Replaced);
        }

        /// <summary>After the state was set as a whole: put the object where and how the state says, and tell whoever shows it.</summary>
        private void ShowLoadedState(StateReset reason)
        {
            ApplyLoadedState();

            // The state was set as a whole: what was on its way to the object belonged to the old one.
            WorldSimulation.Forget(this, this, false);
            awake = false;
            if (IsSwitchedOn && !properties.IsAtRest)
            {
                awake = WorldSimulation.Wake(this);
            }

            Restored?.Invoke(this, reason);
        }

        /// <summary>Makes the object in the scene match a state that was set as a whole. Silent: no event, no effect.</summary>
        private void ApplyLoadedState()
        {
            ApplyOffset();
            spent = properties.IsDestroyed;
            SetCollidersEnabled(!spent);
        }

        private void ApplyOffset()
        {
            if (properties.Kinetic != null && restKnown)
            {
                MoveTarget.position = restPosition + properties.Kinetic.Offset;
            }
        }

        /// <summary>
        /// True if this object was destroyed without OnDestroy (Unity skips it for an object that was
        /// never switched on), yet still listens to the world. It stops listening and gives its
        /// identifier up, so it neither touches a destroyed object nor overwrites what its successor saves.
        /// </summary>
        private bool LeftWithoutNotice()
        {
            if (this != null)
            {
                return false;
            }

            WorldState.Reloaded -= HandleWorldReloaded;
            WorldState.Capturing -= Persist;
            listensToWorld = false;
            if (persistent)
            {
                PersistentIdRegistry.Unregister(persistentId, this);
                persistent = false;
            }

            return true;
        }

        /// <summary>Writes what is meant to last into the world state. Does nothing for an object without an identifier.</summary>
        private void Persist()
        {
            if (LeftWithoutNotice())
            {
                return;
            }

            if (persistent && properties != null)
            {
                WorldState.Current.SetObject(persistentId, properties.Capture(definition.Properties.persisted));
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
                // Cleanup: nothing can hit it, stand on it or be blocked by it any more.
                spent = true;
                SetCollidersEnabled(false);
                onDestroyed.Invoke();
            }

            Persist();
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

        // ------------------------------------------------------------ reactions

        /// <summary>
        /// Hands the reactions that have fallen due to the world simulation. They are queued there and
        /// carried out within its budgets; nothing is delivered from here, so a reaction can never run
        /// back into the object that is sending it.
        /// </summary>
        private void SendReactions()
        {
            int count = properties.PendingEmissionCount;
            if (count == 0)
            {
                return;
            }

            if (Application.isPlaying)
            {
                for (int i = 0; i < count; i++)
                {
                    EmissionRequest request = properties.GetPendingEmission(i);
                    ReactionEmission rule = properties.GetEmissionRule(request.Rule);
                    if (rule != null)
                    {
                        Send(rule, request);
                    }
                }
            }

            properties.ClearPendingEmissions();
        }

        private void Send(ReactionEmission rule, in EmissionRequest request)
        {
            Vector3 origin = AimPoint;
            int found = 0;

            // Linked targets first, in the order they are listed.
            if (rule.toLinkedTargets && linkedTargets != null)
            {
                for (int i = 0; i < linkedTargets.Length && found < MaxReactionTargets; i++)
                {
                    GameObject linked = linkedTargets[i];
                    if (linked == null || !linked.TryGetComponent(out IMagicEffectReceiver receiver) ||
                        ReferenceEquals(receiver, this) || IndexOfTarget(receiver, found) >= 0)
                    {
                        continue;
                    }

                    ReactionTargets[found] = receiver;
                    ReactionPoints[found] = linked.transform.position;
                    ReactionDistances[found] = -1f;
                    found++;
                }
            }

            int linkedCount = found;

            // Then whatever stands within reach, nearest first, so the order does not depend on the physics engine.
            if (rule.radius > 0f)
            {
                int overlaps = Physics.OverlapSphereNonAlloc(origin, rule.radius, ReactionOverlaps, reactionLayers,
                    QueryTriggerInteraction.Ignore);
                Transform self = transform;

                for (int i = 0; i < overlaps; i++)
                {
                    Collider other = ReactionOverlaps[i];
                    ReactionOverlaps[i] = null;
                    if (other == null || other.transform.IsChildOf(self))
                    {
                        continue;
                    }

                    IMagicEffectReceiver receiver = other.GetComponentInParent<IMagicEffectReceiver>();
                    if (receiver == null || ReferenceEquals(receiver, this))
                    {
                        continue;
                    }

                    Vector3 point = other.bounds.ClosestPoint(origin);
                    float distance = (point - origin).sqrMagnitude;
                    int known = IndexOfTarget(receiver, found);

                    // When the list is full, only something nearer than the farthest in it can still
                    // count, so the nearest are kept whatever order the physics engine reports them in.
                    int farthest = -1;
                    if (known < 0 && found >= MaxReactionTargets)
                    {
                        farthest = FarthestInReach(linkedCount, found);
                        if (farthest < 0 || distance >= ReactionDistances[farthest])
                        {
                            continue;
                        }
                    }

                    // A wall stops a reaction as it stops an attack's area; creatures do not shield.
                    Transform targetTransform = receiver is Component component ? component.transform : other.transform;
                    if (AreaStrike.IsBehindCover(self, targetTransform, origin, point, reactionLayers))
                    {
                        continue;
                    }

                    if (known >= 0)
                    {
                        // Several colliders of one target: it counts once, at its nearest point.
                        if (known >= linkedCount && distance < ReactionDistances[known])
                        {
                            ReactionDistances[known] = distance;
                            ReactionPoints[known] = point;
                        }

                        continue;
                    }

                    if (farthest >= 0)
                    {
                        ReactionTargets[farthest] = receiver;
                        ReactionPoints[farthest] = point;
                        ReactionDistances[farthest] = distance;
                        continue;
                    }

                    ReactionTargets[found] = receiver;
                    ReactionPoints[found] = point;
                    ReactionDistances[found] = distance;
                    found++;
                }

                SortByDistance(linkedCount, found);
            }

            for (int i = 0; i < found; i++)
            {
                IMagicEffectReceiver receiver = ReactionTargets[i];
                ReactionTargets[i] = null;

                Vector3 direction = ReactionPoints[i] - origin;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

                var effect = new MagicEffect(rule.effect, rule.magnitude, direction, rule.duration, ReactionPoints[i], gameObject,
                    -1, rule.targets, request.EventId, request.Depth);
                WorldSimulation.React(receiver, effect, rule.priority);
            }
        }

        /// <summary>The farthest of the targets found by reach (after the linked ones), or -1 if there are none.</summary>
        private static int FarthestInReach(int from, int to)
        {
            int farthest = -1;
            for (int i = from; i < to; i++)
            {
                if (farthest < 0 || ReactionDistances[i] > ReactionDistances[farthest])
                {
                    farthest = i;
                }
            }

            return farthest;
        }

        private static int IndexOfTarget(IMagicEffectReceiver receiver, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(ReactionTargets[i], receiver))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Insertion sort of the targets found by distance. A handful of entries; equal distances keep their order.</summary>
        private static void SortByDistance(int from, int to)
        {
            for (int i = from + 1; i < to; i++)
            {
                IMagicEffectReceiver receiver = ReactionTargets[i];
                Vector3 point = ReactionPoints[i];
                float distance = ReactionDistances[i];

                int j = i - 1;
                while (j >= from && ReactionDistances[j] > distance)
                {
                    ReactionTargets[j + 1] = ReactionTargets[j];
                    ReactionPoints[j + 1] = ReactionPoints[j];
                    ReactionDistances[j + 1] = ReactionDistances[j];
                    j--;
                }

                ReactionTargets[j + 1] = receiver;
                ReactionPoints[j + 1] = point;
                ReactionDistances[j + 1] = distance;
            }
        }

        // ------------------------------------------------------------- movement

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
