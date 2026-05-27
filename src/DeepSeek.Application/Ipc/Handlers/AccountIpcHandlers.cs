using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc.Handlers;

public sealed class AccountsGetAllHandler : IIpcHandler
{
    public string Channel => "accounts:getAll";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetAccounts(args));
}
