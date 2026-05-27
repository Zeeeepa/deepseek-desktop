using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc.Handlers;

public sealed class ContextManagementGetConfigHandler : IIpcHandler
{
    public string Channel => "contextManagement:getConfig";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetContextManagementConfig());
}

public sealed class ContextManagementUpdateConfigHandler : IIpcHandler
{
    public string Channel => "contextManagement:updateConfig";

    public async Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        await host.UpdateContextManagementConfigAsync(args, cancellationToken).ConfigureAwait(false);
}
