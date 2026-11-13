using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Uses abilities on behalf of a character and enforces the shared rules: cooldown, mana cost,
    /// and no casting when defeated. It does not know what any ability does, where input comes
    /// from, or how a cast is shown; those belong to the ability asset, the input bridge and the
    /// presentation components. Mage, Rogue and Monk can all use it with different ability lists.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityCaster : MonoBehaviour
    {
        [Tooltip("Abilities this character can use. Slot 0 is the primary attack.")]
        [SerializeField] private List<AbilityDefinition> abilities = new List<AbilityDefinition>();

        [Tooltip("Optional. Without mana, abilities are free.")]
        [SerializeField] private Mana mana;

        [Tooltip("Optional. A defeated character cannot use abilities.")]
        [SerializeField] private Health health;

        [Tooltip("Optional. Decides where abilities are aimed. Without it they go straight ahead.")]
        [SerializeField] private AimProvider aimProvider;

        [Tooltip("Optional. Where abilities start from. Without it, a point at chest height is used.")]
        [SerializeField] private Transform castPoint;

        [Tooltip("Start point measured from this object's position, used when no cast point is set.")]
        [SerializeField] private Vector3 castOffset = new Vector3(0f, 1.25f, 0f);

        private readonly List<float> cooldowns = new List<float>();

        /// <summary>Raised when an ability was used.</summary>
        public event Action<AbilityDefinition, AbilityContext> CastSucceeded;

        /// <summary>Raised when an ability could not be used, with the reason.</summary>
        public event Action<AbilityDefinition, CastResult> CastFailed;

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;

        /// <summary>Connects the caster's dependencies. Called by the setup tooling and by tests.</summary>
        public void Bind(Mana manaSource, Health healthSource, AimProvider aim)
        {
            mana = manaSource;
            health = healthSource;
            aimProvider = aim;
        }

        /// <summary>Puts an ability into a slot, extending the list if needed.</summary>
        public void SetAbility(int slot, AbilityDefinition ability)
        {
            if (slot < 0)
            {
                return;
            }

            while (abilities.Count <= slot)
            {
                abilities.Add(null);
            }

            abilities[slot] = ability;
        }

        public AbilityDefinition GetAbility(int slot)
        {
            return slot >= 0 && slot < abilities.Count ? abilities[slot] : null;
        }

        /// <summary>Seconds until the ability in the slot can be used again.</summary>
        public float GetCooldownRemaining(int slot)
        {
            return slot >= 0 && slot < cooldowns.Count ? Mathf.Max(0f, cooldowns[slot]) : 0f;
        }

        /// <summary>True if the ability in the slot is off cooldown, whatever its mana cost.</summary>
        public bool IsReady(int slot)
        {
            return GetAbility(slot) != null && GetCooldownRemaining(slot) <= 0f;
        }

        /// <summary>Tries to use the ability in the given slot.</summary>
        public CastResult TryCast(int slot)
        {
            AbilityDefinition ability = GetAbility(slot);
            if (ability == null)
            {
                return CastResult.NoAbility;
            }

            if (health != null && !health.IsAlive)
            {
                return Fail(ability, CastResult.CasterDefeated);
            }

            if (GetCooldownRemaining(slot) > 0f)
            {
                return Fail(ability, CastResult.OnCooldown);
            }

            if (mana != null && !mana.TrySpend(ability.ManaCost))
            {
                return Fail(ability, CastResult.NotEnoughMana);
            }

            AbilityContext context = BuildContext();
            ability.Execute(context);

            while (cooldowns.Count <= slot)
            {
                cooldowns.Add(0f);
            }

            cooldowns[slot] = ability.Cooldown;
            CastSucceeded?.Invoke(ability, context);
            return CastResult.Success;
        }

        /// <summary>Advances cooldowns by the given time. Called every frame; public so it can be tested.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < cooldowns.Count; i++)
            {
                if (cooldowns[i] > 0f)
                {
                    cooldowns[i] -= deltaTime;
                }
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private CastResult Fail(AbilityDefinition ability, CastResult reason)
        {
            CastFailed?.Invoke(ability, reason);
            return reason;
        }

        private AbilityContext BuildContext()
        {
            Vector3 origin = castPoint != null ? castPoint.position : transform.TransformPoint(castOffset);

            if (aimProvider != null)
            {
                AimSolution aim = aimProvider.GetAim(origin);
                return new AbilityContext(gameObject, origin, aim.Point, aim.Direction);
            }

            Vector3 forward = transform.forward;
            return new AbilityContext(gameObject, origin, origin + forward * 50f, forward);
        }
    }
}
