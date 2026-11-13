using UnityEngine;

namespace Threefold.World
{
    /// <summary>Slowly turns an object and lets it rise and fall, for floating magical props.</summary>
    [DisallowMultipleComponent]
    public sealed class FloatingMotion : MonoBehaviour
    {
        [Tooltip("Degrees per second around the vertical axis.")]
        [SerializeField] private float turnSpeed = 28f;

        [Tooltip("How far the object rises and falls from its starting height, in metres.")]
        [Min(0f)]
        [SerializeField] private float bobHeight = 0.18f;

        [Tooltip("Seconds for one full rise and fall.")]
        [Min(0.1f)]
        [SerializeField] private float bobPeriod = 3.2f;

        private Vector3 restPosition;

        private void OnEnable()
        {
            restPosition = transform.localPosition;
        }

        private void OnDisable()
        {
            transform.localPosition = restPosition;
        }

        private void Update()
        {
            transform.Rotate(0f, turnSpeed * Time.deltaTime, 0f, Space.Self);

            float offset = Mathf.Sin(Time.time * (2f * Mathf.PI / bobPeriod)) * bobHeight;
            transform.localPosition = restPosition + Vector3.up * offset;
        }
    }
}
