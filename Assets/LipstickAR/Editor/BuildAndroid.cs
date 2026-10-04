using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LipstickAR.Editor
{
    public static class BuildAndroid
    {
        [MenuItem("LipstickAR/Build Android APK")]
        public static void Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Builds/RujAR.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"LipstickAR build: {report.summary.result}, {report.summary.totalSize} bytes, {report.summary.totalErrors} errors");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
