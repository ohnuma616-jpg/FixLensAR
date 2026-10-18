using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// OpenXR loader は使わない。libRayNeoXRUnityInterfaces の SLAM だけ最小で試す。
/// 前回 enable 中に落ちていたら自動スキップする。
/// </summary>
public class RayNeoSlamProbe : MonoBehaviour
{
    public string LastStatus { get; private set; } = "未実行";
    public int TrackerCode { get; private set; } = -1;
    public bool Tracking { get; private set; }

    const string GuardName = "slam_probe_guard.txt";

    [DllImport("RayNeoXRUnityInterfaces")]
    static extern void RayNeoApi_EnableSlamHeadTracker();

    [DllImport("RayNeoXRUnityInterfaces")]
    static extern void RayNeoApi_DisableSlamHeadTracker();

    [DllImport("RayNeoXRUnityInterfaces")]
    static extern int RayNeoApi_GetHeadTrackerStatus();

    [DllImport("RayNeoXRUnityInterfaces")]
    static extern void RayNeoApi_NineAxisOrientation(float[] orientation);

    IEnumerator Start()
    {
#if UNITY_EDITOR
        LastStatus = "Editor";
        yield break;
#else
        LastStatus = "loader必須のため無効";
        yield break;
#endif
    }

    void RunProbe()
    {
        string guard = Path.Combine(Application.persistentDataPath, GuardName);
        if (File.Exists(guard))
        {
            string prev = File.ReadAllText(guard).Trim();
            if (prev == "armed")
            {
                LastStatus = "前回クラッシュ疑いのためスキップ";
                Debug.LogWarning("RayNeoSlamProbe: skip because previous launch died while enabling SLAM");
                File.WriteAllText(guard, "skipped");
                return;
            }
        }

        try
        {
            TrackerCode = RayNeoApi_GetHeadTrackerStatus();
            LastStatus = "status0=" + TrackerName(TrackerCode);
            Debug.Log("RayNeoSlamProbe before enable: " + LastStatus);
        }
        catch (DllNotFoundException)
        {
            LastStatus = "ネイティブライブラリなし";
            Debug.LogWarning("RayNeoSlamProbe: RayNeoXRUnityInterfaces not found");
            return;
        }
        catch (Exception exception)
        {
            LastStatus = "status失敗 " + exception.GetType().Name;
            Debug.LogWarning("RayNeoSlamProbe get status: " + exception);
            return;
        }

        try
        {
            File.WriteAllText(guard, "armed");
            RayNeoApi_EnableSlamHeadTracker();
            TrackerCode = RayNeoApi_GetHeadTrackerStatus();
            LastStatus = "enable後 " + TrackerName(TrackerCode);
            Debug.Log("RayNeoSlamProbe after enable: " + LastStatus);
            StartCoroutine(WatchTracking(guard));
        }
        catch (Exception exception)
        {
            LastStatus = "enable失敗 " + exception.GetType().Name;
            File.WriteAllText(guard, "failed");
            Debug.LogWarning("RayNeoSlamProbe enable: " + exception);
        }
    }

    IEnumerator WatchTracking(string guard)
    {
        float until = Time.unscaledTime + 4f;
        while (Time.unscaledTime < until)
        {
            try
            {
                TrackerCode = RayNeoApi_GetHeadTrackerStatus();
                Tracking = TrackerCode == 1;
                LastStatus = TrackerName(TrackerCode);
                if (Tracking)
                    break;
            }
            catch (Exception exception)
            {
                LastStatus = "watch失敗";
                Debug.LogWarning("RayNeoSlamProbe watch: " + exception);
                break;
            }
            yield return new WaitForSeconds(0.4f);
        }

        File.WriteAllText(guard, "ok");
        LogNineAxis();
        Debug.Log("RayNeoSlamProbe final: " + LastStatus + " tracking=" + Tracking);
    }

    void LogNineAxis()
    {
        try
        {
            float[] q = new float[4];
            RayNeoApi_NineAxisOrientation(q);
            Debug.Log("RayNeoSlamProbe 9axis " + q[0] + "," + q[1] + "," + q[2] + "," + q[3]);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("RayNeoSlamProbe 9axis: " + exception.Message);
        }
    }

    static string TrackerName(int code)
    {
        if (code == 0) return "初期化中";
        if (code == 1) return "TRACKING";
        if (code == 2) return "失敗";
        return "code=" + code;
    }

    void OnDestroy()
    {
#if !UNITY_EDITOR
        try
        {
            RayNeoApi_DisableSlamHeadTracker();
        }
        catch
        {
        }
#endif
    }
}
