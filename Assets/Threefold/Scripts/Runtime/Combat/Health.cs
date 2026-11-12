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

        /// <summary>World position that attacks should aim at.</summary>
        public Vector3 AimPoint => transform.TransformPoint(aimPointOffset);

        /// <summary>Sets the maximum and the aim point. Called by the setup tooling.</summary>
        public void Configure(float maximum, Vector3 aimOffset)
        {
            pool.SetMaximum(maximum, true);
            aimPointOffset = aimOffset;
        }

        public void ApplyDamage(in DamageInfo damage)
        {
            if (defeated || invulnerable || damage.Amount <= 0f)
            {
                return;
            }

            float removed = pool.Remove(damage.Amount);
            if (removed <= 0f)
            {
                return;
            }

            bool nowDefeated = pool.IsEmpty;
            defeated = nowDefeated;

            var applied = new DamageInfo(removed, damage.Point, damage.Direction, damage.Source);
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
