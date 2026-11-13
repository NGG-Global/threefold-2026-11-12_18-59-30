using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>
    /// Makes a puzzle element's state visible through glowing materials and an optional light:
    /// dim while dormant, a slow pulse while it is ready and waiting for the player, bright while
    /// active, and a brief warm flash when an attempt is refused. Colours fade rather than jump.
    ///
    /// The glow is set per renderer through a material property block, so the shared material is
    /// never changed. The material must have emission enabled (URP Lit with Emission ticked).
    /// </summary>
    public sealed class PuzzleGlow : MonoBehaviour
    {
        [SerializeField] private PuzzleElement source;

        [Tooltip("Optional. While this element is active the glow shows the waiting pulse, instead of " +
                 "following the source's own readiness. Example: a plate that beckons while the timer runs.")]
        [SerializeField] private PuzzleElement readyWhile;

        [SerializeField] private Renderer[] renderers = new Renderer[0];

        [Tooltip("Material slot that glows on each renderer, or -1 for all slots.")]
        [SerializeField] private int materialSlot = -1;

        [Tooltip("Optional. Its intensity follows the glow.")]
        [SerializeField] private Light glowLight;

        [Header("Colours")]
        [ColorUsage(false, true)]
        [SerializeField] private Color dormantColor = new Color(0.04f, 0.05f, 0.12f);

        [ColorUsage(false, true)]
        [SerializeField] private Color readyColor = new Color(0.3f, 0.42f, 1.3f);

        [ColorUsage(false, true)]
        [SerializeField] private Color activeColor = new Color(1.1f, 1.6f, 3.6f);

        [ColorUsage(false, true)]
        [SerializeField] private Color rejectedColor = new Color(1.8f, 0.45f, 0.25f);

        [Header("Light intensity")]
        [Min(0f)] [SerializeField] private float dormantLight;
        [Min(0f)] [SerializeField] private float readyLight = 0.6f;
        [Min(0f)] [SerializeField] private float activeLight = 2.4f;

        [Header("Motion")]
        [Tooltip("Pulses per second while waiting.")]
        [Min(0f)]
        [SerializeField] private float pulseRate = 0.8f;

        [Tooltip("How far the waiting pulse dims, 0 to 1.")]
        [Range(0f, 1f)]
        [SerializeField] private float pulseDepth = 0.5f;

        [Tooltip("How quickly the colour follows a change. Higher is faster.")]
        [Min(0.1f)]
        [SerializeField] private float fadeSpeed = 6f;

        [Min(0.05f)]
        [SerializeField] private float rejectedSeconds = 0.45f;

        private MaterialPropertyBlock block;
        private Color current;
        private float currentLight;
        private float rejectedRemaining;
        private PuzzleElement subscribed;

        public PuzzleElement Source => source;
        public Renderer[] Renderers => renderers;
        public int MaterialSlot => materialSlot;

        /// <summary>Connects the glow. Called by the setup tooling.</summary>
        public void Bind(PuzzleElement element, PuzzleElement readyWhileElement, Renderer[] targets, int slot, Light light)
        {
            source = element;
            readyWhile = readyWhileElement;
            renderers = targets ?? new Renderer[0];
            materialSlot = slot;
            glowLight = light;
        }

        /// <summary>Sets the three state colours. Called by the setup tooling.</summary>
        public void SetColors(Color dormant, Color ready, Color active)
        {
            dormantColor = dormant;
            readyColor = ready;
            activeColor = active;
        }

        /// <summary>Sets the light intensity for each state. Called by the setup tooling.</summary>
        public void SetLightLevels(float dormant, float ready, float active)
        {
            dormantLight = Mathf.Max(0f, dormant);
            readyLight = Mathf.Max(0f, ready);
            activeLight = Mathf.Max(0f, active);
        }

        private void OnEnable()
        {
            subscribed = source;
            if (subscribed != null)
            {
                subscribed.Rejected += HandleRejected;
            }

            GetTarget(false, out current, out currentLight);
            Apply(current, currentLight);
        }

        private void OnDisable()
        {
            if (subscribed != null)
            {
                subscribed.Rejected -= HandleRejected;
                subscribed = null;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            GetTarget(true, out Color target, out float targetLight);

            float blend = 1f - Mathf.Exp(-fadeSpeed * deltaTime);
            current = Color.Lerp(current, target, blend);
            currentLight = Mathf.Lerp(currentLight, targetLight, blend);

            Color shown = current;
            if (rejectedRemaining > 0f)
            {
                rejectedRemaining -= deltaTime;
                shown = Color.Lerp(current, rejectedColor, Mathf.Clamp01(rejectedRemaining / rejectedSeconds));
            }

            Apply(shown, currentLight);
        }

        private void GetTarget(bool pulse, out Color color, out float intensity)
        {
            bool active = source != null && source.IsActive;
            bool ready = readyWhile != null ? readyWhile.IsActive : source != null && source.IsReady;

            if (active)
            {
                color = activeColor;
                intensity = activeLight;
                return;
            }

            if (ready)
            {
                float wave = pulse ? 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * 2f * Mathf.PI) : 1f;
                float level = 1f - pulseDepth * (1f - wave);
                color = readyColor * level;
                intensity = readyLight * level;
                return;
            }

            color = dormantColor;
            intensity = dormantLight;
        }

        private void HandleRejected(PuzzleElement element)
        {
            rejectedRemaining = rejectedSeconds;
        }

        private void Apply(Color color, float intensity)
        {
            GlowUtility.SetEmission(renderers, materialSlot, color, ref block);

            if (glowLight != null)
            {
                glowLight.intensity = intensity;
                bool lit = intensity > 0.01f;
                if (glowLight.enabled != lit)
                {
                    glowLight.enabled = lit;
                }
            }
        }
    }

    /// <summary>Sets the emission colour of renderers without changing their shared materials.</summary>
    public static class GlowUtility
    {
        public static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public static void SetEmission(Renderer[] renderers, int materialSlot, Color color, ref MaterialPropertyBlock block)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                SetEmission(renderers[i], materialSlot, color, ref block);
            }
        }

        public static void SetEmission(Renderer target, int materialSlot, Color color, ref MaterialPropertyBlock block)
        {
            if (target == null)
            {
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            // The slot is not range-checked here because reading the material list allocates every
            // frame; the scene validation checks it instead.
            if (materialSlot >= 0)
            {
                target.GetPropertyBlock(block, materialSlot);
                block.SetColor(EmissionColorId, color);
                target.SetPropertyBlock(block, materialSlot);
            }
            else
            {
                target.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, color);
                target.SetPropertyBlock(block);
            }
        }
    }
}
