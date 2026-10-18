using System;
using System.IO;
using UnityEngine;

/// <summary>
/// OCR または手入力の出力先となる手順ドキュメント。
/// Unity 側はこの JSON を読み、表示と進行だけを担当する。
/// </summary>
[Serializable]
public class ProcedureDocument
{
    public string kitId;
    public string kitName;
    public string source;
    public ProcedureStep[] steps;
}

/// <summary>説明書1ステップ分。OCR が後から埋める想定のフィールドを含む。</summary>
[Serializable]
public class ProcedureStep
{
    [Tooltip("ステップID。例: s01")]
    public string id;

    [Tooltip("説明書の出典ページ。OCR のやり直し用")]
    public int sourcePage;

    [Tooltip("ランナー／パーツ番号。例: A-1, B-3")]
    public string[] partIds;

    [TextArea(2, 4)]
    [Tooltip("中央に表示する作業指示")]
    public string instruction;

    [Tooltip("下部に表示する操作ヒント")]
    public string hint;

    [Tooltip("空なら Step n/m を自動生成する")]
    public string stepNumber;

    [Tooltip("cut / trim / fit / glue / wait / paint / check。空なら通常手順")]
    public string type;

    [Tooltip("wait の秒数。0 ならタイマーなし")]
    public int durationSec;

    public bool IsWait
    {
        get
        {
            if (durationSec > 0)
                return true;
            return !string.IsNullOrEmpty(type) &&
                   string.Equals(type.Trim(), "wait", StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>手順 JSON の読み込み。失敗時は false を返す。</summary>
public static class ProcedureDocumentLoader
{
    public static bool TryLoad(TextAsset jsonAsset, out ProcedureDocument document)
    {
        document = null;
        if (jsonAsset == null || string.IsNullOrEmpty(jsonAsset.text))
            return false;

        return TryParse(jsonAsset.text, out document);
    }

    public static bool TryLoadFromResources(string resourcesPath, out ProcedureDocument document)
    {
        document = null;
        if (string.IsNullOrEmpty(resourcesPath))
            return false;

        TextAsset asset = Resources.Load<TextAsset>(resourcesPath);
        return TryLoad(asset, out document);
    }

    public static bool TryParse(string json, out ProcedureDocument document)
    {
        document = null;
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            ProcedureDocument parsed = JsonUtility.FromJson<ProcedureDocument>(json);
            if (parsed == null || parsed.steps == null || parsed.steps.Length == 0)
                return false;

            document = parsed;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("ProcedureDocument の解析に失敗しました: " + exception.Message);
            return false;
        }
    }

    public static bool TryLoadFromFile(string path, out ProcedureDocument document)
    {
        document = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        try
        {
            return TryParse(File.ReadAllText(path), out document);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("手順ファイルを読めません: " + path + " / " + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// 実機では persistentDataPath/Procedures の JSON を優先する。
    /// active.json があればそれを使い、なければ最初の *.json。
    /// </summary>
    public static bool TryLoadFromPersistent(out ProcedureDocument document, out string loadedPath)
    {
        document = null;
        loadedPath = null;
        string folder = Path.Combine(Application.persistentDataPath, "Procedures");
        string active = Path.Combine(folder, "active.json");
        if (TryLoadFromFile(active, out document))
        {
            loadedPath = active;
            return true;
        }

        if (!Directory.Exists(folder))
            return false;

        string[] files = Directory.GetFiles(folder, "*.json");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
        {
            if (TryLoadFromFile(files[i], out document))
            {
                loadedPath = files[i];
                return true;
            }
        }

        return false;
    }

    public static string ToJson(ProcedureDocument document)
    {
        return JsonUtility.ToJson(document, true);
    }
}
