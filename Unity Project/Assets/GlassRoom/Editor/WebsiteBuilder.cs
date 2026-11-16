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
            // Two builds: pictures packed for computers' graphics chips (DXT), and again for
            // phones' (ASTC). iPhones can't read DXT, so with it they unpack every picture to full
            // size in memory, and phones with less memory crash while loading. The game code is the
            // same in both, so the page just loads the pictures file its device can read.
            if (Directory.Exists(AstcFolder)) Directory.Delete(AstcFolder, true);
            BuildReport report, astc = null;
            // (Phones' first, kept aside: Unity removes a previous build's files when it builds again.)
            string keptPhone = null, keptCode = null;
            try
            {
                astc = BuildTo(AstcFolder, WebGLTextureSubtarget.ASTC);
                if (astc.summary.result == BuildResult.Succeeded)
                {
                    keptPhone = Path.Combine(Path.GetTempPath(), "glassroom-phone-" + Path.GetFileName(FindFile(AstcFolder, ".data.unityweb")));
                    File.Copy(FindFile(AstcFolder, ".data.unityweb"), keptPhone, true);
                    keptCode = Path.GetFileName(FindFile(AstcFolder, ".wasm.unityweb")) + "|" + Path.GetFileName(FindFile(AstcFolder, ".framework.js.unityweb"));
                }
                report = BuildTo(OutputFolder, WebGLTextureSubtarget.DXT);
            }
            finally
            {
                EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
                SetPictureFormat(TextureCompressionFormat.DXTC);
                CleanUpWebScene();
            }
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Website build failed: {summary.result}, {summary.totalErrors} errors");
                return;
            }
            if (keptPhone == null) Debug.LogError("Website build: the phone (ASTC) build failed; computers' pictures only.");
            else AddPhonePictures(keptPhone, keptCode);
            if (Directory.Exists(AstcFolder)) Directory.Delete(AstcFolder, true);
            Debug.Log($"Website build succeeded: {Path.GetFullPath(OutputFolder)} ({summary.totalSize / (1024 * 1024)} MB)");
        }

        const string AstcFolder = "WebBuild-ASTC";

        // Player Settings > Web > Texture Compression Format (what the build actually goes by).
        static void SetPictureFormat(TextureCompressionFormat format)
        {
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var targets = settings.FindProperty("m_BuildTargetDefaultTextureCompressionFormat");
            for (int i = 0; targets != null && i < targets.arraySize; i++)
            {
                var entry = targets.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("m_BuildTarget").stringValue != "WebGL") continue;
                var formats = entry.FindPropertyRelative("m_Formats");
                formats.arraySize = 1;
                formats.GetArrayElementAtIndex(0).intValue = (int)format;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static BuildReport BuildTo(string folder, WebGLTextureSubtarget pictures)
        {
            SetPictureFormat(pictures == WebGLTextureSubtarget.ASTC ? TextureCompressionFormat.ASTC : TextureCompressionFormat.DXTC);
            EditorUserBuildSettings.webGLBuildSubtarget = pictures;
            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { WebScene },
                locationPathName = folder,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
        }

        static string FindFile(string folder, string ending)
        {
            string dir = Path.Combine(folder, "Build");
            if (!Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir)) if (f.EndsWith(ending)) return f;
            return null;
        }

        // Puts the phone build's pictures file next to the computer build's, and has the page
        // pick between them (window.astcPictures, set in the page template).
        static void AddPhonePictures(string keptPhone, string phoneCode)
        {
            // Only if the game code really is the same in both.
            string code = Path.GetFileName(FindFile(OutputFolder, ".wasm.unityweb")) + "|" + Path.GetFileName(FindFile(OutputFolder, ".framework.js.unityweb"));
            if (code != phoneCode)
            {
                Debug.LogError("Website build: the two builds' code differs; computers' pictures only.");
                return;
            }
            string dxt = FindFile(OutputFolder, ".data.unityweb");
            if (dxt == null) return;
            string phoneName = Path.GetFileName(keptPhone).Replace("glassroom-phone-", "");
            File.Copy(keptPhone, Path.Combine(OutputFolder, "Build", phoneName), true);
            File.Delete(keptPhone);
            string page = Path.Combine(OutputFolder, "index.html");
            string html = File.ReadAllText(page);
            string line = $"dataUrl: \"Build/{Path.GetFileName(dxt)}\"";
            if (!html.Contains(line)) { Debug.LogError("Website build: couldn't find the pictures file in the page."); return; }
            html = html.Replace(line, $"dataUrl: window.astcPictures ? \"Build/{phoneName}\" : \"Build/{Path.GetFileName(dxt)}\"");
            File.WriteAllText(page, html);
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
            SetPictureFormat(TextureCompressionFormat.DXTC);
            AssetDatabase.SaveAssets();
        }
    }
}
