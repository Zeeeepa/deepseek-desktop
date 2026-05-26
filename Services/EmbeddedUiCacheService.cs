using System.IO;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 内嵌 UI（Agent / API 管理）随发布版本自动失效 WebView2 磁盘缓存，避免像网页一样手动强刷。
/// </summary>
public static class EmbeddedUiCacheService
{
    private static readonly string BuildMarkerPath =
        Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "embedded-ui-build.txt");

    private const CoreWebView2BrowsingDataKinds EmbeddedUiCacheKinds =
        CoreWebView2BrowsingDataKinds.FileSystems
        | CoreWebView2BrowsingDataKinds.IndexedDb
        | CoreWebView2BrowsingDataKinds.LocalStorage
        | CoreWebView2BrowsingDataKinds.WebSql
        | CoreWebView2BrowsingDataKinds.CacheStorage
        | CoreWebView2BrowsingDataKinds.DiskCache
        | CoreWebView2BrowsingDataKinds.ServiceWorkers;

    public static async Task EnsureFreshUiAsync(CoreWebView2 core, CancellationToken ct = default)
    {
        try
        {
            var current = AppNavigation.EmbeddedUiBuild.ToString();
            var previous = File.Exists(BuildMarkerPath)
                ? (await File.ReadAllTextAsync(BuildMarkerPath, ct).ConfigureAwait(false)).Trim()
                : "";

            if (string.Equals(previous, current, StringComparison.Ordinal))
                return;

            try
            {
                await core.Profile.ClearBrowsingDataAsync(EmbeddedUiCacheKinds).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NotImplementedException or InvalidCastException)
            {
                // 系统 WebView2 运行时过旧，不支持 Profile2；跳过清缓存，应用仍可启动。
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BuildMarkerPath)!);
            await File.WriteAllTextAsync(BuildMarkerPath, current, ct).ConfigureAwait(false);
        }
        catch
        {
            // 缓存刷新失败不应阻止主窗口启动。
        }
    }
}
