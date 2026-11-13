using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Reads the project's rendering settings and says what the visual style needs from them. It
    /// only reads; <see cref="ApplyChanges"/> is a separate step that the caller decides on.
    ///
    /// Exactly one project setting matters to the style and cannot be set while the game runs:
    /// whether a URP asset allows soft shadows. Everything else that the quality levels vary
    /// (resolution, shadow distance and size, anti-aliasing, HDR) is set at runtime by
    /// VisualStyleController and put back afterwards, so the project files are left alone.
    /// </summary>
    internal static class RenderingSettingsCheck
    {
        private const string SoftShadowsProperty = "m_SoftShadowsSupported";

        internal sealed class Finding
        {
            /// <summary>What was looked at, for example the asset's path.</summary>
            public string Subject;

            public string Text;

            /// <summary>True if the style proposes to change this setting.</summary>
            public bool ProposesChange;

            /// <summary>True for something that will not work until it is dealt with by hand.</summary>
            public bool Warning;

            internal Object Asset;
        }

        /// <summary>Inspects the colour space, every URP asset used by a quality level, and their renderers.</summary>
        internal static List<Finding> Inspect()
        {
            var findings = new List<Finding>();

            findings.Add(PlayerSettings.colorSpace == ColorSpace.Linear
                ? new Finding { Subject = "Colour space", Text = "Linear. Correct for the style." }
                : new Finding
                {
                    Subject = "Colour space", Warning = true,
                    Text = "Gamma. The style's colours and light response assume Linear (Project Settings > Player > " +
                           "Other Settings > Color Space). This is not changed automatically.",
                });

            var seen = new HashSet<Object>();
            var assets = new List<KeyValuePair<string, RenderPipelineAsset>>();

            string[] levels = QualitySettings.names;
            for (int i = 0; i < levels.Length; i++)
            {
                RenderPipelineAsset asset = QualitySettings.GetRenderPipelineAssetAt(i);
                if (asset != null && seen.Add(asset))
                {
                    assets.Add(new KeyValuePair<string, RenderPipelineAsset>($"quality level '{levels[i]}'", asset));
                }
            }

            RenderPipelineAsset fallback = GraphicsSettings.defaultRenderPipeline;
            if (fallback != null && seen.Add(fallback))
            {
                assets.Add(new KeyValuePair<string, RenderPipelineAsset>("the default in Graphics settings", fallback));
            }

            if (assets.Count == 0)
            {
                findings.Add(new Finding
                {
                    Subject = "Render pipeline", Warning = true,
                    Text = "No render pipeline asset is assigned to any quality level. The style needs URP.",
                });
                return findings;
            }

            foreach (KeyValuePair<string, RenderPipelineAsset> entry in assets)
            {
                InspectPipelineAsset(entry.Value, entry.Key, findings);
            }

            return findings;
        }

        /// <summary>Makes the changes the findings propose. Returns how many settings were changed.</summary>
        internal static int ApplyChanges(List<Finding> findings, StringBuilder report)
        {
            int changed = 0;

            foreach (Finding finding in findings)
            {
                if (!finding.ProposesChange || finding.Asset == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(finding.Asset);
                SerializedProperty softShadows = serialized.FindProperty(SoftShadowsProperty);
                if (softShadows == null || softShadows.boolValue)
                {
                    continue;
                }

                softShadows.boolValue = true;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(finding.Asset);
                changed++;
                report.AppendLine($"Changed '{AssetDatabase.GetAssetPath(finding.Asset)}': Soft Shadows off -> on.");
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return changed;
        }

        internal static int CountProposedChanges(List<Finding> findings)
        {
            int count = 0;
            foreach (Finding finding in findings)
            {
                if (finding.ProposesChange)
                {
                    count++;
                }
            }

            return count;
        }

        internal static string Describe(List<Finding> findings)
        {
            var text = new StringBuilder();
            foreach (Finding finding in findings)
            {
                string mark = finding.ProposesChange ? "CHANGE PROPOSED" : (finding.Warning ? "WARNING" : "ok");
                text.AppendLine($"[{mark}] {finding.Subject}: {finding.Text}");
            }

            return text.ToString().TrimEnd();
        }

        private static void InspectPipelineAsset(RenderPipelineAsset asset, string usedBy, List<Finding> findings)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string subject = $"{path} ({usedBy})";

            if (!(asset is UniversalRenderPipelineAsset))
            {
                findings.Add(new Finding
                {
                    Subject = subject, Warning = true,
                    Text = "This is not a URP asset. The style's shader only works with URP.",
                });
                return;
            }

            var serialized = new SerializedObject(asset);

            bool? softShadows = ReadBool(serialized, SoftShadowsProperty);
            if (softShadows == null)
            {
                findings.Add(new Finding
                {
                    Subject = subject, Warning = true,
                    Text = "The Soft Shadows setting was not found under its expected name; this URP version may differ. Nothing is changed.",
                });
            }
            else if (softShadows == false)
            {
                findings.Add(new Finding
                {
                    Subject = subject, ProposesChange = true, Asset = asset,
                    Text = "Soft Shadows is off, so shadow edges are crisp at every quality level. The Medium and High " +
                           "levels ask for soft edges (four samples, URP's setting for mobile). Proposed: switch it on. " +
                           "Cost: a few extra shadow-map samples per pixel where shadows are received.",
                });
            }
            else
            {
                findings.Add(new Finding { Subject = subject, Text = "Soft Shadows is on." });
            }

            if (ReadBool(serialized, "m_MainLightShadowsSupported") == false)
            {
                findings.Add(new Finding
                {
                    Subject = subject,
                    Text = "Main light shadows are off. The style works without shadows; forms are still shaped by the light response.",
                });
            }

            if (ReadBool(serialized, "m_UseSRPBatcher") == false)
            {
                findings.Add(new Finding
                {
                    Subject = subject,
                    Text = "The SRP Batcher is off. The style's shader supports it, and it lowers the cost of many objects. Not changed.",
                });
            }

            if (ReadBool(serialized, "m_RequireDepthTexture") == true || ReadBool(serialized, "m_RequireOpaqueTexture") == true)
            {
                findings.Add(new Finding
                {
                    Subject = subject,
                    Text = "A depth or opaque texture is requested. The style does not need either; each costs a full-screen copy. Not changed.",
                });
            }

            SerializedProperty scale = serialized.FindProperty("m_RenderScale");
            SerializedProperty distance = serialized.FindProperty("m_ShadowDistance");
            if (scale != null && distance != null)
            {
                findings.Add(new Finding
                {
                    Subject = subject,
                    Text = $"Render scale {scale.floatValue:0.##}, shadow distance {distance.floatValue:0} m. While the game " +
                           "runs, the quality level sets these, together with shadow map size, anti-aliasing and HDR, and " +
                           "puts them back afterwards. The file is not changed.",
                });
            }

            InspectRenderers(serialized, subject, findings);
        }

        private static void InspectRenderers(SerializedObject pipeline, string subject, List<Finding> findings)
        {
            SerializedProperty renderers = pipeline.FindProperty("m_RendererDataList");
            if (renderers == null || !renderers.isArray)
            {
                return;
            }

            for (int i = 0; i < renderers.arraySize; i++)
            {
                Object rendererData = renderers.GetArrayElementAtIndex(i).objectReferenceValue;
                if (rendererData == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(rendererData);
                SerializedProperty mode = serialized.FindProperty("m_RenderingMode");

                // 0 Forward, 1 Deferred, 2 Forward+ in URP 17.
                if (mode != null && mode.intValue == 1)
                {
                    findings.Add(new Finding
                    {
                        Subject = $"{AssetDatabase.GetAssetPath(rendererData)} (renderer of {subject})", Warning = true,
                        Text = "This renderer uses Deferred rendering. The style's shader has no deferred pass, so its " +
                               "objects would not be drawn. Use Forward or Forward+. Not changed.",
                    });
                }
            }
        }

        private static bool? ReadBool(SerializedObject serialized, string property)
        {
            SerializedProperty found = serialized.FindProperty(property);
            return found != null && found.propertyType == SerializedPropertyType.Boolean ? found.boolValue : (bool?)null;
        }
    }
}
