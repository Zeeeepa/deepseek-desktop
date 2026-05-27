namespace DeepSeekBrowser.Services;

public sealed record DsdApiHealth
{
    public bool ApiListening { get; init; }
    public bool ConfigLoggedIn { get; init; }
    public bool BridgeReady { get; init; }
    public bool BridgeHasUserToken { get; init; }
    public string? BridgePage { get; init; }
    public string BaseUrl { get; init; } = "";
    public string? Error { get; init; }

    public bool CanChat => ConfigLoggedIn && BridgeReady && BridgeHasUserToken;

    public string Summary =>
        CanChat
            ? "已登录，API 管理可用"
            : !ConfigLoggedIn
                ? "未配置：请在 API 管理中添加 DeepSeek 账户并填写用户 Token"
                : !BridgeReady
                    ? "桥接未就绪：无法加载 chat.deepseek.com（检查网络/代理）"
                    : !BridgeHasUserToken
                        ? "桥接无 Token：请在 API 管理中添加 DeepSeek 账户"
                        : Error ?? "API 管理不可用";
}
