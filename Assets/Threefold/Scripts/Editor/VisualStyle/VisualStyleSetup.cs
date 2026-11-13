using System.Collections.Generic;
using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Interface;
using Threefold.VisualStyle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Applies Threefold's visual style, Soft Illustrated Realism, to a scene, and takes it off
    /// again. Applying it:
    ///
    ///   generates the painted textures and the material presets (existing ones are kept);
    ///   replaces the grey-box environment materials in the scene with their painted counterparts;
    ///   gives the Mage its soft-lit materials with the contour line;
    ///   sets the lighting: a warm sun, cool ambient fill, distance haze and a tinted sky;
    ///   adds the controller for quality levels and lighting, and a small test panel;
    ///   optionally adds a few wood, cloth and metal props.
    ///
    /// Removing it puts the grey-box materials and lighting back and deletes what was added, so
    /// the two looks can be compared. Nothing in the project's rendering settings is changed
    /// without being shown first: see Threefold > Visual Style > Check Rendering Settings.
    ///
    /// The style is a proposal for review (Docs/ArtBible.md, section 12), not an approved result.
    /// </summary>
    public static class VisualStyleSetup
    {
        private const string MenuRoot = "Threefold/Visual Style/";
        private const string DialogTitle = "Visual Style";
        internal const string ControllerName = "VisualStyle";
        private const string PanelName = "VisualStyleTestPanel";
        private const int UiLayer = 5;

        /// <summary>Where the props go in a scene, or null for none.</summary>
        internal sealed class PropPlacement
        {
            public Vector3 Position;
            public float Yaw;
        }

        // ---------------------------------------------------------------- menu

        [MenuItem(MenuRoot + "Apply To Open Scene", false, 20)]
        public static void ApplyToOpenScene()
        {
            if (!ReadyForEditing())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();

            var report = new StringBuilder();
            Apply(scene, report, false, null);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} applied.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nSave the scene with Ctrl+S. Use Remove From Open Scene to compare with the earlier look.", "OK");

            OfferSettingChanges();
        }

        [MenuItem(MenuRoot + "Remove From Open Scene", false, 21)]
        public static void RemoveFromOpenScene()
        {
            if (!ReadyForEditing())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();

            var report = new StringBuilder();
            Remove(scene, report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} removed.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nThe style's assets are kept. Save the scene with Ctrl+S.", "OK");
        }

        [MenuItem(MenuRoot + "Check Rendering Settings", false, 40)]
        public static void CheckRenderingSettings()
        {
            List<RenderingSettingsCheck.Finding> findings = RenderingSettingsCheck.Inspect();
            string text = RenderingSettingsCheck.Describe(findings);
            Debug.Log($"[Threefold] Rendering settings:\n{text}");

            if (RenderingSettingsCheck.CountProposedChanges(findings) == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, text + "\n\nNo change is proposed.", "OK");
                return;
            }

            if (EditorUtility.DisplayDialog(DialogTitle,
                    text + "\n\nMake the proposed change(s)? Nothing else is touched.", "Change", "Leave as it is"))
            {
                var report = new StringBuilder();
                RenderingSettingsCheck.ApplyChanges(findings, report);
                Debug.Log($"[Threefold] {report.ToString().TrimEnd()}");
                EditorUtility.DisplayDialog(DialogTitle, report.ToString().TrimEnd(), "OK");
            }
        }

        [MenuItem(MenuRoot + "Repaint Textures", false, 41)]
        public static void RepaintTextures()
        {
            if (!EditorUtility.DisplayDialog(DialogTitle,
                    "Paint the style's textures again from code?\n\nTextures in 'Assets/Threefold/Generated/VisualStyle/Textures' " +
                    "are overwritten, including any that were edited by hand. Materials are kept.", "Repaint", "Cancel"))
            {
                return;
            }

            var report = new StringBuilder();
            VisualStyleAssets.Ensure(report, true);
            Debug.Log($"[Threefold] {report.ToString().TrimEnd()}");
            EditorUtility.DisplayDialog(DialogTitle, report.ToString().TrimEnd(), "OK");
        }

        [MenuItem(MenuRoot + "Apply To Open Scene", true)]
        [MenuItem(MenuRoot + "Remove From Open Scene", true)]
        [MenuItem(MenuRoot + "Repaint Textures", true)]
        private static bool CanEdit()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        // ------------------------------------------------------------ interface

        /// <summary>True if the style has been applied to the scene.</summary>
        internal static bool IsApplied(Scene scene)
        {
            return FindInScene<VisualStyleController>(scene) != null;
        }

        /// <summary>
        /// For the level setup commands. On a build server the one proposed project setting is
        /// changed, because nothing there is kept; in an open Editor it is only reported.
        /// </summary>
        internal static void ApplyDuringSetup(Scene scene, StringBuilder report, PropPlacement props)
        {
            Apply(scene, report, Application.isBatchMode, props);
        }

        /// <param name="changeProjectSettings">Also make the proposed change to the URP assets, without asking.</param>
        /// <param name="props">Where to put the wood, cloth and metal props, or null for none.</param>
        internal static void Apply(Scene scene, StringBuilder report, bool changeProjectSettings, PropPlacement props)
        {
            VisualStyleAssets assets = VisualStyleAssets.Ensure(report);

            int swapped = SwapMaterials(scene, assets.Replacements);
            report.AppendLine(swapped > 0
                ? $"Replaced materials on {swapped} renderer(s) with their painted counterparts."
                : "No grey-box materials were left to replace.");

            Light sun = FindSun(scene);
            Camera viewCamera = FindCamera(scene);
            VisualStyleController controller = EnsureController(scene, sun, viewCamera, report);

            ApplyLighting(controller, sun, assets, report);
            EnsureTestPanel(scene, controller, report);
            EnsureProps(scene, assets, props, report);

            List<RenderingSettingsCheck.Finding> findings = RenderingSettingsCheck.Inspect();
            foreach (RenderingSettingsCheck.Finding finding in findings)
            {
                if (finding.Warning)
                {
                    report.AppendLine($"WARNING: {finding.Subject}: {finding.Text}");
                }
            }

            int proposed = RenderingSettingsCheck.CountProposedChanges(findings);
            if (proposed > 0 && changeProjectSettings)
            {
                RenderingSettingsCheck.ApplyChanges(findings, report);
            }
            else if (proposed > 0)
            {
                report.AppendLine($"{proposed} change(s) to the project's rendering settings are proposed and were NOT made: " +
                                  "soft shadow edges need them. See Threefold > Visual Style > Check Rendering Settings.");
            }

            report.AppendLine(assets.UsesIllustratedShader
                ? "The visual style is applied. It is a proposal: judge it in Play mode and on a device."
                : "The visual style is applied with the stock shader, because the style's own shader is not usable (see above).");
        }

        /// <summary>Puts the grey-box materials and lighting back and deletes what the style added to the scene.</summary>
        internal static void Remove(Scene scene, StringBuilder report)
        {
            VisualStyleAssets assets = VisualStyleAssets.Ensure(report);

            var reverse = new Dictionary<Material, Material>();
            foreach (KeyValuePair<Material, Material> pair in assets.Replacements)
            {
                reverse[pair.Value] = pair.Key;
            }

            int swapped = SwapMaterials(scene, reverse);
            report.AppendLine($"Put the earlier materials back on {swapped} renderer(s).");

            Light sun = FindSun(scene);
            if (sun != null)
            {
                Undo.RecordObject(sun, DialogTitle);
                Undo.RecordObject(sun.transform, DialogTitle);
            }

            var greyBoxSky = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentAssets.MaterialFolder + "/Sky.mat");
            FantasyPlaygroundSetup.ApplyGreyBoxLighting(sun, greyBoxSky);
            if (sun != null)
            {
                MarkChanged(sun);
                MarkChanged(sun.transform);
            }

            report.AppendLine("Put the earlier lighting back.");

            foreach (GameObject added in new[]
                     {
                         FindObject<VisualStyleTestPanel>(scene), FindObject<VisualStyleController>(scene),
                         StyleProps.Find(scene),
                     })
            {
                if (added != null)
                {
                    Undo.DestroyObjectImmediate(added);
                }
            }

            report.AppendLine("Removed the style controller, the test panel and the style props.");
        }

        /// <summary>Reports problems with an applied style. Called by the scene validator.</summary>
        internal static void Validate(Scene scene, System.Action<string> problem)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer candidate in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in candidate.sharedMaterials)
                    {
                        if (material != null && material.shader == null)
                        {
                            problem($"'{candidate.name}' uses the material '{material.name}', which has no shader.");
                        }
                    }
                }
            }

            VisualStyleController controller = FindInScene<VisualStyleController>(scene);
            if (controller == null)
            {
                return;
            }

            if (controller.Sun == null)
            {
                problem("The visual style controller has no sun assigned, so lighting and shadow quality cannot be switched.");
            }

            if (controller.QualityLevelCount == 0)
            {
                problem("The visual style controller has no quality levels.");
            }

            if (controller.LightingCount == 0)
            {
                problem("The visual style controller has no lighting presets.");
            }

            // A Mage with a model should carry the contour line when the style's shader is usable.
            ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
            Shader shader = VisualStyleAssets.FindUsableShader(out string _);
            if (motor == null || shader == null)
            {
                return;
            }

            SkinnedMeshRenderer[] skinned = motor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length == 0)
            {
                return;
            }

            bool outlined = false;
            foreach (SkinnedMeshRenderer mesh in skinned)
            {
                foreach (Material material in mesh.sharedMaterials)
                {
                    outlined |= material != null && material.shader == shader &&
                                material.GetShaderPassEnabled(VisualStyleAssets.OutlinePass);
                }
            }

            if (!outlined)
            {
                problem("The visual style is applied, but the Mage has no material with a contour line.");
            }
        }

        // -------------------------------------------------------------- private

        private static bool ReadyForEditing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode first.", "OK");
                return false;
            }

            return true;
        }

        private static void OfferSettingChanges()
        {
            List<RenderingSettingsCheck.Finding> findings = RenderingSettingsCheck.Inspect();
            if (RenderingSettingsCheck.CountProposedChanges(findings) > 0)
            {
                CheckRenderingSettings();
            }
        }

        /// <summary>Replaces materials on every renderer in the scene. Returns how many renderers changed.</summary>
        private static int SwapMaterials(Scene scene, Dictionary<Material, Material> replacements)
        {
            if (replacements.Count == 0)
            {
                return 0;
            }

            int changed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer candidate in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = candidate.sharedMaterials;
                    bool any = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null && replacements.TryGetValue(materials[i], out Material replacement) &&
                            replacement != null)
                        {
                            materials[i] = replacement;
                            any = true;
                        }
                    }

                    if (any)
                    {
                        Undo.RecordObject(candidate, DialogTitle);
                        candidate.sharedMaterials = materials;
                        MarkChanged(candidate);
                        changed++;
                    }
                }
            }

            return changed;
        }

        private static Light FindSun(Scene scene)
        {
            Light sun = RenderSettings.sun;
            if (sun != null && sun.gameObject.scene == scene)
            {
                return sun;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light candidate in root.GetComponentsInChildren<Light>(true))
                {
                    if (candidate.type == LightType.Directional)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static Camera FindCamera(Scene scene)
        {
            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
            if (rig != null && rig.TryGetComponent(out Camera rigCamera))
            {
                return rigCamera;
            }

            return FindInScene<Camera>(scene);
        }

        private static GameObject FindObject<T>(Scene scene) where T : Component
        {
            T found = FindInScene<T>(scene);
            return found != null ? found.gameObject : null;
        }

        private static VisualStyleController EnsureController(Scene scene, Light sun, Camera viewCamera, StringBuilder report)
        {
            VisualStyleController controller = FindInScene<VisualStyleController>(scene);
            if (controller == null)
            {
                var host = new GameObject(ControllerName);
                Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                controller = host.AddComponent<VisualStyleController>();
                report.AppendLine("Added the style controller: quality levels Low, Medium and High; lighting Golden Afternoon and Overcast Morning.");
            }

            Undo.RecordObject(controller, DialogTitle);
            controller.BindMissing(sun, viewCamera);
            MarkChanged(controller);

            if (sun == null)
            {
                report.AppendLine("WARNING: the scene has no directional light, so the style's lighting has no sun.");
            }

            return controller;
        }

        /// <summary>Sets the scene's lighting from the controller's current preset, so tuned presets are respected.</summary>
        private static void ApplyLighting(VisualStyleController controller, Light sun, VisualStyleAssets assets, StringBuilder report)
        {
            LightingPreset preset = controller.CurrentLighting ?? LightingPreset.GoldenAfternoon();

            if (sun != null)
            {
                Undo.RecordObject(sun, DialogTitle);
                Undo.RecordObject(sun.transform, DialogTitle);
                sun.shadows = LightShadows.Soft;
            }

            VisualStyleController.ApplyLighting(preset, sun, assets.Sky);

            if (assets.Sky != null)
            {
                RenderSettings.skybox = assets.Sky;
                EditorUtility.SetDirty(assets.Sky);
            }

            if (sun != null)
            {
                RenderSettings.sun = sun;
                MarkChanged(sun);
                MarkChanged(sun.transform);
            }

            DynamicGI.UpdateEnvironment();
            report.AppendLine($"Set the lighting to '{preset.displayName}': sun, ambient light, haze from {preset.fogStart:0} m, and sky.");
        }

        /// <summary>Three small buttons in the top right corner, for switching quality, lines and lighting on a device.</summary>
        private static void EnsureTestPanel(Scene scene, VisualStyleController controller, StringBuilder report)
        {
            if (FindInScene<VisualStyleTestPanel>(scene) != null)
            {
                return;
            }

            var root = new GameObject(PanelName, typeof(Canvas));
            root.layer = UiLayer;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            root.AddComponent<DensityCanvasScaler>();

            RectTransform safeArea = TouchControlsSetup.CreateRect("SafeArea", root.transform);
            TouchControlsSetup.Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            RectTransform content = TouchControlsSetup.CreateRect("Content", safeArea);
            TouchControlsSetup.Stretch(content);

            TapButton quality = CreatePanelButton(content, "Quality", 0, out Text qualityLabel);
            TapButton outline = CreatePanelButton(content, "Lines", 1, out Text outlineLabel);
            TapButton lighting = CreatePanelButton(content, "Light", 2, out Text lightingLabel);

            VisualStyleTestPanel panel = root.AddComponent<VisualStyleTestPanel>();
            panel.Bind(controller, quality, outline, lighting, qualityLabel, outlineLabel, lightingLabel, content.gameObject);

            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            report.AppendLine("Added the style test panel (top right; Editor and development builds only). Keys: F6, F7, F8.");
        }

        private static TapButton CreatePanelButton(RectTransform parent, string name, int row, out Text label)
        {
            RectTransform rect = TouchControlsSetup.CreateRect(name, parent);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(168f, 30f);
            rect.anchoredPosition = new Vector2(-12f, -12f - row * 36f);

            Image background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.08f, 0.55f);
            background.raycastTarget = false;

            TapButton button = rect.gameObject.AddComponent<TapButton>();

            RectTransform textRect = TouchControlsSetup.CreateRect("Label", rect);
            TouchControlsSetup.Stretch(textRect);
            label = textRect.gameObject.AddComponent<Text>();
            label.font = UiFont.Default;
            label.text = name;
            label.fontSize = 13;
            label.color = new Color(1f, 0.97f, 0.9f, 0.95f);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return button;
        }

        private static void EnsureProps(Scene scene, VisualStyleAssets assets, PropPlacement placement, StringBuilder report)
        {
            if (placement == null || StyleProps.Find(scene) != null)
            {
                return;
            }

            EnvironmentAssets environment = EnvironmentAssets.Ensure(false, new StringBuilder());
            GameObject props = StyleProps.Build(environment, assets, placement.Position, placement.Yaw);
            Undo.RegisterCreatedObjectUndo(props, DialogTitle);
            report.AppendLine("Added props in wood, cloth and metal (crates, banners, a brazier), so all five material families can be judged.");
        }
    }
}
