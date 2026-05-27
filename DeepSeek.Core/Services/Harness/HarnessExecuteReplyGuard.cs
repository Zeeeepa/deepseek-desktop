using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>Execute 阶段：模型只写任务分析/空 json 围栏、未真正调用工具时的判定。</summary>
public static class HarnessExecuteReplyGuard
{
    private static readonly Regex ImplementationPrompt = new(
        @"写|实现|创建|开发|游戏|网页|页面|html|小程序|脚本|程序",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsImplementationLikePrompt(string? prompt) =>
        !string.IsNullOrWhiteSpace(prompt) && ImplementationPrompt.IsMatch(prompt);

    public static bool IsProseOnlyTaskAnalysis(string? answer, string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(answer) || HarnessEmptyReply.IsEmpty(answer))
            return false;
        if (!IsImplementationLikePrompt(userPrompt))
            return false;

        var t = answer.Trim();
        if (t.Length < 40)
            return false;
        if (HarnessXmlToolCallParser.HasToolCallMarkers(t))
            return false;
        if (HarnessXmlToolCallParser.HasIncompleteToolArtifacts(t))
            return false;

        var hasAnalysis = t.Contains("任务分析", StringComparison.Ordinal)
                          || t.Contains("验收标准", StringComparison.Ordinal)
                          || t.Contains("计划", StringComparison.Ordinal);
        if (!hasAnalysis)
            return false;

        return !t.Contains("<tool_calling>", StringComparison.OrdinalIgnoreCase)
               && !t.Contains("Call:", StringComparison.OrdinalIgnoreCase)
               && !HarnessXmlToolCallParser.ContainsLooseJsonToolObject(t)
               && !Regex.IsMatch(t, @"\b(?:write_file|list_dir|read_file|run_shell)\b", RegexOptions.IgnoreCase);
    }
}
