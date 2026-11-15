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
    public static class Platforms
    {
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
            Build("Build/Windows/Infinigame.exe", BuildTarget.StandaloneWindows64);
#else
            throw new InvalidOperationException("Resolve the official OpenXR packages before building Windows.");
#endif
        }
        public static void IOS()
        {
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "games.infinigame.player");
            Build("Build/iOS", BuildTarget.iOS);
        }
        static void Build(string path, BuildTarget target)
        {
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, path, target, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
