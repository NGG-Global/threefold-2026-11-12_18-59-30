using Threefold.Combat;
using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// Makes one enemy's defeat part of a level's lasting progress, like an opened gate. The element
    /// becomes active when the enemy is defeated, locks, and is remembered under its persistence key in
    /// the one world state, so it is saved with everything else. A door can use it as its input (the
    /// casting floor's gate opens when its Guardian falls).
    ///
    /// While the defeat is remembered, a restart does not bring the enemy back
    /// (<see cref="EnemyController.StaysDefeated"/>), and when the level is loaded with the defeat
    /// already saved the enemy is simply not there: nothing is played again. When the saved state is
    /// replaced and the defeat is no longer in it (a new game, a return to an earlier checkpoint), the
    /// enemy comes back at its post.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersistentDefeat : PuzzleElement
    {
        [Tooltip("The enemy whose defeat is remembered.")]
        [SerializeField] private EnemyController enemy;

        private Health watched;
        private bool started;

        public EnemyController Enemy => enemy;

        /// <summary>Connects the enemy. Called by the setup tooling.</summary>
        public void Bind(EnemyController target)
        {
            bool listening = isActiveAndEnabled;
            if (listening)
            {
                Unsubscribe();
            }

            enemy = target;
            if (listening)
            {
                Subscribe();
            }
        }

        /// <summary>Only a defeat, which is locked, is worth remembering.</summary>
        protected override bool PersistsUnlockedState => false;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Start()
        {
            started = true;
            ApplyRemembered();
        }

        protected override void OnRestored()
        {
            // During Awake the enemy may not be set up yet; Start applies what was remembered.
            if (started)
            {
                ApplyRemembered();
            }
        }

        private void Subscribe()
        {
            if (enemy != null && watched == null)
            {
                watched = enemy.Health;
                if (watched != null)
                {
                    watched.Defeated += HandleDefeated;
                }
            }
        }

        private void Unsubscribe()
        {
            if (watched != null)
            {
                watched.Defeated -= HandleDefeated;
                watched = null;
            }
        }

        private void HandleDefeated()
        {
            if (enemy != null)
            {
                enemy.StaysDefeated = true;
            }

            SetElementActive(true);
            Lock();
        }

        /// <summary>Makes the enemy match what is remembered, silently.</summary>
        private void ApplyRemembered()
        {
            if (enemy == null || !Application.isPlaying)
            {
                return;
            }

            bool defeated = IsLocked && IsActive;
            enemy.StaysDefeated = defeated;

            if (defeated)
            {
                // Saved as defeated but standing, as after a load: it is simply not there.
                if (enemy.gameObject.activeSelf && enemy.Health != null && enemy.Health.IsAlive)
                {
                    enemy.gameObject.SetActive(false);
                }

                return;
            }

            // No longer remembered: it comes back at its post.
            bool wasHidden = !enemy.gameObject.activeSelf;
            if (wasHidden)
            {
                enemy.gameObject.SetActive(true);
            }

            if (wasHidden || (enemy.Health != null && !enemy.Health.IsAlive))
            {
                enemy.Restore();
            }
        }
    }
}
