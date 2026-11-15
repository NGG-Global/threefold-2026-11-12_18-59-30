using Threefold.Magic;
using Threefold.Pooling;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows magic arriving at magical objects, the same on every object: Arcane charge flowing into it from
    /// where it struck, fire licking where heat struck, a pressure streak along a push, a brace closing round it.
    /// What changed afterwards (an activation, ignition, a fall, a brace held) is shown by the object's own state
    /// visuals, configured in its definition. Which arrival is shown follows from the effect and the object's
    /// answer (<see cref="WorldReactionVfx"/>), never from the kind of object. Only the player's own attacks show
    /// an arrival; a chain reaction shows its result. It reads the world and changes nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldMagicFeedback : MonoBehaviour
    {
        private const int MaxEach = 6;
        private const int RecentCapacity = 16;

        [Tooltip("Arcane charge flowing into an object. Needs an InflowEffect.")]
        [SerializeField] private PooledObject chargeInflow;

        [Tooltip("Fire licking an object where heat struck it.")]
        [SerializeField] private PooledObject heatArrival;

        [Tooltip("A pressure streak along a push, at the object. Points along its forward axis.")]
        [SerializeField] private PooledObject forceArrival;

        [Tooltip("A brace closing round an object. An InflowEffect (shell only) or a burst.")]
        [SerializeField] private PooledObject braceArrival;

        [Tooltip("Optional. Discipline colours. Found in the scene when empty; the built-in colours are used without it.")]
        [SerializeField] private PlayerDisciplines disciplines;

        [Tooltip("Optional. Scales particle counts with the player's graphics preset.")]
        [SerializeField] private VisualStyleController style;

        [Range(MagicVfxQuality.MinIntensity, 1f)]
        [SerializeField] private float intensity = 1f;

        [Tooltip("The same arrival on the same object is shown at most once in this many seconds, so a held guard or a " +
                 "multi-hit attack does not flood the object.")]
        [Min(0f)]
        [SerializeField] private float repeatSeconds = 0.5f;

        private readonly MagicalObject[] recentObjects = new MagicalObject[RecentCapacity];
        private readonly WorldArrivalLook[] recentLooks = new WorldArrivalLook[RecentCapacity];
        private readonly float[] recentTimes = new float[RecentCapacity];
        private int recentNext;
        private bool styleSearched;
        private bool disciplinesSearched;

        /// <summary>Connects the effects. Called by the setup tooling.</summary>
        public void Bind(PooledObject charge, PooledObject heat, PooledObject force, PooledObject brace,
            PlayerDisciplines playerDisciplines, VisualStyleController styleController)
        {
            chargeInflow = charge;
            heatArrival = heat;
            forceArrival = force;
            braceArrival = brace;
            disciplines = playerDisciplines;
            style = styleController;
        }

        /// <summary>Connects only the effects that are not set. Returns true if anything changed.</summary>
        public bool FillEffects(PooledObject charge, PooledObject heat, PooledObject force, PooledObject brace)
        {
            bool changed = false;
            changed |= Fill(ref chargeInflow, charge);
            changed |= Fill(ref heatArrival, heat);
            changed |= Fill(ref forceArrival, force);
            changed |= Fill(ref braceArrival, brace);
            return changed;
        }

        public float Intensity
        {
            get => intensity;
            set => intensity = MagicVfxQuality.ClampIntensity(value);
        }

        private void OnEnable()
        {
            MagicalObject.AnyEffectReceived += HandleEffect;
            styleSearched = false;
            disciplinesSearched = false;
        }

        private void OnDisable()
        {
            MagicalObject.AnyEffectReceived -= HandleEffect;
        }

        private void HandleEffect(MagicalObject target, MagicEffect effect, EffectResponse response)
        {
            if (target == null || !WorldReactionVfx.ShowsArrival(effect.Depth))
            {
                return;
            }

            WorldArrivalLook look = WorldReactionVfx.Choose(effect.Kind, response);
            if (look == WorldArrivalLook.None || ShownRecently(target, look))
            {
                return;
            }

            Vector3 centre = Centre(target, out float radius);
            Vector3 struck = effect.Point.sqrMagnitude > 0.0001f ? effect.Point : centre;
            Color color = ColorOf(effect.Discipline, effect.Kind);
            MagicVfxLevel level = CurrentLevel();

            switch (look)
            {
                case WorldArrivalLook.ChargeInflow:
                {
                    Vector3 from = MagicVfxGeometry.InflowStart(struck, centre, effect.Direction, 0.6f, 2.5f);
                    PlayInflow(chargeInflow, from, centre, radius, color, level, level.Secondaries);
                    break;
                }

                case WorldArrivalLook.Brace:
                    // The brace closes in from all round: the shell is the effect, on every preset.
                    PlayInflow(braceArrival, centre, centre, radius * 1.15f, color, level, true);
                    break;

                case WorldArrivalLook.Heat:
                {
                    Vector3 normal = struck - centre;
                    Quaternion rotation = normal.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(normal.normalized, Vector3.up)
                        : Quaternion.identity;
                    SpawnBurst(heatArrival, struck, rotation, level);
                    break;
                }

                case WorldArrivalLook.Force:
                {
                    Vector3 push = effect.Direction;
                    push.y = 0f;
                    Quaternion rotation = push.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(push.normalized, Vector3.up)
                        : Quaternion.identity;
                    SpawnBurst(forceArrival, centre, rotation, level);
                    break;
                }
            }
        }

        private void PlayInflow(PooledObject prefab, Vector3 from, Vector3 centre, float radius, Color color,
            in MagicVfxLevel level, bool withShell)
        {
            PooledObject instance = Pools.Spawn(prefab, from, Quaternion.identity, MaxEach);
            if (instance == null)
            {
                return;
            }

            if (instance.TryGetComponent(out InflowEffect inflow))
            {
                inflow.Play(from, centre, radius, color, level.EmitterRateScale, withShell);
            }
            else
            {
                BurstParticleShare.Apply(instance, level.BurstShare);
            }
        }

        private void SpawnBurst(PooledObject prefab, Vector3 position, Quaternion rotation, in MagicVfxLevel level)
        {
            PooledObject instance = Pools.Spawn(prefab, position, rotation, MaxEach);
            if (instance != null)
            {
                BurstParticleShare.Apply(instance, level.BurstShare);
            }
        }

        private bool ShownRecently(MagicalObject id, WorldArrivalLook look)
        {
            float now = Time.time;
            for (int i = 0; i < RecentCapacity; i++)
            {
                if (recentObjects[i] == id && recentLooks[i] == look && now - recentTimes[i] < repeatSeconds)
                {
                    return true;
                }
            }

            recentObjects[recentNext] = id;
            recentLooks[recentNext] = look;
            recentTimes[recentNext] = now;
            recentNext = (recentNext + 1) % RecentCapacity;
            return false;
        }

        /// <summary>The middle of what is drawn of the object, and a radius that just covers it.</summary>
        private static Vector3 Centre(MagicalObject target, out float radius)
        {
            Renderer shown = target.GetComponentInChildren<Renderer>();
            if (shown != null)
            {
                Bounds bounds = shown.bounds;
                radius = Mathf.Clamp(bounds.extents.magnitude, 0.2f, 2f);
                return bounds.center;
            }

            radius = 0.5f;
            return target.transform.position + Vector3.up * 0.5f;
        }

        private Color ColorOf(int slot, MagicEffectKind kind)
        {
            if (disciplines == null && !disciplinesSearched)
            {
                disciplinesSearched = true;
                disciplines = FindAnyObjectByType<PlayerDisciplines>();
            }

            if (slot >= 0)
            {
                return disciplines != null ? disciplines.GetInfo(slot).Color : DisciplineCatalog.Get(slot).Color;
            }

            // An effect without a discipline (a hazard, a test) takes the colour of the discipline that carries its kind.
            switch (kind)
            {
                case MagicEffectKind.Heat:
                    return DisciplineCatalog.Get(MagicDiscipline.Flame).Color;
                case MagicEffectKind.ArcaneCharge:
                    return DisciplineCatalog.Get(MagicDiscipline.Arcane).Color;
                default:
                    return DisciplineCatalog.Get(MagicDiscipline.Aegis).Color;
            }
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

        private static bool Fill(ref PooledObject field, PooledObject value)
        {
            if (field != null || value == null)
            {
                return false;
            }

            field = value;
            return true;
        }
    }
}
