using UnityEngine;

namespace Threefold.Persistence
{
    /// <summary>
    /// Writes the world state to disk when it has changed: a few seconds after the last change, and
    /// at once when the app is paused or closed. On a phone the pause is the moment that counts,
    /// because the app may be ended afterwards without another call.
    ///
    /// It exists only while saving to disk is switched on, is created by <see cref="WorldStateBootstrap"/>,
    /// and has an update only while there is something unsaved.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class WorldStateSaver : MonoBehaviour
    {
        private static WorldStateSaver instance;
        private float sinceChange;

        /// <summary>Seconds after a change before it is written. Further changes do not push the write back.</summary>
        public static float SaveInterval { get; set; } = 5f;

        public static WorldStateSaver Existing => instance;

        /// <summary>Creates the saver if there is none. Called once at start-up when saving to disk is on.</summary>
        public static WorldStateSaver Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            var host = new GameObject("WorldStateSaver");
            DontDestroyOnLoad(host);
            return host.AddComponent<WorldStateSaver>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            WorldState.Current.Changed += HandleChanged;
            enabled = WorldState.IsDirty;
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            WorldState.Current.Changed -= HandleChanged;
            instance = null;
        }

        private void Update()
        {
            if (!WorldState.IsDirty)
            {
                sinceChange = 0f;
                enabled = false;
                return;
            }

            // Real time, so a paused game still saves what changed before the pause.
            sinceChange += Time.unscaledDeltaTime;
            if (sinceChange >= Mathf.Max(0.5f, SaveInterval))
            {
                Save();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Save();
            }
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void HandleChanged()
        {
            enabled = true;
        }

        private void Save()
        {
            sinceChange = 0f;
            if (!WorldState.Save())
            {
                Debug.LogWarning($"[Threefold] The world state could not be saved: {WorldState.LastSaveProblem}");
            }
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            SaveInterval = 5f;
        }
    }
}
