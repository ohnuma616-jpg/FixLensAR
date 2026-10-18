using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// RayNeo X3 の 1280x480 は左右 640px ずつ。同じ3段UIを左右に複製して両眼で重なるようにする。
/// TapInsight の BinocularSbsLayout と同じく「論理画面は片眼幅、同じ内容を左右に出す」。
/// OpenXR は使わない。二重適用しない。
/// </summary>
[DefaultExecutionOrder(-50)]
public class BinocularSbsUi : MonoBehaviour
{
    static readonly string[] BandNames = { "TopText", "CenterText", "BottomText" };

    TMP_Text[] sourceTexts;
    TMP_Text[] cloneTexts;
    bool applied;

    void Awake()
    {
        ApplyLayout();
    }

    public void ApplyLayout()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
            return;

        RectTransform root = canvas.transform as RectTransform;
        FlattenNestedPanes(root);

        Transform existingLeft = root.Find("LeftEye");
        Transform existingRight = root.Find("RightEye");
        if (existingLeft != null && existingRight != null)
        {
            BindEyes(existingLeft, existingRight);
            applied = true;
            return;
        }

        var left = CreateEyePane(root, "LeftEye", 0f, 0.5f);
        var right = CreateEyePane(root, "RightEye", 0.5f, 1f);

        var moving = new List<Transform>();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != left && child != right)
                moving.Add(child);
        }

        for (int i = 0; i < moving.Count; i++)
            moving[i].SetParent(left, false);

        ApplyThreeBandLayout(left);
        RectTransform clone = Instantiate(left, right);
        clone.name = "RightEyeContent";
        StretchFull(clone);
        BindEyes(left, right);
        applied = true;
    }

    void BindEyes(Transform left, Transform right)
    {
        sourceTexts = CollectBandTexts(left);
        cloneTexts = CollectBandTexts(right);
        if (cloneTexts.Length == 0)
        {
            Transform content = right.Find("RightEyeContent");
            if (content != null)
                cloneTexts = CollectBandTexts(content);
        }
    }

    static TMP_Text[] CollectBandTexts(Transform root)
    {
        var list = new List<TMP_Text>();
        for (int i = 0; i < BandNames.Length; i++)
        {
            TMP_Text text = FindBand(root, BandNames[i]);
            if (text != null)
                list.Add(text);
        }

        return list.ToArray();
    }

    static TMP_Text FindBand(Transform root, string name)
    {
        Transform direct = root.Find(name);
        if (direct != null)
            return direct.GetComponent<TMP_Text>();

        TMP_Text[] all = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name)
                return all[i];
        }

        return null;
    }

    static void FlattenNestedPanes(RectTransform root)
    {
        Transform left = root.Find("LeftEye");
        if (left == null)
            return;

        bool nested = left.Find("LeftEye") != null || left.Find("RightEye") != null;
        if (!nested)
            return;

        var keep = new List<Transform>();
        for (int i = 0; i < BandNames.Length; i++)
        {
            TMP_Text text = FindBand(root, BandNames[i]);
            if (text != null && !keep.Contains(text.transform))
                keep.Add(text.transform);
        }

        for (int i = 0; i < keep.Count; i++)
            keep[i].SetParent(root, false);

        DestroyImmediate(left.gameObject);
        Transform right = root.Find("RightEye");
        if (right != null)
            DestroyImmediate(right.gameObject);
    }

    void LateUpdate()
    {
        if (sourceTexts == null || cloneTexts == null)
            return;

        int n = sourceTexts.Length < cloneTexts.Length ? sourceTexts.Length : cloneTexts.Length;
        for (int i = 0; i < n; i++)
        {
            if (sourceTexts[i] != null && cloneTexts[i] != null)
                cloneTexts[i].text = sourceTexts[i].text;
        }
    }

    static RectTransform CreateEyePane(RectTransform parent, string name, float xMin, float xMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(xMin, 0f);
        rt.anchorMax = new Vector2(xMax, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        return rt;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    static void ApplyThreeBandLayout(Transform eye)
    {
        PlaceBand(eye, "TopText", 0.90f, 1f, 28f);
        PlaceBand(eye, "CenterText", 0.10f, 0.90f, 34f);
        PlaceBand(eye, "BottomText", 0f, 0.10f, 26f);
    }

    static void PlaceBand(Transform eye, string name, float yMin, float yMax, float fontSize)
    {
        Transform child = eye.Find(name);
        if (child == null)
            return;

        RectTransform rt = child as RectTransform;
        rt.anchorMin = new Vector2(0.04f, yMin);
        rt.anchorMax = new Vector2(0.96f, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        TMP_Text text = child.GetComponent<TMP_Text>();
        if (text == null)
            return;

        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = 16f;
        text.fontSizeMax = fontSize;
        text.alignment = TextAlignmentOptions.Center;
    }
}
