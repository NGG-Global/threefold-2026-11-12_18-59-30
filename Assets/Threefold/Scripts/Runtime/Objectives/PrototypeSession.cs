using System;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Threefold.Objectives
{
    /// <summary>
    /// Restarts the prototype level: revives the targets, refills the player's health and mana,
    /// returns the player to the starting point and clears the objective. Nothing is reloaded, so
    /// a restart is instant, also on a phone.
    ///
    /// It also handles the player's defeat: movement and attacks stop until the next restart, and
    /// <see cref="PlayerDefeated"/> lets the display offer a restart.
    ///
    /// On desktop the R key (or Start on a gamepad) restarts at any time. On touch devices the
    /// completion screen has a restart button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeSession : MonoBehaviour
    {
        [SerializeField] private DefeatTargetsObjective objective;
        [SerializeField] private Health playerHealth;
        [SerializeField] private Mana playerMana;
        [SerializeField] private ThirdPersonMotor playerMotor;
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Tooltip("Restart with the R key or the gamepad's Start button.")]
        [SerializeField] private bool restartShortcutEnabled = true;

        [Tooltip("Optional. The prefix of this level's persistent ids and puzzle keys. When set, a restart after the " +
                 "objective is complete replays the level from its beginning: what was saved under the prefix is " +
                 "forgotten and the checkpoints with it. Other levels' saved state is kept.")]
        [SerializeField] private string replayScope = string.Empty;

        private Vector3 startPosition;
        private Quaternion startRotation = Quaternion.identity;
        private bool startKnown;
        private Vector3 respawnPosition;
        private Quaternion respawnRotation = Quaternion.identity;
        private bool hasRespawn;
        private PlayerAbilityInput playerAbilityInput;

        /// <summary>Raised after the level has been restarted.</summary>
        public event Action Restarted;

        /// <summary>Raised after the player was put back at the respawn point without a restart (a hazard).</summary>
        public event Action Respawned;

        /// <summary>Raised when the player is defeated.</summary>
        public event Action PlayerDefeated;

        /// <summary>True from the player's defeat until the next restart.</summary>
        public bool IsPlayerDefeated { get; private set; }

        public DefeatTargetsObjective Objective => objective;

        /// <summary>True while a checkpoint has set where the player comes back; otherwise the player comes back at the start.</summary>
        public bool HasRespawnPoint => hasRespawn;

        public string ReplayScope
        {
            get => replayScope;
            set => replayScope = value ?? string.Empty;
        }

        /// <summary>Where the player comes back on a restart or after a hazard: a checkpoint's spot, or the start.</summary>
        public Vector3 RespawnPosition => hasRespawn ? respawnPosition : startPosition;

        /// <summary>Sets where the player comes back. Called by a level's checkpoints.</summary>
        public void SetRespawnPoint(Vector3 position, Quaternion rotation)
        {
            respawnPosition = position;
            respawnRotation = rotation;
            hasRespawn = true;
        }

        /// <summary>The player comes back at the start again.</summary>
        public void ClearRespawnPoint()
        {
            hasRespawn = false;
        }

        /// <summary>
        /// Puts the player back at the respawn point, with nothing else reset: no healing, no reload. For a
        /// hazard such as molten slag, after it has dealt its damage.
        /// </summary>
        public void Respawn()
        {
            PlacePlayer();
            Respawned?.Invoke();
        }

        /// <summary>
        /// Puts the saved world back to the last checkpoint, then restarts there. Lasting changes made since
        /// the checkpoint are undone (a slag plug broken after it stands again), which lets a tester try another
        /// way through. Returns false if no checkpoint was taken.
        /// </summary>
        public bool RetryFromCheckpoint()
        {
            if (!WorldState.RevertToCheckpoint())
            {
                return false;
            }

            Restart();
            return true;
        }

        /// <summary>Connects the session. Called by the setup tooling.</summary>
        public void Bind(DefeatTargetsObjective sessionObjective, Health health, Mana mana, ThirdPersonMotor motor,
            ThirdPersonCamera rig)
        {
            objective = sessionObjective;
            playerHealth = health;
            playerMana = mana;
            playerMotor = motor;
            cameraRig = rig;
        }

        /// <summary>
        /// Starts a new attempt, from the furthest checkpoint reached if a level has checkpoints. After the
        /// objective is complete, a level with a replay scope starts again from its beginning instead.
        /// </summary>
        public void Restart()
        {
            bool replay = objective != null && objective.IsComplete && !string.IsNullOrEmpty(replayScope);
            if (replay)
            {
                // Forgotten first, so that the objective's goals are no longer reached when progress is cleared below.
                hasRespawn = false;
                WorldState.ForgetScope(replayScope);
            }

            if (objective != null)
            {
                var targets = objective.Targets;
                for (int i = 0; i < targets.Count; i++)
                {
                    Health target = targets[i];
                    if (target == null)
                    {
                        continue;
                    }

                    // A dummy or an enemy also has to bring back its body, colliders and position.
                    if (target.TryGetComponent(out IRestorable restorable))
                    {
                        restorable.Restore();
                    }
                    else
                    {
                        target.Revive();
                    }
                }
            }

            if (playerHealth != null)
            {
                playerHealth.Revive();

                // A burn caught from the world does not carry over into the new attempt.
                if (playerHealth.TryGetComponent(out PlayerEffectReceiver hazards))
                {
                    hazards.Restore();
                }
            }

            if (playerMana != null)
            {
                playerMana.Refill();
            }

            PlacePlayer();

            if (objective != null)
            {
                objective.ResetProgress();
            }

            // The world takes its saved state again: what is persistent stays as it is, and everything
            // temporary (heat, charge, braces, a glide) is reset.
            WorldState.ReloadWorld();

            Restarted?.Invoke();
        }

        private void PlacePlayer()
        {
            if (playerMotor != null && hasRespawn)
            {
                playerMotor.Teleport(respawnPosition, respawnRotation);
            }
            else if (playerMotor != null && startKnown)
            {
                playerMotor.Teleport(startPosition, startRotation);
            }

            if (cameraRig != null)
            {
                cameraRig.SnapToTarget();
            }
        }

        /// <summary>
        /// Forgets everything that was saved about the world, on disk as well, and starts a new
        /// attempt: solved puzzles are unsolved, opened gates closed, destroyed barriers whole.
        /// </summary>
        public void NewGame()
        {
            WorldState.NewGame();
            Restart();
        }

        private void Awake()
        {
            if (playerHealth != null)
            {
                playerAbilityInput = playerHealth.GetComponent<PlayerAbilityInput>();
            }
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.Defeated += HandlePlayerDefeated;
                playerHealth.Revived += HandlePlayerRevived;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Defeated -= HandlePlayerDefeated;
                playerHealth.Revived -= HandlePlayerRevived;
            }
        }

        private void HandlePlayerDefeated()
        {
            IsPlayerDefeated = true;
            SetPlayerControl(false);
            PlayerDefeated?.Invoke();
        }

        private void HandlePlayerRevived()
        {
            IsPlayerDefeated = false;
            SetPlayerControl(true);
        }

        private void SetPlayerControl(bool on)
        {
            if (playerMotor != null)
            {
                playerMotor.MovementInputEnabled = on;
            }

            if (playerAbilityInput != null)
            {
                playerAbilityInput.enabled = on;
            }
        }

        private void Start()
        {
            if (playerMotor != null)
            {
                startPosition = playerMotor.transform.position;
                startRotation = playerMotor.transform.rotation;
                startKnown = true;
            }
        }

        private void Update()
        {
            if (!restartShortcutEnabled)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;

            bool pressed = (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                           || (gamepad != null && gamepad.startButton.wasPressedThisFrame);

            if (pressed)
            {
                Restart();
            }
        }
    }
}
