using Threefold.Combat;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Makes a damaged object flash and jolt for a moment, so every hit is clearly registered.
    /// The colour is applied through a property block, which leaves the shared material untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Health health;

        [Tooltip("Object whose renderers flash and whose scale jolts. Usually the visual root.")]
        [SerializeField] private Transform visual;

        [ColorUsage(false, true)]
        [SerializeField] private Color flashColor = new Color(2.2f, 2f, 1.7f, 1f);

        [Min(0.01f)]
        [SerializeField] private float flashSeconds = 0.09f;

        [Tooltip("How much the object swells when hit, as a share of its size.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float punchScale = 0.1f;

        [Min(0.01f)]
        [SerializeField] private float punchSeconds = 0.16f;

        [Tooltip("How much the object gives under a blow: it is squashed across and stretched upwards by this share, swings " +
                 "back once and comes to rest. 0 switches it off. Presentation only.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float squash = 0.07f;

        [Min(0.01f)]
        [SerializeField] private float squashSeconds = 0.3f;

        private Renderer[] renderers = new Renderer[0];
        private MaterialPropertyBlock block;
        private Vector3 restScale = Vector3.one;
        private float flashRemaining;
        private float punchRemaining;
        private float squashRemaining;
        private bool flashing;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(Health source, Transform visualRoot)
        {
            health = source;
            visual = visualRoot;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();

            if (visual == null)
            {
                visual = transform;
            }

            renderers = visual.GetComponentsInChildren<Renderer>(true);
            restScale = visual.localScale;
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Damaged += HandleDamaged;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
            }

            ClearFlash();
            if (visual != null)
            {
                visual.localScale = restScale;
            }
            punchRemaining = 0f;
            squashRemaining = 0f;
        }

        private void Update()
        {
            if (flashing)
            {
                flashRemaining -= Time.deltaTime;
                if (flashRemaining <= 0f)
                {
                    ClearFlash();
                }
            }

            if (punchRemaining > 0f || squashRemaining > 0f)
            {
                punchRemaining = Mathf.Max(0f, punchRemaining - Time.deltaTime);
                squashRemaining = Mathf.Max(0f, squashRemaining - Time.deltaTime);

                float swell = 1f + punchScale * Mathf.Clamp01(punchRemaining / punchSeconds);
                float give = squash * Threefold.Combat.ImpactLayers.Squash(1f - Mathf.Clamp01(squashRemaining / squashSeconds));
                visual.localScale = new Vector3(restScale.x * swell * (1f - give), restScale.y * swell * (1f + give),
                    restScale.z * swell * (1f - give));
            }
        }

        private void HandleDamaged(DamageInfo damage)
        {
            flashRemaining = flashSeconds;
            punchRemaining = punchSeconds;
            squashRemaining = squash > 0f ? squashSeconds : 0f;

            if (flashing)
            {
                return;
            }

            flashing = true;
            block.SetColor(BaseColorId, flashColor);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(block);
                }
            }
        }

        private void ClearFlash()
        {
            if (!flashing)
            {
                return;
            }

            flashing = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(null);
                }
            }
        }
    }
}
