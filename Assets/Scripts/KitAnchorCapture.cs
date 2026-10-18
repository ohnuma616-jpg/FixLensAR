using System;
using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// OpenXR / ShareCamera は使わない。WebCamTexture でキットを2秒静止注視したら JPEG を残す。
/// 画面中央には出さない（透過UIを塞がない）。
/// </summary>
public class KitAnchorCapture : MonoBehaviour
{
    public const float StillSeconds = 2f;
    const float SampleInterval = 0.2f;
    const float StillLumaDelta = 12f;
    const int Grid = 24;

    public bool IsAnchored { get; private set; }
    public string SavedPath { get; private set; }
    public string Status { get; private set; }
    public Vector2 OverlayShift { get; private set; }
    public bool TrackOk { get; private set; }

    WebCamTexture webcam;
    Color32[] pixels;
    float[] previous;
    float[] locked;
    float stillElapsed;
    bool watching;
    bool tracking;
    float trackTimer;

    public IEnumerator WatchUntilAnchored()
    {
        IsAnchored = false;
        SavedPath = null;
        Status = "カメラ準備";

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            float wait = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && wait < 20f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Status = "カメラ権限なし";
            yield break;
        }
#endif

#if UNITY_EDITOR
        Status = "Editor: 原点スキップ可";
        yield break;
#else
        if (!OpenCamera())
        {
            Status = "カメラを開けません";
            yield break;
        }

