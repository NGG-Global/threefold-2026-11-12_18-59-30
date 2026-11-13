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
    public sealed class TrainingDummy : MonoBehaviour, IRestorable, IHeatReceiver
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

        [Tooltip("How the dummy responds to heat from flame attacks. Straw catches fire easily. It cannot be " +
                 "pushed: it stands on a post.")]
        [SerializeField] private HeatSettings heat = new HeatSettings { susceptibility = 1.4f };

        private Health health;
        private HeatCarrier heatCarrier;
        private float respawnRemaining;
        private bool down;

        public float HeatFraction => heatCarrier != null ? heatCarrier.Meter.Fraction : 0f;

        public bool IsBurning => heatCarrier != null && heatCarrier.Meter.IsBurning;

        public HeatSettings Heat => heat;

        public bool ReceiveHeat(float amount, GameObject source)
        {
            return !down && Carrier.Receive(amount, source);
        }

        private HeatCarrier Carrier
        {
            get
            {
                if (heatCarrier == null)
                {
                    heatCarrier = new HeatCarrier(heat);
                    heatCarrier.Bind(health != null ? health : GetComponent<Health>());
                }

                return heatCarrier;
            }
        }

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
            heatCarrier?.Clear();
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
            if (!down)
            {
                heatCarrier?.Tick(Time.deltaTime);
            }

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
            heatCarrier?.Clear();

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
