using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Infinigame.Editor
{
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
