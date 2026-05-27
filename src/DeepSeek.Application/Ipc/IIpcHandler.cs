using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc;

public interface IIpcHandler
{
    string Channel { get; }

    Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken);
}
