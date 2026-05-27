using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc.Handlers;

public sealed class ConfigGetHandler : IIpcHandler
{
    public string Channel => "config:get";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.BuildConfig());
}

public sealed class ConfigUpdateHandler : IIpcHandler
{
    public string Channel => "config:update";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        host.UpdateConfigAsync(args, cancellationToken);
}
