// Stand-ins for the four render-pipeline members the Editor code uses. Verified separately against package source.
namespace UnityEngine.Rendering
{
    public sealed class VolumeProfile : UnityEngine.ScriptableObject { }
    public class Volume : UnityEngine.MonoBehaviour { public bool isGlobal; public VolumeProfile sharedProfile; }
}
namespace UnityEngine.Rendering.Universal
{
    public class UniversalAdditionalCameraData : UnityEngine.MonoBehaviour { public bool renderPostProcessing { get; set; } }
    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this UnityEngine.Camera camera) { return null; }
    }
}
