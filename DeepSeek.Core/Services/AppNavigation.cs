namespace DeepSeekBrowser.Services;

public static class AppNavigation
{
    public const string DeepSeekUrl = "https://chat.deepseek.com/";
    public const int AgentUiBuild = 23;

    public static string ChatSessionUrl(string? sessionId)
    {
        var id = (sessionId ?? "").Trim();
        return string.IsNullOrEmpty(id)
            ? DeepSeekUrl
            : $"https://chat.deepseek.com/a/chat/s/{id}";
    }

    public static string AgentPageUrl =>
        $"https://ds-agent.local/index.html?build={AgentUiBuild}";

    public const string Chat2ApiAgentEmbedUrl = "https://ds-chat2api.local/index.html#/providers";

    public const string Chat2ApiConsoleUrl = "https://ds-chat2api.local/index.html";

    public static bool IsAgentPage(string? source) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains("ds-agent.local", StringComparison.OrdinalIgnoreCase) &&
        !IsChat2ApiAgentPage(source);

    public static bool IsChat2ApiAgentPage(string? source) =>
        !string.IsNullOrEmpty(source) &&
        (source.Contains("ds-chat2api.local", StringComparison.OrdinalIgnoreCase) ||
         (source.Contains("ds-agent.local", StringComparison.OrdinalIgnoreCase) &&
          source.Contains("/chat2api/", StringComparison.OrdinalIgnoreCase)));
}
