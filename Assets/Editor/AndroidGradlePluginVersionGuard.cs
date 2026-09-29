using System.Reflection;
using GooglePlayServices;
using UnityEditor;
using UnityEngine;

// External Dependency Manager 1.2.188 asks BuildPipeline.GetPlaybackEngineDirectory
// through its own reflection helper, which returns an empty path on this Unity
// version. The Jetifier check then compares a null Android Gradle Plugin version
// and the resolve job dies, which stalls the Android build.
[InitializeOnLoad]
static class AndroidGradlePluginVersionGuard
{
    static int attempts;

    static AndroidGradlePluginVersionGuard()
    {
        Apply();
        EditorApplication.update += ApplyUntilDetected;
    }

    static void ApplyUntilDetected()
    {
        Apply();
        attempts++;

        string version = PlayServicesResolver.AndroidGradlePluginVersion;
        if (!string.IsNullOrEmpty(version) || attempts > 30)
            EditorApplication.update -= ApplyUntilDetected;
    }

    static void Apply()
    {
        string playback = BuildPipeline.GetPlaybackEngineDirectory(
            BuildTarget.Android, BuildOptions.None);
        if (string.IsNullOrEmpty(playback)) return;

        FieldInfo engineField = typeof(PlayServicesResolver).GetField(
            "androidPlaybackEngineDirectory",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (engineField == null) return;

        string current = engineField.GetValue(null) as string;
        if (string.IsNullOrEmpty(current))
            engineField.SetValue(null, playback);
    }
}
