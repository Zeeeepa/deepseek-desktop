using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.AppLayer.Ipc;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.OAuth;

namespace DeepSeekBrowser.Services;

/// <summary>
/// WebView2 版 DSD API 管理台 IPC 桥：将 <c>window.electronAPI</c> 调用映射到内嵌 DSD API 栈。
/// </summary>
public sealed partial class DsdApiIpcBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly LocalOpenAiServer _localApi;
    private readonly WebInjectService _web;
    private readonly Func<Window?> _owner;
    private readonly Func<AppConfig, CancellationToken, Task>? _onStackSync;
    private AppConfig _config;
    private readonly long _proxyStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly Dictionary<string, JsonElement> _uiStore = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (object Value, long ExpiresAt)> _readCache = new(StringComparer.Ordinal);
    private readonly DsdOAuthInAppLoginService _oauthLogin;
    private readonly Action<string, object?[]>? _emitIpcEvent;
    private bool _oauthBrowserLoginPending;
    private static bool _ipcEventHubHooked;

    private static readonly HashSet<string> CachedReadChannels = new(StringComparer.Ordinal)
    {
        "proxy:getStatus",
        "proxy:getStatistics",
        "config:get",
        "providers:getBuiltin",
        "providers:checkAllStatus",
        "statistics:get",
        "statistics:getToday",
        "logs:getStats",
        "requestLogs:getStats",
        "session:getConfig",
        "managementApi:getConfig",
        "contextManagement:getConfig",
        "accounts:getAll",
    };

    public DsdApiIpcBridge(
        LocalOpenAiServer localApi,
        WebInjectService web,
        Func<Window?> owner,
        Func<AppConfig, CancellationToken, Task>? onStackSync = null,
        Action<string, object?[]>? emitIpcEvent = null)
    {
        _localApi = localApi;
        _web = web;
        _owner = owner;
        _onStackSync = onStackSync;
        _emitIpcEvent = emitIpcEvent;
        _config = ConfigStore.Load();
        if (!_localApi.IsListening)
            _localApi.Start();
        _oauthLogin = new DsdOAuthInAppLoginService(owner, web, localApi);
        _ipcHost = new DsdApiIpcHostAdapter(this);
        HookIpcEventHub();
        EmitProxyStatusChanged();
    }

    private void HookIpcEventHub()
    {
        if (_emitIpcEvent is null || _ipcEventHubHooked) return;
        _ipcEventHubHooked = true;
        DsdApiIpcEventHub.Event += (channel, args) => _emitIpcEvent(channel, args);
    }

    private void EmitOAuthProgress(string status, string message) =>
        DsdApiIpcEventHub.PublishOAuthProgress(status, message);

    private void EmitProxyStatusChanged() =>
        DsdApiIpcEventHub.Publish("proxy:statusChanged", ProxyStatus());

    public void RefreshConfig(AppConfig config)
    {
        _config = config;
        _localApi.UpdateConfig(config);
        _readCache.Clear();
        _ipcRouter.ClearCache();
    }

    public async Task<object?> InvokeAsync(string channel, JsonElement[] args, CancellationToken ct = default)
    {
        if (_ipcRouter.IsCachedReadChannel(channel, args) && _ipcRouter.TryGetCached(channel, out var routerHit))
            return routerHit;

        if (CachedReadChannels.Contains(channel) && args.Length == 0 && TryGetCached(channel, out var hit))
            return hit;

        var result = await InvokeCoreAsync(channel, args, ct);

        if (_ipcRouter.IsCachedReadChannel(channel, args) && args.Length == 0 && result is not null)
            _ipcRouter.SetCached(channel, result);

        if (CachedReadChannels.Contains(channel) && args.Length == 0 && result is not null)
            SetCached(channel, result);

        return result;
    }

    private bool TryGetCached(string channel, out object? value)
    {
        value = null;
        if (!_readCache.TryGetValue(channel, out var entry)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > entry.ExpiresAt)
        {
            _readCache.Remove(channel);
            return false;
        }
        value = entry.Value;
        return true;
    }

    private void SetCached(string channel, object value) =>
        _readCache[channel] = (value, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2000);
    private async Task<object?> InvokeCoreAsync(string channel, JsonElement[] args, CancellationToken ct)
    {
        if (IsRouterDispatchPending(channel))
        {
            var dispatch = await _ipcRouter.DispatchAsync(_ipcHost, channel, args, ct).ConfigureAwait(false);
            if (dispatch.IsHandled)
                return dispatch.Value;
        }

        return await LegacyChannelDispatchAsync(channel, args, ct).ConfigureAwait(false);
    }
}
