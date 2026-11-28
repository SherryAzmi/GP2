using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// In the built app: starts the KneeTracker camera app from the KneeTracker folder beside the program, so nobody has
/// to remember it. Checked again at every scene change, in case it was closed. Does nothing if it is already running,
/// and closes on exit only the copy it started. Not used in the Unity Editor.
/// </summary>
public static class CameraAppLauncher
{
    const string AppName = "KneeTracker";
    static Process started;

    static string ExePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", AppName, AppName + ".exe"));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Application.isEditor || Application.platform != RuntimePlatform.WindowsPlayer) return;
        EnsureRunning();
        SceneManager.sceneLoaded += (scene, mode) => EnsureRunning();
        Application.quitting += Close;
    }

    static void EnsureRunning()
    {
        try
        {
            if (Process.GetProcessesByName(AppName).Length > 0) return;
            string exe = ExePath;
            if (!File.Exists(exe))
            {
                Debug.LogWarning("[CameraApp] Not found: " + exe);
                return;
            }
            started = Process.Start(new ProcessStartInfo(exe, "--app-mode knee")
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized, // its console; the camera window still opens
            });
            Debug.Log("[CameraApp] Started " + exe);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[CameraApp] Could not start: " + e.Message);
        }
    }

    static void Close()
    {
        try
        {
            if (started != null && !started.HasExited) started.Kill();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[CameraApp] Could not close: " + e.Message);
        }
    }
}
