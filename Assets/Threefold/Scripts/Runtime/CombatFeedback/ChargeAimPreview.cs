using System.Collections.Generic;
using Threefold.Combat;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Shows where a charged attack will go while it is being held.
    ///
    /// A shot aimed with the camera (Arcane Arrow) gets a ring at the point it would strike. The
    /// ring keeps the same size on screen at any distance, tightens as the charge builds, and is
    /// brighter when it rests on something. An attack along the ground (Flame Wave, Force Pulse)
    /// gets the outline of the area it would cover, growing with the charge.
    ///
    /// Both are flat unlit shapes in the discipline's colour. Nothing is drawn when no charge is held.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChargeAimPreview : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. The reticle faces this camera and keeps its size on screen. The main camera is used if empty.")]
        [SerializeField] private Camera viewCamera;

        [Header("Parts")]
        [Tooltip("Shows the area of a ground attack. Not a child of the character, so it does not turn with the body.")]
        [SerializeField] private MeshFilter areaFilter;
        [SerializeField] private MeshRenderer areaRenderer;

        [Tooltip("The reticle of a camera-aimed shot.")]
        [SerializeField] private MeshFilter reticleFilter;
        [SerializeField] private MeshRenderer reticleRenderer;

        [Header("Look")]
        [Tooltip("Reticle size as a share of its distance from the camera, at no charge.")]
        [Range(0.005f, 0.2f)]
        [SerializeField] private float reticleSize = 0.045f;

        [Tooltip("Reticle size at full charge, relative to its size at no charge.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float reticleFullChargeScale = 0.55f;

        [Range(0f, 1f)]
        [SerializeField] private float areaOpacityMin = 0.16f;

        [Range(0f, 1f)]
        [SerializeField] private float areaOpacityMax = 0.4f;

        [Tooltip("Height of the area above the ground, so it does not flicker against the floor.")]
        [Min(0f)]
        [SerializeField] private float groundOffset = 0.05f;

        private readonly Dictionary<int, Mesh> sectorMeshes = new Dictionary<int, Mesh>();
        private MaterialPropertyBlock block;
        private Mesh ringMesh;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(DisciplineCombat disciplineCombat, MeshFilter area, MeshRenderer areaShape, MeshFilter reticle,
            MeshRenderer reticleShape)
        {
            combat = disciplineCombat;
            areaFilter = area;
            areaRenderer = areaShape;
            reticleFilter = reticle;
            reticleRenderer = reticleShape;
        }

        public DisciplineCombat Combat => combat;

        /// <summary>The camera the reticle faces. The main camera is used while this is empty.</summary>
        public Camera ViewCamera
        {
            get => viewCamera;
            set => viewCamera = value;
        }

        private void OnEnable()
        {
            Hide();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in sectorMeshes.Values)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }

            sectorMeshes.Clear();

            if (ringMesh != null)
            {
                Destroy(ringMesh);
            }
        }

        private void LateUpdate()
        {
            if (combat == null || !combat.TryGetChargePreview(out ChargePreview preview) || preview.Discipline == null)
            {
                Hide();
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            Color color = preview.Discipline.Color;

            if (preview.Mode == ChargedTargetingMode.GroundDirection)
            {
                ShowArea(preview, color);
                SetVisible(reticleRenderer, false);
            }
            else
            {
                ShowReticle(preview, color);
                SetVisible(areaRenderer, false);
            }
        }

        private void ShowArea(in ChargePreview preview, Color color)
        {
            if (areaFilter == null || areaRenderer == null)
            {
                return;
            }

            AreaShape area = preview.Area;
            areaFilter.sharedMesh = GetSector(area.ArcDegrees);

            Transform shape = areaFilter.transform;
            shape.position = area.Origin + Vector3.up * groundOffset;
            shape.rotation = Quaternion.LookRotation(area.Forward, Vector3.up);
            SetWorldScale(shape, new Vector3(area.Reach, 1f, area.Reach));

            color.a = Mathf.Lerp(areaOpacityMin, areaOpacityMax, preview.Power);
            block.SetColor(BaseColorId, color);
            areaRenderer.SetPropertyBlock(block);
            SetVisible(areaRenderer, true);
        }

        private void ShowReticle(in ChargePreview preview, Color color)
        {
            if (reticleFilter == null || reticleRenderer == null)
            {
                return;
            }

            Camera view = viewCamera != null ? viewCamera : Camera.main;
            if (view == null)
            {
                SetVisible(reticleRenderer, false);
                return;
            }

            if (ringMesh == null)
            {
                ringMesh = AimShapes.BuildRing(0.78f, 1f, 40, "ChargeReticle");
            }

            reticleFilter.sharedMesh = ringMesh;

            Transform viewTransform = view.transform;
            Vector3 toPoint = preview.Point - viewTransform.position;
            float distance = Mathf.Max(0.5f, toPoint.magnitude);

            // Pulled a little towards the camera so the ring is not buried in the surface it rests on.
            Transform shape = reticleFilter.transform;
            shape.position = preview.Point - toPoint.normalized * Mathf.Min(0.25f, distance * 0.05f);
            shape.rotation = viewTransform.rotation;

            float size = distance * reticleSize * Mathf.Lerp(1f, reticleFullChargeScale, preview.Power);
            SetWorldScale(shape, new Vector3(size, size, size));

            color.a = preview.HasHit ? 0.95f : 0.55f;
            block.SetColor(BaseColorId, color);
            reticleRenderer.SetPropertyBlock(block);
            SetVisible(reticleRenderer, true);
        }

        private Mesh GetSector(float arcDegrees)
        {
            int key = Mathf.Clamp(Mathf.RoundToInt(arcDegrees), 10, 360);
            if (!sectorMeshes.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = AimShapes.BuildSector(1f, key, "ChargeArea" + key);
                sectorMeshes[key] = mesh;
            }

            return mesh;
        }

        private void Hide()
        {
            SetVisible(areaRenderer, false);
            SetVisible(reticleRenderer, false);
        }

        /// <summary>Sets the size in the world whatever the parent's scale is.</summary>
        private static void SetWorldScale(Transform shape, Vector3 worldScale)
        {
            Transform parent = shape.parent;
            if (parent == null)
            {
                shape.localScale = worldScale;
                return;
            }

            Vector3 parentScale = parent.lossyScale;
            shape.localScale = new Vector3(
                Mathf.Approximately(parentScale.x, 0f) ? worldScale.x : worldScale.x / parentScale.x,
                Mathf.Approximately(parentScale.y, 0f) ? worldScale.y : worldScale.y / parentScale.y,
                Mathf.Approximately(parentScale.z, 0f) ? worldScale.z : worldScale.z / parentScale.z);
        }

        private static void SetVisible(Renderer target, bool visible)
        {
            if (target != null && target.enabled != visible)
            {
                target.enabled = visible;
            }
        }
    }
}
