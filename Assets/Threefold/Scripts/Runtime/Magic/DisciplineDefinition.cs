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

        [Tooltip("Oriented slash or force burst played when a melee swing starts. World-space at swing height.")]
        public PooledObject meleeSwingEffect;

        [Tooltip("Flat shape flashed on the ground for a swing and for an area attack. Shared by all disciplines; " +
                 "the discipline's colour tints it.")]
        public PooledObject sweepEffect;

        [Tooltip("Looping charge buildup at the hands while a charged attack is held. Stopped on release or cancel.")]
        public PooledObject chargeLoopEffect;

        [Tooltip("Effect spawned at the caster when the charged attack is released.")]
        public PooledObject releaseEffect;

        [Tooltip("Optional. Spark or shield flash where a projectile is deflected. Falls back to the shared guard spark.")]
        public PooledObject deflectEffect;

        [Tooltip("Height above the character origin for melee swing and charge effects, in metres.")]
        [Min(0f)]
        public float effectHeight = 1.15f;

        [Tooltip("Animator trigger set when the charged attack is released. Ignored if the Animator has no such parameter.")]
        public string chargedAnimationTrigger = "Cast";

        [Tooltip("If the Animator has no trigger for a melee swing, use the casting gesture instead, so a swing is " +
                 "visible on a character without melee animations.")]
        public bool castGestureForMelee = true;

        [Header("Body effects (timed by the combat animation cues)")]
        [Tooltip("Trail on the striking hand or foot. Attached to the bone; drawn in world space.")]
        public PooledObject limbTrailEffect;

        [Tooltip("Energy gathering at the striking limb before contact.")]
        public PooledObject gatherEffect;

        [Tooltip("Concentrated burst at the striking limb at the moment of contact, along the swing.")]
        public PooledObject contactEffect;

        [Tooltip("Contact of a heavy strike. Falls back to the contact effect.")]
        public PooledObject heavyContactEffect;

        [Tooltip("Embers or motes left where the limb was after a strike that made contact.")]
        public PooledObject lingerEffect;

        [Tooltip("When each body effect starts, grows and stops. Uses MagicVfxDefaults until configured.")]
        public BodyEffectTiming bodyTiming = new BodyEffectTiming();

        [Header("Charged attack effects")]
        [Tooltip("A wave front or pulse ring that travels over the charged attack's area after release.")]
        public PooledObject areaWaveEffect;

        [Tooltip("Seconds the wave front takes to cross the area. Visual only: the hit is resolved at release.")]
        [Min(0.05f)]
        public float areaWaveSeconds = 0.22f;

        [Tooltip("A ribbon that follows the charged projectile and fades where it ends.")]
        public PooledObject projectileTrailEffect;

        [Header("Defence")]
        [Tooltip("Optional. A restrained barrier held in front of the Mage while blocking with this discipline.")]
        public PooledObject blockBarrierEffect;
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

        [Header("Guard")]
        [Tooltip("What this discipline's guard does to the world around the character while block is held. The " +
                 "protection block gives is the same for every discipline.")]
        [SerializeField] private GuardEffectProfile guardEffect = new GuardEffectProfile();

        [Header("Presentation")]
        [SerializeField] private DisciplinePresentation presentation = new DisciplinePresentation();

        // Which version of the generated values this asset holds; see DisciplineDefaults.DataVersion.
        [SerializeField, HideInInspector] private int dataVersion;

        public int DataVersion => dataVersion;
        public MagicDiscipline Discipline => discipline;
        public string DisplayName => displayName;
        public Color Color => color;
        public ProceduralShape Icon => icon;
        public SpellElement Element => element;
        public MeleeComboProfile Melee => melee;
        public LayerMask MeleeHitLayers => meleeHitLayers;
        public ChargedAbilityDefinition ChargedAbility => chargedAbility;
        public GuardEffectProfile GuardEffect => guardEffect;
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

        /// <summary>Sets what the guard does to the world. Called by the setup tooling and by tests.</summary>
        public void ConfigureGuard(GuardEffectProfile guard)
        {
            guardEffect = guard ?? new GuardEffectProfile();
        }

        /// <summary>Marks the values as those of the current version. Called by the setup tooling on a new asset.</summary>
        public void MarkCurrent()
        {
            dataVersion = DisciplineDefaults.DataVersion;
        }

        /// <summary>
        /// Brings an asset generated by an earlier version up to date, once: values that version did
        /// not have are filled from the defaults, and values tuned since are kept. The charged ability
        /// asset may change too. Returns true if the asset was behind.
        /// </summary>
        public bool Upgrade()
        {
            if (dataVersion >= DisciplineDefaults.DataVersion)
            {
                return false;
            }

            // Each version's values are filled once, oldest first.
            if (dataVersion < 1)
            {
                ProjectileProfile projectile = chargedAbility is ChargedProjectileDefinition shot ? shot.Projectile : null;
                DisciplineDefaults.FillWorldEffects(discipline, melee, guardEffect, projectile);
            }

            if (dataVersion < 2)
            {
                DisciplineDefaults.FillCombinations(discipline, guardEffect);
            }

            if (dataVersion < 3)
            {
                DisciplineDefaults.FillMartialStyles(discipline, melee);
            }

            dataVersion = DisciplineDefaults.DataVersion;
            return true;
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
