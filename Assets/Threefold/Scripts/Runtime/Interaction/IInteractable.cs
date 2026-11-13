using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Interaction
{
    /// <summary>
    /// Anything the player can use directly with the interact control: a door, a lever, a chest.
    /// The player's <see cref="Interactor"/> picks the nearest one in front of the character and
    /// the prompt shows its verb. Implementations register with <see cref="InteractionRegistry"/>
    /// while they are enabled, so finding them costs no physics queries.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>One short word shown in the prompt, such as "Open" or "Pull".</summary>
        string InteractionVerb { get; }

        /// <summary>World position the prompt points at and that distance is measured to.</summary>
        Vector3 InteractionPoint { get; }

        /// <summary>
        /// The object whose colliders belong to this interactable. They do not block the line of
        /// sight to its interaction point.
        /// </summary>
        Transform InteractionRoot { get; }

        /// <summary>Whether it can be used now. Something that cannot is not offered at all.</summary>
        bool CanInteract(GameObject interactor);

        void Interact(GameObject interactor);
    }

    /// <summary>Every enabled interactable. Interactables add and remove themselves.</summary>
    public static class InteractionRegistry
    {
        private static readonly List<IInteractable> ActiveList = new List<IInteractable>();

        /// <summary>Do not modify.</summary>
        public static IReadOnlyList<IInteractable> Active => ActiveList;

        public static void Register(IInteractable interactable)
        {
            if (interactable != null && !ActiveList.Contains(interactable))
            {
                ActiveList.Add(interactable);
            }
        }

        public static void Unregister(IInteractable interactable)
        {
            ActiveList.Remove(interactable);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveList.Clear();
        }
    }
}
