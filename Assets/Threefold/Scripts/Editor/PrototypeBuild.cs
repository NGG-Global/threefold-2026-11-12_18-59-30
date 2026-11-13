using System;
using System.IO;
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
        private const string LogDirectory = "build/logs";
        private const string ProductName = "Threefold";
        private const string CompanyName = "NGG Global";

        private static readonly object LogLock = new object();
        private static StreamWriter logWriter;

        /// <summary>
        /// Assembles the Fantasy Playground and the Combat Arena, validates both and builds a
        /// development APK that starts in the playground; its south gate leads to the arena.
        /// The output path is taken from the -customBuildPath argument when present. The Editor
        /// exits with code 0 on success and 1 on any failure.
        /// </summary>
        public static void BuildAndroid()
        {
            StartLog();

            try
            {
                // The arena is assembled first: the playground is then the scene left open, and its
                // portal to the arena already finds the arena in Build Settings.
                if (!AssembleAndValidate(CombatArenaSetup.BuildForAutomation) ||
                    !AssembleAndValidate(FantasyPlaygroundSetup.BuildForAutomation))
                {
                    Exit(1);
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
                    scenes = new[] { FantasyPlaygroundSetup.ScenePath, CombatArenaSetup.ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                WriteReport(report);

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[Threefold] Android build succeeded: {outputPath} ({summary.totalSize} bytes).");
                    Exit(0);
                }
                else
                {
                    Debug.LogError($"[Threefold] Android build finished with result {summary.result} and {summary.totalErrors} error(s).");
                    Exit(1);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Exit(1);
            }
        }

        private static bool AssembleAndValidate(Func<Scene> assemble)
        {
            Scene scene = assemble();

            var problems = new StringBuilder();
            int problemCount = PrototypeValidator.Validate(scene, problems);
            if (problemCount > 0)
            {
                Debug.LogError($"[Threefold] The assembled scene '{scene.path}' has {problemCount} problem(s); the build was stopped.\n{problems}");
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ logs

        // The build server shows Unity's output only in its console. These two files are written
        // next to the build so they can be collected and downloaded when a build fails.

        private static void StartLog()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                logWriter = new StreamWriter(Path.Combine(LogDirectory, "threefold-build.log"), false) { AutoFlush = true };
                logWriter.WriteLine($"Threefold build log. Unity {Application.unityVersion}. Started {DateTime.UtcNow:u}.");
                Application.logMessageReceivedThreaded += HandleLogMessage;
            }
            catch (Exception exception)
            {
                // A build must not fail because its log file could not be opened.
                logWriter = null;
                Debug.LogWarning($"[Threefold] The build log file could not be created: {exception.Message}");
            }
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            StreamWriter writer = logWriter;
            if (writer == null)
            {
                return;
            }

            lock (LogLock)
            {
                writer.WriteLine($"[{type}] {condition}");

                bool isProblem = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                if (isProblem && !string.IsNullOrEmpty(stackTrace))
                {
                    writer.WriteLine(stackTrace);
                }
            }
        }

        /// <summary>Writes the outcome of the player build: result, size, time, each step and every error or warning.</summary>
        private static void WriteReport(BuildReport report)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                BuildSummary summary = report.summary;
                var text = new StringBuilder();

                text.AppendLine($"Result:    {summary.result}");
                text.AppendLine($"Output:    {summary.outputPath}");
                text.AppendLine($"Size:      {summary.totalSize} bytes");
                text.AppendLine($"Duration:  {summary.totalTime}");
                text.AppendLine($"Errors:    {summary.totalErrors}");
                text.AppendLine($"Warnings:  {summary.totalWarnings}");
                text.AppendLine();

                foreach (BuildStep step in report.steps)
                {
                    text.AppendLine($"{step.duration}  {step.name}");

                    foreach (BuildStepMessage message in step.messages)
                    {
                        if (message.type != LogType.Log)
                        {
                            text.AppendLine($"    [{message.type}] {message.content}");
                        }
                    }
                }

                File.WriteAllText(Path.Combine(LogDirectory, "build-report.txt"), text.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Threefold] The build report could not be written: {exception.Message}");
            }
        }

        private static void Exit(int code)
        {
            Application.logMessageReceivedThreaded -= HandleLogMessage;

            lock (LogLock)
            {
                if (logWriter != null)
                {
                    logWriter.WriteLine($"Finished {DateTime.UtcNow:u} with exit code {code}.");
                    logWriter.Dispose();
                    logWriter = null;
                }
            }

            EditorApplication.Exit(code);
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
