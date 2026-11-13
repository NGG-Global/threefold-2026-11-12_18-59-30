using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>How one use of an ability is aimed and how far it was charged.</summary>
    public readonly struct CastOptions
    {
        /// <summary>How far a charged ability was charged, 0 to 1.</summary>
        public readonly float Charge;

        /// <summary>Degrees the aim is turned about the vertical axis; positive turns right.</summary>
        public readonly float AimYawOffset;

        /// <summary>Degrees the aim is raised; negative lowers it.</summary>
        public readonly float AimPitchOffset;

        /// <summary>True when <see cref="Direction"/> replaces the character's usual aim.</summary>
        public readonly bool HasDirection;

        public readonly Vector3 Direction;

        /// <summary>The discipline the ability is used with, as its selector slot, or -1 if none.</summary>
        public readonly int Discipline;

        private CastOptions(float charge, float yaw, float pitch, bool hasDirection, Vector3 direction, int discipline)
        {
            Charge = charge;
            AimYawOffset = yaw;
            AimPitchOffset = pitch;
            HasDirection = hasDirection;
            Direction = direction;
            Discipline = discipline;
        }

        /// <summary>The character's usual aim, fully charged.</summary>
        public static CastOptions Default => new CastOptions(1f, 0f, 0f, false, Vector3.zero, -1);

        /// <summary>The character's usual aim, turned and raised by the given angles.</summary>
        public static CastOptions Aimed(float charge, float yawOffsetDegrees, float pitchOffsetDegrees, int discipline = -1)
        {
            return new CastOptions(charge, yawOffsetDegrees, pitchOffsetDegrees, false, Vector3.zero, discipline);
        }

        /// <summary>A fixed direction, for attacks aimed along the ground.</summary>
        public static CastOptions Directed(float charge, Vector3 direction, int discipline = -1)
        {
            return new CastOptions(charge, 0f, 0f, true, direction, discipline);
        }
    }

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

        [Tooltip("Multiplies the damage of every ability used. Enemies use it for difficulty.")]
        [Min(0f)]
        [SerializeField] private float damageMultiplier = 1f;

        // Cooldowns are kept per ability, so an ability used directly and from a slot shares one.
        private readonly List<AbilityDefinition> coolingAbilities = new List<AbilityDefinition>(4);
        private readonly List<float> coolingRemaining = new List<float>(4);

        /// <summary>Raised when an ability was used.</summary>
        public event Action<AbilityDefinition, AbilityContext> CastSucceeded;

        /// <summary>Raised when an ability could not be used, with the reason.</summary>
        public event Action<AbilityDefinition, CastResult> CastFailed;

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;

        public Transform CastPoint => castPoint;

        public float DamageMultiplier
        {
            get => damageMultiplier;
            set => damageMultiplier = Mathf.Max(0f, value);
        }

        /// <summary>Sets where abilities start from. Called by the setup tooling.</summary>
        public void SetCastPoint(Transform point)
        {
            castPoint = point;
        }

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
            return GetCooldownRemaining(GetAbility(slot));
        }

        /// <summary>Seconds until the ability can be used again.</summary>
        public float GetCooldownRemaining(AbilityDefinition ability)
        {
            int index = ability != null ? coolingAbilities.IndexOf(ability) : -1;
            return index >= 0 ? Mathf.Max(0f, coolingRemaining[index]) : 0f;
        }

        /// <summary>True if the ability in the slot is off cooldown, whatever its mana cost.</summary>
        public bool IsReady(int slot)
        {
            return GetAbility(slot) != null && GetCooldownRemaining(slot) <= 0f;
        }

        /// <summary>Tries to use the ability in the given slot.</summary>
        public CastResult TryCast(int slot)
        {
            return TryCast(GetAbility(slot), CastOptions.Default);
        }

        /// <summary>
        /// Tries to use the ability in the given slot, with its aim turned about the vertical axis by
        /// the given angle in degrees (positive turns right). Used to steer an attack aimed by dragging.
        /// </summary>
        public CastResult TryCast(int slot, float aimYawOffsetDegrees)
        {
            return TryCast(GetAbility(slot), CastOptions.Aimed(1f, aimYawOffsetDegrees, 0f));
        }

        /// <summary>
        /// Whether the ability could be used right now, without using it and without any feedback:
        /// the caster is alive, the ability is off cooldown and its mana cost can be paid.
        /// </summary>
        public CastResult CheckCast(AbilityDefinition ability)
        {
            if (ability == null)
            {
                return CastResult.NoAbility;
            }

            if (health != null && !health.IsAlive)
            {
                return CastResult.CasterDefeated;
            }

            if (GetCooldownRemaining(ability) > 0f)
            {
                return CastResult.OnCooldown;
            }

            return mana != null && !mana.CanAfford(ability.ManaCost) ? CastResult.NotEnoughMana : CastResult.Success;
        }

        /// <summary>
        /// Tries to use an ability that need not be in a slot, such as the charged attack of the
        /// selected discipline. The same rules apply: alive, off cooldown, mana paid.
        /// </summary>
        public CastResult TryCast(AbilityDefinition ability, in CastOptions options)
        {
            if (ability == null)
            {
                return CastResult.NoAbility;
            }

            if (health != null && !health.IsAlive)
            {
                return Fail(ability, CastResult.CasterDefeated);
            }

            if (GetCooldownRemaining(ability) > 0f)
            {
                return Fail(ability, CastResult.OnCooldown);
            }

            if (mana != null && !mana.TrySpend(ability.ManaCost))
            {
                return Fail(ability, CastResult.NotEnoughMana);
            }

            AbilityContext context = BuildContext(options);
            ability.Execute(context);
            StartCooldown(ability);
            CastSucceeded?.Invoke(ability, context);
            return CastResult.Success;
        }

        /// <summary>
        /// Where a use of an ability with these options would start and go, without using it. Used to
        /// show the aim while a charge is held.
        /// </summary>
        public AbilityContext PreviewContext(in CastOptions options)
        {
            return BuildContext(options);
        }

        /// <summary>
        /// Announces that an ability was refused before it was even tried, for example a charge that
        /// cannot be paid for, so the same feedback plays as for a failed cast.
        /// </summary>
        public void ReportRefusal(AbilityDefinition ability, CastResult reason)
        {
            if (reason != CastResult.Success)
            {
                CastFailed?.Invoke(ability, reason);
            }
        }

        /// <summary>Advances cooldowns by the given time. Called every frame; public so it can be tested.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = coolingRemaining.Count - 1; i >= 0; i--)
            {
                coolingRemaining[i] -= deltaTime;
                if (coolingRemaining[i] <= 0f)
                {
                    coolingAbilities.RemoveAt(i);
                    coolingRemaining.RemoveAt(i);
                }
            }
        }

        private void StartCooldown(AbilityDefinition ability)
        {
            if (ability.Cooldown <= 0f)
            {
                return;
            }

            int index = coolingAbilities.IndexOf(ability);
            if (index >= 0)
            {
                coolingRemaining[index] = ability.Cooldown;
                return;
            }

            coolingAbilities.Add(ability);
            coolingRemaining.Add(ability.Cooldown);
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

        private AbilityContext BuildContext(in CastOptions options)
        {
            Vector3 origin = castPoint != null ? castPoint.position : transform.TransformPoint(castOffset);

            if (options.HasDirection && options.Direction.sqrMagnitude > 0.0001f)
            {
                Vector3 fixedDirection = options.Direction.normalized;
                return new AbilityContext(gameObject, origin, origin + fixedDirection * 50f, fixedDirection,
                    damageMultiplier, options.Charge, options.Discipline);
            }

            Vector3 point;
            Vector3 direction;
            if (aimProvider != null)
            {
                AimSolution aim = aimProvider.GetAim(origin);
                point = aim.Point;
                direction = aim.Direction;
            }
            else
            {
                direction = transform.forward;
                point = origin + direction * 50f;
            }

            if (Mathf.Abs(options.AimYawOffset) > 0.01f || Mathf.Abs(options.AimPitchOffset) > 0.01f)
            {
                // Turning about the origin keeps the distance to the aim point.
                float distance = Vector3.Distance(origin, point);
                direction = ChargedAim.ApplyOffsets(direction, options.AimYawOffset, options.AimPitchOffset);
                point = origin + direction * distance;
            }

            return new AbilityContext(gameObject, origin, point, direction, damageMultiplier, options.Charge,
                options.Discipline);
        }
    }
}
