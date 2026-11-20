using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Player builds from the menu (AiOn → Build) or the command line:
    /// <code>Unity.exe -batchmode -quit -projectPath . -executeMethod AiOn.EditorTools.BuildScript.BuildWindows [-buildOutput Builds/Windows]</code>
    /// The scenes are the enabled ones in Build Settings (Boot first). The look assets are rebuilt first so a fresh
    /// clone never ships without Resources/Look. In batch mode a failed build exits with code 1.
    /// </summary>
    public static class BuildScript
    {
        public const string DefaultOutput = "Builds/Windows";

        [MenuItem("AiOn/Build/Windows Player")]
        public static void BuildWindowsMenu() => Build(BuildTarget.StandaloneWindows64, DefaultOutput, false);

        [MenuItem("AiOn/Build/Windows Player (Development)")]
        public static void BuildWindowsDevMenu() => Build(BuildTarget.StandaloneWindows64, DefaultOutput + "-dev", true);

        /// <summary>Command-line entry point.</summary>
        public static void BuildWindows()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildOutput");
            string output = i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultOutput;
            bool dev = args.Contains("-development");
            var ok = Build(BuildTarget.StandaloneWindows64, output, dev);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>The enabled Build Settings scenes, in order.</summary>
        public static string[] Scenes() => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        static bool Build(BuildTarget target, string output, bool development)
        {
            var scenes = Scenes();
            if (scenes.Length == 0) { Debug.LogError("[Build] no enabled scenes in Build Settings"); return false; }
            GraphicsLookBuilder.BuildAll();

            Directory.CreateDirectory(output);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                locationPathName = Path.Combine(output, "AiOn.exe"),
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] {s.outputPath}: {s.totalSize / (1024f * 1024f):0.0} MB in {s.totalTime.TotalSeconds:0}s, {scenes.Length} scenes");
                return true;
            }
            Debug.LogError($"[Build] {s.result}: {s.totalErrors} errors (see the build report)");
            return false;
        }
    }
}
