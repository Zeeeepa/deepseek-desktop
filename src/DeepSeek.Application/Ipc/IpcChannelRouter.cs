using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc;

public sealed class IpcChannelRouter
{
    private static readonly HashSet<string> CachedReadChannels = new(StringComparer.Ordinal)
    {
        "config:get",
        "providers:getBuiltin",
        "providers:checkAllStatus",
        "session:getConfig",
        "contextManagement:getConfig",
        "accounts:getAll",
        "statistics:get",
        "statistics:getToday",
        "logs:getStats",
        "requestLogs:getStats",
        "managementApi:getConfig",
    };

    private readonly Dictionary<string, IIpcHandler> _handlers;
    private readonly Dictionary<string, (object Value, long ExpiresAt)> _readCache = new(StringComparer.Ordinal);
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(2);

    public IpcChannelRouter(IEnumerable<IIpcHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Channel, StringComparer.Ordinal);
    }

    public static IpcChannelRouter CreateDefault() => new(IpcHandlerRegistry.All);

    public bool IsCachedReadChannel(string channel, JsonElement[] args) =>
        CachedReadChannels.Contains(channel) && args.Length == 0;

    public bool TryGetCached(string channel, out object? value)
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

    public void SetCached(string channel, object value) =>
        _readCache[channel] = (value, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)_cacheTtl.TotalMilliseconds);

    public void ClearCache() => _readCache.Clear();

    public async Task<IpcDispatchResult> DispatchAsync(
        IDsdApiIpcHost host,
        string channel,
        JsonElement[] args,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(channel, out var handler))
            return IpcDispatchResult.NotHandled;

        var result = await handler.HandleAsync(host, args, cancellationToken).ConfigureAwait(false);
        return IpcDispatchResult.Handled(result);
    }
}

public readonly struct IpcDispatchResult
{
    public bool IsHandled { get; init; }
    public object? Value { get; init; }

    public static IpcDispatchResult NotHandled => default;

    public static IpcDispatchResult Handled(object? value) => new() { IsHandled = true, Value = value };
}