        watching = true;
        Status = "キットを2秒見つめる";
        stillElapsed = 0f;
        while (watching && !IsAnchored)
            yield return SampleOnce();
#endif
    }

    public void Skip()
    {
        watching = false;
        tracking = false;
        TrackOk = false;
        IsAnchored = true;
        Status = "原点スキップ";
        CloseCamera();
    }

    public void CaptureNow()
    {
        if (webcam != null && webcam.isPlaying)
            TrySaveFrame();
        else
            Skip();
    }

    IEnumerator SampleOnce()
    {
        yield return new WaitForSeconds(SampleInterval);
        if (webcam == null || !webcam.didUpdateThisFrame || webcam.width < 8)
            yield break;

        int w = webcam.width;
        int h = webcam.height;
        if (pixels == null || pixels.Length != w * h)
            pixels = new Color32[w * h];
        webcam.GetPixels32(pixels);

        if (previous == null || previous.Length != Grid * Grid)
            previous = new float[Grid * Grid];

        float delta = 0f;
        float brightness = 0f;
        int cells = Grid * Grid;
        for (int gy = 0; gy < Grid; gy++)
        {
            for (int gx = 0; gx < Grid; gx++)
            {
                int px = gx * w / Grid;
                int py = gy * h / Grid;
                Color32 c = pixels[py * w + px];
                float luma = (c.r * 0.299f) + (c.g * 0.587f) + (c.b * 0.114f);
                int i = gy * Grid + gx;
                delta += Mathf.Abs(luma - previous[i]);
                previous[i] = luma;
                brightness += luma;
            }
        }

        delta /= cells;
        brightness /= cells;
        if (brightness < 8f)
        {
            stillElapsed = 0f;
            Status = "暗すぎます";
            yield break;
        }

        if (delta < StillLumaDelta)
            stillElapsed += SampleInterval;
        else
            stillElapsed = 0f;

        if (stillElapsed >= StillSeconds)
            TrySaveFrame();
        else
            Status = "静止 " + stillElapsed.ToString("0.0") + "/" + StillSeconds.ToString("0") + "秒";
    }

    bool OpenCamera()
    {
        CloseCamera();
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogWarning("WebCamTexture: カメラデバイスがありません");
            return false;
        }

        string name = devices[0].name;
        for (int i = 0; i < devices.Length; i++)
        {
            string n = devices[i].name ?? string.Empty;
            Debug.Log("WebCam: " + n + " front=" + devices[i].isFrontFacing);
            if (!devices[i].isFrontFacing)
                name = devices[i].name;
        }

        webcam = new WebCamTexture(name, 640, 480, 30);
        webcam.Play();
        Debug.Log("WebCamTexture 開始: " + name);
        return true;
    }

    void TrySaveFrame()
    {
        if (webcam == null || webcam.width < 8)
        {
            Skip();
            return;
        }

        try
        {
            Texture2D tex = new Texture2D(webcam.width, webcam.height, TextureFormat.RGB24, false);
            tex.SetPixels(webcam.GetPixels());
            tex.Apply();
            byte[] jpg = tex.EncodeToJPG(70);
            Destroy(tex);

            string dir = Path.Combine(Application.persistentDataPath, "Calibration");
            Directory.CreateDirectory(dir);
            SavedPath = Path.Combine(dir, "kit_anchor.jpg");
            File.WriteAllBytes(SavedPath, jpg);
            string gallery = CopyToGallery(SavedPath);
            IsAnchored = true;
            watching = false;
            Status = "原点を記録 " + (jpg.Length / 1024) + "KB";
            Debug.Log("キット原点JPEG: " + SavedPath + " (" + jpg.Length + " bytes) gallery=" + gallery);
            BeginTracking();
        }
        catch (Exception exception)
        {
            Status = "保存失敗";
            Debug.LogWarning("原点JPEGの保存に失敗: " + exception.Message);
            Skip();
        }
    }

    void BeginTracking()
    {
        if (previous == null)
            return;
        locked = new float[previous.Length];
        Array.Copy(previous, locked, previous.Length);
        tracking = true;
        OverlayShift = Vector2.zero;
        TrackOk = true;
    }

    void Update()
    {
        if (!tracking || webcam == null || !webcam.isPlaying)
            return;
        trackTimer += Time.unscaledDeltaTime;
        if (trackTimer < 0.05f)
            return;
        trackTimer = 0f;
        TrackShift();
    }

    void TrackShift()
    {
        if (webcam.width < 8 || locked == null)
            return;
        int w = webcam.width;
        int h = webcam.height;
        if (pixels == null || pixels.Length != w * h)
            pixels = new Color32[w * h];
        if (!webcam.didUpdateThisFrame)
            return;
        webcam.GetPixels32(pixels);

        const int search = 6;
        float best = float.MaxValue;
        int bestDx = 0;
        int bestDy = 0;
        int cells = Grid * Grid;

        for (int dy = -search; dy <= search; dy++)
        {
            for (int dx = -search; dx <= search; dx++)
            {
                float sad = 0f;
                int count = 0;
                for (int gy = 0; gy < Grid; gy++)
                {
                    int sy = gy + dy;
                    if (sy < 0 || sy >= Grid)
                        continue;
                    for (int gx = 0; gx < Grid; gx++)
                    {
                        int sx = gx + dx;
                        if (sx < 0 || sx >= Grid)
                            continue;
                        int px = sx * w / Grid;
                        int py = sy * h / Grid;
                        Color32 c = pixels[py * w + px];
                        float luma = (c.r * 0.299f) + (c.g * 0.587f) + (c.b * 0.114f);
                        sad += Mathf.Abs(luma - locked[gy * Grid + gx]);
                        count++;
                    }
                }
                if (count < cells / 2)
                    continue;
                sad /= count;
                if (sad < best)
                {
                    best = sad;
                    bestDx = dx;
                    bestDy = dy;
                }
            }
        }

        TrackOk = best < 40f;
        float cellW = w / (float)Grid;
        float cellH = h / (float)Grid;
        // Unity WebCam は下原点。実機では上下が逆だったので Y は反転しない。
        OverlayShift = new Vector2(bestDx * cellW, bestDy * cellH);
        if (!TrackOk)
            OverlayShift = Vector2.zero;
    }

    static string CopyToGallery(string srcPath)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var plugin = new AndroidJavaClass("com.workguidance.media.GallerySaver"))
            {
                return plugin.CallStatic<string>("saveJpegFile", srcPath, "kit_anchor.jpg");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("アルバム保存に失敗: " + exception.Message);
            return exception.Message;
        }
#else
        return "editor";
#endif
    }

    void CloseCamera()
    {
        if (webcam == null)
            return;
        if (webcam.isPlaying)
            webcam.Stop();
        Destroy(webcam);
        webcam = null;
    }

    void OnDestroy()
    {
        CloseCamera();
    }
}
