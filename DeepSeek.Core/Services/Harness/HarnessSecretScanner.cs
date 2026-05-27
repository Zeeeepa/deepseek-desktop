using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessSecretScanner
{
    private static readonly Regex[] Patterns =
    [
        new(@"sk-[a-zA-Z0-9]{20,}", RegexOptions.Compiled),
        new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),
        new(@"(?i)(api[_-]?key|secret|token)\s*[:=]\s*['""]?[a-zA-Z0-9_\-]{16,}", RegexOptions.Compiled)
    ];

    public static bool ContainsSecret(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return Patterns.Any(p => p.IsMatch(text));
    }

    public static string? RedactIfNeeded(string text)
    {
        if (!ContainsSecret(text)) return null;
        return "[内容已拦截：检测到疑似 API Key/Secret，请勿上传或写入仓库]";
    }
}
