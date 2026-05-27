using System.Text.Json;
using DeepSeekBrowser.AppLayer.Ipc;
using DeepSeekBrowser.AppLayer.Ipc.Handlers;

namespace DeepSeekBrowser.AppLayer.Tests.Ipc;

public sealed class FakeIpcHost : IDsdApiIpcHost
{
    public object BuildConfig() => new { ok = true, layer = "config" };

    public Task<object?> UpdateConfigAsync(JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(new { updated = true });

    public object GetProviders() => new[] { new { id = "deepseek", name = "DeepSeek" } };

    public object GetBuiltinProviders() => Array.Empty<object>();

    public object CheckAllProviderStatus() => new { deepseek = "online" };

    public object GetAccounts(JsonElement[] args) => Array.Empty<object>();

    public object GetSessionConfig() => new { deleteAfterTimeout = false };

    public Task<object> UpdateSessionConfigAsync(JsonElement[] args) =>
        Task.FromResult<object>(new { deleteAfterTimeout = true });

    public object GetContextManagementConfig() => new { enabled = false };

    public Task<object> UpdateContextManagementConfigAsync(JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object>(new { enabled = true });

    public object GetStatistics() => new { totalRequests = 0 };

    public object GetStatisticsToday() => new { today = 0 };

    public object GetAppLogStats() => new { total = 0 };

    public object GetRequestLogStats() => new { total = 0 };

    public object GetManagementApiConfig() => new { enabled = false };
}

public class IpcChannelRouterTests
{
    [Fact]
    public async Task Dispatches_config_get()
    {
        var router = IpcChannelRouter.CreateDefault();
        var host = new FakeIpcHost();
        var result = await router.DispatchAsync(host, "config:get", [], CancellationToken.None);
        Assert.True(result.IsHandled);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task Dispatches_providers_getAll()
    {
        var router = IpcChannelRouter.CreateDefault();
        var host = new FakeIpcHost();
        var result = await router.DispatchAsync(host, "providers:getAll", [], CancellationToken.None);
        Assert.True(result.IsHandled);
    }

    [Fact]
    public async Task Dispatches_session_updateConfig()
    {
        var router = IpcChannelRouter.CreateDefault();
        var host = new FakeIpcHost();
        var result = await router.DispatchAsync(host, "session:updateConfig", [JsonDocument.Parse("{}").RootElement], CancellationToken.None);
        Assert.True(result.IsHandled);
    }

    [Fact]
    public async Task Unknown_channel_not_handled()
    {
        var router = IpcChannelRouter.CreateDefault();
        var host = new FakeIpcHost();
        var result = await router.DispatchAsync(host, "proxy:start", [], CancellationToken.None);
        Assert.False(result.IsHandled);
    }

    [Fact]
    public async Task Dispatches_statistics_get()
    {
        var router = IpcChannelRouter.CreateDefault();
        var host = new FakeIpcHost();
        var result = await router.DispatchAsync(host, "statistics:get", [], CancellationToken.None);
        Assert.True(result.IsHandled);
    }

    [Fact]
    public async Task Dispatches_accounts_getAll()
    {
        var router = IpcChannelRouter.CreateDefault();
        var host = new FakeIpcHost();
        var result = await router.DispatchAsync(host, "accounts:getAll", [], CancellationToken.None);
        Assert.True(result.IsHandled);
    }

    [Fact]
    public void Caches_read_channels()
    {
        var router = IpcChannelRouter.CreateDefault();
        router.SetCached("session:getConfig", new { enabled = false });
        Assert.True(router.TryGetCached("session:getConfig", out var hit));
        Assert.NotNull(hit);
    }
}

public class HandlerUnitTests
{
    [Fact]
    public async Task ContextManagement_handlers_use_host()
    {
        var host = new FakeIpcHost();
        var get = new ContextManagementGetConfigHandler();
        var cfg = await get.HandleAsync(host, [], CancellationToken.None);
        Assert.NotNull(cfg);

        var update = new ContextManagementUpdateConfigHandler();
        var updated = await update.HandleAsync(host, [JsonDocument.Parse("{\"enabled\":true}").RootElement], CancellationToken.None);
        Assert.NotNull(updated);
    }
}
