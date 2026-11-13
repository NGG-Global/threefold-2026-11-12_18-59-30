using Threefold.Combat;
using Threefold.Interface;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows a number that rises and fades wherever damage lands. A fixed set of labels is created
    /// once and reused, and the texts for common values are cached, so hits allocate no memory.
    /// Place it on an object inside a Screen Space Overlay canvas.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class DamageNumbers : MonoBehaviour
    {
        private const int CachedTextCount = 1000;
        private const float PopSeconds = 0.12f;

        private static readonly string[] CachedTexts = new string[CachedTextCount];

        [Tooltip("Camera used to place numbers on screen. If empty, the main camera is used.")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("Most numbers visible at once. The oldest is reused beyond this.")]
        [Range(1, 32)]
        [SerializeField] private int labelCount = 12;

        [Min(0.1f)]
        [SerializeField] private float lifetime = 0.8f;

        [Tooltip("How fast a number rises, in metres per second.")]
        [SerializeField] private float riseSpeed = 1.3f;

        [Tooltip("Random sideways spread, in metres, so numbers from rapid hits do not overlap.")]
        [Min(0f)]
        [SerializeField] private float spread = 0.25f;

        [Min(1)]
        [SerializeField] private int fontSize = 26;

        [SerializeField] private Color color = new Color(1f, 0.95f, 0.8f, 1f);
        [SerializeField] private Color defeatColor = new Color(1f, 0.78f, 0.35f, 1f);

        private Label[] labels = new Label[0];
        private int nextLabel;

        private struct Label
        {
            public Text Text;
            public RectTransform Rect;
            public Vector3 WorldPosition;
            public Color Color;
            public float Age;
            public bool Active;
        }

        private void Awake()
        {
            BuildLabels();
        }

        private void OnEnable()
        {
            CombatEvents.DamageDealt += HandleDamageDealt;
        }

        private void OnDisable()
        {
            CombatEvents.DamageDealt -= HandleDamageDealt;
        }

        private void LateUpdate()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                {
                    return;
                }
            }

            float deltaTime = Time.deltaTime;

            for (int i = 0; i < labels.Length; i++)
            {
                if (!labels[i].Active)
                {
                    continue;
                }

                labels[i].Age += deltaTime;
                float age = labels[i].Age;

                if (age >= lifetime)
                {
                    labels[i].Active = false;
                    labels[i].Text.enabled = false;
                    continue;
                }

                labels[i].WorldPosition += Vector3.up * (riseSpeed * deltaTime);
                Vector3 screen = viewCamera.WorldToScreenPoint(labels[i].WorldPosition);

                // Behind the camera: keep it alive but out of sight.
                bool inFront = screen.z > 0f;
                labels[i].Text.enabled = inFront;
                if (!inFront)
                {
                    continue;
                }

                labels[i].Rect.position = new Vector3(screen.x, screen.y, 0f);

                float progress = age / lifetime;
                Color tint = labels[i].Color;
                tint.a *= 1f - progress * progress;
                labels[i].Text.color = tint;

                float pop = 1f + 0.5f * Mathf.Clamp01(1f - age / PopSeconds);
                labels[i].Rect.localScale = new Vector3(pop, pop, 1f);
            }
        }

        private void HandleDamageDealt(DamageEvent damage)
        {
            if (labels.Length == 0)
            {
                return;
            }

            int index = nextLabel;
            nextLabel = (nextLabel + 1) % labels.Length;

            Vector3 offset = new Vector3(Random.Range(-spread, spread), Random.Range(0f, spread), Random.Range(-spread, spread));

            labels[index].Active = true;
            labels[index].Age = 0f;
            labels[index].WorldPosition = damage.Point + offset;
            labels[index].Color = damage.Defeated ? defeatColor : color;
            labels[index].Text.text = GetText(Mathf.RoundToInt(damage.Amount));
            labels[index].Text.enabled = true;
        }

        private static string GetText(int value)
        {
            if (value < 0 || value >= CachedTextCount)
            {
                return value.ToString();
            }

            if (CachedTexts[value] == null)
            {
                CachedTexts[value] = value.ToString();
            }

            return CachedTexts[value];
        }

        private void BuildLabels()
        {
            Font font = UiFont.Default;
            if (font == null)
            {
                return;
            }

            labels = new Label[labelCount];

            for (int i = 0; i < labels.Length; i++)
            {
                var labelObject = new GameObject("DamageNumber", typeof(RectTransform));
                labelObject.layer = gameObject.layer;

                var rect = (RectTransform)labelObject.transform;
                rect.SetParent(transform, false);
                rect.sizeDelta = new Vector2(120f, 40f);

                Text text = labelObject.AddComponent<Text>();
                text.font = font;
                text.fontSize = fontSize;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
                text.enabled = false;

                Shadow shadow = labelObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
                shadow.effectDistance = new Vector2(1.5f, -1.5f);

                labels[i].Text = text;
                labels[i].Rect = rect;
            }
        }
    }
}
