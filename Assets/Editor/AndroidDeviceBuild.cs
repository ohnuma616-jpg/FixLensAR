using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class AndroidDeviceBuild
{
    const string TriggerPath = "Temp/request-android-device-build";
    const string StatusPath = "Temp/android-device-build-status.txt";
    const string ApkPath = "Builds/WorkGuidance.apk";

    static bool pending;
    static double readyAt;

    static AndroidDeviceBuild()
    {
        EditorApplication.update += PollTrigger;
    }

    static void PollTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        if (pending)
        {
            if (EditorApplication.timeSinceStartup < readyAt)
                return;
            pending = false;
            if (EditorUtility.scriptCompilationFailed)
            {
                File.WriteAllText(StatusPath, "failed: compile errors");
                Debug.LogError("実機ビルド中止: スクリプトコンパイルエラーがあります。");
                return;
            }
            EditorApplication.delayCall += BuildApk;
            return;
        }

        if (!File.Exists(TriggerPath))
            return;

        try
        {
            File.Delete(TriggerPath);
        }
        catch
        {
            return;
        }

        pending = true;
        readyAt = EditorApplication.timeSinceStartup + 4.0;
        File.WriteAllText(StatusPath, "queued");
    }

    [MenuItem("Work Guidance/Build APK")]
    public static void BuildApk()
    {
        if (EditorApplication.isPlaying)
        {
            File.WriteAllText(StatusPath, "failed: exit Play Mode first");
            Debug.LogError("実機ビルドの前に再生モードを停止してください。");
            return;
        }

        Directory.CreateDirectory("Builds");
        if (File.Exists(ApkPath))
            File.Delete(ApkPath);
        File.WriteAllText(StatusPath, "building Gude.unity");

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Gude.unity" },
            locationPathName = ApkPath,
            targetGroup = BuildTargetGroup.Android,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.CleanBuildCache
        };

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(options);
        }
        catch (Exception exception)
        {
            File.WriteAllText(StatusPath, "failed: " + exception.Message);
            Debug.LogError("Android ビルド例外: " + exception);
            return;
        }

        if (report == null || report.summary.result != BuildResult.Succeeded)
        {
            string reason = report == null ? "null report" : report.summary.result.ToString();
            if (EditorUtility.scriptCompilationFailed)
                reason += " compile errors";
            File.WriteAllText(StatusPath, "failed: " + reason);
            Debug.LogError("Android ビルド失敗: " + reason);
            return;
        }

        string fullPath = Path.GetFullPath(ApkPath);
        File.WriteAllText(StatusPath, "succeeded:" + fullPath);
        Debug.Log("Android APK を出力しました: " + fullPath);
    }
}
