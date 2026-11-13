using UnityEngine;
using UnityEngine.UI;

namespace Threefold.Interface
{
    /// <summary>
    /// Gives the Image on the same object one of the generated shapes from
    /// <see cref="ProceduralSprites"/>. The shape is assigned whenever the object is enabled, in
    /// the Editor as well, because generated sprites cannot be saved in a scene.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class ProceduralImage : MonoBehaviour
    {
        [SerializeField] private ProceduralShape shape = ProceduralShape.Disc;

        public ProceduralShape Shape
        {
            get => shape;
            set
            {
                shape = value;
                Apply();
            }
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Apply()
        {
            if (TryGetComponent(out Image image))
            {
                image.sprite = ProceduralSprites.Get(shape);
            }
        }
    }
}
