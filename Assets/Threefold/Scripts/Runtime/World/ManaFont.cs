using Threefold.Combat;
using UnityEngine;

namespace Threefold.World
{
    /// <summary>
    /// A place of power: while the player stands close to it, their mana returns quickly. It gives
    /// the level a reason to move between the open ground and the centre during a fight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManaFont : MonoBehaviour
    {
        [Tooltip("Whose mana is restored. Normally the player's.")]
        [SerializeField] private Mana recipient;

        [Tooltip("Distance within which mana is restored, in metres, measured along the ground.")]
        [Min(0.1f)]
        [SerializeField] private float radius = 4.5f;

        [Tooltip("Mana restored per second, in addition to normal regeneration.")]
        [Min(0f)]
        [SerializeField] private float manaPerSecond = 16f;

        /// <summary>True while the recipient is inside the radius.</summary>
        public bool IsRecipientInRange { get; private set; }

        /// <summary>Connects the font. Called by the setup tooling.</summary>
        public void Bind(Mana manaRecipient, float range, float restorePerSecond)
        {
            recipient = manaRecipient;
            radius = Mathf.Max(0.1f, range);
            manaPerSecond = Mathf.Max(0f, restorePerSecond);
        }

        private void Update()
        {
            if (recipient == null)
            {
                IsRecipientInRange = false;
                return;
            }

            Vector3 offset = recipient.transform.position - transform.position;
            offset.y = 0f;
            IsRecipientInRange = offset.sqrMagnitude <= radius * radius;

            if (IsRecipientInRange)
            {
                recipient.Restore(manaPerSecond * Time.deltaTime);
            }
        }
    }
}
