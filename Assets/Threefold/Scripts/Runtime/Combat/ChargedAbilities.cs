using System.Collections.Generic;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// An ability that is charged by holding and released: the special attack of a discipline. The
    /// longer it was held, up to the full charge time, the stronger it is. Mana cost and cooldown are
    /// the ordinary ability values, enforced by <see cref="AbilityCaster"/>; the charge arrives in
    /// <see cref="AbilityContext.Charge"/>.
    /// </summary>
    public abstract class ChargedAbilityDefinition : AbilityDefinition
    {
        [SerializeField] private ChargeProfile charge = new ChargeProfile();

        public ChargeProfile Charge => charge;

        /// <summary>Sets the charge values. Called by the setup tooling and by tests.</summary>
        public void ConfigureCharge(ChargeProfile profile)
        {
            charge = profile ?? new ChargeProfile();
        }

        protected float Damage(float power) => ChargeScaling.Scale(charge.damageMin, charge.damageMax, power);

        protected float Stagger(float power) => ChargeScaling.Scale(charge.staggerMin, charge.staggerMax, power);
    }
}
