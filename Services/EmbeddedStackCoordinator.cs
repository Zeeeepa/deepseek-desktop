using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 桌面内嵌栈：本地对话服务 + 网页桥，启动后自动联通（Agent 走进程内 Harness）。
/// </summary>
public static class EmbeddedStackCoordinator
{
    public static async Task EnsureLinkedAsync(
        AppConfig config,
        LocalOpenAiServer localApi,
        WebInjectService web,
        CancellationToken ct = default)
    {
        Chat2ApiCompat.EnsureDefaultMappings(config);
        localApi.UpdateConfig(config);

        if (!string.IsNullOrWhiteSpace(config.WebUserToken) && config.EnableExternalOpenAiApi)
            localApi.EnsureExternalApiListening();

        await EmbeddedStackBridgeLinker.LinkWebBridgeAsync(config, web, ct)
            .ConfigureAwait(false);
    }
}
