using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;
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
    /// Editor's quality level and render-pipeline asset, not the phone's; nothing is animated (the Mage is
    /// given one pose of a clip per picture) and what the game adds while it runs is absent; and no touch control
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

        /// <summary>
        /// Poses the Mage with one moment of a clip of Mage.fbx, through the Editor's animation preview
        /// (AnimationMode), which plays Humanoid clips on a model outside Play mode. The first attempt,
        /// asking the Animator to update itself, left the model in its rest pose (runs 21 to 24). Nothing
        /// moves after this: a picture shows a pose, not animation, and nothing the game adds while it runs
        /// (the bearing, the swing of cloak and hair, blinking) is in it. Returns what was done, for the log.
        /// </summary>
        private static string PoseMage(Scene scene, string clipName, float clipTime)
        {
            Animator animator = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "Mage")
                {
                    continue;
                }

                foreach (Animator candidate in root.GetComponentsInChildren<Animator>(true))
                {
                    if (candidate.avatar != null && candidate.avatar.isHuman)
                    {
                        animator = candidate;
                    }
                }
            }

            if (animator == null)
            {
                return "no Mage with a Humanoid Animator was found; rest pose";
            }

            AnimationClip clip = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(CharacterModelPostprocessor.MageModelPath))
            {
                if (asset is AnimationClip candidate && candidate.name == clipName)
                {
                    clip = candidate;
                }
            }

            if (clip == null)
            {
                return $"Mage.fbx has no clip '{clipName}'; rest pose";
            }

            try
            {
                if (!AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                }

                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, Mathf.Clamp01(clipTime) * clip.length);
                AnimationMode.EndSampling();
                return $"posed with '{clipName}' at {Mathf.Clamp01(clipTime) * clip.length:0.00} s of {clip.length:0.00} s";
            }
            catch (Exception exception)
            {
                return $"could not be posed with '{clipName}' ({exception.Message}); rest pose";
            }
        }

        // ------------------------------------------------------------ metrics

        private const int TimedRenders = 20;

        /// <summary>
        /// What one view costs to draw, as far as the Editor can say without a device: the time of a render
        /// call on the main thread (an average of several, after one that is not counted), and the counts
        /// Unity keeps of the last frame it drew. The time is CPU time of submitting the frame on this
        /// computer; it is not the GPU's time and not a phone's. A count of 0 means Unity did not report it.
        /// </summary>
        private static void AppendViewMetrics(StringBuilder metrics, string name, Camera view)
        {
            view.Render();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < TimedRenders; i++)
            {
                view.Render();
            }

            watch.Stop();
            double milliseconds = watch.Elapsed.TotalMilliseconds / TimedRenders;

            metrics.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "view {0,-18} render call {1,6:0.00} ms   draw calls {2,4}   of which SRP Batcher {3,4}   set-pass {4,4}   triangles {5,7}   shadow casters {6,3}",
                name, milliseconds, UnityStats.drawCalls, UnityStats.srpBatcherDrawCalls, UnityStats.setPassCalls, UnityStats.triangles,
                UnityStats.shadowCasters));
        }

        /// <summary>
        /// What the scene holds, counted exactly: renderers, triangles, materials, shaders with their keyword
        /// sets (each set is a shader variant the scene asks for), and textures with the memory Unity reports
        /// for them in the Editor (uncompressed or desktop formats; a phone's ASTC textures are smaller).
        /// Written to build/renders/metrics.txt beside the pictures.
        /// </summary>
        private static void WriteMetrics(Scene scene, StringBuilder views)
        {
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            var variants = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int renderers = 0;
            int shadowCasters = 0;
            long triangles = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    renderers++;
                    shadowCasters += renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off ? 1 : 0;

                    Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                    if (mesh != null)
                    {
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            triangles += (long)mesh.GetIndexCount(i) / 3;
                        }
                    }

                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null || !materials.Add(material))
                        {
                            continue;
                        }

                        string[] keywords = material.shaderKeywords;
                        Array.Sort(keywords, StringComparer.Ordinal);
                        string variant = (material.shader != null ? material.shader.name : "(no shader)") + "  [" +
                                         (keywords.Length > 0 ? string.Join(" ", keywords) : "no keywords") + "]";
                        variants[variant] = variants.TryGetValue(variant, out int count) ? count + 1 : 1;

                        foreach (int id in material.GetTexturePropertyNameIDs())
                        {
                            Texture texture = material.GetTexture(id);
                            if (texture != null)
                            {
                                textures.Add(texture);
                            }
                        }
                    }
                }
            }

            long textureBytes = 0;
            var textureLines = new List<string>();
            foreach (Texture texture in textures)
            {
                long bytes = Profiler.GetRuntimeMemorySizeLong(texture);
                textureBytes += bytes;
                textureLines.Add(string.Format(CultureInfo.InvariantCulture, "  {0,7:0} KB  {1}x{2}  {3}", bytes / 1024.0,
                    texture.width, texture.height, texture.name));
            }

            textureLines.Sort(StringComparer.Ordinal);
            textureLines.Reverse();

            var text = new StringBuilder();
            text.AppendLine("Reference Courtyard: rendering metrics (Unity Editor, not a device)");
            text.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}; " +
                            $"quality level {QualitySettings.names[QualitySettings.GetQualityLevel()]}; {Width}x{Height}");
            text.AppendLine();
            text.AppendLine("Per view (render call: CPU time on this computer, average of " + TimedRenders + "; counts as Unity reported them, 0 = not reported):");
            text.Append(views);
            text.AppendLine();
            text.AppendLine("Whole scene, counted:");
            text.AppendLine($"  renderers {renderers}, of which cast shadows {shadowCasters}");
            text.AppendLine($"  triangles {triangles}");
            text.AppendLine($"  materials {materials.Count}");
            text.AppendLine($"  shader variants asked for by materials {variants.Count}");
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  textures {0}, {1:0.0} MB as Unity holds them in the Editor", textures.Count,
                textureBytes / (1024.0 * 1024.0)));
            text.AppendLine();
            text.AppendLine("Shader variants (materials using each):");
            foreach (KeyValuePair<string, int> variant in variants)
            {
                text.AppendLine($"  {variant.Value,3}  {variant.Key}");
            }

            text.AppendLine();
            text.AppendLine("Textures:");
            foreach (string line in textureLines)
            {
                text.AppendLine(line);
            }

            File.WriteAllText(Path.Combine(Folder, "metrics.txt"), text.ToString());
            Debug.Log("[Threefold] Rendering metrics written.\n" + text);
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
            var metrics = new StringBuilder();

            try
            {
                view.targetTexture = target;
                view.fieldOfView = L.CameraVerticalFov;

                // The first picture Unity draws after starting showed one texture on every surface (run 21:
                // the whole courtyard in the foliage's leaves), and the pictures after it were right. Why is
                // not established. Two pictures are therefore drawn and thrown away before the first is kept.
                L.Viewpoint first = L.Viewpoints[0];
                view.transform.SetPositionAndRotation(first.Position, Quaternion.LookRotation(first.Target - first.Position, Vector3.up));
                view.Render();
                view.Render();

                foreach (L.Viewpoint viewpoint in L.Viewpoints)
                {
                    string pose = PoseMage(scene, viewpoint.Clip, viewpoint.ClipTime);
                    Debug.Log($"[Threefold] Review render '{viewpoint.Name}': the Mage {pose}.");
                    metrics.AppendLine($"pose {viewpoint.Name,-18} {pose}");

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

                    AppendViewMetrics(metrics, viewpoint.Name, view);
                }
            }
            finally
            {
                // The scene is not saved after this, so the camera's changes are not kept.
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }

                RenderTexture.active = before;
                view.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(picture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            WriteMetrics(scene, metrics);

            Debug.Log($"[Threefold] Review renders: {written} written to '{Folder}'. Graphics device: " +
                      $"{SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}. Quality level: " +
                      $"{QualitySettings.names[QualitySettings.GetQualityLevel()]}.");
            return written == L.Viewpoints.Count;
        }
    }
}
