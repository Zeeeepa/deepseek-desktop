namespace DeepSeekBrowser.Services.OAuth;

/// <summary>
/// 对齐 Chat2API-main <c>tokenExtractionConfig.ts</c> 的供应商 OAuth 抓取配置。
/// </summary>
public static class DsdOAuthTokenExtraction
{
    public enum TokenSourceKind
    {
        LocalStorage,
        Cookie,
        NetworkHeader
    }

    public sealed record TokenSource(TokenSourceKind Kind, string Key, string? HeaderPattern = null);

    public sealed record Config(
        string LoginUrl,
        IReadOnlyList<TokenSource> TokenSources,
        IReadOnlyList<string> TargetDomains,
        string WindowTitle);

    private static readonly Dictionary<string, Config> Configs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kimi"] = new(
            "https://www.kimi.com",
            [new(TokenSourceKind.NetworkHeader, "token", "^Bearer\\s+(.+)$")],
            [".kimi.com", "kimi.com"],
            "Kimi 登录 — DeepSeek Desktop"),
        ["deepseek"] = new(
            "https://chat.deepseek.com",
            [new(TokenSourceKind.LocalStorage, "userToken")],
            [".deepseek.com", "deepseek.com"],
            "DeepSeek 登录"),
        ["glm"] = new(
            "https://chatglm.cn",
            [new(TokenSourceKind.Cookie, "chatglm_refresh_token")],
            [".chatglm.cn", "chatglm.cn"],
            "GLM 登录 — DeepSeek Desktop"),
        ["qwen"] = new(
            "https://www.qianwen.com",
            [new(TokenSourceKind.Cookie, "tongyi_sso_ticket")],
            [".qianwen.com", "qianwen.com"],
            "通义千问登录 — DeepSeek Desktop"),
        ["minimax"] = new(
            "https://agent.minimaxi.com",
            [
                new(TokenSourceKind.LocalStorage, "_token"),
                new(TokenSourceKind.LocalStorage, "user_detail_agent")
            ],
            [".minimaxi.com", "minimaxi.com"],
            "MiniMax 登录 — DeepSeek Desktop"),
        ["zai"] = new(
            "https://chat.z.ai",
            [
                new(TokenSourceKind.LocalStorage, "token"),
                new(TokenSourceKind.Cookie, "token")
            ],
            [".z.ai", "z.ai", "chat.z.ai"],
            "Z.ai 登录 — DeepSeek Desktop"),
        ["mimo"] = new(
            "https://aistudio.xiaomimimo.com",
            [
                new(TokenSourceKind.Cookie, "serviceToken"),
                new(TokenSourceKind.Cookie, "userId"),
                new(TokenSourceKind.Cookie, "xiaomichatbot_ph")
            ],
            [".xiaomimimo.com", "xiaomimimo.com"],
            "MiMo 登录 — DeepSeek Desktop"),
        ["qwen-ai"] = new(
            "https://chat.qwen.ai",
            [
                new(TokenSourceKind.LocalStorage, "token"),
                new(TokenSourceKind.Cookie, "token")
            ],
            [".qwen.ai", "qwen.ai", "chat.qwen.ai"],
            "Qwen AI 登录 — DeepSeek Desktop"),
        ["perplexity"] = new(
            "https://www.perplexity.ai",
            [
                new(TokenSourceKind.Cookie, "__Secure-next-auth.session-token"),
                new(TokenSourceKind.Cookie, "next-auth.session-token")
            ],
            [".perplexity.ai", "perplexity.ai"],
            "Perplexity 登录 — DeepSeek Desktop")
    };

    public static Config? GetConfig(string providerId) =>
        Configs.TryGetValue(providerId.Trim(), out var cfg) ? cfg : null;
}
