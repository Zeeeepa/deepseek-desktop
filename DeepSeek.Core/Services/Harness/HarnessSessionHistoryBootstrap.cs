using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// 当 Harness 状态未持久化 Messages 时，从 Agent UI 会话记录恢复多轮上下文。
/// </summary>
public static class HarnessSessionHistoryBootstrap
{
    public static List<ChatMessage>? TryBuildHistory(string? sessionId, string currentPrompt)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        AgentSessionData? session;
        try
        {
            session = new AgentSessionStore().Load(sessionId);
        }
        catch
        {
            return null;
        }

        if (session?.Messages is not { Count: > 0 } uiMessages)
            return null;

        var current = (currentPrompt ?? "").Trim();
        var history = new List<ChatMessage>();

        for (var i = 0; i < uiMessages.Count; i++)
        {
            var m = uiMessages[i];
            if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                var text = (m.Text ?? "").Trim();
                if (text.Length == 0)
                    continue;
                // 当前轮 user 消息由 HarnessComposer / state.Messages 注入，避免重复
                if (i == uiMessages.Count - 1 && text == current)
                    continue;
                history.Add(new ChatMessage { Role = "user", Content = text });
            }
            else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                var answer = (m.Answer ?? "").Trim();
                if (answer.Length == 0)
                    continue;
                history.Add(new ChatMessage { Role = "assistant", Content = answer });
            }
        }

        return history.Count > 0 ? history : null;
    }
}
