using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Aims at whatever is under the centre of the screen. A ray is cast from the camera through the
    /// screen centre, and the ability is directed from the character towards the point that ray
    /// reaches, so a shot lands where the crosshair is even though it starts at the character.
    ///
    /// A small aim assist pulls the aim onto a nearby target that is close to the crosshair, which
    /// makes aiming with a thumb on a touchscreen forgiving.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraAimProvider : AimProvider
    {
        [Tooltip("Camera to aim with. If empty, the main camera is used.")]
        [SerializeField] private Camera aimCamera;

        [Tooltip("Layers the aim ray can land on.")]
        [SerializeField] private LayerMask aimLayers = Physics.DefaultRaycastLayers;

        [Tooltip("How far the aim reaches, in metres.")]
        [Min(1f)]
        [SerializeField] private float maxDistance = 60f;

        [Header("Aim assist")]
        [Tooltip("A target within this many degrees of the crosshair is aimed at directly. 0 switches assist off.")]
        [Range(0f, 30f)]
        [SerializeField] private float assistAngle = 7f;

        [Tooltip("Aim assist only considers targets within this distance, in metres.")]
        [Min(0f)]
        [SerializeField] private float assistRange = 35f;

        private const float MinimumAimDistance = 1.5f;

        private readonly RaycastHit[] hits = new RaycastHit[8];

        public Camera AimCamera
        {
            get => aimCamera;
            set => aimCamera = value;
        }

        public override AimSolution GetAim(Vector3 origin)
        {
            Camera viewCamera = ResolveCamera();
            if (viewCamera == null)
            {
                Vector3 forward = transform.forward;
                return new AimSolution(origin + forward * maxDistance, forward);
            }

            Transform view = viewCamera.transform;
            Vector3 viewDirection = view.forward;

            // Start the ray level with the character so nothing between camera and character is hit.
            float skip = Mathf.Max(0f, Vector3.Dot(origin - view.position, viewDirection));
            Vector3 rayStart = view.position + viewDirection * skip;

            Vector3 point = rayStart + viewDirection * maxDistance;
            float nearest = maxDistance;

            int count = Physics.RaycastNonAlloc(rayStart, viewDirection, hits, maxDistance, aimLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance < nearest && !hits[i].transform.IsChildOf(transform))
                {
                    nearest = hits[i].distance;
                    point = hits[i].point;
                }
            }

            if (TryFindAssistTarget(view.position, viewDirection, out Vector3 assisted))
            {
                point = assisted;
            }

            Vector3 toPoint = point - origin;

            // A point beside or behind the character would send the shot sideways; fire along the view instead.
            if (toPoint.magnitude < MinimumAimDistance || Vector3.Dot(toPoint, viewDirection) <= 0f)
            {
                return new AimSolution(origin + viewDirection * maxDistance, viewDirection);
            }

            return new AimSolution(point, toPoint.normalized);
        }

        private bool TryFindAssistTarget(Vector3 viewPosition, Vector3 viewDirection, out Vector3 aimPoint)
        {
            aimPoint = default;
            if (assistAngle <= 0f)
            {
                return false;
            }

            float bestAngle = assistAngle;
            bool found = false;
            var candidates = Health.Active;

            for (int i = 0; i < candidates.Count; i++)
            {
                Health candidate = candidates[i];
                if (candidate == null || !candidate.IsAlive || candidate.transform.IsChildOf(transform))
                {
                    continue;
                }

                Vector3 candidatePoint = candidate.AimPoint;
                Vector3 toCandidate = candidatePoint - viewPosition;

                if ((candidatePoint - transform.position).sqrMagnitude > assistRange * assistRange)
                {
                    continue;
                }

                float angle = Vector3.Angle(viewDirection, toCandidate);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    aimPoint = candidatePoint;
                    found = true;
                }
            }

            return found;
        }

        private Camera ResolveCamera()
        {
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            return aimCamera;
        }
    }
}
