using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 作業誘導コンパニオンのステップ進行と、上・中・下の3段UIテキスト更新を管理する。
/// 手順の正は JSON（将来の OCR 出力）。インスペクター一覧はフォールバック。
/// </summary>
[RequireComponent(typeof(HandsFreeVoiceInput))]
public class WorkGuidanceManager : MonoBehaviour
{
    const string DefaultResourcesPath = "Procedures/sample_plamodel";

    [Header("手順データ")]
    [SerializeField]
    [Tooltip("OCR または手編集の手順 JSON。未指定なら Resources のデフォルトを読む")]
    TextAsset procedureJson;

    [SerializeField]
    [Tooltip("TextAsset 未指定時に Resources.Load するパス（拡張子なし）")]
    string procedureResourcesPath = DefaultResourcesPath;

    [SerializeField]
    [Tooltip("JSON が無いときだけ使う仮データ")]
    List<ProcedureStep> fallbackSteps = new List<ProcedureStep>();

    [Header("3段UI（TextMeshPro）")]
    [SerializeField]
    [Tooltip("上部UI: ステップ番号")]
    TMP_Text headerText;

    [SerializeField]
    [Tooltip("中央UI: 作業指示")]
    TMP_Text instructionText;

    [SerializeField]
    [Tooltip("下部UI: 操作ヒント")]
    TMP_Text hintText;

    readonly List<ProcedureStep> workSteps = new List<ProcedureStep>();

    int currentStepIndex;
    bool isCompleted;
    bool calibrating;
    bool awaitingResumeChoice;
    Coroutine hintFlash;
    Coroutine waitRoutine;
    string voiceStatusOverride;
    string loadedKitId;
    ProcedureProgress pendingResume;
    KitAnchorCapture anchorCapture;
    KitWorldHint worldHint;
    RayNeoSlamProbe slamProbe;

    void Awake()
    {
        if (GetComponent<HandsFreeVoiceInput>() == null)
            gameObject.AddComponent<HandsFreeVoiceInput>();

        Canvas canvas = null;
        if (headerText != null)
            canvas = headerText.canvas;
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>();
        if (canvas != null && canvas.GetComponent<BinocularSbsUi>() == null)
            canvas.gameObject.AddComponent<BinocularSbsUi>();

        anchorCapture = GetComponent<KitAnchorCapture>();
        if (anchorCapture == null)
            anchorCapture = gameObject.AddComponent<KitAnchorCapture>();
        worldHint = GetComponent<KitWorldHint>();
        if (worldHint == null)
            worldHint = gameObject.AddComponent<KitWorldHint>();
        slamProbe = GetComponent<RayNeoSlamProbe>();
        if (slamProbe == null)
            slamProbe = gameObject.AddComponent<RayNeoSlamProbe>();
    }

    void Start()
    {
        LoadSteps();
        currentStepIndex = 0;
        isCompleted = false;
        if (ProcedureProgress.TryLoad(loadedKitId, out ProcedureProgress saved) && ProcedureProgress.HasResume(saved))
        {
            pendingResume = saved;
            awaitingResumeChoice = true;
            string body = saved.completed
                ? "前回は完了しています"
                : "Step " + (saved.currentStepIndex + 1) + "/" + workSteps.Count + " から続けられます";
            SetUiTexts("再開", body, "「次へ」で続き　「戻る」で最初から");
            return;
        }

        StartCoroutine(BootWithCalibration());
    }

    void Update()
    {
        // ハンズフリー操作のテスト用: Space で次ステップへ進む
        if (WasSpacePressedThisFrame())
        {
            if (awaitingResumeChoice)
                ResumeContinue();
            else
                NextStep();
        }

        if (calibrating && anchorCapture != null && hintText != null && hintFlash == null)
            hintText.text = string.IsNullOrEmpty(anchorCapture.Status)
                ? "キットを2秒見つめる　「次へ」でスキップ"
                : anchorCapture.Status;
    }

    /// <summary>次の作業ステップへ進み、3段UIを更新する。最後の次は完了表示。</summary>
    public void NextStep()
    {
        if (awaitingResumeChoice)
        {
            ResumeContinue();
            return;
        }

        if (calibrating)
        {
            FinishCalibration(true);
            return;
        }

        if (isCompleted)
            return;

        if (workSteps.Count == 0)
        {
            ShowCompleted();
            return;
        }

        if (currentStepIndex >= workSteps.Count - 1)
        {
            ShowCompleted();
            return;
        }

        currentStepIndex++;
        ApplyCurrentStep();
        PersistProgress();
    }

