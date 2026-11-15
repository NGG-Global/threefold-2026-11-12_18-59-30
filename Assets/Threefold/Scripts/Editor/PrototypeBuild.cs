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
        private const string StampFolder = "Assets/Threefold/Generated/Resources";
        private const string StampPath = StampFolder + "/" + Threefold.DevTools.BuildStamp.ResourceName + ".txt";

        private static readonly object LogLock = new object();
        private static StreamWriter logWriter;

        /// <summary>
        /// Assembles the Broken Foundry, the Combat Arena and the Fantasy Playground, validates them and
        /// builds a development APK that starts in the playground; its south gate leads to the arena.
        /// The foundry has no portal yet: in a development build the diagnostics (three-finger tap)
        /// offer "Next scene".
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
                if (!AssembleAndValidate(BrokenFoundrySetup.BuildForAutomation) ||
                    !AssembleAndValidate(CombatArenaSetup.BuildForAutomation) ||
                    !AssembleAndValidate(FantasyPlaygroundSetup.BuildForAutomation))
                {
                    Exit(1);
                    return;
                }

                ApplyPlayerSettings();
                WriteBuildStamp();
                EditorUserBuildSettings.buildAppBundle = false;

                string outputPath = GetArgument("-customBuildPath") ?? DefaultOutputPath;
                if (!outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += ".apk";
                }

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { FantasyPlaygroundSetup.ScenePath, CombatArenaSetup.ScenePath, BrokenFoundrySetup.ScenePath },
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
        /// <summary>
        /// Writes which build this is into the game, for the development diagnostics: the commit and the run
        /// number that GitHub Actions gives the build (GITHUB_SHA, GITHUB_RUN_NUMBER), or, for a build started
        /// by hand, the commit read from the Git folder and no run number. A build never fails over this: if
        /// the file cannot be written, a file left by an earlier build is removed, so the game says "not
        /// stamped" instead of naming the wrong build.
        /// </summary>
        private static void WriteBuildStamp()
        {
            try
            {
                string commit = Environment.GetEnvironmentVariable("GITHUB_SHA");
                if (string.IsNullOrWhiteSpace(commit))
                {
                    commit = Threefold.DevTools.BuildStamp.CommitFromGitFiles(ReadGitFile);
                }

                Threefold.DevTools.BuildStamp stamp = Threefold.DevTools.BuildStamp.Create(
                    commit,
                    Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"),
                    Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
                    Environment.GetEnvironmentVariable("GITHUB_REF_NAME"),
                    DateTime.UtcNow);

                Directory.CreateDirectory(StampFolder);
                File.WriteAllText(StampPath, stamp.Serialize());
                AssetDatabase.ImportAsset(StampPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                Debug.Log($"[Threefold] Build stamp: {stamp.Describe()}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Threefold] The build stamp could not be written ({exception.Message}); the diagnostics will say 'not stamped'.");
                try
                {
                    AssetDatabase.DeleteAsset(StampPath);
                }
                catch (Exception)
                {
                    // Nothing more can be done; the build goes on.
                }
            }
        }

        // A file of the project's Git folder, or null. The project folder is Unity's working directory.
        private static string ReadGitFile(string relativePath)
        {
            try
            {
                string path = Path.Combine(".git", relativePath);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

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
