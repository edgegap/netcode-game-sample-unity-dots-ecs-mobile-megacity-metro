// Command-line client builds for end-to-end tests. The server is built by the Edgegap plugin (EdgegapPluginBuild).
// Usage (project closed in the Editor):
//   <Unity> -batchmode -quit -projectPath <project> -executeMethod EdgegapClientBuild.Build -edgegapClientTarget win64|android|...
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class EdgegapClientBuild
{
    /// <summary>
    /// Client build for end-to-end tests: -executeMethod EdgegapClientBuild.Build -edgegapClientTarget webgl|win64|mac|linux|android
    /// Output: Builds/Client/&lt;target&gt;/. Switches the active build target (slow the first time).
    /// </summary>
    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-edgegapClientTarget");
        string name = i >= 0 && i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : "webgl";

        (BuildTarget target, string path) = name switch
        {
            "win64" => (BuildTarget.StandaloneWindows64, "Builds/Client/win64/Game.exe"),
            "mac" => (BuildTarget.StandaloneOSX, "Builds/Client/mac/Game.app"),
            "linux" => (BuildTarget.StandaloneLinux64, "Builds/Client/linux/Game.x86_64"),
            "android" => (BuildTarget.Android, "Builds/Client/android/MegacityMetro.apk"),
            _ => (BuildTarget.WebGL, "Builds/Client/webgl"),
        };

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Fail("No enabled scenes in Build Settings.");
            return;
        }

        bool standalone = target != BuildTarget.WebGL && target != BuildTarget.Android;

        // Android: an APK (not an App Bundle) that can be sideloaded for testing. ARM64 + IL2CPP come from
        // the project's Player settings; without a custom keystore Unity signs it with the debug key.
        if (target == BuildTarget.Android)
            EditorUserBuildSettings.buildAppBundle = false;

        // -edgegapAndroidX86_64: also include x86_64, so the APK runs natively in the Android Emulator
        // (and on ChromeOS). The project setting is restored after the build.
        var architectures = PlayerSettings.Android.targetArchitectures;
        bool x86_64 = target == BuildTarget.Android && Array.IndexOf(args, "-edgegapAndroidX86_64") >= 0;
        if (x86_64)
        {
            PlayerSettings.Android.targetArchitectures = architectures | AndroidArchitecture.X86_64;
            path = "Builds/Client/android/MegacityMetro-arm64-x86_64.apk";
        }

        if (standalone)
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

        // -edgegapClientMono: Mono instead of the project's IL2CPP for quick test clients (minutes instead of
        // ~40 min of IL2CPP Master linking). The project setting is restored after the build.
        var group = UnityEditor.Build.NamedBuildTarget.Standalone;
        var backend = PlayerSettings.GetScriptingBackend(group);
        bool mono = standalone && Array.IndexOf(args, "-edgegapClientMono") >= 0;
        if (mono)
        {
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.Mono2x);
            path = path.Replace($"Builds/Client/{name}/", $"Builds/Client/{name}-mono/"); // IL2CPP output can't be reused
        }

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                subtarget = standalone ? (int)StandaloneBuildSubtarget.Player : 0,
                locationPathName = path,
            });
        }
        finally
        {
            if (mono)
                PlayerSettings.SetScriptingBackend(group, backend);
            if (x86_64)
                PlayerSettings.Android.targetArchitectures = architectures;
        }

        if (report.summary.result != BuildResult.Succeeded)
        {
            Fail($"Client build {report.summary.result} with {report.summary.totalErrors} error(s).");
            return;
        }
        Debug.Log($"EdgegapClientBuild | Succeeded: {path}");
    }

    private static void Fail(string message)
    {
        Debug.LogError($"EdgegapClientBuild | {message}");
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
