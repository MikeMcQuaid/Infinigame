using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Infinigame.Editor
{
    // Music dropped into Resources/Music is played from disk as it goes, not held whole in memory:
    // a track is minutes long.
    class MusicImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Music/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
        }
    }

    [InitializeOnLoad]
    static class EditorTools
    {
        const string Scene = "Assets/Scenes/Main.unity";

        // A project opened for the first time shows an empty untitled scene, where Play does nothing.
        // Open the game scene instead.
        static EditorTools() => EditorApplication.delayCall += () =>
        {
            var open = SceneManager.GetActiveScene();
            if (!Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode && string.IsNullOrEmpty(open.path) && !open.isDirty)
                EditorSceneManager.OpenScene(Scene);
        };

        // Entry point for script/build.
        static void BuildMac()
        {
            var report = BuildPipeline.BuildPlayer(new[] { Scene }, "Build/Infinigame.app", BuildTarget.StandaloneOSX, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