    /// <summary>1つ前のステップへ戻る。完了画面からは最後のステップへ戻る。</summary>
    public void PreviousStep()
    {
        if (awaitingResumeChoice)
        {
            ResumeFromStart();
            return;
        }

        if (workSteps.Count == 0)
            return;

        if (isCompleted)
        {
            isCompleted = false;
            currentStepIndex = workSteps.Count - 1;
            ApplyCurrentStep();
            PersistProgress();
            return;
        }

        if (currentStepIndex <= 0)
        {
            ApplyCurrentStep();
            return;
        }

        currentStepIndex--;
        ApplyCurrentStep();
        PersistProgress();
    }

    /// <summary>現在の指示を中央に出し直す。</summary>
    public void RepeatCurrentStep()
    {
        if (calibrating || awaitingResumeChoice)
            return;
        ApplyCurrentStep();
    }

    public void ResumeFromStart()
    {
        if (!awaitingResumeChoice)
            return;
        awaitingResumeChoice = false;
        pendingResume = null;
        currentStepIndex = 0;
        isCompleted = false;
        StartCoroutine(BootWithCalibration());
    }

    public void ResumeContinue()
    {
        if (!awaitingResumeChoice || pendingResume == null)
            return;
        awaitingResumeChoice = false;
        currentStepIndex = Mathf.Clamp(pendingResume.currentStepIndex, 0, Mathf.Max(0, workSteps.Count - 1));
        isCompleted = pendingResume.completed;
        pendingResume = null;
        if (isCompleted)
            ShowCompleted();
        else
            ApplyCurrentStep();
    }

    public void LockAnchorNow()
    {
        if (!calibrating || anchorCapture == null)
            return;
        anchorCapture.CaptureNow();
    }

    public void NotifyUnmatchedSpeech(string phrase)
    {
        if (!awaitingResumeChoice || string.IsNullOrEmpty(phrase))
            return;
        if (hintFlash != null)
            StopCoroutine(hintFlash);
        hintFlash = StartCoroutine(FlashHint("聞こえた：" + phrase));
    }

    public void NotifyVoiceHeard(VoiceCommandInterpreter.Command command)
    {
        string flash = "了解";
        if (command == VoiceCommandInterpreter.Command.Next)
            flash = "了解：次へ";
        else if (command == VoiceCommandInterpreter.Command.Previous)
            flash = "了解：戻る";
        else if (command == VoiceCommandInterpreter.Command.Repeat)
            flash = "了解：もう一度";
        else if (command == VoiceCommandInterpreter.Command.Anchor)
            flash = "了解：原点";
        else if (command == VoiceCommandInterpreter.Command.ResumeStart)
            flash = "了解：最初から";
        else if (command == VoiceCommandInterpreter.Command.ResumeContinue)
            flash = "了解：続きから";

        if (hintFlash != null)
            StopCoroutine(hintFlash);
        hintFlash = StartCoroutine(FlashHint(flash));
    }

    public void ShowVoiceStatus(string status)
    {
        voiceStatusOverride = status;
        RefreshHintOnly();
    }

    IEnumerator FlashHint(string flash)
    {
        if (hintText != null)
            hintText.text = flash;
        yield return new WaitForSeconds(1.2f);
        hintFlash = null;
        RefreshHintOnly();
    }

    void RefreshHintOnly()
    {
        if (hintText == null)
            return;
        if (!string.IsNullOrEmpty(voiceStatusOverride))
        {
            hintText.text = voiceStatusOverride;
            return;
        }

        if (awaitingResumeChoice)
        {
            hintText.text = "「次へ」で続き　「戻る」で最初から";
            return;
        }

        if (isCompleted)
        {
            hintText.text = "「戻る」で最後の手順へ";
            return;
        }

        if (workSteps.Count == 0)
            return;

        hintText.text = workSteps[currentStepIndex].hint;
    }

    IEnumerator BootWithCalibration()
    {
        calibrating = true;
        SetUiTexts(
            "準備",
            "キット全体を2秒見つめて原点を決める",
            "「次へ」でスキップ　「原点」で記録");

        if (anchorCapture != null)
            yield return anchorCapture.WatchUntilAnchored();

        FinishCalibration(false);
    }

    void FinishCalibration(bool skipped)
    {
        if (!calibrating)
            return;

        calibrating = false;
        if (anchorCapture != null)
        {
            if (skipped)
                anchorCapture.Skip();
            Debug.Log("原点: anchored=" + anchorCapture.IsAnchored + " " + anchorCapture.Status + " " + anchorCapture.SavedPath);
        }

        if (worldHint != null && !skipped)
            worldHint.LockToCurrentPose();

        ApplyCurrentStep();
        PersistProgress();
    }

