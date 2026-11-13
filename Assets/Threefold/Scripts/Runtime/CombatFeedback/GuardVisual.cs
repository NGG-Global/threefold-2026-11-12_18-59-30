using Threefold.Combat;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows the guard while the player blocks: a pale arc on the ground in front of the character,
    /// as wide as the guarded arc, so it is plain which side is covered. It brightens for a moment
    /// when a hit is stopped. The same for every discipline. A stand-in until the character has a
    /// blocking pose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuardVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private PlayerGuard guard;
        [SerializeField] private MeshFilter arcFilter;
        [SerializeField] private MeshRenderer arcRenderer;

        [Tooltip("How far in front of the character the arc reaches, in metres.")]
        [Min(0.2f)]
        [SerializeField] private float radius = 1.25f;

        [SerializeField] private Color guardColor = new Color(1f, 0.96f, 0.86f, 0.3f);
        [SerializeField] private Color blockedColor = new Color(1f, 0.98f, 0.9f, 0.85f);

        [Tooltip("Seconds the arc stays bright after stopping a hit.")]
        [Min(0.01f)]
        [SerializeField] private float flashSeconds = 0.22f;

        [Min(0f)]
        [SerializeField] private float groundOffset = 0.05f;

        private MaterialPropertyBlock block;
        private Mesh arcMesh;
        private float builtArc = -1f;
        private float flashRemaining;
        private PlayerGuard subscribed;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(PlayerGuard playerGuard, MeshFilter filter, MeshRenderer arcShape)
        {
            guard = playerGuard;
            arcFilter = filter;
            arcRenderer = arcShape;
        }

        public PlayerGuard Guard => guard;

        private void OnEnable()
        {
            subscribed = guard;
            if (subscribed != null)
            {
                subscribed.Blocked += HandleBlocked;
            }

            SetVisible(false);
        }

        private void OnDisable()
        {
            if (subscribed != null)
            {
                subscribed.Blocked -= HandleBlocked;
                subscribed = null;
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (arcMesh != null)
            {
                Destroy(arcMesh);
            }
        }

        private void LateUpdate()
        {
            flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);

            if (guard == null || arcFilter == null || arcRenderer == null || !guard.IsBlocking)
            {
                SetVisible(false);
                return;
            }

            if (arcMesh == null || !Mathf.Approximately(builtArc, guard.GuardArcDegrees))
            {
                if (arcMesh != null)
                {
                    Destroy(arcMesh);
                }

                builtArc = guard.GuardArcDegrees;
                arcMesh = AimShapes.BuildSector(1f, builtArc, "GuardArc");
                arcFilter.sharedMesh = arcMesh;
            }

            Transform body = guard.transform;
            Vector3 forward = body.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            Transform shape = arcFilter.transform;
            shape.position = body.position + Vector3.up * groundOffset;
            shape.rotation = Quaternion.LookRotation(forward, Vector3.up);

            Vector3 parentScale = shape.parent != null ? shape.parent.lossyScale : Vector3.one;
            shape.localScale = new Vector3(radius / Mathf.Max(parentScale.x, 0.0001f), 1f,
                radius / Mathf.Max(parentScale.z, 0.0001f));

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            block.SetColor(BaseColorId, Color.Lerp(guardColor, blockedColor, flashRemaining / flashSeconds));
            arcRenderer.SetPropertyBlock(block);
            SetVisible(true);
        }

        private void HandleBlocked(BlockedHit hit)
        {
            flashRemaining = flashSeconds;
        }

        private void SetVisible(bool visible)
        {
            if (arcRenderer != null && arcRenderer.enabled != visible)
            {
                arcRenderer.enabled = visible;
            }
        }
    }
}
