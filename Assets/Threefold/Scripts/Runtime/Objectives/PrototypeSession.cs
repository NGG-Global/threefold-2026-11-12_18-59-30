using System;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
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

        private Vector3 startPosition;
        private Quaternion startRotation = Quaternion.identity;
        private bool startKnown;
        private PlayerAbilityInput playerAbilityInput;

        /// <summary>Raised after the level has been restarted.</summary>
        public event Action Restarted;

        /// <summary>Raised when the player is defeated.</summary>
        public event Action PlayerDefeated;

        /// <summary>True from the player's defeat until the next restart.</summary>
        public bool IsPlayerDefeated { get; private set; }

        public DefeatTargetsObjective Objective => objective;

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

        /// <summary>Starts a new attempt.</summary>
        public void Restart()
        {
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
            }

            if (playerMana != null)
            {
                playerMana.Refill();
            }

            if (playerMotor != null && startKnown)
            {
                playerMotor.Teleport(startPosition, startRotation);
            }

            if (cameraRig != null)
            {
                cameraRig.SnapToTarget();
            }

            if (objective != null)
            {
                objective.ResetProgress();
            }

            Restarted?.Invoke();
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