    void LoadSteps()
    {
        workSteps.Clear();
        loadedKitId = null;

        if (ProcedureDocumentLoader.TryLoadFromPersistent(out ProcedureDocument fromDevice, out string devicePath))
        {
            UseDocument(fromDevice);
            Debug.Log("手順JSONを端末から読み込みました: " + fromDevice.kitName + " (" + workSteps.Count + " steps) " + devicePath);
            return;
        }

        if (ProcedureDocumentLoader.TryLoad(procedureJson, out ProcedureDocument fromAsset))
        {
            UseDocument(fromAsset);
            Debug.Log("手順JSONを読み込みました: " + fromAsset.kitName + " (" + workSteps.Count + " steps)");
            return;
        }

        if (ProcedureDocumentLoader.TryLoadFromResources(procedureResourcesPath, out ProcedureDocument fromResources))
        {
            UseDocument(fromResources);
            Debug.Log("手順JSONを Resources から読み込みました: " + fromResources.kitName + " (" + workSteps.Count + " steps)");
            return;
        }

        if (fallbackSteps != null && fallbackSteps.Count > 0)
        {
            workSteps.AddRange(fallbackSteps);
            loadedKitId = "fallback";
            Debug.LogWarning("手順JSONが見つからないため、インスペクターのフォールバックを使います。");
        }
    }

    void UseDocument(ProcedureDocument document)
    {
        workSteps.AddRange(document.steps);
        loadedKitId = string.IsNullOrEmpty(document.kitId) ? document.kitName : document.kitId;
    }

    void ApplyCurrentStep()
    {
        StopWait();
        if (workSteps.Count == 0)
        {
            ShowCompleted();
            return;
        }

        if (isCompleted)
        {
            ShowCompleted();
            return;
        }

        ProcedureStep step = workSteps[currentStepIndex];
        SetUiTexts(BuildHeader(step), step.instruction, step.hint);
        if (worldHint != null)
            worldHint.SetLabel(FormatPartIds(step.partIds));
        if (step.IsWait)
            waitRoutine = StartCoroutine(RunWait(step));
    }

    IEnumerator RunWait(ProcedureStep step)
    {
        int total = Mathf.Max(1, step.durationSec);
        for (int left = total; left > 0; left--)
        {
            if (instructionText != null)
                instructionText.text = step.instruction + "\n" + left + "秒";
            if (hintText != null && hintFlash == null && string.IsNullOrEmpty(voiceStatusOverride))
                hintText.text = "待つ　「次へ」で先に進む";
            yield return new WaitForSeconds(1f);
        }

        if (instructionText != null)
            instructionText.text = step.instruction + "\n待ち完了";
        if (hintText != null && hintFlash == null && string.IsNullOrEmpty(voiceStatusOverride))
            hintText.text = "「次へ」で進む";
        waitRoutine = null;
    }

    void StopWait()
    {
        if (waitRoutine == null)
            return;
        StopCoroutine(waitRoutine);
        waitRoutine = null;
    }

    void PersistProgress()
    {
        ProcedureProgress.Save(loadedKitId, currentStepIndex, isCompleted);
    }

    string BuildHeader(ProcedureStep step)
    {
        string number = string.IsNullOrEmpty(step.stepNumber)
            ? "Step " + (currentStepIndex + 1) + "/" + workSteps.Count
            : step.stepNumber;

        string parts = FormatPartIds(step.partIds);
        return string.IsNullOrEmpty(parts) ? number : number + " | " + parts;
    }

    static string FormatPartIds(string[] partIds)
    {
        if (partIds == null || partIds.Length == 0)
            return string.Empty;

        var usable = new List<string>();
        for (int i = 0; i < partIds.Length; i++)
        {
            if (!string.IsNullOrEmpty(partIds[i]))
                usable.Add(partIds[i]);
        }

        return usable.Count == 0 ? string.Empty : string.Join(", ", usable);
    }

    void ShowCompleted()
    {
        isCompleted = true;
        StopWait();
        SetUiTexts("完了", "全作業が完了しました", "「戻る」で最後の手順へ");
        if (worldHint != null)
            worldHint.Hide();
        PersistProgress();
    }

    void SetUiTexts(string header, string instruction, string hint)
    {
        if (headerText != null)
            headerText.text = header;
        if (instructionText != null)
            instructionText.text = instruction;
        if (hintText != null)
            hintText.text = hint;
    }

    static bool WasSpacePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }
}
