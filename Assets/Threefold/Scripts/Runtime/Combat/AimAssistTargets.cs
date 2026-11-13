using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Something other than a character that aim assist may pull the aim onto, such as a crystal
    /// that has to be struck with a spell. Characters are found through <see cref="Health"/>.
    /// </summary>
    public interface IAimAssistTarget
    {
        /// <summary>Whether aim assist should consider this target at the moment.</summary>
        bool IsAimAssistAvailable { get; }

        /// <summary>World position to aim at.</summary>
        Vector3 AimPoint { get; }
    }

    /// <summary>Every registered <see cref="IAimAssistTarget"/>. Targets register themselves while enabled.</summary>
    public static class AimAssistTargets
    {
        private static readonly List<IAimAssistTarget> ActiveList = new List<IAimAssistTarget>();

        /// <summary>Do not modify.</summary>
        public static IReadOnlyList<IAimAssistTarget> Active => ActiveList;

        public static void Register(IAimAssistTarget target)
        {
            if (target != null && !ActiveList.Contains(target))
            {
                ActiveList.Add(target);
            }
        }

        public static void Unregister(IAimAssistTarget target)
        {
            ActiveList.Remove(target);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveList.Clear();
        }
    }
}
