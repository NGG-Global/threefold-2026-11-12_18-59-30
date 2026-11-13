using System.Collections.Generic;
using Threefold.Combat;
using Threefold.Objectives;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// Coordinates the enemies of a scene. It holds the attack tokens that keep fights readable
    /// (how many enemies may attack at once and how far apart their attacks start), applies the
    /// difficulty to every enemy, tells enemies who the player is and whether they are on screen,
    /// and puts every enemy back when the level restarts. One per scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyDirector : MonoBehaviour
    {
        [Tooltip("The player the enemies fight. If empty, the first Health on the Player team is used.")]
        [SerializeField] private Health player;

        [Tooltip("The camera the player sees through, for keeping attacks from off screen fair. If empty, the main camera is used.")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("Difficulty applied to every enemy. If empty, Normal values are used.")]
        [SerializeField] private EnemyDifficulty difficulty;

        [Tooltip("Optional. Enemies are restored when this session restarts the level.")]
        [SerializeField] private PrototypeSession session;

        [Tooltip("How far outside the screen edge, as a share of the screen, an enemy still counts as visible.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float onScreenMargin = 0.05f;

        private readonly AttackTokenPool tokens = new AttackTokenPool();
        private readonly List<EnemyController> enemies = new List<EnemyController>();
        private EnemyDifficultySettings fallbackSettings;

        public AttackTokenPool Tokens => tokens;
        public IReadOnlyList<EnemyController> Enemies => enemies;
        public EnemyDifficulty Difficulty => difficulty;

        public EnemyDifficultySettings Settings
        {
            get
            {
                if (difficulty != null)
                {
                    return difficulty.Settings;
                }

                return fallbackSettings ?? (fallbackSettings = EnemyDifficultySettings.Normal());
            }
        }

        public Health Player
        {
            get
            {
                if (player == null)
                {
                    var candidates = Health.Active;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (candidates[i] != null && candidates[i].Team == CombatTeam.Player)
                        {
                            player = candidates[i];
                            break;
                        }
                    }
                }

                return player;
            }
        }

        /// <summary>Connects the director. Called by the setup tooling.</summary>
        public void Bind(Health playerHealth, Camera playerCamera, EnemyDifficulty enemyDifficulty, PrototypeSession levelSession)
        {
            player = playerHealth;
            viewCamera = playerCamera;
            difficulty = enemyDifficulty;
            session = levelSession;
        }

        /// <summary>Fills only the references that are empty, keeping any set by hand. Called by the setup tooling.</summary>
        public void BindMissing(Health playerHealth, Camera playerCamera, EnemyDifficulty enemyDifficulty,
            PrototypeSession levelSession)
        {
            if (player == null)
            {
                player = playerHealth;
            }

            if (viewCamera == null)
            {
                viewCamera = playerCamera;
            }

            if (difficulty == null)
            {
                difficulty = enemyDifficulty;
            }

            if (session == null)
            {
                session = levelSession;
            }
        }

        public bool HasPlayer => player != null;
        public bool HasSession => session != null;

        /// <summary>Changes the difficulty for every enemy, at any time.</summary>
        public void SetDifficulty(EnemyDifficulty newDifficulty)
        {
            difficulty = newDifficulty;
            ApplyPacing();

            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] != null)
                {
                    enemies[i].ApplyDifficulty(Settings, false);
                }
            }
        }

        public void Register(EnemyController enemy)
        {
            if (enemy != null && !enemies.Contains(enemy))
            {
                enemies.Add(enemy);
            }
        }

        public void Unregister(EnemyController enemy)
        {
            enemies.Remove(enemy);
        }

        /// <summary>True when a point is in front of the camera and within the screen, with a margin.</summary>
        public bool IsOnScreen(Vector3 worldPosition)
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                {
                    return true;
                }
            }

            Vector3 viewport = viewCamera.WorldToViewportPoint(worldPosition);
            return viewport.z > 0f &&
                   viewport.x >= -onScreenMargin && viewport.x <= 1f + onScreenMargin &&
                   viewport.y >= -onScreenMargin && viewport.y <= 1f + onScreenMargin;
        }

        /// <summary>Puts every enemy back at its post and clears all attack tokens.</summary>
        public void RestoreAll()
        {
            tokens.Clear();

            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] != null)
                {
                    enemies[i].Restore();
                }
            }
        }

        private void Awake()
        {
            ApplyPacing();
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.Restarted += RestoreAll;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.Restarted -= RestoreAll;
            }
        }

        private void Update()
        {
            tokens.Update(Time.deltaTime);
        }

        private void ApplyPacing()
        {
            EnemyDifficultySettings settings = Settings;
            tokens.Configure(settings.maxSimultaneousAttackers, settings.maxSimultaneousMelee,
                settings.minSecondsBetweenAttacks, settings.offscreenAttackDelay);
        }
    }
}
