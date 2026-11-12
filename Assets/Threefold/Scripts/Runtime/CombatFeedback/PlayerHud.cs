using Threefold.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows the player's health and mana and reacts to casting: the crosshair kicks when a spell
    /// is cast, and the mana bar and crosshair flash when there is not enough mana.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHud : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Mana mana;
        [SerializeField] private AbilityCaster caster;

        [SerializeField] private ResourceBar healthBar;
        [SerializeField] private ResourceBar manaBar;

        [Tooltip("Optional. Marks the point the player is aiming at.")]
        [SerializeField] private Graphic crosshair;

        [SerializeField] private Color crosshairRefusedColor = new Color(1f, 0.32f, 0.26f, 0.95f);

        [Tooltip("How much the crosshair grows when a spell is cast.")]
        [Range(0f, 1f)]
        [SerializeField] private float crosshairKick = 0.45f;

        [Min(0.05f)]
        [SerializeField] private float crosshairSettleSeconds = 0.18f;

        [Min(0.05f)]
        [SerializeField] private float refusedSeconds = 0.4f;

        private Color crosshairRestColor = Color.white;
        private float kickRemaining;
        private float refusedRemaining;

        /// <summary>Connects the HUD. Called by the setup tooling.</summary>
        public void Bind(Health playerHealth, Mana playerMana, AbilityCaster playerCaster, ResourceBar healthResourceBar,
            ResourceBar manaResourceBar, Graphic crosshairGraphic)
        {
            health = playerHealth;
            mana = playerMana;
            caster = playerCaster;
            healthBar = healthResourceBar;
            manaBar = manaResourceBar;
            crosshair = crosshairGraphic;
        }

        private void OnEnable()
        {
            if (crosshair != null)
            {
                crosshairRestColor = crosshair.color;
            }

            if (caster != null)
            {
                caster.CastSucceeded += HandleCastSucceeded;
                caster.CastFailed += HandleCastFailed;
            }
        }

        private void OnDisable()
        {
            if (caster != null)
            {
                caster.CastSucceeded -= HandleCastSucceeded;
                caster.CastFailed -= HandleCastFailed;
            }
        }

        private void Update()
        {
            if (health != null && healthBar != null)
            {
                healthBar.SetFraction(health.Fraction);
            }

            if (mana != null && manaBar != null)
            {
                manaBar.SetFraction(mana.Fraction);
            }

            UpdateCrosshair();
        }

        private void UpdateCrosshair()
        {
            if (crosshair == null)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;

            if (kickRemaining > 0f)
            {
                kickRemaining = Mathf.Max(0f, kickRemaining - deltaTime);
                float scale = 1f + crosshairKick * (kickRemaining / crosshairSettleSeconds);
                crosshair.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            if (refusedRemaining > 0f)
            {
                refusedRemaining = Mathf.Max(0f, refusedRemaining - deltaTime);
                crosshair.color = Color.Lerp(crosshairRestColor, crosshairRefusedColor, refusedRemaining / refusedSeconds);
            }
        }

        private void HandleCastSucceeded(AbilityDefinition ability, AbilityContext context)
        {
            kickRemaining = crosshairSettleSeconds;
        }

        private void HandleCastFailed(AbilityDefinition ability, CastResult reason)
        {
            if (reason != CastResult.NotEnoughMana)
            {
                return;
            }

            refusedRemaining = refusedSeconds;

            if (manaBar != null)
            {
                manaBar.Flash();
            }
        }
    }
}
