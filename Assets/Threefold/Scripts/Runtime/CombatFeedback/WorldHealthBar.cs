using Threefold.Combat;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// A small health bar that floats above an object and always faces the camera. It is hidden
    /// while the object is unharmed or defeated, so it only appears when it carries information.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldHealthBar : MonoBehaviour
    {
        [SerializeField] private Health health;

        [Tooltip("The part that shrinks as health is lost. A child of this object, one unit wide at full health.")]
        [SerializeField] private Transform fill;

        [Tooltip("Object holding the bar's renderers. Switched off while the bar is hidden.")]
        [SerializeField] private GameObject visuals;

        private Camera viewCamera;
        private Vector3 fillRestScale = Vector3.one;
        private Vector3 fillRestPosition;
        private float shownFraction = -1f;

        /// <summary>Connects the bar. Called by the setup tooling.</summary>
        public void Bind(Health source, Transform fillTransform, GameObject visualRoot)
        {
            health = source;
            fill = fillTransform;
            visuals = visualRoot;
        }

        private void Awake()
        {
            if (fill != null)
            {
                fillRestScale = fill.localScale;
                fillRestPosition = fill.localPosition;
            }
        }

        private void LateUpdate()
        {
            if (health == null)
            {
                return;
            }

            float fraction = health.Fraction;
            bool show = health.IsAlive && fraction < 0.999f;

            if (visuals != null && visuals.activeSelf != show)
            {
                visuals.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera != null)
            {
                transform.rotation = viewCamera.transform.rotation;
            }

            if (fill != null && !Mathf.Approximately(fraction, shownFraction))
            {
                shownFraction = fraction;

                // Shrink from the right: scale the bar and shift it left by half of what was removed.
                fill.localScale = new Vector3(fillRestScale.x * fraction, fillRestScale.y, fillRestScale.z);
                fill.localPosition = fillRestPosition + Vector3.left * (fillRestScale.x * (1f - fraction) * 0.5f);
            }
        }
    }
}
