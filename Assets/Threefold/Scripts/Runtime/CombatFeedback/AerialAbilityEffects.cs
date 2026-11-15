using Threefold.Characters;
using Threefold.Magic;
using Threefold.Pooling;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows the disciplines' aerial abilities from their events: a burst when an ability starts, an effect held
    /// on the body for as long as it lasts (Arcane's rising ribbon, Flame's jets while propelling, Aegis's trail),
    /// stopped the moment it pauses or ends, and a flash where a dash is stopped by a wall. Which effect plays is
    /// in each discipline's presentation. Pooled; it changes nothing in the movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AerialAbilityEffects : MonoBehaviour
    {
        private const int MaxBursts = 4;
        private const int MaxHeld = 4;

        [SerializeField] private AerialAbilities abilities;

        [Tooltip("Optional. Hands and chest. Found on the Mage when empty.")]
        [SerializeField] private DisciplineVfxSockets sockets;

        [Tooltip("Optional. Scales particle counts and trail lengths with the player's graphics preset.")]
        [SerializeField] private VisualStyleController style;

        [Range(MagicVfxQuality.MinIntensity, 1f)]
        [SerializeField] private float intensity = 1f;

        private readonly PooledObject[] held = new PooledObject[2];
        private readonly int[] heldGeneration = new int[2];
        private AerialAbilities subscribed;
        private bool styleSearched;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(AerialAbilities aerialAbilities, DisciplineVfxSockets vfxSockets, VisualStyleController styleController)
        {
            abilities = aerialAbilities;
            sockets = vfxSockets;
            style = styleController;
        }

        public AerialAbilities Abilities => abilities;

        public float Intensity
        {
            get => intensity;
            set => intensity = MagicVfxQuality.ClampIntensity(value);
        }

        private void OnEnable()
        {
            subscribed = abilities;
            if (subscribed != null)
            {
                subscribed.AbilityEvent += HandleEvent;
                if (sockets == null)
                {
                    sockets = subscribed.GetComponentInChildren<DisciplineVfxSockets>(true);
                }
            }

            styleSearched = false;
        }

        private void OnDisable()
        {
            if (subscribed != null)
            {
                subscribed.AbilityEvent -= HandleEvent;
                subscribed = null;
            }

            StopHeld(0.05f);
        }

        private void HandleEvent(AerialAbilityEvent aerialEvent)
        {
            PlayerDisciplines disciplines = subscribed != null ? subscribed.GetComponent<PlayerDisciplines>() : null;
            DisciplineDefinition definition = disciplines != null ? disciplines.GetDefinition(aerialEvent.Slot) : null;
            if (definition == null)
            {
                StopHeld(0.05f);
                return;
            }

            DisciplinePresentation look = definition.Presentation;
            AerialEvent e = aerialEvent.Event;

            switch (e.Kind)
            {
                case AerialEventKind.Activated:
                case AerialEventKind.Resumed:
                {
                    Vector3 direction = e.Direction.sqrMagnitude > 0.0001f ? e.Direction : Vector3.up;
                    Quaternion rotation = Quaternion.LookRotation(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.forward : Vector3.up);
                    if (e.Kind == AerialEventKind.Activated)
                    {
                        Spawn(look.aerialBurstEffect, BurstPoint(look.aerialSustainPlacement), rotation, MaxBursts);
                    }

                    StopHeld(0.02f);
                    Hold(look.aerialSustainEffect, look.aerialSustainPlacement);
                    break;
                }

                case AerialEventKind.Paused:
                    // Propulsion stopped: the jets go out at once; what is already in the air fades.
                    StopHeld(0.06f);
                    break;

                case AerialEventKind.Ended:
                    StopHeld(e.Reason == AerialEndReason.Wall ? 0.05f : 0.12f);
                    if (e.Reason == AerialEndReason.Wall && look.aerialImpactEffect != null)
                    {
                        Vector3 normal = e.Direction.sqrMagnitude > 0.0001f ? e.Direction : -subscribed.transform.forward;
                        Spawn(look.aerialImpactEffect, aerialEvent.Point + Vector3.up * 1.0f,
                            Quaternion.LookRotation(normal, Vector3.up), MaxBursts);
                    }

                    break;
            }
        }

        private Vector3 BurstPoint(AerialEffectPlacement placement)
        {
            Transform body = subscribed.transform;
            switch (placement)
            {
                case AerialEffectPlacement.Chest:
                    return sockets != null ? sockets.Chest.position : body.position + Vector3.up * 1.3f;
                case AerialEffectPlacement.BothHands:
                    return sockets != null ? sockets.HandsMidpoint : body.position + Vector3.up * 1.1f;
                default:
                    return body.position + Vector3.up * 0.05f;
            }
        }

        private void Hold(PooledObject prefab, AerialEffectPlacement placement)
        {
            if (prefab == null || subscribed == null)
            {
                return;
            }

            Transform body = subscribed.transform;
            switch (placement)
            {
                case AerialEffectPlacement.BothHands when sockets != null:
                    Attach(0, prefab, sockets.LeftHand, Vector3.zero);
                    Attach(1, prefab, sockets.RightHand, Vector3.zero);
                    break;

                case AerialEffectPlacement.Chest when sockets != null:
                    Attach(0, prefab, sockets.Chest, Vector3.zero);
                    break;

                default:
                    Attach(0, prefab, body, new Vector3(0f, 0.15f, 0f));
                    break;
            }
        }

        private void Attach(int index, PooledObject prefab, Transform parent, Vector3 localOffset)
        {
            if (parent == null)
            {
                return;
            }

            Vector3 position = parent.TransformPoint(localOffset);
            PooledObject instance = Spawn(prefab, position, parent.rotation, MaxHeld);
            if (instance == null)
            {
                return;
            }

            instance.transform.SetParent(parent, true);
            held[index] = instance;
            heldGeneration[index] = instance.Generation;
        }

        private void StopHeld(float drainSeconds)
        {
            for (int i = 0; i < held.Length; i++)
            {
                PooledObject instance = held[i];
                held[i] = null;
                if (instance == null || !instance.IsSpawn(heldGeneration[i]))
                {
                    continue;
                }

                if (instance.TryGetComponent(out PooledEffect effect))
                {
                    effect.StopEarly(Mathf.Max(drainSeconds, effect.LongestTrailSeconds));
                }
                else
                {
                    instance.ReturnToPool();
                }
            }
        }

        private PooledObject Spawn(PooledObject prefab, Vector3 position, Quaternion rotation, int maxInstances)
        {
            if (prefab == null)
            {
                return null;
            }

            PooledObject instance = Pools.Spawn(prefab, position, rotation, maxInstances);
            if (instance == null)
            {
                return null;
            }

            MagicVfxLevel level = CurrentLevel();
            BurstParticleShare.Apply(instance, level.BurstShare);
            if (instance.TryGetComponent(out PooledEffect effect))
            {
                effect.SetTrailTimeScale(level.TrailTimeScale);
            }

            return instance;
        }

        private MagicVfxLevel CurrentLevel()
        {
            if (style == null && !styleSearched)
            {
                styleSearched = true;
                style = FindAnyObjectByType<VisualStyleController>();
            }

            return style != null
                ? MagicVfxQuality.For(style.QualityLevel, style.QualityLevelCount, intensity)
                : MagicVfxQuality.For(0, 0, intensity);
        }
    }
}
