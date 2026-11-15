using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Infinigame.Editor
{
    [InitializeOnLoad]
    class ContentImport : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        static ContentImport()
        {
            EditorApplication.delayCall += Sync;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Sync(); };
        }
        public void OnPreprocessBuild(BuildReport report) => Sync();
        static void Sync()
        {
            foreach (string name in new[] { "STORY", "MISSIONS", "FACTIONS", "COLONIES" })
            {
                string source = File.ReadAllText(name + ".md"), target = $"Assets/Infinigame/Resources/{name}.txt";
                if (File.Exists(target) && File.ReadAllText(target) == source) continue;
                File.WriteAllText(target, source);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}
