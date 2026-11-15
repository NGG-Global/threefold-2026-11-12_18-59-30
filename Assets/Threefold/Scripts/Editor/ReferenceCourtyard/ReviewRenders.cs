using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using L = Threefold.EditorTools.ReferenceCourtyardLayout;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Renders the Reference Courtyard from the viewpoints of its layout into PNG files, with Unity started
    /// from the command line (Tools/build/render-review.cmd), so that the scene can be looked at without
    /// opening the Editor or installing a build.
    ///
    /// What the pictures are, and are not: they are rendered by Unity's own renderer with the game's shaders,
    /// materials and lighting, through the scene's own camera. They are rendered in the Editor, which uses the
    /// Editor's quality level and render-pipeline asset, not the phone's; nothing is animated, so the Mage
    /// stands in the model's rest pose and effects that need the game to run are absent; and no touch control
    /// or HUD is drawn. They show composition, materials and light. They do not show how the game looks or
    /// performs on a device.
    /// </summary>
    public static class ReviewRenders
    {
        private const string Folder = "build/renders";
        private const int Width = 1600;
        private const int Height = 900;

        /// <summary>Entry point for -executeMethod. Exits with 0 when every picture was written, otherwise 1.</summary>
        public static void CaptureReferenceCourtyard()
        {
            int code = 1;
            try
            {
                code = Capture() ? 0 : 1;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            EditorApplication.Exit(code);
        }

        private static bool Capture()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.LogError("[Threefold] Unity was started without a graphics device (-nographics), so nothing can be rendered.");
                return false;
            }

            Scene scene = ReferenceCourtyardSetup.BuildForAutomation();
            Camera view = SceneSetupUtility.FindInScene<Camera>(scene);
            if (view == null)
            {
                Debug.LogError("[Threefold] The Reference Courtyard has no camera to render with.");
                return false;
            }

            Directory.CreateDirectory(Folder);
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture before = RenderTexture.active;
            int written = 0;

            try
            {
                view.targetTexture = target;
                view.fieldOfView = L.CameraVerticalFov;

                foreach (L.Viewpoint viewpoint in L.Viewpoints)
                {
                    view.transform.SetPositionAndRotation(viewpoint.Position,
                        Quaternion.LookRotation(viewpoint.Target - viewpoint.Position, Vector3.up));
                    view.Render();

                    RenderTexture.active = target;
                    picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    picture.Apply();

                    string path = Path.Combine(Folder, "ReferenceCourtyard-" + viewpoint.Name + ".png");
                    File.WriteAllBytes(path, picture.EncodeToPNG());
                    Debug.Log($"[Threefold] Review render written: {path}");
                    written++;
                }
            }
            finally
            {
                // The scene is not saved after this, so the camera's changes are not kept.
                RenderTexture.active = before;
                view.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(picture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            Debug.Log($"[Threefold] Review renders: {written} written to '{Folder}'. Graphics device: " +
                      $"{SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}. Quality level: " +
                      $"{QualitySettings.names[QualitySettings.GetQualityLevel()]}.");
            return written == L.Viewpoints.Count;
        }
    }
}
