using Threefold.Cameras;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Adds a little camera shake to the player's combat: a light kick when casting, a firmer one
    /// when a hit lands, and a strong one when the player is hurt. Amounts are kept small so the
    /// view stays readable on a phone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCameraFeedback : MonoBehaviour
    {
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Tooltip("The player character. Only combat involving this object shakes the camera.")]
        [SerializeField] private GameObject player;

        [Tooltip("Optional. The player's caster, for the kick when a spell is cast.")]
        [SerializeField] private AbilityCaster caster;

        [Range(0f, 1f)]
        [SerializeField] private float castTrauma = 0.12f;

        [Range(0f, 1f)]
        [SerializeField] private float hitTrauma = 0.24f;

        [Range(0f, 1f)]
        [SerializeField] private float defeatTrauma = 0.45f;

        [Range(0f, 1f)]
        [SerializeField] private float hurtTrauma = 0.6f;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(ThirdPersonCamera rig, GameObject playerObject, AbilityCaster playerCaster)
        {
            cameraRig = rig;
            player = playerObject;
            caster = playerCaster;
        }

        private void OnEnable()
        {
            CombatEvents.DamageDealt += HandleDamageDealt;

            if (caster != null)
            {
                caster.CastSucceeded += HandleCastSucceeded;
            }
        }

        private void OnDisable()
        {
            CombatEvents.DamageDealt -= HandleDamageDealt;

            if (caster != null)
            {
                caster.CastSucceeded -= HandleCastSucceeded;
            }
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            if (cameraRig != null)
            {
                cameraRig.AddTrauma(castTrauma);
            }
        }

        private void HandleDamageDealt(DamageEvent damage)
        {
            if (cameraRig == null || player == null)
            {
                return;
            }

            if (damage.Target == player)
            {
                cameraRig.AddTrauma(hurtTrauma);
            }
            else if (damage.Source == player)
            {
                cameraRig.AddTrauma(damage.Defeated ? defeatTrauma : hitTrauma);
            }
        }
    }
}
