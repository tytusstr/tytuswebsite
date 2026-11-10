using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GlassRoom.Editor
{
    /// <summary>
    /// Builds the game as a website (WebGL) for computer and phone browsers: no Unity logo or
    /// splash, and a page that shows only a loading bar (Assets/WebGLTemplates/Minimal).
    /// Files are compressed and still work on hosts like GitHub Pages that don't label them.
    /// </summary>
    public static class WebsiteBuilder
    {
        public const string OutputFolder = "WebBuild";
        const string Scene = "Assets/Scenes/GlassRoom.unity";

        [MenuItem("Tools/Glass Room/Build Website")]
        public static void Build()
        {
            ApplyWebSettings();

            if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = OutputFolder,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"Website build succeeded: {Path.GetFullPath(OutputFolder)} ({summary.totalSize / (1024 * 1024)} MB)");
            else
                Debug.LogError($"Website build failed: {summary.result}, {summary.totalErrors} errors");
        }

        static void ApplyWebSettings()
        {
            PlayerSettings.productName = "Tytuswebsite";
            // No "Made with Unity" splash and no Unity logo.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            // Page with nothing but a loading bar.
            PlayerSettings.WebGL.template = "PROJECT:Minimal";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.showDiagnostics = false;
            AssetDatabase.SaveAssets();
        }
    }
}
