using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 桌面内嵌栈：网页桥接与 TUI 配置同步（模块间进程内通信，不经本地代理端口）。
/// </summary>
public static class EmbeddedStackBridgeLinker
{
    public static async Task LinkWebBridgeAsync(
        AppConfig config,
        IWebInjectBridge web,
        CancellationToken ct = default)
    {
        DsdOpenAiCompat.EnsureDefaultMappings(config);
        AgentDesktopConfigSync.Apply(config);

        var bridgeToken = AccountCredentials.ResolveWebUserTokenForRoute(null, config, "deepseek");
        if (string.IsNullOrWhiteSpace(bridgeToken))
            return;

        try
        {
            await web.SyncApiBridgeTokenAsync(bridgeToken, ct).ConfigureAwait(false);
        }
        catch
        {
            // 桥接页未就绪时稍后重试
        }

        try
        {
            await web.EnsureApiBridgeReadyAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        DsdApiHealth? health = null;
        try
        {
            health = await web.ProbeDsdApiHealthAsync(bridgeToken, InternalChatChannel.DesktopV1, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        DsdApiProviderService.WriteIntegrationFile(config, health);
    }
}
