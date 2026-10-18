using System;
using System.IO;
using UnityEngine;

[Serializable]
public class ProcedureProgress
{
    public string kitId;
    public int currentStepIndex;
    public bool completed;
    public string updatedAt;

    public static string FilePath
    {
        get { return Path.Combine(Application.persistentDataPath, "Procedures", "progress.json"); }
    }

    public static bool TryLoad(string kitId, out ProcedureProgress progress)
    {
        progress = null;
        if (string.IsNullOrEmpty(kitId) || !File.Exists(FilePath))
            return false;

        try
        {
            ProcedureProgress parsed = JsonUtility.FromJson<ProcedureProgress>(File.ReadAllText(FilePath));
            if (parsed == null || string.IsNullOrEmpty(parsed.kitId))
                return false;
            if (!string.Equals(parsed.kitId, kitId, StringComparison.Ordinal))
                return false;
            progress = parsed;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("進行の読み込みに失敗: " + exception.Message);
            return false;
        }
    }

    public static void Save(string kitId, int currentStepIndex, bool completed)
    {
        if (string.IsNullOrEmpty(kitId))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var data = new ProcedureProgress
            {
                kitId = kitId,
                currentStepIndex = currentStepIndex,
                completed = completed,
                updatedAt = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("進行の保存に失敗: " + exception.Message);
        }
    }

    public static bool HasResume(ProcedureProgress progress)
    {
        return progress != null;
    }
}
