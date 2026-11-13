using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.World
{
    /// <summary>
    /// A gateway to another level: when the player walks into it, that scene is loaded. It checks
    /// the player's distance rather than relying on trigger colliders, so it works with any kind
    /// of character body. If the target scene is not in the build, the portal hides itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenePortal : MonoBehaviour
    {
        [Tooltip("Name of the scene to load, as listed in Build Settings.")]
        [SerializeField] private string sceneName = "";

        [Tooltip("The player.")]
        [SerializeField] private Transform player;

        [Tooltip("How close the player must come, in metres, measured along the ground.")]
        [Min(0.1f)]
        [SerializeField] private float radius = 1.1f;

        [Tooltip("Hidden when the target scene cannot be loaded.")]
        [SerializeField] private GameObject visuals;

        [Tooltip("Seconds after the level starts before the portal works, so arriving next to it does not send the player straight back.")]
        [Min(0f)]
        [SerializeField] private float armDelaySeconds = 1.5f;

        private bool available;
        private bool loading;
        private float armedAt;

        public string SceneName => sceneName;

        /// <summary>Connects the portal. Called by the setup tooling.</summary>
        public void Bind(string targetScene, Transform playerTransform, GameObject portalVisuals)
        {
            sceneName = targetScene;
            player = playerTransform;
            visuals = portalVisuals;
        }

        private void Start()
        {
            available = !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);
            armedAt = Time.time + armDelaySeconds;

            if (!available)
            {
                Debug.LogWarning($"Portal '{name}' leads to '{sceneName}', which is not in Build Settings, so it is hidden.", this);
                if (visuals != null)
                {
                    visuals.SetActive(false);
                }
            }
        }

        private void Update()
        {
            if (!available || loading || player == null || Time.time < armedAt)
            {
                return;
            }

            Vector3 offset = player.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > radius * radius || Mathf.Abs(player.position.y - transform.position.y) > 2f)
            {
                return;
            }

            loading = true;
            SceneManager.LoadScene(sceneName);
        }
    }
}
