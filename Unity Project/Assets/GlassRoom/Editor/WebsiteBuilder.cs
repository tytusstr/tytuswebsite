using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
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
        // The website is built from a copy of the scene whose speaker streams songs from files
        // (StreamingAssets/Music) instead of carrying them inside the game data.
        const string WebScene = "Assets/Scenes/GlassRoom_Web.unity";
        const string WebMusicFolder = "Assets/StreamingAssets/Music";

        [MenuItem("Tools/Glass Room/Build Website")]
        public static void Build()
        {
            ApplyWebSettings();

            if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            if (!PrepareWebScene()) return;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { WebScene },
                locationPathName = OutputFolder,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report;
            var occlusion = SetAmbientOcclusion(false);
            try { report = BuildPipeline.BuildPlayer(options); }
            finally
            {
                foreach (var feature in occlusion) feature.SetActive(true);
                CleanUpWebScene();
            }
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"Website build succeeded: {Path.GetFullPath(OutputFolder)} ({summary.totalSize / (1024 * 1024)} MB)");
            else
                Debug.LogError($"Website build failed: {summary.result}, {summary.totalErrors} errors");
        }

        // Copies the songs to StreamingAssets and saves a copy of the scene whose speaker refers
        // to those files by name. The real scene is saved first and left unchanged.
        static bool PrepareWebScene()
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var speaker = Object.FindAnyObjectByType<MusicSpeaker>();
            if (speaker != null)
            {
                Directory.CreateDirectory(WebMusicFolder);
                var names = new System.Collections.Generic.List<string>();
                void Add(AudioClip clip)
                {
                    if (clip == null) return;
                    string path = AssetDatabase.GetAssetPath(clip);
                    string name = Path.GetFileName(path);
                    File.Copy(path, Path.Combine(WebMusicFolder, name), true);
                    names.Add(name);
                }
                Add(speaker.firstTrack);
                if (speaker.playlist != null) foreach (var clip in speaker.playlist) Add(clip);
                speaker.webTracks = names.ToArray();
                speaker.firstTrack = null;
                speaker.playlist = new AudioClip[0];
            }
            bool saved = EditorSceneManager.SaveScene(scene, WebScene, true);
            // Reload the real scene from disk, dropping the website-only changes.
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            AssetDatabase.Refresh();
            if (!saved) Debug.LogError("Website build: could not save the website copy of the scene.");
            return saved;
        }

        // Ambient occlusion (soft shading in corners) draws the top half of the screen slightly
        // darker in Safari, split by a line across the middle, so the website is built without
        // it. Returns the effects it turned off so they can be turned back on for the editor.
        static System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> SetAmbientOcclusion(bool on)
        {
            var changed = new System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                    if (feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion" && feature.isActive != on)
                    {
                        feature.SetActive(on);
                        changed.Add(feature);
                    }
            }
            return changed;
        }

        static void CleanUpWebScene()
        {
            AssetDatabase.DeleteAsset(WebScene);
            AssetDatabase.DeleteAsset(WebMusicFolder);
            if (Directory.Exists("Assets/StreamingAssets") && Directory.GetFileSystemEntries("Assets/StreamingAssets").Length == 0)
                AssetDatabase.DeleteAsset("Assets/StreamingAssets");
            AssetDatabase.Refresh();
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
