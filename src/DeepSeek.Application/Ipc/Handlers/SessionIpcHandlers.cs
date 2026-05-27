using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc.Handlers;

public sealed class SessionGetConfigHandler : IIpcHandler
{
    public string Channel => "session:getConfig";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetSessionConfig());
}

public sealed class SessionUpdateConfigHandler : IIpcHandler
{
    public string Channel => "session:updateConfig";

    public async Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        await host.UpdateSessionConfigAsync(args).ConfigureAwait(false);
}
