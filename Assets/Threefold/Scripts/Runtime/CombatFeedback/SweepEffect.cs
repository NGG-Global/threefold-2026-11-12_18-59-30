using System.Collections.Generic;
using Threefold.Pooling;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// A flat sector that flashes on the ground and fades: the arc of a melee swing, a Flame Wave or
    /// a Force Pulse. One pooled prefab serves every discipline; the caller gives the reach, the arc
    /// and the colour. The shape sweeps outward from most of its size to all of it while it fades,
    /// which reads as motion without any particles.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PooledObject))]
    public sealed class SweepEffect : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // One unit-sized mesh per arc, shared by every instance and scaled to the reach.
        private static readonly Dictionary<int, Mesh> Meshes = new Dictionary<int, Mesh>();

        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;

        [Tooltip("Height above the ground, so the shape does not flicker against the floor.")]
        [Min(0f)]
        [SerializeField] private float groundOffset = 0.06f;

        [Tooltip("Size at the start, relative to the full reach.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float startScale = 0.55f;

        private PooledObject pooled;
        private MaterialPropertyBlock block;
        private Color color;
        private float reach = 1f;
        private float duration = 0.2f;
        private float elapsed;
        private bool playing;

        /// <summary>Connects the renderer. Called by the setup tooling.</summary>
        public void Bind(MeshFilter filter, MeshRenderer shapeRenderer)
        {
            meshFilter = filter;
            meshRenderer = shapeRenderer;
        }

        /// <summary>Shows the shape at this object's position, opening along its forward direction.</summary>
        public void Play(float shapeReach, float arcDegrees, Color shapeColor, float seconds)
        {
            reach = Mathf.Max(0.1f, shapeReach);
            color = shapeColor;
            duration = Mathf.Max(0.05f, seconds);
            elapsed = 0f;
            playing = true;

            if (meshFilter != null)
            {
                meshFilter.sharedMesh = GetMesh(arcDegrees);
            }

            Vector3 position = transform.position;
            transform.position = new Vector3(position.x, position.y + groundOffset, position.z);
            Apply(0f);
        }

        private void Awake()
        {
            pooled = GetComponent<PooledObject>();
            block = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            playing = false;
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            Apply(progress);

            if (progress >= 1f)
            {
                playing = false;
                pooled.ReturnToPool();
            }
        }

        private void Apply(float progress)
        {
            // Fast at first, settling: an ease-out on the size and a straight fade.
            float eased = 1f - (1f - progress) * (1f - progress);
            float scale = reach * Mathf.Lerp(startScale, 1f, eased);
            transform.localScale = new Vector3(scale, 1f, scale);

            if (meshRenderer == null)
            {
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            Color shown = color;
            shown.a *= 1f - progress;
            block.SetColor(BaseColorId, shown);
            meshRenderer.SetPropertyBlock(block);
            meshRenderer.enabled = shown.a > 0.003f;
        }

        private static Mesh GetMesh(float arcDegrees)
        {
            int key = Mathf.Clamp(Mathf.RoundToInt(arcDegrees / 5f) * 5, 10, 360);
            if (!Meshes.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = AimShapes.BuildSector(1f, key, "Sweep" + key);
                mesh.hideFlags = HideFlags.HideAndDontSave;
                Meshes[key] = mesh;
            }

            return mesh;
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Meshes.Clear();
        }
    }
}
