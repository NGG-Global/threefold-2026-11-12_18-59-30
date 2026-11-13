using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>Why an attempt to use an ability did or did not go through.</summary>
    public enum CastResult
    {
        Success,
        NoAbility,
        OnCooldown,
        NotEnoughMana,
        CasterDefeated,
    }

    /// <summary>Everything an ability needs to know about the moment it is used.</summary>
    public readonly struct AbilityContext
    {
        /// <summary>The character using the ability.</summary>
        public readonly GameObject Caster;

        /// <summary>World position the ability starts from.</summary>
        public readonly Vector3 Origin;

        /// <summary>World position being aimed at.</summary>
        public readonly Vector3 AimPoint;

        /// <summary>Normalised direction from the origin towards the aim point.</summary>
        public readonly Vector3 Direction;

        public AbilityContext(GameObject caster, Vector3 origin, Vector3 aimPoint, Vector3 direction)
        {
            Caster = caster;
            Origin = origin;
            AimPoint = aimPoint;
            Direction = direction;
        }
    }

    /// <summary>
    /// Data that defines one ability: what it costs, how often it can be used, and what it does.
    /// Each kind of ability is a subclass (a projectile spell today; a melee strike or a dash for
    /// the Rogue and Monk later), and each individual ability is an asset that designers can tune
    /// without touching code. <see cref="AbilityCaster"/> handles cost and cooldown for all of them.
    /// </summary>
    public abstract class AbilityDefinition : ScriptableObject
    {
        [Header("Ability")]
        [SerializeField] private string displayName = "Ability";

        [Tooltip("Mana spent on each use.")]
        [Min(0f)]
        [SerializeField] private float manaCost = 10f;

        [Tooltip("Seconds before the ability can be used again.")]
        [Min(0f)]
        [SerializeField] private float cooldown = 0.4f;

        public string DisplayName => displayName;
        public float ManaCost => manaCost;
        public float Cooldown => cooldown;

        /// <summary>Sets the shared values. Called by the setup tooling and by tests.</summary>
        public void ConfigureAbility(string abilityName, float cost, float cooldownSeconds)
        {
            displayName = abilityName;
            manaCost = Mathf.Max(0f, cost);
            cooldown = Mathf.Max(0f, cooldownSeconds);
        }

        /// <summary>Performs the ability. Cost and cooldown have already been dealt with.</summary>
        public abstract void Execute(in AbilityContext context);
    }
}
