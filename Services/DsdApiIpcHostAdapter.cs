using System.Text.Json;
using DeepSeekBrowser.AppLayer.Ipc;

namespace DeepSeekBrowser.Services;

internal sealed class DsdApiIpcHostAdapter : IDsdApiIpcHost
{
    private readonly DsdApiIpcBridge _bridge;

    public DsdApiIpcHostAdapter(DsdApiIpcBridge bridge) => _bridge = bridge;

    public object BuildConfig() => _bridge.BuildDsdApiConfigForIpc();

    public Task<object?> UpdateConfigAsync(JsonElement[] args, CancellationToken cancellationToken) =>
        _bridge.ConfigUpdateForIpcAsync(args);

    public object GetProviders() => _bridge.GetProvidersForIpc();

    public object GetBuiltinProviders() => _bridge.GetBuiltinProvidersForIpc();

    public object CheckAllProviderStatus() => _bridge.CheckAllProviderStatusForIpc();

    public object GetAccounts(JsonElement[] args) => _bridge.GetAccountsForIpc(args);

    public object GetSessionConfig() => _bridge.SessionConfigForIpc();

    public Task<object> UpdateSessionConfigAsync(JsonElement[] args) =>
        _bridge.SessionUpdateConfigForIpcAsync(args);

    public object GetContextManagementConfig() => _bridge.ContextManagementConfigForIpc();

    public Task<object> UpdateContextManagementConfigAsync(JsonElement[] args, CancellationToken cancellationToken) =>
        _bridge.ContextManagementUpdateForIpcAsync(args, cancellationToken);

    public object GetStatistics() => _bridge.StatisticsForIpc();

    public object GetStatisticsToday() => _bridge.StatisticsTodayForIpc();

    public object GetAppLogStats() => _bridge.AppLogStatsForIpc();

    public object GetRequestLogStats() => _bridge.RequestLogStatsForIpc();

    public object GetManagementApiConfig() => _bridge.ManagementApiConfigForIpc();
}
