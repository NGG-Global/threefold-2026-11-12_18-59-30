using System;
using Threefold.Combat;
using Threefold.Persistence;
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
    ///   current checkpoint is worked out again from what is remembered.
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
        private bool replaced;

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
            if (reload == WorldReload.StateReplaced)
            {
                replaced = true;
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
            else if (replaced)
            {
                TakeRemembered(false);
            }

            replaced = false;
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

                WorldState.SaveCheckpoint();
            }

            CheckpointReached?.Invoke(checkpoint);
        }

        /// <summary>Makes the furthest remembered checkpoint current, and optionally puts the player there.</summary>
        private void TakeRemembered(bool placePlayer)
        {
            progress.Restore(order => order < byOrder.Length && byOrder[order] != null && byOrder[order].IsReached);
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
