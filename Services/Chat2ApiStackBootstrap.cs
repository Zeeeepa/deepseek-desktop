using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.DeepSeekTui;

namespace DeepSeekBrowser.Services;

/// <summary>网页登录后：启动 Chat2API、同步 DeepSeek-TUI 配置并预热运行时。</summary>
public static class Chat2ApiStackBootstrap
{
    public static async Task OnWebLoginAsync(
        AppConfig config,
        LocalOpenAiServer localApi,
        DeepSeekTuiHost? tuiHost,
        WebInjectService web,
        CancellationToken ct = default)
    {
        Chat2ApiCompat.EnsureDefaultMappings(config);
        localApi.UpdateConfig(config);
        localApi.Start();

        if (!string.IsNullOrWhiteSpace(config.WebUserToken))
            await web.SyncApiBridgeTokenAsync(config.WebUserToken).ConfigureAwait(false);

        try
        {
            await web.EnsureApiBridgeReadyAsync().ConfigureAwait(false);
        }
        catch
        {
            // 桥接页未就绪时稍后重试
        }

        Chat2ApiHealth? health = null;
        try
        {
            health = await web.ProbeChat2ApiHealthAsync(config.WebUserToken, localApi.BaseUrl, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        Chat2ApiProviderService.WriteIntegrationFile(config, health);

        if (tuiHost is null)
            return;

        try
        {
            await DeepSeekTuiBundle.EnsureBinariesAsync(config, ct).ConfigureAwait(false);
            DeepSeekTuiConfigSync.Apply(config);
            await tuiHost.EnsureRunningAsync(config, ct).ConfigureAwait(false);
        }
        catch
        {
            // Agent 发消息时会再次尝试
        }
    }
}
