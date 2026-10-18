using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

/// <summary>
/// 「次へ / 戻る / もう一度」を音声で受け、WorkGuidanceManager に渡す。
/// Android は端末の SpeechRecognizer、Editor（Windows）は KeywordRecognizer。
/// </summary>
public class HandsFreeVoiceInput : MonoBehaviour
{
    [SerializeField]
    WorkGuidanceManager guidance;

    [SerializeField]
    bool logHeardPhrases = true;

    bool androidListening;
    float lastDispatchTime = -10f;
    VoiceCommandInterpreter.Command lastCommand;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    KeywordRecognizer keywordRecognizer;
    static readonly string[] EditorKeywords =
    {
        "次へ", "つぎへ", "次", "進む",
        "戻る", "もどる", "前へ",
        "もう一度", "もういちど", "リピート",
        "最初から", "続きから", "最初", "続き"
    };
#endif

    void Awake()
    {
        if (guidance == null)
            guidance = GetComponent<WorkGuidanceManager>();
    }

    IEnumerator Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        yield return RequestMicrophoneThenStart();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        StartEditorKeywords();
        yield break;
#else
        Debug.Log("HandsFreeVoiceInput: このプラットフォームでは音声入力を開始しません。Space キーで次へ進めます。");
        yield break;
#endif
    }

    void OnDestroy()
    {
        StopListening();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        // グラスは起動直後に pause が来ることがある。音声ループは止めない。
        if (!pauseStatus && isActiveAndEnabled)
            StartCoroutine(RestartAfterResume());
    }

    IEnumerator RestartAfterResume()
    {
        yield return new WaitForSeconds(0.4f);
#if UNITY_ANDROID && !UNITY_EDITOR
        yield return RequestMicrophoneThenStart();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        StartEditorKeywords();
#endif
    }

    /// <summary>Android プラグインが UnitySendMessage で呼ぶ。</summary>
    public void OnSpeechHeard(string phrase)
    {
        if (!string.IsNullOrEmpty(phrase) && phrase.StartsWith("STATUS:"))
        {
            HandleStatus(phrase.Substring(7));
            return;
        }

        Dispatch(phrase);
    }

    void HandleStatus(string status)
    {
        Debug.Log("HandsFreeVoiceInput status: " + status);
        if (guidance == null)
            return;

        if (status == "unavailable")
            guidance.ShowVoiceStatus("音声認識が使えません");
        else if (status == "loading")
            guidance.ShowVoiceStatus("音声準備中");
        else if (status == "listening")
            guidance.ShowVoiceStatus(null);
        else if (status == "error:mic")
            guidance.ShowVoiceStatus("マイクを開けません");
        else if (status.StartsWith("error:"))
            guidance.ShowVoiceStatus("音声待機中");
    }

    void Dispatch(string phrase)
    {
        if (guidance == null)
            return;

        VoiceCommandInterpreter.Command command = VoiceCommandInterpreter.Interpret(phrase);
        if (command == VoiceCommandInterpreter.Command.None)
        {
            if (logHeardPhrases)
                Debug.Log("音声（未一致）: " + phrase);
            guidance.NotifyUnmatchedSpeech(phrase);
            return;
        }

        if (command == lastCommand && Time.unscaledTime - lastDispatchTime < 2f)
            return;
        lastDispatchTime = Time.unscaledTime;
        lastCommand = command;

        if (logHeardPhrases)
            Debug.Log("音声コマンド: " + command + " / " + phrase);

        switch (command)
        {
            case VoiceCommandInterpreter.Command.Next:
                guidance.NextStep();
                break;
            case VoiceCommandInterpreter.Command.Previous:
                guidance.PreviousStep();
                break;
            case VoiceCommandInterpreter.Command.Repeat:
                guidance.RepeatCurrentStep();
                break;
            case VoiceCommandInterpreter.Command.Anchor:
                guidance.LockAnchorNow();
                break;
            case VoiceCommandInterpreter.Command.ResumeStart:
                guidance.ResumeFromStart();
                break;
            case VoiceCommandInterpreter.Command.ResumeContinue:
                guidance.ResumeContinue();
                break;
        }

        guidance.NotifyVoiceHeard(command);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    IEnumerator RequestMicrophoneThenStart()
    {
        yield return new WaitForSeconds(0.6f);

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            float elapsed = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && elapsed < 20f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.LogWarning("マイク権限が無いため音声操作を開始できません。");
            yield break;
        }

        StartAndroidSpeech();
    }

    void StartAndroidSpeech()
    {
        if (androidListening)
            return;

        using (var plugin = new AndroidJavaClass("com.workguidance.speech.KeywordSpeechPlugin"))
        {
            plugin.CallStatic("start", gameObject.name, nameof(OnSpeechHeard));
        }

        androidListening = true;
        Debug.Log("HandsFreeVoiceInput: Android 音声認識を開始しました。");
    }

    void StopAndroidSpeech()
    {
        if (!androidListening)
            return;

        try
        {
            using (var plugin = new AndroidJavaClass("com.workguidance.speech.KeywordSpeechPlugin"))
            {
                plugin.CallStatic("stop");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("音声認識の停止に失敗: " + exception.Message);
        }

        androidListening = false;
    }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    void StartEditorKeywords()
    {
        StopEditorKeywords();
        try
        {
            keywordRecognizer = new KeywordRecognizer(EditorKeywords);
            keywordRecognizer.OnPhraseRecognized += OnEditorPhraseRecognized;
            keywordRecognizer.Start();
            Debug.Log("HandsFreeVoiceInput: Editor キーワード認識を開始しました。");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Editor の音声認識を開始できません（Windows の音声認識 / 日本語パックを確認）: " + exception.Message);
        }
    }

    void StopEditorKeywords()
    {
        if (keywordRecognizer == null)
            return;

        keywordRecognizer.OnPhraseRecognized -= OnEditorPhraseRecognized;
        if (keywordRecognizer.IsRunning)
            keywordRecognizer.Stop();
        keywordRecognizer.Dispose();
        keywordRecognizer = null;
    }

    void OnEditorPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        Dispatch(args.text);
    }
#endif

    void StopListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StopAndroidSpeech();
#endif
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        StopEditorKeywords();
#endif
    }
}
