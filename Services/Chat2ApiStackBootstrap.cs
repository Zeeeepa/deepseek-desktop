using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>网页登录后：启动内嵌栈并预热网页桥。</summary>
public static class Chat2ApiStackBootstrap
{
    public static Task OnWebLoginAsync(
        AppConfig config,
        LocalOpenAiServer localApi,
        WebInjectService web,
        CancellationToken ct = default) =>
        EmbeddedStackCoordinator.EnsureLinkedAsync(config, localApi, web, ct);
}
