// Stand-ins for the render-pipeline members that Threefold's code uses. The compile check cannot
// build the real URP assemblies (they depend on Burst, Collections and Mathematics), so only the
// members in use are declared here. Each was checked against the source of URP 17.6.0 and Core
// 17.6.0, the versions bundled with Unity 6000.6.0f1:
//
//   Volume.isGlobal, Volume.sharedProfile            Core, Runtime/Volume/Volume.cs
//   UniversalAdditionalCameraData.renderPostProcessing   URP, Runtime/UniversalAdditionalCameraData.cs
//   CameraExtensions.GetUniversalAdditionalCameraData    same file
//   UniversalAdditionalLightData.softShadowQuality   URP, Runtime/UniversalAdditionalLightData.cs
//   SoftShadowQuality                                URP, Runtime/Data/UniversalRenderPipelineAsset.cs
//   UniversalRenderPipelineAsset: renderScale, shadowDistance, mainLightShadowmapResolution,
//     msaaSampleCount, supportsHDR (public get and set); supportsSoftShadows (public get only,
//     its setter is internal)                        URP, Runtime/Data/UniversalRenderPipelineAsset.cs
//
// A stand-in proves that Threefold's code is consistent with these declarations, nothing more.
namespace UnityEngine.Rendering
{
    public sealed class VolumeProfile : UnityEngine.ScriptableObject { }
    public class Volume : UnityEngine.MonoBehaviour { public bool isGlobal; public VolumeProfile sharedProfile; }
}
namespace UnityEngine.Rendering.Universal
{
    public enum SoftShadowQuality { UsePipelineSettings, Low, Medium, High }

    public class UniversalAdditionalCameraData : UnityEngine.MonoBehaviour { public bool renderPostProcessing { get; set; } }

    public class UniversalAdditionalLightData : UnityEngine.MonoBehaviour { public SoftShadowQuality softShadowQuality { get; set; } }

    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this UnityEngine.Camera camera) { return null; }
    }

    public class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        public float renderScale { get; set; }
        public float shadowDistance { get; set; }
        public int mainLightShadowmapResolution { get; set; }
        public int msaaSampleCount { get; set; }
        public bool supportsHDR { get; set; }
        public bool supportsSoftShadows { get; internal set; }
        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline() { return null; }
    }
}
