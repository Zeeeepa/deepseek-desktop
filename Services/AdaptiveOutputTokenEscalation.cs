using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>网页对话输出截断检测与多轮续写（Chat2API / 桥接路径）。</summary>
public static class AdaptiveOutputTokenEscalation
{
    public const int CappedDefaultMaxTokens = 8_000;
    public const int EscalatedMaxTokens = 64_000;
    public const int MaxRecoveryAttempts = 3;

    public const string RecoveryUserMessage =
        "你的上一轮回答因长度被截断。请从上次中断处直接继续，不要重复已输出内容；若涉及工具调用请拆成更小步骤。";

    public const string TruncatedToolGuidance =
        "上一轮输出不完整。请将 write/edit 拆成小步：先写骨架，再多次增量修改。";

    public static bool IsTruncated(WebChatResult result)
    {
        if (result.FinishReason is "length")
            return true;
        return result.IsLikelyTruncated;
    }

    public static bool DetectHeuristicTruncation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var t = content.TrimEnd();
        if (t.Length < 800)
            return false;

        if (t.Contains("Final Answer:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (t.EndsWith("```") || t.EndsWith("</tool_calling>"))
            return false;

        if (t.EndsWith("Action Input:", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("Action:", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("Thought:", StringComparison.OrdinalIgnoreCase))
            return true;

        var last = t[^1];
        if (last is '.' or '。' or '!' or '?' or ')' or ']' or '}' or '"')
            return false;

        return t.Length > 1_500;
    }

    public static async Task<WebChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        AppConfig config,
        Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<WebChatResult>> sendAsync,
        Action<string>? onLog,
        CancellationToken ct)
    {
        if (!config.EnableAdaptiveOutputEscalation)
            return await sendAsync(messages, ct);

        var result = await sendAsync(messages, ct);
        if (!IsTruncated(result))
            return result;

        onLog?.Invoke("[DeepSeek] 检测到输出可能被截断，尝试续写…");

        var working = messages.ToList();
        var accumulated = result.Content ?? "";
        var reasoning = result.ReasoningContent;

        for (var attempt = 1; attempt <= MaxRecoveryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(accumulated))
                working.Add(new ChatMessage { Role = "assistant", Content = accumulated });

            working.Add(new ChatMessage { Role = "user", Content = RecoveryUserMessage });
            onLog?.Invoke($"[DeepSeek] 续写第 {attempt}/{MaxRecoveryAttempts} 轮…");

            var cont = await sendAsync(working, ct);
            if (!string.IsNullOrWhiteSpace(cont.Content))
                accumulated = string.IsNullOrWhiteSpace(accumulated)
                    ? cont.Content!
                    : accumulated + "\n" + cont.Content;

            if (!string.IsNullOrWhiteSpace(cont.ReasoningContent))
                reasoning = string.IsNullOrWhiteSpace(reasoning)
                    ? cont.ReasoningContent
                    : reasoning + "\n" + cont.ReasoningContent;

            if (!IsTruncated(cont))
            {
                return new WebChatResult
                {
                    Content = accumulated,
                    ReasoningContent = reasoning,
                    ToolCalls = cont.ToolCalls,
                    Model = cont.Model,
                    FinishReason = cont.FinishReason ?? "stop"
                };
            }
        }

        onLog?.Invoke("[DeepSeek] 续写次数已用尽，返回已生成部分。");
        return new WebChatResult
        {
            Content = accumulated + "\n\n" + TruncatedToolGuidance,
            ReasoningContent = reasoning,
            ToolCalls = result.ToolCalls,
            Model = result.Model,
            FinishReason = "length"
        };
    }
}
