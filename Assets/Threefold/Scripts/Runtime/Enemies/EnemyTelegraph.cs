using System.Collections.Generic;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// Warns the player of an attack before it lands:
    ///
    ///   melee: the area that will be struck is drawn on the ground and fills up as the wind-up
    ///   runs out, so the player can see both where and when;
    ///   ranged: a glow gathers at the point of casting, and a thin line shows where the shot
    ///   will go. The line follows the player, then holds still and brightens when the aim locks,
    ///   which is the moment to step aside.
    ///
    /// Everything is a few unlit quads and one line, coloured through property blocks: no
    /// particles, no lights, no textures beyond the shared glow. Nothing is shown between attacks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyTelegraph : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const float StrikeFlashSeconds = 0.15f;
        private const float SegmentDegrees = 10f;

        [Header("Ground area (melee)")]
        [Tooltip("Shows the whole area that will be struck.")]
        [SerializeField] private MeshFilter areaOutline;

        [Tooltip("Grows from the enemy outwards until it fills the area, as the wind-up runs out.")]
        [SerializeField] private MeshFilter areaFill;

        [Header("Charge")]
        [Tooltip("A glow that grows at the point of casting during the wind-up. Faces the camera.")]
        [SerializeField] private Renderer chargeGlow;

        [Tooltip("Size of the charge glow when fully grown, in metres.")]
        [Min(0.05f)]
        [SerializeField] private float chargeSize = 0.8f;

        [Header("Aim line (ranged)")]
        [SerializeField] private LineRenderer aimLine;
        [SerializeField] private TargetAimProvider aim;

        [Tooltip("Where the line starts. Usually the caster's cast point.")]
        [SerializeField] private Transform castPoint;

        [Header("Colours")]
        [SerializeField] private Color areaColor = new Color(1f, 0.52f, 0.2f, 0.18f);
        [SerializeField] private Color fillColor = new Color(1f, 0.4f, 0.12f, 0.42f);
        [SerializeField] private Color lockedColor = new Color(1f, 0.25f, 0.1f, 0.6f);

        [ColorUsage(true, true)]
        [SerializeField] private Color chargeColor = new Color(2.4f, 1.3f, 0.5f, 1f);

        [SerializeField] private Color aimColor = new Color(1f, 0.6f, 1f, 0.25f);
        [SerializeField] private Color aimLockedColor = new Color(1f, 0.55f, 1f, 0.85f);

        private readonly List<Mesh> areaMeshes = new List<Mesh>();
        private Renderer outlineRenderer;
        private Renderer fillRenderer;
        private MaterialPropertyBlock block;
        private Camera viewCamera;
        private Vector3 chargeRestScale = Vector3.one;

        private bool active;
        private bool locked;
        private AttackKind kind;
        private float duration;
        private float elapsed;
        private float flashRemaining;

        /// <summary>Connects the parts. Called by the setup tooling.</summary>
        public void Bind(MeshFilter outline, MeshFilter fill, Renderer charge, LineRenderer line,
            TargetAimProvider aimProvider, Transform lineStart)
        {
            areaOutline = outline;
            areaFill = fill;
            chargeGlow = charge;
            aimLine = line;
            aim = aimProvider;
            castPoint = lineStart;
        }

        public void SetColors(Color area, Color fill, Color locked, Color charge)
        {
            areaColor = area;
            fillColor = fill;
            lockedColor = locked;
            chargeColor = charge;
        }

        /// <summary>
        /// Builds the ground shape of one attack: a slice of a circle with the attack's reach and
        /// arc, or a full circle for 360 degrees. Built once, when the enemy starts.
        /// </summary>
        public void PrepareArea(int attackIndex, float reach, float arcDegrees)
        {
            if (attackIndex < 0)
            {
                return;
            }

            while (areaMeshes.Count <= attackIndex)
            {
                areaMeshes.Add(null);
            }

            if (areaMeshes[attackIndex] != null)
            {
                Destroy(areaMeshes[attackIndex]);
            }

            areaMeshes[attackIndex] = BuildSector(reach, arcDegrees);
        }

        public void Begin(int attackIndex, AttackKind attackKind, float windupSeconds)
        {
            active = true;
            locked = false;
            kind = attackKind;
            duration = Mathf.Max(0.05f, windupSeconds);
            elapsed = 0f;
            flashRemaining = 0f;

            Mesh area = attackIndex >= 0 && attackIndex < areaMeshes.Count ? areaMeshes[attackIndex] : null;
            bool showArea = kind == AttackKind.Melee && area != null;

            if (areaOutline != null)
            {
                areaOutline.sharedMesh = area;
            }

            if (areaFill != null)
            {
                areaFill.sharedMesh = area;
                areaFill.transform.localScale = new Vector3(0.001f, 1f, 0.001f);
            }

            SetVisible(outlineRenderer, showArea);
            SetVisible(fillRenderer, showArea);
            SetVisible(chargeGlow, true);

            if (aimLine != null)
            {
                aimLine.enabled = kind == AttackKind.Ranged && aim != null;
            }

            UpdateVisuals();
        }

        /// <summary>The attack stops following the target.</summary>
        public void Lock()
        {
            locked = true;
        }

        /// <summary>The attack lands: a short, bright flash, then everything is hidden.</summary>
        public void Strike()
        {
            flashRemaining = StrikeFlashSeconds;
        }

        public void End(bool interrupted)
        {
            active = false;

            if (interrupted || flashRemaining <= 0f)
            {
                flashRemaining = 0f;
                HideAll();
            }
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            outlineRenderer = areaOutline != null ? areaOutline.GetComponent<Renderer>() : null;
            fillRenderer = areaFill != null ? areaFill.GetComponent<Renderer>() : null;

            if (chargeGlow != null)
            {
                chargeRestScale = chargeGlow.transform.localScale;
            }

            HideAll();
        }

        private void OnDisable()
        {
            active = false;
            flashRemaining = 0f;
            HideAll();
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in areaMeshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }

            areaMeshes.Clear();
        }

        private void LateUpdate()
        {
            if (flashRemaining > 0f)
            {
                flashRemaining -= Time.deltaTime;
                UpdateFlash();
                if (flashRemaining <= 0f && !active)
                {
                    HideAll();
                }

                return;
            }

            if (!active)
            {
                return;
            }

            elapsed += Time.deltaTime;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = progress * progress * (3f - 2f * progress);

            if (fillRenderer != null && fillRenderer.enabled)
            {
                areaFill.transform.localScale = new Vector3(Mathf.Max(0.001f, eased), 1f, Mathf.Max(0.001f, eased));
                SetColor(fillRenderer, locked ? lockedColor : fillColor);
            }

            if (outlineRenderer != null && outlineRenderer.enabled)
            {
                Color outline = areaColor;
                outline.a *= 0.6f + 0.4f * Mathf.Sin(elapsed * 14f) * progress + 0.4f * progress;
                SetColor(outlineRenderer, outline);
            }

            if (chargeGlow != null)
            {
                float size = Mathf.Lerp(0.15f, 1f, eased) * (locked ? 1f + 0.15f * Mathf.Sin(elapsed * 40f) : 1f);
                chargeGlow.transform.localScale = chargeRestScale * (chargeSize * size);
                FaceCamera(chargeGlow.transform);
                SetColor(chargeGlow, chargeColor * Mathf.Lerp(0.4f, 1f, progress));
            }

            if (aimLine != null && aimLine.enabled)
            {
                Vector3 start = castPoint != null ? castPoint.position : transform.position + Vector3.up * 1.5f;
                aimLine.SetPosition(0, start);
                aimLine.SetPosition(1, aim.CurrentAimPoint(start));

                Color color = locked ? aimLockedColor : aimColor;
                aimLine.startColor = color;
                aimLine.endColor = new Color(color.r, color.g, color.b, color.a * 0.3f);
            }
        }

        private void UpdateFlash()
        {
            float strength = Mathf.Clamp01(flashRemaining / StrikeFlashSeconds);

            if (fillRenderer != null && fillRenderer.enabled)
            {
                areaFill.transform.localScale = Vector3.one;
                Color flash = lockedColor;
                flash.a = Mathf.Lerp(0f, 0.85f, strength);
                SetColor(fillRenderer, flash);
            }

            SetVisible(outlineRenderer, false);

            if (chargeGlow != null)
            {
                chargeGlow.transform.localScale = chargeRestScale * (chargeSize * (1f + 0.6f * (1f - strength)));
                FaceCamera(chargeGlow.transform);
                SetColor(chargeGlow, chargeColor * strength);
            }

            if (aimLine != null)
            {
                aimLine.enabled = false;
            }
        }

        private void HideAll()
        {
            SetVisible(outlineRenderer, false);
            SetVisible(fillRenderer, false);
            SetVisible(chargeGlow, false);

            if (aimLine != null)
            {
                aimLine.enabled = false;
            }
        }

        private void FaceCamera(Transform target)
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                {
                    return;
                }
            }

            target.rotation = viewCamera.transform.rotation;
        }

        private void SetColor(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }

            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }

        private static void SetVisible(Renderer target, bool visible)
        {
            if (target != null)
            {
                target.enabled = visible;
            }
        }

        /// <summary>A flat fan facing up, centred on the enemy, pointing forward (+Z).</summary>
        private static Mesh BuildSector(float reach, float arcDegrees)
        {
            arcDegrees = Mathf.Clamp(arcDegrees, 10f, 360f);
            int segments = Mathf.Max(3, Mathf.CeilToInt(arcDegrees / SegmentDegrees));
            var vertices = new Vector3[segments + 2];
            var uvs = new Vector2[segments + 2];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            float start = -arcDegrees * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float radians = (start + arcDegrees * i / segments) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                vertices[i + 1] = direction * reach;
                uvs[i + 1] = new Vector2(0.5f + direction.x * 0.5f, 0.5f + direction.z * 0.5f);
            }

            // Angles increase clockwise seen from above, so this winding faces upwards.
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            var mesh = new Mesh { name = "TelegraphArea" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
