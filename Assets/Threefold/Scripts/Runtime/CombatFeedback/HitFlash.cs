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

        private Renderer[] renderers = new Renderer[0];
        private MaterialPropertyBlock block;
        private Vector3 restScale = Vector3.one;
        private float flashRemaining;
        private float punchRemaining;
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
            visual.localScale = restScale;
            punchRemaining = 0f;
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

            if (punchRemaining > 0f)
            {
                punchRemaining -= Time.deltaTime;
                float progress = Mathf.Clamp01(punchRemaining / punchSeconds);
                visual.localScale = restScale * (1f + punchScale * progress);
            }
        }

        private void HandleDamaged(DamageInfo damage)
        {
            flashRemaining = flashSeconds;
            punchRemaining = punchSeconds;

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
