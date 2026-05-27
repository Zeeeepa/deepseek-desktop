using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc.Handlers;

public sealed class ProvidersGetAllHandler : IIpcHandler
{
    public string Channel => "providers:getAll";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetProviders());
}

public sealed class ProvidersGetBuiltinHandler : IIpcHandler
{
    public string Channel => "providers:getBuiltin";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetBuiltinProviders());
}

public sealed class ProvidersCheckAllStatusHandler : IIpcHandler
{
    public string Channel => "providers:checkAllStatus";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.CheckAllProviderStatus());
}
