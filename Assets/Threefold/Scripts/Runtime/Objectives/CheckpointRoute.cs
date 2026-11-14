using System;
using System.Collections.Generic;
using Threefold.Combat;
using Threefold.Persistence;
using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.Objectives
{
    /// <summary>
    /// A level's checkpoints and hazards, and the one update that checks the player against them.
    ///
    /// - The furthest checkpoint reached is where a restart, a defeat or a hazard puts the player
    ///   (<see cref="PrototypeSession.SetRespawnPoint"/>). Reaching a further one also takes the world
    ///   state's checkpoint (<see cref="WorldState.SaveCheckpoint"/>), which "retry from checkpoint" returns to.
    /// - When the level loads, the furthest checkpoint remembered in the saved world state is current
    ///   again and the player starts there.
    /// - When the saved state is replaced as a whole (a new game, a replay, a return to a checkpoint), the
    ///   current checkpoint and the session's respawn point are worked out again at once from what the
    ///   store remembers, so a restart that follows puts the player in the right place.
    ///
    /// It looks only at the player's position against a few boxes: no physics and no allocation per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CheckpointRoute : MonoBehaviour
    {
        [SerializeField] private PrototypeSession session;
        [SerializeField] private Transform player;
        [SerializeField] private Health playerHealth;

        [Tooltip("The level's checkpoints. Their order numbers must be 0 to n-1, each once.")]
        [SerializeField] private Checkpoint[] checkpoints = new Checkpoint[0];

        [SerializeField] private HazardVolume[] hazards = new HazardVolume[0];

        private CheckpointProgress progress;
        private Checkpoint[] byOrder = new Checkpoint[0];
        private bool started;

        /// <summary>Raised when the player reaches a checkpoint for the first time.</summary>
        public event Action<Checkpoint> CheckpointReached;

        public PrototypeSession Session => session;
        public Transform Player => player;
        public int CheckpointCount => checkpoints != null ? checkpoints.Length : 0;
        public int HazardCount => hazards != null ? hazards.Length : 0;

        /// <summary>The furthest checkpoint reached, or null before the first.</summary>
        public Checkpoint Current
        {
            get
            {
                if (progress == null || !progress.HasCurrent || progress.Current >= byOrder.Length)
                {
                    return null;
                }

                return byOrder[progress.Current];
            }
        }

        public Checkpoint GetCheckpoint(int index) => checkpoints[index];
        public HazardVolume GetHazard(int index) => hazards[index];

        /// <summary>Connects the route. Called by the setup tooling.</summary>
        public void Bind(PrototypeSession owningSession, Transform playerTransform, Health health, Checkpoint[] levelCheckpoints,
            HazardVolume[] levelHazards)
        {
            session = owningSession;
            player = playerTransform;
            playerHealth = health;
            checkpoints = levelCheckpoints ?? new Checkpoint[0];
            hazards = levelHazards ?? new HazardVolume[0];
            Index();
        }

        /// <summary>
        /// Fills what is missing and keeps what is set: empty references are filled, checkpoints and hazards
        /// that are not listed yet are added, and entries whose object was deleted are dropped. Called by the
        /// setup tooling. Returns true if anything changed.
        /// </summary>
        public bool FillMissing(PrototypeSession owningSession, Transform playerTransform, Health health, Checkpoint[] levelCheckpoints,
            HazardVolume[] levelHazards)
        {
            bool changed = false;
            if (session == null && owningSession != null) { session = owningSession; changed = true; }
            if (player == null && playerTransform != null) { player = playerTransform; changed = true; }
            if (playerHealth == null && health != null) { playerHealth = health; changed = true; }

            Checkpoint[] mergedCheckpoints = Merge(checkpoints, levelCheckpoints, ref changed);
            HazardVolume[] mergedHazards = Merge(hazards, levelHazards, ref changed);
            checkpoints = mergedCheckpoints;
            hazards = mergedHazards;
            Index();
            return changed;
        }

        private static T[] Merge<T>(T[] existing, T[] wanted, ref bool changed) where T : UnityEngine.Object
        {
            var merged = new List<T>();
            if (existing != null)
            {
                foreach (T item in existing)
                {
                    if (item != null && !merged.Contains(item)) merged.Add(item);
                    else changed = true;
                }
            }

            if (wanted != null)
            {
                foreach (T item in wanted)
                {
                    if (item != null && !merged.Contains(item))
                    {
                        merged.Add(item);
                        changed = true;
                    }
                }
            }

            return merged.ToArray();
        }

        private void Awake()
        {
            Index();
        }

        private void OnEnable()
        {
            WorldState.Reloaded += HandleReloaded;
        }

        private void OnDisable()
        {
            WorldState.Reloaded -= HandleReloaded;
        }

        private void HandleReloaded(WorldReload reload)
        {
            // At once, from the store itself, so that it does not matter whether the checkpoints have taken
            // their state yet, and a restart that follows (a retry, a new game, a replay) already finds the
            // respawn point that belongs to the state just put in place.
            if (reload == WorldReload.StateReplaced && progress != null)
            {
                TakeRemembered(false);
            }
        }

        private void Update()
        {
            // The first frame, after every Start: the session knows the level's start by then, and a
            // checkpoint remembered from a saved game can put the player where the game was left.
            if (!started)
            {
                started = true;
                TakeRemembered(true);
                WorldState.SaveCheckpoint();
            }

            if (player == null)
            {
                return;
            }

            Vector3 position = player.position;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                Checkpoint checkpoint = checkpoints[i];
                if (checkpoint != null && !checkpoint.IsReached && checkpoint.Contains(position))
                {
                    Reach(checkpoint);
                }
            }

            for (int i = 0; i < hazards.Length; i++)
            {
                HazardVolume hazard = hazards[i];
                if (hazard != null && hazard.Contains(position) && hazard.Strike(playerHealth, Time.time) && session != null)
                {
                    session.Respawn();
                    return;
                }
            }
        }

        private void Reach(Checkpoint checkpoint)
        {
            checkpoint.MarkReached();
            if (progress.Reach(checkpoint.Order))
            {
                if (session != null)
                {
                    session.SetRespawnPoint(checkpoint.RespawnPosition, checkpoint.RespawnRotation);
                }

                if (checkpoint.TakesWorldCheckpoint)
                {
                    WorldState.SaveCheckpoint();
                }
            }

            CheckpointReached?.Invoke(checkpoint);
        }

        /// <summary>Makes the furthest remembered checkpoint current, and optionally puts the player there.</summary>
        private void TakeRemembered(bool placePlayer)
        {
            progress.Restore(order => order < byOrder.Length && IsRemembered(byOrder[order]));
            if (session == null)
            {
                return;
            }

            Checkpoint current = Current;
            if (current == null)
            {
                session.ClearRespawnPoint();
                return;
            }

            session.SetRespawnPoint(current.RespawnPosition, current.RespawnRotation);

            // The first checkpoint is where the level starts anyway; only a later one moves the player.
            if (placePlayer && current.Order > 0)
            {
                session.Respawn();
            }
        }

        /// <summary>Whether the store remembers a checkpoint as reached. One without a key can only answer for itself.</summary>
        private static bool IsRemembered(Checkpoint checkpoint)
        {
            if (checkpoint == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(checkpoint.PersistenceKey))
            {
                return checkpoint.IsReached;
            }

            return PuzzleSessionState.TryGet(checkpoint.PersistenceKey, out PuzzleRecord record) && record.Active && record.Locked;
        }

        private void Index()
        {
            int count = 0;
            if (checkpoints != null)
            {
                foreach (Checkpoint checkpoint in checkpoints)
                {
                    if (checkpoint != null)
                    {
                        count = Mathf.Max(count, checkpoint.Order + 1);
                    }
                }
            }

            byOrder = new Checkpoint[count];
            if (checkpoints != null)
            {
                foreach (Checkpoint checkpoint in checkpoints)
                {
                    if (checkpoint != null && checkpoint.Order >= 0 && byOrder[checkpoint.Order] == null)
                    {
                        byOrder[checkpoint.Order] = checkpoint;
                    }
                }
            }

            progress = new CheckpointProgress(count);
        }
    }
}
