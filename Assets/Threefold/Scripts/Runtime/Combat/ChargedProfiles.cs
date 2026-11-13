using System;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>How a charged attack is aimed.</summary>
    public enum ChargedTargetingMode
    {
        /// <summary>At what the camera's crosshair points at; dragging offsets the aim by a few degrees. For precise shots.</summary>
        CameraAim,

        /// <summary>Along the ground in the direction of the drag, relative to the camera; no drag means straight ahead. For area attacks.</summary>
        GroundDirection,
    }

    /// <summary>The part of a charged attack that every kind shares: timing, aiming and how the hit scales with charge.</summary>
    [Serializable]
    public sealed class ChargeProfile
    {
        [Header("Timing")]
        [Tooltip("Seconds of charging to reach full strength. Holding longer adds nothing.")]
        [Min(0.05f)]
        public float fullChargeSeconds = 1f;

        [Tooltip("Movement speed while charging, relative to normal.")]
        [Range(0f, 1f)]
        public float moveSpeedMultiplier = 0.85f;

        [Header("Targeting")]
        public ChargedTargetingMode targeting = ChargedTargetingMode.CameraAim;

        [Tooltip("Camera aim only: degrees a full sideways drag turns the shot.")]
        [Range(0f, 90f)]
        public float maxAimYawDegrees = 25f;

        [Tooltip("Camera aim only: degrees a full vertical drag raises or lowers the shot.")]
        [Range(0f, 45f)]
        public float maxAimPitchDegrees = 12f;

        [Header("Hit, uncharged to fully charged")]
        [Min(0f)]
        public float damageMin = 10f;

        [Min(0f)]
        public float damageMax = 30f;

        [Min(0f)]
        public float staggerMin = 10f;

        [Min(0f)]
        public float staggerMax = 30f;

        public SpellElement element = SpellElement.Arcane;

        [Header("Presentation")]
        [Range(0f, 1f)]
        public float releaseTrauma = 0.2f;
    }

    /// <summary>A charged attack that launches one projectile (Arcane Arrow).</summary>
    [Serializable]
    public sealed class ProjectileProfile
    {
        [Tooltip("Metres per second, uncharged.")]
        [Min(0.1f)]
        public float speedMin = 30f;

        [Min(0.1f)]
        public float speedMax = 46f;

        [Tooltip("Distance after which the projectile fades out.")]
        [Min(1f)]
        public float range = 55f;

        [Tooltip("Collision radius, uncharged.")]
        [Min(0.01f)]
        public float radiusMin = 0.16f;

        [Min(0.01f)]
        public float radiusMax = 0.24f;

        [Tooltip("Visual size of the projectile, uncharged.")]
        [Min(0.1f)]
        public float scaleMin = 0.9f;

        [Min(0.1f)]
        public float scaleMax = 1.5f;

        [Tooltip("How far away the impact can be heard by enemies.")]
        [Min(0f)]
        public float noiseRadius = 10f;

        [Header("Effects on what it strikes, uncharged to fully charged")]
        [Tooltip("Arcane charge given to a target that can hold it, uncharged.")]
        [Min(0f)]
        public float arcaneChargeMin;

        [Min(0f)]
        public float arcaneChargeMax;

        /// <summary>The magical effects the projectile carries at the given charge, apart from its damage.</summary>
        public EffectPayload Effects(float power)
        {
            return new EffectPayload
            {
                arcaneCharge = ChargeScaling.Scale(arcaneChargeMin, arcaneChargeMax, power), targets = EffectTargets.All,
            };
        }
    }

    /// <summary>A charged attack that hits a sector in front of the caster (Flame Wave, Force Pulse).</summary>
    [Serializable]
    public sealed class AreaProfile
    {
        [Tooltip("How far the attack reaches, uncharged.")]
        [Min(0.5f)]
        public float reachMin = 4f;

        [Min(0.5f)]
        public float reachMax = 7f;

        [Range(10f, 360f)]
        public float arcDegrees = 80f;

        [Min(0.1f)]
        public float verticalReach = 2f;

        [Tooltip("Most targets one release can affect. The nearest are chosen.")]
        [Min(1)]
        public int maxTargets = 6;

        [Tooltip("Heat added to each target that can be heated, uncharged.")]
        [Min(0f)]
        public float heatMin;

        [Min(0f)]
        public float heatMax;

        [Tooltip("Metres each target that can be pushed is moved, uncharged.")]
        [Min(0f)]
        public float pushMin;

        [Min(0f)]
        public float pushMax;

        [Tooltip("How far away the attack can be heard by enemies.")]
        [Min(0f)]
        public float noiseRadius = 12f;

        /// <summary>The magical effects the attack carries at the given charge, apart from its damage.</summary>
        public EffectPayload Effects(float power)
        {
            return new EffectPayload
            {
                heat = ChargeScaling.Scale(heatMin, heatMax, power),
                force = ChargeScaling.Scale(pushMin, pushMax, power),
                targets = EffectTargets.All,
            };
        }
    }
}
