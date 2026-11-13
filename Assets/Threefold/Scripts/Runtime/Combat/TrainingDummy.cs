using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// A stationary practice target. When its health runs out it bursts, disappears, and returns at
    /// full health after a short time, so there is always something to hit.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class TrainingDummy : MonoBehaviour, IRestorable
    {
        private const int MaxDefeatEffects = 4;

        [Tooltip("Object holding the dummy's visible parts. Hidden while the dummy is defeated.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("Colliders switched off while the dummy is defeated, so shots pass through.")]
        [SerializeField] private Collider[] colliders = new Collider[0];

        [Tooltip("Optional. Effect spawned when the dummy is defeated.")]
        [SerializeField] private PooledObject defeatEffectPrefab;

        [Tooltip("Whether a defeated dummy returns by itself. Objective targets stay down until the level restarts.")]
        [SerializeField] private bool respawns = true;

        [Tooltip("Seconds until a defeated dummy returns.")]
        [Min(0.1f)]
        [SerializeField] private float respawnSeconds = 4f;

        private Health health;
        private float respawnRemaining;
        private bool down;

        /// <summary>Connects the dummy's parts. Called by the setup tooling.</summary>
        public void Bind(GameObject visuals, Collider[] bodyColliders, PooledObject defeatEffect)
        {
            visualRoot = visuals;
            colliders = bodyColliders ?? new Collider[0];
            defeatEffectPrefab = defeatEffect;
        }

        public bool Respawns
        {
            get => respawns;
            set => respawns = value;
        }

        /// <summary>Brings the dummy back at full health immediately, whether or not it was defeated.</summary>
        public void Restore()
        {
            if (health == null)
            {
                health = GetComponent<Health>();
            }

            down = false;
            SetPresent(true);
            health.Revive();
        }

        private void Awake()
        {
            health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            health.Defeated += HandleDefeated;
        }

        private void OnDisable()
        {
            health.Defeated -= HandleDefeated;
        }

        private void Update()
        {
            if (!down || !respawns)
            {
                return;
            }

            respawnRemaining -= Time.deltaTime;
            if (respawnRemaining <= 0f)
            {
                down = false;
                SetPresent(true);
                health.Revive();
            }
        }

        private void HandleDefeated()
        {
            down = true;
            respawnRemaining = respawnSeconds;

            if (defeatEffectPrefab != null)
            {
                Pools.Spawn(defeatEffectPrefab, health.AimPoint, Quaternion.identity, MaxDefeatEffects);
            }

            SetPresent(false);
        }

        private void SetPresent(bool present)
        {
            if (visualRoot != null)
            {
                visualRoot.SetActive(present);
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = present;
                }
            }
        }
    }
}
