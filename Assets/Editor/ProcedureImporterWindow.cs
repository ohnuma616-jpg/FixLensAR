using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 説明書の OCR テキスト／画像から手順 JSON を作る。実機は JSON だけ読む。
/// </summary>
public class ProcedureImporterWindow : EditorWindow
{
    const string DefaultHintNote = "実機は persistentDataPath/Procedures/active.json を優先します。";

    string kitId = "imported-kit";
    string kitName = "取り込みキット";
    string sourceText = "";
    string previewJson = "";
    Vector2 textScroll;
    Vector2 jsonScroll;
    ProcedureDocument parsed;

    [MenuItem("Work Guidance/手順JSONを取り込む")]
    public static void Open()
    {
        GetWindow<ProcedureImporterWindow>("手順JSON取り込み");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("説明書 → 手順 JSON", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "画像は Windows OCR（日本語）、または他ツールで取ったテキストを貼り付けます。\n" +
            DefaultHintNote,
            MessageType.Info);

        kitId = EditorGUILayout.TextField("kitId", kitId);
        kitName = EditorGUILayout.TextField("kitName", kitName);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("画像を OCR"))
            RunWindowsOcrOnImages();
        if (GUILayout.Button("テキストファイルを開く"))
            OpenTextFile();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("OCR / 貼り付けテキスト");
        textScroll = EditorGUILayout.BeginScrollView(textScroll, GUILayout.MinHeight(180));
        sourceText = EditorGUILayout.TextArea(sourceText, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("手順に変換"))
            ParseNow();

        if (parsed != null && parsed.steps != null)
        {
            EditorGUILayout.LabelField("ステップ数: " + parsed.steps.Length);
            jsonScroll = EditorGUILayout.BeginScrollView(jsonScroll, GUILayout.MinHeight(160));
            EditorGUILayout.TextArea(previewJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Resources に保存"))
                SaveToResources();
            if (GUILayout.Button("実機用 JSON を書き出す"))
                SaveForDevice();
            if (GUILayout.Button("実機へ push"))
                PushActiveJson();
            EditorGUILayout.EndHorizontal();
        }
    }

    void ParseNow()
    {
        parsed = ProcedureManualParser.Parse(sourceText, kitId, kitName, "ocr");
        previewJson = ProcedureDocumentLoader.ToJson(parsed);
        if (parsed.steps == null || parsed.steps.Length == 0)
            EditorUtility.DisplayDialog("手順JSON", "ステップを切れませんでした。番号付きの行（1. や 手順1）があるか確認してください。", "OK");
        else
            Debug.Log("手順に変換: " + parsed.kitName + " / " + parsed.steps.Length + " steps");
    }

    void SaveToResources()
    {
        if (parsed == null || parsed.steps == null || parsed.steps.Length == 0)
            return;

        string safeId = SanitizeFileName(parsed.kitId);
        string dir = Path.Combine(Application.dataPath, "Resources/Procedures");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, safeId + ".json");
        File.WriteAllText(path, ProcedureDocumentLoader.ToJson(parsed), new UTF8Encoding(false));
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("保存", "保存しました:\n" + path, "OK");
    }

    void SaveForDevice()
    {
        if (parsed == null || parsed.steps == null || parsed.steps.Length == 0)
            return;

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Procedures");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "active.json");
        File.WriteAllText(path, ProcedureDocumentLoader.ToJson(parsed), new UTF8Encoding(false));
        EditorUtility.DisplayDialog(
            "実機用 JSON",
            path + "\n\nadb push 先:\nAndroid/data/<package>/files/Procedures/active.json",
            "OK");
    }

    void OpenTextFile()
    {
        string path = EditorUtility.OpenFilePanel("説明書テキスト", "", "txt,md,json");
        if (string.IsNullOrEmpty(path))
            return;
        sourceText = File.ReadAllText(path);
    }

    void RunWindowsOcrOnImages()
    {
        string[] paths = OpenImagePaths();
        if (paths == null || paths.Length == 0)
            return;

        string script = Path.Combine(Application.dataPath, "Editor/WindowsImageOcr.ps1");
        var builder = new StringBuilder();
        for (int i = 0; i < paths.Length; i++)
        {
            try
            {
                string text = RunOcrScript(script, paths[i]);
                if (builder.Length > 0)
                    builder.Append("\n\n");
                builder.Append(text.Trim());
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "Windows OCR",
                    paths[i] + "\n\n" + exception.Message + "\n日本語 OCR パックが入っているか確認してください。",
                    "OK");
                return;
            }
        }

        if (sourceText.Length > 0)
            sourceText += "\n\n";
        sourceText += builder.ToString();
        ParseNow();
    }

    static string[] OpenImagePaths()
    {
        string panel = EditorUtility.OpenFilePanel("説明書画像", "", "png,jpg,jpeg,bmp,tif,tiff");
        if (string.IsNullOrEmpty(panel))
            return Array.Empty<string>();
        return new[] { panel };
    }

    static string RunOcrScript(string scriptPath, string imagePath)
    {
        var info = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" -ImagePath \"" + imagePath + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(info))
        {
            if (process == null)
                throw new InvalidOperationException("PowerShell を起動できません。");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? output : error);
            return output;
        }
    }

    void PushActiveJson()
    {
        SaveForDevice();
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Procedures/active.json");
        string pkg = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        string dest = "/sdcard/Android/data/" + pkg + "/files/Procedures/active.json";
        string adb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android/Sdk/platform-tools/adb.exe");
        if (!File.Exists(adb))
        {
            EditorUtility.DisplayDialog("adb", "adb が見つかりません:\n" + adb, "OK");
            return;
        }

        RunCommand(adb, "shell mkdir -p /sdcard/Android/data/" + pkg + "/files/Procedures");
        string result = RunCommand(adb, "push \"" + path + "\" " + dest);
        EditorUtility.DisplayDialog("push", dest + "\n\n" + result, "OK");
    }

    static string RunCommand(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (var process = Process.Start(info))
        {
            if (process == null)
                return "failed to start";
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (output + "\n" + error).Trim();
        }
    }

    static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "imported-kit";
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '-');
        return value;
    }
}
