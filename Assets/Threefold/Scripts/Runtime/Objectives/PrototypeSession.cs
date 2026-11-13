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

        /// <summary>Raised after the level has been restarted.</summary>
        public event Action Restarted;

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

                    // A dummy also has to bring its body and collider back, not only its health.
                    if (target.TryGetComponent(out TrainingDummy dummy))
                    {
                        dummy.Restore();
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
