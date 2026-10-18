using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 説明書 OCR テキストを ProcedureDocument へ落とす。Unity は JSON だけ読む。
/// </summary>
public static class ProcedureManualParser
{
    public const string DefaultHint = "「次へ」「進む」　「戻る」　「もう一度」";

    static readonly Regex PageRegex = new Regex(
        @"(?:p(?:age)?[\s\.:：]*|ページ[\s]*)(\d+)|(\d+)\s*ページ",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex StepStartRegex = new Regex(
        @"^(?:手順|工程|STEP|Step|step)\s*([0-9]{1,2})[\s\.．、,：:\)）]*|" +
        @"^([0-9]{1,2})[\s]*[\.．、,：:\)）]|" +
        @"^([①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳])",
        RegexOptions.Compiled);

    static readonly Regex PartIdRegex = new Regex(
        @"[A-Za-zＡ-Ｚａ-ｚ]{1,2}\s*[-−ーｰ]?\s*[0-9０-９]{1,3}[A-Za-zＡ-Ｚａ-ｚ]?",
        RegexOptions.Compiled);

    public static ProcedureDocument Parse(string raw, string kitId, string kitName, string source)
    {
        var document = new ProcedureDocument
        {
            kitId = string.IsNullOrEmpty(kitId) ? "imported-" + DateTime.Now.ToString("yyyyMMdd-HHmm") : kitId,
            kitName = string.IsNullOrEmpty(kitName) ? "取り込み手順" : kitName,
            source = string.IsNullOrEmpty(source) ? "ocr" : source
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            document.steps = Array.Empty<ProcedureStep>();
            return document;
        }

        string[] lines = NormalizeNewlines(raw).Split('\n');
        var steps = new List<ProcedureStep>();
        var current = new StringBuilder();
        int page = 0;
        int stepNumber = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            int parsedPage = TryParsePage(line);
            if (parsedPage > 0)
            {
                page = parsedPage;
                if (LooksLikePageOnly(line))
                    continue;
            }

            int parsedStep = TryParseStepStart(line, out string rest);
            if (parsedStep > 0)
            {
                FlushStep(steps, current, ref stepNumber, page);
                stepNumber = parsedStep;
                if (!string.IsNullOrEmpty(rest))
                    current.Append(rest);
                continue;
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(line);
        }

        FlushStep(steps, current, ref stepNumber, page);

        if (steps.Count == 0)
            steps.AddRange(SplitByParagraph(raw, page));

        document.steps = steps.ToArray();
        return document;
    }

    static void FlushStep(List<ProcedureStep> steps, StringBuilder current, ref int stepNumber, int page)
    {
        string instruction = current.ToString().Trim();
        current.Length = 0;
        if (instruction.Length == 0)
            return;

        if (stepNumber <= 0)
            stepNumber = steps.Count + 1;

        string[] parts = ExtractPartIds(instruction);
        steps.Add(new ProcedureStep
        {
            id = "s" + stepNumber.ToString("00"),
            sourcePage = page,
            partIds = parts,
            instruction = instruction,
            hint = DefaultHint,
            stepNumber = string.Empty
        });
        stepNumber = 0;
    }

    static List<ProcedureStep> SplitByParagraph(string raw, int page)
    {
        var steps = new List<ProcedureStep>();
        string[] blocks = Regex.Split(NormalizeNewlines(raw), @"\n\s*\n");
        int n = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            string instruction = blocks[i].Replace('\n', ' ').Trim();
            if (instruction.Length < 8)
                continue;

            n++;
            steps.Add(new ProcedureStep
            {
                id = "s" + n.ToString("00"),
                sourcePage = page,
                partIds = ExtractPartIds(instruction),
                instruction = instruction,
                hint = DefaultHint,
                stepNumber = string.Empty
            });
        }

        return steps;
    }

    static string[] ExtractPartIds(string text)
    {
        var seen = new HashSet<string>();
        var list = new List<string>();
        MatchCollection matches = PartIdRegex.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            string id = NormalizePartId(matches[i].Value);
            if (id.Length == 0 || !seen.Add(id))
                continue;
            list.Add(id);
        }

        return list.ToArray();
    }

    static string NormalizePartId(string raw)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c >= 'Ａ' && c <= 'Ｚ')
                c = (char)(c - 'Ａ' + 'A');
            else if (c >= 'ａ' && c <= 'ｚ')
                c = (char)(c - 'ａ' + 'a');
            else if (c >= '０' && c <= '９')
                c = (char)(c - '０' + '0');
            else if (c == '−' || c == 'ー' || c == 'ｰ')
                c = '-';
            else if (char.IsWhiteSpace(c))
                continue;

            builder.Append(c);
        }

        string value = builder.ToString().ToUpperInvariant();
        Match match = Regex.Match(value, @"^([A-Z]{1,2})-?([0-9]{1,3})([A-Z]?)$");
        if (!match.Success)
            return string.Empty;

        return match.Groups[1].Value + "-" + match.Groups[2].Value + match.Groups[3].Value;
    }

    static int TryParsePage(string line)
    {
        Match match = PageRegex.Match(line);
        if (!match.Success)
            return 0;

        string digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        int page;
        return int.TryParse(digits, out page) ? page : 0;
    }

    static bool LooksLikePageOnly(string line)
    {
        string compact = PageRegex.Replace(line, string.Empty).Trim();
        return compact.Length <= 2;
    }

    static int TryParseStepStart(string line, out string rest)
    {
        rest = string.Empty;
        Match match = StepStartRegex.Match(line);
        if (!match.Success)
            return 0;

        int number = 0;
        if (match.Groups[1].Success)
            int.TryParse(match.Groups[1].Value, out number);
        else if (match.Groups[2].Success)
            int.TryParse(match.Groups[2].Value, out number);
        else if (match.Groups[3].Success)
            number = CircledNumber(match.Groups[3].Value);

        rest = line.Substring(match.Length).Trim();
        return number;
    }

    static int CircledNumber(string token)
    {
        if (string.IsNullOrEmpty(token))
            return 0;
        int code = token[0];
        if (code >= 0x2460 && code <= 0x2473)
            return code - 0x2460 + 1;
        return 0;
    }

    static string NormalizeNewlines(string raw)
    {
        return raw.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
