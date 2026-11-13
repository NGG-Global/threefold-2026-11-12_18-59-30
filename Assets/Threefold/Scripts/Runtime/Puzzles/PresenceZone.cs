using Threefold.Characters;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Active while the player (or, if allowed, another character or a heavy object) is inside a
    /// box: the top of a lookout, a doorway, a ring of runes. The box is checked with a physics
    /// query every frame rather than with trigger messages, because traversal switches the
    /// character's collider off and on, and leaving a trigger that way is not reported reliably.
    /// While its collider is off (hanging, climbing, vaulting), the character is not detected.
    /// </summary>
    [DisallowMultipleComponent]
    public class PresenceZone : PuzzleElement
    {
        [Header("Detection")]
        [Tooltip("Centre of the detection box, in this object's space.")]
        [SerializeField] private Vector3 detectionCentre = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Size of the detection box, in this object's space.")]
        [SerializeField] private Vector3 detectionSize = new Vector3(1.2f, 1f, 1.2f);

        [Tooltip("The player counts: a CharacterController with a Third Person Motor.")]
        [SerializeField] private bool detectPlayer = true;

        [Tooltip("Other characters with a CharacterController count as well.")]
        [SerializeField] private bool detectOtherCharacters;

        [Tooltip("Physics objects with a non-kinematic Rigidbody of at least the minimum mass count as well.")]
        [SerializeField] private bool detectPhysicsObjects;

        [Min(0f)]
        [SerializeField] private float minimumMass = 5f;

        [Tooltip("Layers that are checked.")]
        [SerializeField] private LayerMask detectionLayers = Physics.AllLayers;

        [Tooltip("Seconds something must stay before the zone activates.")]
        [Min(0f)]
        [SerializeField] private float activationDelay;

        [Tooltip("Seconds the zone must be empty before it deactivates. Stops flicker at the edges.")]
        [Min(0f)]
        [SerializeField] private float releaseDelay = 0.2f;

        private readonly Collider[] overlaps = new Collider[16];
        private PresenceDebounce debounce;

        public Vector3 DetectionCentre => detectionCentre;
        public Vector3 DetectionSize => detectionSize;

        /// <summary>A plate's state comes from what stands on it, so only a locked state is remembered.</summary>
        protected override bool PersistsUnlockedState => false;

        /// <summary>Sets the detection box. Called by the setup tooling.</summary>
        public void ConfigureDetection(Vector3 centre, Vector3 size, float delayOn, float delayOff)
        {
            detectionCentre = centre;
            detectionSize = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            activationDelay = Mathf.Max(0f, delayOn);
            releaseDelay = Mathf.Max(0f, delayOff);
        }

        /// <summary>Chooses what counts as present. Called by the setup tooling.</summary>
        public void ConfigureFilter(bool player, bool otherCharacters, bool physicsObjects, float heavierThan)
        {
            detectPlayer = player;
            detectOtherCharacters = otherCharacters;
            detectPhysicsObjects = physicsObjects;
            minimumMass = Mathf.Max(0f, heavierThan);
        }

        protected virtual void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>Checks the box and updates the state. Runs every frame; public so tests can drive it.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsLocked)
            {
                bool present = debounce.Update(IsSomethingPresent(), deltaTime, activationDelay, releaseDelay);
                SetElementActive(present);
            }

            OnTicked(deltaTime);
        }

        /// <summary>True if something that counts is inside the box right now.</summary>
        public bool IsSomethingPresent()
        {
            Vector3 scale = transform.lossyScale;
            Vector3 halfExtents = new Vector3(
                Mathf.Abs(detectionSize.x * scale.x),
                Mathf.Abs(detectionSize.y * scale.y),
                Mathf.Abs(detectionSize.z * scale.z)) * 0.5f;

            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(detectionCentre), halfExtents, overlaps,
                transform.rotation, detectionLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (Counts(overlaps[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Runs after every check, for subclasses that animate.</summary>
        protected virtual void OnTicked(float deltaTime)
        {
        }

        protected override void OnRestored()
        {
            debounce.Reset(IsActive);
        }

        private bool Counts(Collider other)
        {
            if (other is CharacterController)
            {
                bool isPlayer = other.TryGetComponent(out ThirdPersonMotor _);
                return isPlayer ? detectPlayer : detectOtherCharacters;
            }

            if (!detectPhysicsObjects)
            {
                return false;
            }

            Rigidbody body = other.attachedRigidbody;
            return body != null && !body.isKinematic && body.mass >= minimumMass;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = IsActive ? new Color(0.5f, 0.8f, 1f, 0.5f) : new Color(1f, 0.85f, 0.4f, 0.4f);
            Gizmos.DrawWireCube(detectionCentre, detectionSize);
        }
    }
}
