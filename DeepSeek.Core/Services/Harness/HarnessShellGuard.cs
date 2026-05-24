using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>拦截明显高危 shell 命令（本地沙盒仍直接跑在宿主机）。</summary>
public static class HarnessShellGuard
{
    private static readonly Regex[] BlockedPatterns =
    [
        new(@"^\s*format\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\brm\s+(-[a-z]*f[a-z]*\s+)?(/|\\|[a-z]:\\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bdel\s+/[fsq]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\brd\s+/s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bRemove-Item\b.+\s-Recurse\b.+\s-Force", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"powershell\s+.*-enc(odedcommand)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bmkfs\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@":\(\)\s*\{\s*:\s*\|\s*:\s*&\s*\}\s*;", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    public static string? BlockReason(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "命令不能为空";

        var text = command.Trim();
        foreach (var pattern in BlockedPatterns)
        {
            if (pattern.IsMatch(text))
                return "已拦截高危命令: " + Truncate(text, 120);
        }

        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
