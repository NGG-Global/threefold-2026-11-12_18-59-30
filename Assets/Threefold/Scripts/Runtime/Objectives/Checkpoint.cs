using Threefold.Puzzles;
using UnityEngine;

namespace Threefold.Objectives
{
    /// <summary>
    /// A place in a level the player comes back to. It is a puzzle element: active and locked once the
    /// player has walked into its box, and remembered under its persistence key in the one world state,
    /// so the furthest checkpoint reached survives a restart, a defeat, and closing the game. A
    /// <see cref="CheckpointRoute"/> watches the player and decides which checkpoint is current; the
    /// checkpoint itself has no update.
    ///
    /// A checkpoint can also be an objective's goal: the one at a level's way out completes the level.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Checkpoint : PuzzleElement
    {
        [Tooltip("Checkpoints are numbered in the order the level is played. The furthest one reached is current.")]
        [SerializeField] private int order;

        [Tooltip("The box that notices the player, in metres, standing on this object's position and turned with it.")]
        [SerializeField] private Vector3 zoneSize = new Vector3(4f, 3f, 2f);

        [Tooltip("Where the player comes back, facing the way it faces. Empty: this object's position.")]
        [SerializeField] private Transform respawnPoint;

        [Tooltip("Reaching it also takes the world state's checkpoint, which 'retry from checkpoint' returns to. Off for a " +
                 "level's last checkpoint, so that a retry after the level is complete goes back to the one before.")]
        [SerializeField] private bool takesWorldCheckpoint = true;

        /// <summary>
        /// How far below its box the player's feet may be and still count: the character stands on the floor
        /// within its controller's skin, and the box stands on the floor exactly.
        /// </summary>
        public const float GroundTolerance = 0.3f;

        public int Order => order;
        public Vector3 ZoneSize => zoneSize;
        public Transform RespawnPoint => respawnPoint;

        public bool TakesWorldCheckpoint
        {
            get => takesWorldCheckpoint;
            set => takesWorldCheckpoint = value;
        }

        /// <summary>True once the player has been here, in this attempt or a saved one.</summary>
        public bool IsReached => IsActive;

        /// <summary>Where the player comes back: the respawn point's position, facing its forward direction across the ground.</summary>
        public Vector3 RespawnPosition => respawnPoint != null ? respawnPoint.position : transform.position;

        public Quaternion RespawnRotation
        {
            get
            {
                Vector3 forward = (respawnPoint != null ? respawnPoint : transform).forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            }
        }

        /// <summary>Only being reached, which locks it, is remembered.</summary>
        protected override bool PersistsUnlockedState => false;

        /// <summary>Sets the checkpoint up. Called by the setup tooling.</summary>
        public void Configure(int checkpointOrder, Vector3 size, Transform respawn)
        {
            order = checkpointOrder;
            zoneSize = size;
            respawnPoint = respawn;
        }

        /// <summary>Whether a point in the world lies inside the checkpoint's box, or just below it (see <see cref="GroundTolerance"/>).</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position);
            float height = zoneSize.y + GroundTolerance;
            local.y += GroundTolerance - height * 0.5f;
            return CheckpointProgress.Contains(local, new Vector3(zoneSize.x * 0.5f, height * 0.5f, zoneSize.z * 0.5f));
        }

        /// <summary>Marks the checkpoint as reached, for good. Returns false if it already was.</summary>
        public bool MarkReached()
        {
            if (IsLocked)
            {
                return false;
            }

            SetElementActive(true);
            Lock();
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + transform.up * (zoneSize.y * 0.5f), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, zoneSize);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawLine(transform.position, RespawnPosition);
            Gizmos.DrawRay(RespawnPosition, RespawnRotation * Vector3.forward);
        }
    }
}
