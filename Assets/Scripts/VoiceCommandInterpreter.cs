/// <summary>
/// ハンズフリー用の短い日本語コマンドをステップ操作へ変換する。
/// </summary>
public static class VoiceCommandInterpreter
{
    public enum Command
    {
        None,
        Next,
        Previous,
        Repeat,
        Anchor,
        ResumeStart,
        ResumeContinue
    }

    public static Command Interpret(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return Command.None;

        string text = Normalize(raw);
        if (text.Length == 0)
            return Command.None;

        if (ContainsAny(text, "最初から", "さいしょから", "はじめから", "最初", "さいしょ", "はじめ"))
            return Command.ResumeStart;

        if (ContainsAny(text, "続きから", "つづきから", "続き", "つづき", "再開", "さいかい"))
            return Command.ResumeContinue;

        if (ContainsAny(text, "原点", "ここ", "記録", "キャリブ"))
            return Command.Anchor;

        if (ContainsAny(text, "もう一度", "もういちど", "もいちど", "繰り返", "くりかえ", "リピート", "りぴーと", "repeat"))
            return Command.Repeat;

        if (ContainsAny(text, "戻って", "もどって", "戻る", "もどる", "前へ", "まえへ", "前に", "まえに", "バック", "back"))
            return Command.Previous;

        if (ContainsAny(text, "次へ", "つぎへ", "次に", "つぎに", "進めて", "すすめて", "進む", "すすむ", "ネクスト", "next", "つぎ", "次"))
            return Command.Next;

        return Command.None;
    }

    static string Normalize(string raw)
    {
        var builder = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsWhiteSpace(c) || c == '。' || c == '、' || c == '.' || c == ',' || c == '!' || c == '！' || c == '?' || c == '？')
                continue;
            builder.Append(c);
        }

        return builder.ToString();
    }

    static bool ContainsAny(string text, params string[] keywords)
    {
        for (int i = 0; i < keywords.Length; i++)
        {
            if (text.IndexOf(keywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
