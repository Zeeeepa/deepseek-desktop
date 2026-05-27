namespace DeepSeekBrowser.Services;

public static class AppNavigation
{
    public const string DeepSeekUrl = "https://chat.deepseek.com/";
    /// <summary>主窗口标题（与 DeepSeek 官网首页一致，不随会话页 document.title 变化）。</summary>
    public const string BrandWindowTitle = "deepseek 探索未至之境";
    /// <summary>内嵌 Agent / API 管理 UI 构建号；每次改 UI 资源后递增，启动时自动清 WebView 缓存。</summary>
    public const int EmbeddedUiBuild = 42;

    public static string ChatSessionUrl(string? sessionId)
    {
        var id = (sessionId ?? "").Trim();
        return string.IsNullOrEmpty(id)
            ? DeepSeekUrl
            : $"https://chat.deepseek.com/a/chat/s/{id}";
    }

    public static string AgentPageUrl =>
        $"https://ds-agent.local/index.html?build={EmbeddedUiBuild}";

    public static string EmbeddedApiManagementUrl =>
        $"https://dsdp-api.local/index.html?build={EmbeddedUiBuild}";

    public static bool IsAgentPage(string? source) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains("ds-agent.local", StringComparison.OrdinalIgnoreCase) &&
        !IsEmbeddedApiManagementPage(source);

    public static bool IsEmbeddedApiManagementPage(string? source) =>
        !string.IsNullOrEmpty(source) &&
        (source.Contains("dsdp-api.local", StringComparison.OrdinalIgnoreCase) ||
         (source.Contains("ds-agent.local", StringComparison.OrdinalIgnoreCase) &&
          source.Contains("/dsd-api/", StringComparison.OrdinalIgnoreCase)));
}
