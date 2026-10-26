using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-click setup for Meta Quest development: installs the Meta XR SDK and
/// applies the Android build settings Quest requires.
/// Menu: Tools > Meta XR
/// </summary>
public static class MetaXRSetup
{
    // Packages are requested by name only so Package Manager resolves the
    // latest version compatible with this Unity editor.
    static readonly string[] Packages =
    {
        "com.meta.xr.sdk.all",      // Core, Interaction, Audio, Platform, Haptics, Building Blocks
        "com.unity.xr.management",  // XR Plug-in Management
    };

    const string DefaultPackageName = "com.gp2.vrproject";

    [MenuItem("Tools/Meta XR/1. Install Meta XR SDK")]
    public static void InstallSdk()
    {
        var request = Client.AddAndRemove(Packages);
        EditorApplication.update += Wait;

        void Wait()
        {
            if (!request.IsCompleted) return;
            EditorApplication.update -= Wait;
            if (request.Status == StatusCode.Success)
                Debug.Log("[MetaXR] SDK installed. Next: Tools > Meta XR > 2. Configure Project for Quest.");
            else
                Debug.LogError($"[MetaXR] Install failed: {request.Error?.message}");
        }
    }

    [MenuItem("Tools/Meta XR/2. Configure Project for Quest")]
    public static void ConfigureProject()
    {
        var android = NamedBuildTarget.Android;

        PlayerSettings.colorSpace = ColorSpace.Linear;

        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new List<GraphicsDeviceType> { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 }.ToArray());

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

        if (PlayerSettings.GetApplicationIdentifier(android).StartsWith("com.UnityTechnologies"))
            PlayerSettings.SetApplicationIdentifier(android, DefaultPackageName);

        AssetDatabase.SaveAssets();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        Debug.Log("[MetaXR] Project configured for Quest. Next: Edit > Project Settings > XR Plug-in Management > " +
                  "Android tab > enable Oculus (or OpenXR + Meta Quest feature group), then run " +
                  "Edit > Project Settings > Meta XR > Fix All.");
    }
}
