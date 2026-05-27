using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services.ApiManagement;

public sealed class DeepSeekWebBridgeAdapter : IApiProviderAdapter
{
    public DeepSeekWebBridgeAdapter(ApiProviderEntry provider) => Provider = provider;

    public ApiProviderEntry Provider { get; }

    public string RouteMode => ApiRouteModes.EmbeddedWeb;

    public IAgentWebChat CreateChatClient(
        AppConfig config,
        IAgentWebChat? webBridge,
        ProviderAccountRecord? account = null)
    {
        if (webBridge is null)
            throw new InvalidOperationException("DeepSeek 网页桥需要 WebView 注入通道。");
        return webBridge;
    }

    public Task<ApiProviderHealth> ProbeHealthAsync(AppConfig config, CancellationToken ct = default)
    {
        var token = AccountCredentials.ResolveWebUserTokenForRoute(null, config, Provider.Id);
        var online = !string.IsNullOrWhiteSpace(token);
        return Task.FromResult(new ApiProviderHealth
        {
            Online = online,
            Message = online ? "已配置 API 账户" : "请在 API 管理中手动添加 DeepSeek 账户"
        });
    }

    public IReadOnlyList<string> ListModels(AppConfig config) =>
        DsdOpenAiCompat.ListModelIds(config).ToList();
}
