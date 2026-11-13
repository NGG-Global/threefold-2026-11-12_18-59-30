using System;
using Threefold.Characters;
using Threefold.InputHandling;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Threefold.Interaction
{
    /// <summary>
    /// Lets the player use things in the world. Each frame it picks the best interactable within
    /// reach (nearest and most in front of the character, with nothing in the way) and, when the
    /// interact control is pressed, uses it. The prompt on screen shows the current choice.
    ///
    /// Nothing is offered while the character hangs, climbs, vaults or is in the air.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] private CharacterInputSource inputSource;

        [Header("Reach")]
        [Tooltip("Greatest horizontal distance to an interaction point, in metres.")]
        [Min(0.1f)]
        [SerializeField] private float reach = 2f;

        [Tooltip("Greatest angle between the character's facing and the interaction point, in degrees.")]
        [Range(10f, 180f)]
        [SerializeField] private float maxAngle = 75f;

        [Tooltip("Within this distance the angle does not matter, in metres.")]
        [Min(0f)]
        [SerializeField] private float closeDistance = 0.9f;

        [Tooltip("Lowest interaction point that can be reached, relative to the feet, in metres.")]
        [SerializeField] private float minHeight = -0.6f;

        [Tooltip("Highest interaction point that can be reached, relative to the feet, in metres.")]
        [SerializeField] private float maxHeight = 2.4f;

        [Header("Line of sight")]
        [Tooltip("Layers that can block the way to an interactable.")]
        [SerializeField] private LayerMask blockingLayers = Physics.DefaultRaycastLayers;

        [Tooltip("Height the line of sight starts from, above the feet, in metres.")]
        [Min(0f)]
        [SerializeField] private float eyeHeight = 1.3f;

        private readonly RaycastHit[] hits = new RaycastHit[8];

        private IInteractable focus;
        private TraversalController traversal;
        private ThirdPersonMotor motor;

        /// <summary>Raised when the interactable on offer changes. The argument may be null.</summary>
        public event Action<IInteractable> FocusChanged;

        /// <summary>Raised after an interactable has been used.</summary>
        public event Action<IInteractable> Interacted;

        /// <summary>What the interact control would use now, or null.</summary>
        public IInteractable Focus => focus;

        public CharacterInputSource InputSource
        {
            get => inputSource;
            set => inputSource = value;
        }

        public float Reach => reach;

        private void Awake()
        {
            traversal = GetComponent<TraversalController>();
            motor = GetComponent<ThirdPersonMotor>();
        }

        private void OnDisable()
        {
            SetFocus(null);
        }

        private void Update()
        {
            SetFocus(CanAct() ? FindBest() : null);

            if (focus != null && inputSource != null && inputSource.InteractPressedThisFrame)
            {
                TryInteract();
            }
        }

        /// <summary>Uses the interactable on offer, if there is one and it can be used.</summary>
        /// <returns>True if something was used.</returns>
        public bool TryInteract()
        {
            if (focus == null || IsDestroyed(focus) || !focus.CanInteract(gameObject))
            {
                return false;
            }

            IInteractable used = focus;
            used.Interact(gameObject);
            Interacted?.Invoke(used);
            return true;
        }

        private bool CanAct()
        {
            if (traversal != null && (traversal.IsHanging || traversal.IsMoving))
            {
                return false;
            }

            return motor == null || motor.IsGrounded;
        }

        private IInteractable FindBest()
        {
            var reachSettings = new InteractionReach(reach, maxAngle, closeDistance, minHeight, maxHeight);
            Vector3 feet = transform.position;
            Vector3 forward = transform.forward;

            IInteractable best = null;
            float bestScore = float.PositiveInfinity;
            var candidates = InteractionRegistry.Active;

            for (int i = 0; i < candidates.Count; i++)
            {
                IInteractable candidate = candidates[i];
                if (candidate == null || IsDestroyed(candidate) || !candidate.CanInteract(gameObject))
                {
                    continue;
                }

                if (!InteractionRules.TryScore(feet, forward, candidate.InteractionPoint, reachSettings, out float score)
                    || score >= bestScore)
                {
                    continue;
                }

                if (HasLineOfSight(candidate))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>True if nothing but the interactable itself lies between the character's eyes and its interaction point.</summary>
        private bool HasLineOfSight(IInteractable candidate)
        {
            Vector3 from = transform.position + Vector3.up * eyeHeight;
            Vector3 to = candidate.InteractionPoint;
            Vector3 direction = to - from;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            Transform root = candidate.InteractionRoot;
            int count = Physics.RaycastNonAlloc(from, direction / distance, hits, distance, blockingLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].transform;
                if (hit.IsChildOf(transform) || (root != null && hit.IsChildOf(root)))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void SetFocus(IInteractable next)
        {
            if (ReferenceEquals(focus, next))
            {
                return;
            }

            focus = next;
            FocusChanged?.Invoke(focus);
        }

        private static bool IsDestroyed(IInteractable interactable)
        {
            return interactable is Object unityObject && unityObject == null;
        }
    }
}
