using System;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Objectives
{
    /// <summary>
    /// A place the player must not fall into, such as molten slag: it hurts, and the player is put back
    /// at the last checkpoint with nothing else reset, so a failed crossing is tried again at once. If
    /// the damage defeats the player, the usual defeat and restart follow instead. A
    /// <see cref="CheckpointRoute"/> checks the player against it; it has no update of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HazardVolume : MonoBehaviour
    {
        [Tooltip("The box that hurts, in metres, standing on this object's position and turned with it.")]
        [SerializeField] private Vector3 size = new Vector3(4f, 1f, 4f);

        [Tooltip("Damage dealt each time the player falls in.")]
        [Min(0f)]
        [SerializeField] private float damage = 25f;

        [Tooltip("Seconds before it can act again, so one fall is one hit.")]
        [Min(0f)]
        [SerializeField] private float cooldown = 1f;

        private float lastActed = float.NegativeInfinity;

        /// <summary>Raised when the hazard has hurt the player. The argument: whether the player was put back.</summary>
        public event Action<bool> Struck;

        public Vector3 Size => size;
        public float Damage => damage;

        /// <summary>Sets the hazard up. Called by the setup tooling.</summary>
        public void Configure(Vector3 boxSize, float damagePerFall)
        {
            size = boxSize;
            damage = damagePerFall;
        }

        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position);
            local.y -= size.y * 0.5f;
            return CheckpointProgress.Contains(local, size * 0.5f);
        }

        /// <summary>
        /// Hurts the player and says whether the player should be put back at the last checkpoint. Does
        /// nothing, and returns false, while it is cooling down or the player is already defeated.
        /// </summary>
        public bool Strike(Health player, float now)
        {
            if (player == null || !player.IsAlive || !HazardRules.Ready(now, lastActed, cooldown))
            {
                return false;
            }

            lastActed = now;
            if (damage > 0f)
            {
                player.ApplyDamage(new DamageInfo(damage, player.transform.position, Vector3.up, gameObject));
            }

            bool returned = HazardRules.ReturnsPlayer(player.Current) && player.IsAlive;
            Struck?.Invoke(returned);
            return returned;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.15f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + transform.up * (size.y * 0.5f), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
