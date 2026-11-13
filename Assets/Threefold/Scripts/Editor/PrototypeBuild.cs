using System;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Entry points for automated builds, called by Unity's command line with -executeMethod.
    ///
    /// The build assembles the playable scene from code before building it. The generated scene
    /// and assets therefore do not have to be committed for a build to work, which is what lets a
    /// build be produced without the development laptop.
    /// </summary>
    public static class PrototypeBuild
    {
        private const string ApplicationIdentifier = "com.nggglobal.threefold";
        private const string DefaultOutputPath = "build/Android/Threefold.apk";
        private const string ProductName = "Threefold";
        private const string CompanyName = "NGG Global";

        /// <summary>
        /// Assembles the Fantasy Playground scene, validates it and builds a development APK.
        /// The output path is taken from the -customBuildPath argument when present. The Editor
        /// exits with code 0 on success and 1 on any failure.
        /// </summary>
        public static void BuildAndroid()
        {
            try
            {
                Scene scene = FantasyPlaygroundSetup.BuildForAutomation();

                var problems = new StringBuilder();
                int problemCount = PrototypeValidator.Validate(scene, problems);
                if (problemCount > 0)
                {
                    Debug.LogError($"[Threefold] The assembled scene has {problemCount} problem(s); the build was stopped.\n{problems}");
                    EditorApplication.Exit(1);
                    return;
                }

                ApplyPlayerSettings();
                EditorUserBuildSettings.buildAppBundle = false;

                string outputPath = GetArgument("-customBuildPath") ?? DefaultOutputPath;
                if (!outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += ".apk";
                }

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { FantasyPlaygroundSetup.ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[Threefold] Android build succeeded: {outputPath} ({summary.totalSize} bytes).");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError($"[Threefold] Android build finished with result {summary.result} and {summary.totalErrors} error(s).");
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Settings the first build depends on. They are applied here, not only stored in the
        /// project settings file, because the repository still carries the project template's
        /// values until the laptop's settings are committed.
        /// </summary>
        private static void ApplyPlayerSettings()
        {
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != ApplicationIdentifier)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationIdentifier);
            }

            if (PlayerSettings.companyName == "DefaultCompany")
            {
                PlayerSettings.companyName = CompanyName;
            }

            PlayerSettings.productName = ProductName;

            // The touch controls are laid out for landscape only.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == name)
                {
                    return arguments[i + 1];
                }
            }

            return null;
        }
    }
}
