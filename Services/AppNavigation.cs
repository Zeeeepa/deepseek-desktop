namespace DeepSeekBrowser.Services;

public static class AppNavigation
{
    public const string DeepSeekUrl = "https://chat.deepseek.com/";
    public const int AgentUiBuild = 20;

    public static string AgentPageUrl =>
        $"https://ds-agent.local/index.html?build={AgentUiBuild}";
    public const string Chat2ApiConsoleUrl = "https://ds-chat2api.local/index.html";

    public static bool IsAgentPage(string? source) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains("ds-agent.local", StringComparison.OrdinalIgnoreCase);
}
