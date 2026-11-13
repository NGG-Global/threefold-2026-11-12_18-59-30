using System;
using Threefold.Combat;
using Threefold.Interface;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>What a discipline looks like in the world. Everything here is optional.</summary>
    [Serializable]
    public sealed class DisciplinePresentation
    {
        [Tooltip("Effect spawned on each target a melee swing hits.")]
        public PooledObject meleeImpactEffect;

        [Tooltip("Flat shape flashed on the ground for a swing and for an area attack. Shared by all disciplines; " +
                 "the discipline's colour tints it.")]
        public PooledObject sweepEffect;

        [Tooltip("Effect spawned at the caster when the charged attack is released.")]
        public PooledObject releaseEffect;

        [Tooltip("Animator trigger set when the charged attack is released. Ignored if the Animator has no such parameter.")]
        public string chargedAnimationTrigger = "Cast";

        [Tooltip("If the Animator has no trigger for a melee swing, use the casting gesture instead, so a swing is " +
                 "visible on a character without melee animations.")]
        public bool castGestureForMelee = true;
    }

    /// <summary>
    /// Everything that defines one discipline, as one asset that can be tuned without code: its
    /// identity and icon, its melee combo (damage, stagger, heat, reach, timing, combo windows,
    /// targeting help), its charged attack (an ability asset with cost, cooldown, charge timing,
    /// damage, status effects and targeting), and its effects and animation triggers.
    ///
    /// The setup command generates the three assets from <see cref="DisciplineDefaults"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Threefold/Discipline", fileName = "NewDiscipline")]
    public sealed class DisciplineDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private MagicDiscipline discipline = MagicDiscipline.Arcane;
        [SerializeField] private string displayName = "Arcane";

        [Tooltip("Accent colour on the controls and of the discipline's effects.")]
        [SerializeField] private Color color = new Color(0.62f, 0.64f, 1f, 1f);

        [SerializeField] private ProceduralShape icon = ProceduralShape.Star;

        [Tooltip("The kind of magic its attacks carry. Magical objects answer only the kinds they accept.")]
        [SerializeField] private SpellElement element = SpellElement.Arcane;

        [Header("Standard attack")]
        [SerializeField] private MeleeComboProfile melee = new MeleeComboProfile();

        [Tooltip("Layers melee swings can hit.")]
        [SerializeField] private LayerMask meleeHitLayers = Physics.DefaultRaycastLayers;

        [Header("Charged attack")]
        [Tooltip("The ability released after charging. Its asset holds cost, cooldown, charge timing, damage and targeting.")]
        [SerializeField] private ChargedAbilityDefinition chargedAbility;

        [Header("Presentation")]
        [SerializeField] private DisciplinePresentation presentation = new DisciplinePresentation();

        public MagicDiscipline Discipline => discipline;
        public string DisplayName => displayName;
        public Color Color => color;
        public ProceduralShape Icon => icon;
        public SpellElement Element => element;
        public MeleeComboProfile Melee => melee;
        public LayerMask MeleeHitLayers => meleeHitLayers;
        public ChargedAbilityDefinition ChargedAbility => chargedAbility;
        public DisciplinePresentation Presentation => presentation;

        /// <summary>Name, colour and icon as the controls show them.</summary>
        public DisciplineInfo Info => new DisciplineInfo(displayName, color, icon);

        /// <summary>Sets the identity. Called by the setup tooling and by tests.</summary>
        public void ConfigureIdentity(MagicDiscipline which, string name, Color accent, ProceduralShape symbol,
            SpellElement magic)
        {
            discipline = which;
            displayName = name;
            color = accent;
            icon = symbol;
            element = magic;
        }

        /// <summary>Sets the combat values. Called by the setup tooling and by tests.</summary>
        public void ConfigureCombat(MeleeComboProfile combo, ChargedAbilityDefinition charged)
        {
            melee = combo ?? new MeleeComboProfile();
            chargedAbility = charged;
        }

        /// <summary>Connects the charged ability if none is set. Returns true if it changed.</summary>
        public bool FillChargedAbility(ChargedAbilityDefinition charged)
        {
            if (chargedAbility != null || charged == null)
            {
                return false;
            }

            chargedAbility = charged;
            return true;
        }
    }
}
