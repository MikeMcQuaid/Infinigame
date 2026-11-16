using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if INFINIGAME_XR
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
#endif

namespace Infinigame.Editor
{
    [InitializeOnLoad]
    public static class Platforms
    {
        static Platforms()
        {
#if INFINIGAME_XR
            EditorApplication.delayCall += () =>
            {
                if (Application.platform == RuntimePlatform.WindowsEditor && !Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) return;
                var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
                if (settings != null) settings.InitManagerOnStart = false;
            };
#endif
        }
        public static void Windows()
        {
#if INFINIGAME_XR
            const string path = "Assets/XR/General.asset";
            System.IO.Directory.CreateDirectory("Assets/XR");
            AssetDatabase.Refresh();
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!XRPackageMetadataStore.AssignLoader(settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone), "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone))
                throw new InvalidOperationException("OpenXR loader could not be assigned.");
            // Unity exposes feature configuration publicly but creates the package asset internally.
            var type = Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor", true);
            type.GetMethod("GetOrCreateInstance").Invoke(null, null);
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            xr.GetFeature<ValveIndexControllerProfile>().enabled = true;
            xr.renderMode = OpenXRSettings.RenderMode.MultiPass;
            settings.SettingsForBuildTarget(BuildTargetGroup.Standalone).InitManagerOnStart = true;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(xr);
            AssetDatabase.SaveAssets();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            try { Build("Build/Windows/Infinigame.exe", BuildTarget.StandaloneWindows64); }
            finally { settings.SettingsForBuildTarget(BuildTargetGroup.Standalone).InitManagerOnStart = false; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); }
#else
            throw new InvalidOperationException("Resolve the official OpenXR packages before building Windows.");
#endif
        }
        public static void SteamDeck()
        {
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
#if INFINIGAME_XR
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            bool automatic = settings != null && settings.InitManagerOnStart;
            if (settings != null) settings.InitManagerOnStart = false;
            try { Build("Build/SteamDeck/Infinigame.exe", BuildTarget.StandaloneWindows64); }
            finally { if (settings != null) settings.InitManagerOnStart = automatic; }
#else
            Build("Build/SteamDeck/Infinigame.exe", BuildTarget.StandaloneWindows64);
#endif
        }
        public static void IOS()
        {
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.mikemcquaid.infinigame");
            string previousTeam = PlayerSettings.iOS.appleDeveloperTeamID;
            string team = Environment.GetEnvironmentVariable("INFINIGAME_APPLE_TEAM");
            if (!string.IsNullOrEmpty(team)) PlayerSettings.iOS.appleDeveloperTeamID = team;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            try { Build("Build/iOS", BuildTarget.iOS); }
            finally { PlayerSettings.iOS.appleDeveloperTeamID = previousTeam; }
        }
        static void Build(string path, BuildTarget target)
        {
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, path, target, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException($"{target} build failed: {report.summary.result}");
        }
    }
}
