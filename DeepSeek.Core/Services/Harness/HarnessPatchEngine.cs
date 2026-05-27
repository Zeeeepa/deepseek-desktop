using System.Text;
using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>Search/Replace 引擎（对齐 deepcode-cli edit-handler 核心匹配策略）。</summary>
public static class HarnessPatchEngine
{
    private const double MinFuzzyScore = 0.8;

    public static HarnessPatchResult Apply(HarnessPatchRequest request, string fileContent)
    {
        var oldString = request.OldString ?? "";
        var newString = request.NewString ?? "";

        if (string.IsNullOrEmpty(oldString) && string.IsNullOrEmpty(newString))
            return Fail("old_string 与 new_string 均为空");

        if (oldString.Length == 0)
        {
            return new HarnessPatchResult
            {
                Success = true,
                MatchCount = 1,
                OriginalContent = fileContent,
                PatchedContent = fileContent + newString,
                UnifiedDiff = BuildUnifiedDiff(request.FilePath, fileContent, fileContent + newString)
            };
        }

        var occurrences = FindOccurrences(fileContent, oldString);
        if (occurrences.Count == 0)
        {
            var loose = FindLooseOccurrences(fileContent, oldString);
            if (loose.Count > 0)
            {
                var best = loose.OrderByDescending(x => x.Score).First();
                occurrences = [best.Match];
            }
            else
            {
                return Fail(
                    "old_string 未在文件中找到",
                    ClosestMatchHint: BuildClosestHint(fileContent, oldString));
            }
        }

        if (!request.ReplaceAll && occurrences.Count > 1)
        {
            return Fail(
                $"old_string 在文件中出现 {occurrences.Count} 次，请设置 replace_all 或提供更精确的上下文",
                ClosestMatchHint: null);
        }

        var patched = ApplyOccurrences(fileContent, occurrences, newString, request.ReplaceAll);
        return new HarnessPatchResult
        {
            Success = true,
            MatchCount = request.ReplaceAll ? occurrences.Count : 1,
            OriginalContent = fileContent,
            PatchedContent = patched,
            UnifiedDiff = BuildUnifiedDiff(request.FilePath, fileContent, patched)
        };
    }

    private static HarnessPatchResult Fail(string error, string? ClosestMatchHint = null) =>
        new() { Success = false, Error = error, ClosestMatchHint = ClosestMatchHint };

    private sealed record MatchOccurrence(int Start, int End);

    private sealed record LooseMatch(MatchOccurrence Match, double Score);

    private static List<MatchOccurrence> FindOccurrences(string text, string needle) =>
        Regex.Matches(text, Regex.Escape(needle))
            .Cast<Match>()
            .Select(m => new MatchOccurrence(m.Index, m.Index + m.Length))
            .ToList();

    private static List<LooseMatch> FindLooseOccurrences(string text, string needle)
    {
        var normalizedNeedle = NormalizeLoose(needle);
        if (normalizedNeedle.Length == 0)
            return [];

        var lines = text.Split('\n');
        var results = new List<LooseMatch>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var normLine = NormalizeLoose(line);
            if (normLine.Contains(normalizedNeedle, StringComparison.Ordinal))
            {
                var idx = normLine.IndexOf(normalizedNeedle, StringComparison.Ordinal);
                var start = line.IndexOf(line.TrimStart()[..Math.Min(line.TrimStart().Length, line.Length)], StringComparison.Ordinal);
                if (start < 0) start = 0;
                results.Add(new LooseMatch(new MatchOccurrence(start, start + needle.Length), 0.85));
            }
        }

        return results;
    }

    private static string NormalizeLoose(string s) =>
        Regex.Replace(s.Trim(), @"\s+", " ");

    private static string ApplyOccurrences(
        string text,
        List<MatchOccurrence> occurrences,
        string newString,
        bool replaceAll)
    {
        if (!replaceAll)
        {
            var m = occurrences[0];
            return text[..m.Start] + newString + text[m.End..];
        }

        var sb = new StringBuilder();
        var last = 0;
        foreach (var m in occurrences.OrderBy(x => x.Start))
        {
            sb.Append(text, last, m.Start - last);
            sb.Append(newString);
            last = m.End;
        }

        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    private static string BuildClosestHint(string content, string needle)
    {
        var lines = content.Split('\n');
        var needleLine = needle.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? needle;
        var bestIdx = -1;
        var bestScore = 0.0;
        for (var i = 0; i < lines.Length; i++)
        {
            var score = Similarity(NormalizeLoose(lines[i]), NormalizeLoose(needleLine));
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        if (bestIdx < 0)
            return "";

        var start = Math.Max(0, bestIdx - 2);
        var end = Math.Min(lines.Length - 1, bestIdx + 2);
        return string.Join('\n', lines[start..(end + 1)]);
    }

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return 0;
        var maxLen = Math.Max(a.Length, b.Length);
        var dist = Levenshtein(a, b);
        return 1.0 - (double)dist / maxLen;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    public static string BuildUnifiedDiff(string path, string before, string after)
    {
        var beforeLines = before.Replace("\r\n", "\n").Split('\n');
        var afterLines = after.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        sb.AppendLine("--- a/" + path);
        sb.AppendLine("+++ b/" + path);
        var max = Math.Max(beforeLines.Length, afterLines.Length);
        for (var i = 0; i < max; i++)
        {
            var b = i < beforeLines.Length ? beforeLines[i] : null;
            var a = i < afterLines.Length ? afterLines[i] : null;
            if (b == a) continue;
            if (b is not null && a is not null && b != a)
            {
                sb.AppendLine("-" + b);
                sb.AppendLine("+" + a);
            }
            else if (b is not null)
                sb.AppendLine("-" + b);
            else if (a is not null)
                sb.AppendLine("+" + a);
        }

        return sb.ToString();
    }
}
