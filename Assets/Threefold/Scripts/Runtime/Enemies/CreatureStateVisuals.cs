using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// Shows an enemy's defences for as long as they last, so no change of state is invisible: a ward
    /// shell while the ward holds, armour plates that show cracks once weakened and fall away when
    /// they break. The moments of change are marked by bursts as well (see the combat feedback). It has no
    /// update: it follows the enemy's announcements.
    ///
    /// The look is a placeholder from primitive parts; the colours are a proposal, not approved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureStateVisuals : MonoBehaviour
    {
        [SerializeField] private EnemyController controller;

        [Tooltip("Shown while the ward holds, hidden while it is disrupted.")]
        [SerializeField] private GameObject wardShell;

        [Tooltip("The armour's parts. Hidden once the armour is broken.")]
        [SerializeField] private GameObject[] armourParts = new GameObject[0];

        [Tooltip("Cracks, shown while the armour is weakened or fractured.")]
        [SerializeField] private GameObject[] crackParts = new GameObject[0];

        /// <summary>Connects the parts. Called by the setup tooling.</summary>
        public void Bind(EnemyController owner, GameObject shell, GameObject[] plates, GameObject[] cracks)
        {
            controller = owner;
            wardShell = shell;
            armourParts = plates ?? new GameObject[0];
            crackParts = cracks ?? new GameObject[0];
        }

        public EnemyController Controller => controller;
        public GameObject WardShell => wardShell;
        public int ArmourPartCount => armourParts != null ? armourParts.Length : 0;

        private void OnEnable()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<EnemyController>();
            }

            if (controller != null)
            {
                controller.DefenceChanged += HandleChanged;
                controller.DefencesRestored += HandleRestored;
            }

            Show();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.DefenceChanged -= HandleChanged;
                controller.DefencesRestored -= HandleRestored;
            }
        }

        private void HandleChanged(EnemyController enemy, WorldPropertyKind kind, int from, int to)
        {
            Show();
        }

        private void HandleRestored(EnemyController enemy)
        {
            Show();
        }

        private void Show()
        {
            CreatureEffectModel model = controller != null ? controller.Effects : null;

            // Before the enemy has started, what its definition says is shown: a ward that is there holds.
            bool wardShown = model != null
                ? model.WardHolds
                : controller != null && controller.Definition != null && controller.Definition.Tuning.ward != null &&
                  controller.Definition.Tuning.ward.enabled;
            SetActive(wardShell, wardShown);

            StructuralState armour = model != null && model.Armour != null ? model.Armour.State : StructuralState.Intact;
            bool broken = armour == StructuralState.Destroyed;
            bool cracked = armour == StructuralState.Weakened || armour == StructuralState.Fractured;

            for (int i = 0; i < armourParts.Length; i++)
            {
                SetActive(armourParts[i], !broken);
            }

            for (int i = 0; i < crackParts.Length; i++)
            {
                SetActive(crackParts[i], cracked);
            }
        }

        private static void SetActive(GameObject part, bool active)
        {
            if (part != null && part.activeSelf != active)
            {
                part.SetActive(active);
            }
        }
    }
}
