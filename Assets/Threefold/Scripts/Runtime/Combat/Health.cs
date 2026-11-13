using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Hit points for anything that can be damaged and defeated. Holds the rules only; visual and
    /// audio reactions subscribe to its events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour, IDamageable
    {
        private static readonly List<Health> ActiveList = new List<Health>();

        [SerializeField] private ResourcePool pool = new ResourcePool(100f);

        [Tooltip("While set, damage is ignored.")]
        [SerializeField] private bool invulnerable;

        [Tooltip("Point that attacks aim at, measured from this object's position.")]
        [SerializeField] private Vector3 aimPointOffset = new Vector3(0f, 1f, 0f);

        [Tooltip("Which side this belongs to. Projectiles and strikes pass over allies. Neutral objects can be hit by anyone.")]
        [SerializeField] private CombatTeam team = CombatTeam.Neutral;

        private readonly List<IDamageModifier> modifiers = new List<IDamageModifier>(2);
        private bool defeated;

        /// <summary>Raised after damage was applied. The amount is what was actually removed.</summary>
        public event Action<DamageInfo> Damaged;

        /// <summary>Raised whenever the current or maximum value changes.</summary>
        public event Action Changed;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action Defeated;

        /// <summary>Raised when a defeated object is restored.</summary>
        public event Action Revived;

        /// <summary>Every enabled Health in the scene. Used for target selection. Do not modify.</summary>
        public static IReadOnlyList<Health> Active => ActiveList;

        public float Current => pool.Current;
        public float Maximum => pool.Maximum;
        public float Fraction => pool.Fraction;
        public bool IsAlive => !defeated;

        public bool Invulnerable
        {
            get => invulnerable;
            set => invulnerable = value;
        }

        public CombatTeam Team
        {
            get => team;
            set => team = value;
        }

        /// <summary>World position that attacks should aim at.</summary>
        public Vector3 AimPoint => transform.TransformPoint(aimPointOffset);

        /// <summary>Sets the maximum and the aim point. Called by the setup tooling.</summary>
        public void Configure(float maximum, Vector3 aimOffset)
        {
            pool.SetMaximum(maximum, true);
            aimPointOffset = aimOffset;
        }

        /// <summary>Changes the maximum without changing the aim point. Used when difficulty scales an enemy.</summary>
        public void SetMaximum(float maximum, bool refill)
        {
            pool.SetMaximum(maximum, refill);
            Changed?.Invoke();
        }

        /// <summary>True when the other object is on the same side, so attacks between them are not applied.</summary>
        public bool IsAlliedWith(Health other)
        {
            return other != null && CombatTeams.AreAllied(team, other.team);
        }

        /// <summary>
        /// Registers something that changes incoming damage, such as a raised guard. Modifiers are
        /// applied in the order they were added.
        /// </summary>
        public void AddModifier(IDamageModifier modifier)
        {
            if (modifier != null && !modifiers.Contains(modifier))
            {
                modifiers.Add(modifier);
            }
        }

        public void RemoveModifier(IDamageModifier modifier)
        {
            modifiers.Remove(modifier);
        }

        public void ApplyDamage(in DamageInfo damage)
        {
            if (defeated || invulnerable || damage.Amount <= 0f)
            {
                return;
            }

            float amount = damage.Amount;
            for (int i = 0; i < modifiers.Count && amount > 0f; i++)
            {
                amount = modifiers[i].ModifyIncomingDamage(damage, amount);
            }

            if (amount <= 0f)
            {
                return;
            }

            float removed = pool.Remove(amount);
            if (removed <= 0f)
            {
                return;
            }

            bool nowDefeated = pool.IsEmpty;
            defeated = nowDefeated;

            DamageInfo applied = damage.WithAmount(removed);
            Damaged?.Invoke(applied);
            Changed?.Invoke();
            CombatEvents.RaiseDamageDealt(new DamageEvent(gameObject, damage.Source, removed, damage.Point, nowDefeated));

            if (nowDefeated)
            {
                Defeated?.Invoke();
            }
        }

        /// <summary>Restores health. Has no effect on a defeated object; use <see cref="Revive"/>.</summary>
        /// <returns>The amount actually restored.</returns>
        public float Heal(float amount)
        {
            if (defeated)
            {
                return 0f;
            }

            float added = pool.Add(amount);
            if (added > 0f)
            {
                Changed?.Invoke();
            }

            return added;
        }

        /// <summary>Restores a defeated object to full health.</summary>
        public void Revive()
        {
            bool wasDefeated = defeated;
            defeated = false;
            pool.Fill();
            Changed?.Invoke();

            if (wasDefeated)
            {
                Revived?.Invoke();
            }
        }

        private void OnEnable()
        {
            ActiveList.Add(this);
        }

        private void OnDisable()
        {
            ActiveList.Remove(this);
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveList.Clear();
        }
    }
}
