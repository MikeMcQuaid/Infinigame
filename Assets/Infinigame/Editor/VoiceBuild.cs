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
            if (process.ExitCode != 0) throw new System.Exception(command + " failed while building voice support");
        }
        [PostProcessBuild(100)] public static void Finish(BuildTarget target, string path)
        {
            string speechTarget = target == BuildTarget.StandaloneOSX ? "macOS" : target == BuildTarget.StandaloneWindows64 ? "Windows" : target == BuildTarget.iOS ? "iOS" : null;
            if (speechTarget == null) return;
            Run("/usr/bin/env", "python3", "script/build-speech", speechTarget);
            string data = target == BuildTarget.StandaloneOSX ? Path.Combine(path, "Contents/Resources/Data/StreamingAssets/Festival")
                : target == BuildTarget.iOS ? Path.Combine(path, "Data/Raw/Festival") : Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_Data/StreamingAssets/Festival");
            Directory.CreateDirectory(data);
            foreach (string file in Directory.GetFiles("Build/Speech/Voice")) File.Copy(file, Path.Combine(data, Path.GetFileName(file)), true);
            if (target == BuildTarget.StandaloneOSX)
            {
                string plugins = Path.Combine(path, "Contents/Plugins"); Directory.CreateDirectory(plugins);
                File.Copy("Build/Speech/macOS/libInfinigameSpeech.dylib", Path.Combine(plugins, "libInfinigameSpeech.dylib"), true);
                string library = Path.Combine(plugins, "libInfinigameVoice.dylib");
                Run("/usr/bin/xcrun", "clang++", "-dynamiclib", "-fobjc-arc", "-arch", "arm64", "-arch", "x86_64", "-mmacosx-version-min=10.15", "-framework", "Foundation", "-framework", "AVFoundation", "-framework", "Speech", "Assets/Plugins/iOS/InfinigameVoice.mm", "-o", library);
                foreach (string key in new[] { "NSMicrophoneUsageDescription", "NSSpeechRecognitionUsageDescription" })
                    Run("/usr/bin/defaults", "write", Path.GetFullPath(Path.Combine(path, "Contents/Info.plist")), key, "-string", "Speak to your robot companion using local dictation.");
                Run("/bin/chmod", "644", Path.Combine(path, "Contents/Info.plist"));
                Run("/usr/bin/codesign", "--force", "--deep", "--sign", "-", path);
            }
            if (target == BuildTarget.StandaloneWindows64)
            {
                string plugins = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_Data/Plugins/x86_64");
                Directory.CreateDirectory(plugins);
                File.Copy("Build/Speech/Windows/InfinigameSpeech.dll", Path.Combine(plugins, "InfinigameSpeech.dll"), true);
            }
#if UNITY_IOS
            if (target == BuildTarget.iOS)
            {
                string plistPath = Path.Combine(path, "Info.plist"); var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
                plist.root.SetString("NSMicrophoneUsageDescription", "Speak to your robot companion.");
                plist.root.SetString("NSSpeechRecognitionUsageDescription", "Turn your voice into text locally for your robot companion.");
                plist.WriteToFile(plistPath);
                string projectPath = PBXProject.GetPBXProjectPath(path); var project = new PBXProject(); project.ReadFromFile(projectPath);
                project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "SafariServices.framework", false);
                project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Speech.framework", false);
                project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AVFoundation.framework", false);
                File.Copy("Build/Speech/iOS/libInfinigameSpeech.a", Path.Combine(path, "Libraries/libInfinigameSpeech.a"), true);
                project.AddFileToBuild(project.GetUnityFrameworkTargetGuid(), project.AddFile("Libraries/libInfinigameSpeech.a", "Libraries/libInfinigameSpeech.a"));
                project.WriteToFile(projectPath);
            }
#endif
        }
    }
}
