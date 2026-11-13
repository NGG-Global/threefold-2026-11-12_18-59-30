using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Marks the root object of a puzzle built by a setup command, so the command finds it again
    /// and completes it instead of building a second copy. It has no behaviour of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleSite : MonoBehaviour
    {
        [Tooltip("Identifies the puzzle to its setup command.")]
        [SerializeField] private string siteId = string.Empty;

        public string SiteId
        {
            get => siteId;
            set => siteId = value ?? string.Empty;
        }
    }
}
