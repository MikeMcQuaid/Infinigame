using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Infinigame.Editor
{
    public static class VoiceBuild
    {
        static void Run(string command, params string[] args)
        {
            var start = new ProcessStartInfo(command) { UseShellExecute = false };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start); process.WaitForExit();
            if (process.ExitCode != 0) throw new System.Exception(command + " failed while building voice input");
        }
        [PostProcessBuild(100)] public static void Finish(BuildTarget target, string path)
        {
            if (target == BuildTarget.StandaloneOSX)
            {
                string plugins = Path.Combine(path, "Contents/Plugins"); Directory.CreateDirectory(plugins);
                string library = Path.Combine(plugins, "libInfinigameVoice.dylib");
                Run("/usr/bin/xcrun", "clang++", "-dynamiclib", "-fobjc-arc", "-arch", "arm64", "-arch", "x86_64", "-mmacosx-version-min=10.15", "-framework", "Foundation", "-framework", "AVFoundation", "-framework", "Speech", "Assets/Plugins/iOS/InfinigameVoice.mm", "-o", library);
                foreach (string key in new[] { "NSMicrophoneUsageDescription", "NSSpeechRecognitionUsageDescription" })
                    Run("/usr/bin/defaults", "write", Path.GetFullPath(Path.Combine(path, "Contents/Info.plist")), key, "-string", "Speak to your robot companion using local dictation.");
                Run("/usr/bin/codesign", "--force", "--deep", "--sign", "-", path);
            }
#if UNITY_IOS
            if (target == BuildTarget.iOS)
            {
                string plistPath = Path.Combine(path, "Info.plist"); var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
                plist.root.SetString("NSMicrophoneUsageDescription", "Speak to your robot companion.");
                plist.root.SetString("NSSpeechRecognitionUsageDescription", "Turn your voice into text locally for your robot companion.");
                plist.WriteToFile(plistPath);
                string projectPath = PBXProject.GetPBXProjectPath(path); var project = new PBXProject(); project.ReadFromFile(projectPath);
                project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Speech.framework", false);
                project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AVFoundation.framework", false);
                project.WriteToFile(projectPath);
            }
#endif
        }
    }
}
