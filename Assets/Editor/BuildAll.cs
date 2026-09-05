using UnityEditor;
using UnityEngine;

// One-command player builds — from the Build menu in the editor, or headless:
//   "$UNITY" -batchmode -quit -projectPath . -executeMethod BuildAll.Windows -logFile build.log
// Outputs land in builds/ (gitignored). Each target needs its Unity build
// support module installed; Android also needs the SDK/NDK/JDK that ship with
// Unity Hub's Android module. See README → Deployment.
public static class BuildAll
{
    static readonly string[] Scenes = { "Assets/Scenes/Main.unity" };

    [MenuItem("Build/Windows x64")]
    public static void Windows() =>
        Build(BuildTarget.StandaloneWindows64, "builds/windows/StarshipCraft.exe");

    [MenuItem("Build/macOS")]
    public static void Mac() =>
        Build(BuildTarget.StandaloneOSX, "builds/macos/StarshipCraft.app");

    [MenuItem("Build/Linux x64 (Steam Deck)")]
    public static void Linux() =>
        Build(BuildTarget.StandaloneLinux64, "builds/linux/StarshipCraft.x86_64");

    [MenuItem("Build/Android APK")]
    public static void Android() =>
        Build(BuildTarget.Android, "builds/android/StarshipCraft.apk");

    [MenuItem("Build/Android AAB (Play Store)")]
    public static void AndroidBundle()
    {
        EditorUserBuildSettings.buildAppBundle = true;
        try { Build(BuildTarget.Android, "builds/android/StarshipCraft.aab"); }
        finally { EditorUserBuildSettings.buildAppBundle = false; }
    }

    [MenuItem("Build/iOS Xcode Project")]
    public static void IOS() =>
        Build(BuildTarget.iOS, "builds/ios");

    [MenuItem("Build/WebGL")]
    public static void WebGL() =>
        Build(BuildTarget.WebGL, "builds/webgl");

    static void Build(BuildTarget target, string path)
    {
        var report = BuildPipeline.BuildPlayer(Scenes, path, target, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Build failed for " + target + ": " + report.summary.result);
        Debug.Log("Built " + target + " -> " + path);
    }

#if UNITY_IOS
    // iOS 14+ requires a usage description before an app may talk to other
    // devices on the local network — which LAN co-op does. Injected here so
    // the exported Xcode project ships ready.
    [UnityEditor.Callbacks.PostProcessBuild]
    public static void PatchPlist(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;
        string plistPath = buildPath + "/Info.plist";
        var plist = new UnityEditor.iOS.Xcode.PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSLocalNetworkUsageDescription",
            "Starship Craft uses the local network for LAN co-op with nearby players.");
        plist.WriteToFile(plistPath);
    }
#endif
}
