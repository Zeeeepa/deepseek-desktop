using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeek.Desktop.Services;

public static class DesktopEmbeddedStack
{
    public static Task EnsureLinkedAsync(
        AppConfig config,
        WinUiWebInjectService web,
        CancellationToken ct = default) =>
        EmbeddedStackBridgeLinker.LinkWebBridgeAsync(config, web, ct);
}
