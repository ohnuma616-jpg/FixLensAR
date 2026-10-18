using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Overlay は頭に張り付く。角速度を積分して逆方向へ動かし、机側に残す仮止め。
/// OpenXR は使わない。片眼あたり約640px / FOV約26° → 24px/deg。
/// </summary>
public class KitWorldHint : MonoBehaviour
{
    const float PixelsPerDegree = 24f;
    static Sprite whiteSprite;

    RectTransform[] frames;
    TMP_Text[] captions;
    Vector3 integratedRad;
    bool locked;
    bool gyroOn;
    bool inView = true;
    float logTimer;
    KitAnchorCapture capture;

    public void LockToCurrentPose()
    {
        EnsureFrames();
        EnableGyro();
        integratedRad = Vector3.zero;
        locked = true;
        if (capture == null)
            capture = FindObjectOfType<KitAnchorCapture>();
        inView = true;
        SetVisible(true);
        Debug.Log("KitWorldHint lock gyro=" + Input.gyro.enabled);
    }

    public void SetLabel(string text)
    {
        if (captions == null)
            return;
        string label = string.IsNullOrEmpty(text) ? "作業位置" : text;
        for (int i = 0; i < captions.Length; i++)
        {
            if (captions[i] != null)
                captions[i].text = label;
        }
    }

    public void Hide()
    {
        locked = false;
        SetVisible(false);
    }

    void LateUpdate()
    {
        if (!locked || frames == null)
            return;

        Vector3 rate = ReadRate();
        integratedRad += rate * Time.unscaledDeltaTime;

        // 実機: 右を向くと X 角速度、下を向くと Y/Z。画面は右=+X・上=+Y。
        // 机に残すには、右を向いたら枠を左へ、下を向いたら枠を上へ。
        // カメラSADは数十ms遅れ＋Y反転するので、表示はジャイロを主、画像は弱いドリフト補正だけ。
        float rightDeg = integratedRad.x * Mathf.Rad2Deg;
        float downDeg = (integratedRad.y + integratedRad.z) * Mathf.Rad2Deg;
        Vector2 pos = new Vector2(-rightDeg * PixelsPerDegree, -downDeg * PixelsPerDegree);
        if (capture != null && capture.TrackOk)
        {
            Vector2 cam = capture.OverlayShift;
            float pull = 1f - Mathf.Exp(-0.7f * Time.unscaledDeltaTime);
            pos = Vector2.Lerp(pos, cam, pull);
        }

        // 端にクランプすると枠の一部が画面に張り付く。視線がキット方向から外れたら消す。
        float limitX = inView ? 300f : 240f;
        float limitY = inView ? 200f : 150f;
        bool nowInView = Mathf.Abs(pos.x) <= limitX && Mathf.Abs(pos.y) <= limitY;
        if (nowInView != inView)
        {
            inView = nowInView;
            SetVisible(inView);
        }
        if (!inView)
            return;

        pos.x = Mathf.Clamp(pos.x, -240f, 240f);
        pos.y = Mathf.Clamp(pos.y, -150f, 150f);

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                frames[i].anchoredPosition = pos;
        }

        logTimer += Time.unscaledDeltaTime;
        if (logTimer >= 1f)
        {
            logTimer = 0f;
            Debug.Log("KitWorldHint pos=" + pos + " track=" + (capture != null && capture.TrackOk));
        }
    }

    static Vector3 ReadRate()
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Gyroscope.current != null)
        {
            Vector3 v = UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue();
            if (v.sqrMagnitude > 0.000001f)
                return v;
        }
#endif
        return Input.gyro.rotationRateUnbiased;
    }

    void EnsureFrames()
    {
        if (frames != null)
            return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
            return;

        Transform root = canvas.transform;
        Transform left = root.Find("LeftEye");
        Transform right = root.Find("RightEye");
        if (left != null && right != null)
            frames = new[] { CreateFrame(left), CreateFrame(right) };
        else
            frames = new[] { CreateFrame(root) };

        captions = new TMP_Text[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                captions[i] = frames[i].GetComponentInChildren<TMP_Text>(true);
        }
    }

    static RectTransform CreateFrame(Transform parent)
    {
        var root = new GameObject("KitHintFrame", typeof(RectTransform));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.38f);
        rt.anchorMax = new Vector2(0.5f, 0.38f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 170f);
        rt.anchoredPosition = Vector2.zero;

        AddBar(rt, "T", new Vector2(0f, 84f), new Vector2(280f, 3f));
        AddBar(rt, "B", new Vector2(0f, -84f), new Vector2(280f, 3f));
        AddBar(rt, "L", new Vector2(-139f, 0f), new Vector2(3f, 170f));
        AddBar(rt, "R", new Vector2(139f, 0f), new Vector2(3f, 170f));
        AddCaption(rt);
        return rt;
    }

    static void AddCaption(RectTransform parent)
    {
        var go = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(260f, 40f);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = "作業位置";
        tmp.fontSize = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0f, 1f, 0.4f, 1f);
        tmp.raycastTarget = false;
    }

    static void AddBar(RectTransform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.sprite = WhiteSprite();
        image.color = new Color(0f, 1f, 0.4f, 1f);
        image.raycastTarget = false;
    }

    static Sprite WhiteSprite()
    {
        if (whiteSprite != null)
            return whiteSprite;
        Texture2D tex = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 4f);
        return whiteSprite;
    }

    void SetVisible(bool visible)
    {
        if (frames == null)
            return;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                frames[i].gameObject.SetActive(visible);
        }
    }

    void EnableGyro()
    {
        Input.gyro.enabled = true;
        Input.gyro.updateInterval = 0.008f;
        Input.compensateSensors = true;
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Gyroscope.current != null)
            InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
        if (AttitudeSensor.current != null)
            InputSystem.EnableDevice(AttitudeSensor.current);
#endif
        gyroOn = true;
    }

    void OnDestroy()
    {
        if (gyroOn)
            Input.gyro.enabled = false;
    }
}
