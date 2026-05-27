using System.Text.Json;
using DeepSeekBrowser.AppLayer.Ipc;

namespace DeepSeekBrowser.Services;

public sealed partial class DsdApiIpcBridge
{
    private readonly IpcChannelRouter _ipcRouter = IpcChannelRouter.CreateDefault();
    private readonly IDsdApiIpcHost _ipcHost;

    internal object BuildDsdApiConfigForIpc() => BuildDsdApiConfig();

    internal Task<object?> ConfigUpdateForIpcAsync(JsonElement[] args) => ConfigUpdate(args);

    internal object GetProvidersForIpc() => GetProviders();

    internal object GetBuiltinProvidersForIpc() => GetBuiltinProviders();

    internal object CheckAllProviderStatusForIpc() => CheckAllProviderStatus();

    internal object GetAccountsForIpc(JsonElement[] args) => GetAccounts(args);

    internal object SessionConfigForIpc() => SessionConfig();

    internal Task<object> SessionUpdateConfigForIpcAsync(JsonElement[] args) => SessionUpdateConfig(args);

    internal object ContextManagementConfigForIpc() => ContextManagementConfig();

    internal Task<object> ContextManagementUpdateForIpcAsync(JsonElement[] args, CancellationToken ct) =>
        ContextManagementUpdateAsync(args, ct);

    internal object StatisticsForIpc() => DsdApiRequestLogStore.Instance.BuildPersistentStatistics();

    internal object StatisticsTodayForIpc() => StatisticsGetToday();

    internal object AppLogStatsForIpc() => DsdAppLogStore.Instance.GetStats();

    internal object RequestLogStatsForIpc() => DsdApiRequestLogStore.Instance.GetStats();

    internal object ManagementApiConfigForIpc() => ManagementApiConfig();

    private bool IsRouterDispatchPending(string channel) =>
        channel is "config:get" or "config:update"
            or "providers:getAll" or "providers:getBuiltin" or "providers:checkAllStatus"
            or "accounts:getAll"
            or "session:getConfig" or "session:updateConfig"
            or "contextManagement:getConfig" or "contextManagement:updateConfig"
            or "statistics:get" or "statistics:getToday"
            or "logs:getStats" or "requestLogs:getStats"
            or "managementApi:getConfig";
}
