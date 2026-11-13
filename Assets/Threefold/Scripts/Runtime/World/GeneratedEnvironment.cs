using UnityEngine;

namespace Threefold.World
{
    /// <summary>
    /// Marks the root object of an environment that was built by a setup command. The command
    /// deletes and rebuilds only an object carrying this marker, so anything placed elsewhere in
    /// the scene by hand is never touched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedEnvironment : MonoBehaviour
    {
        [Tooltip("Name of the generator that built this environment.")]
        [SerializeField] private string generator = string.Empty;

        public string Generator
        {
            get => generator;
            set => generator = value ?? string.Empty;
        }
    }
}
