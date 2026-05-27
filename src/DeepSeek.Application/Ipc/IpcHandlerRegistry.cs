using DeepSeekBrowser.AppLayer.Ipc.Handlers;

namespace DeepSeekBrowser.AppLayer.Ipc;

internal static class IpcHandlerRegistry
{
    public static IReadOnlyList<IIpcHandler> All { get; } =
    [
        new ConfigGetHandler(),
        new ConfigUpdateHandler(),
        new ProvidersGetAllHandler(),
        new ProvidersGetBuiltinHandler(),
        new ProvidersCheckAllStatusHandler(),
        new AccountsGetAllHandler(),
        new SessionGetConfigHandler(),
        new SessionUpdateConfigHandler(),
        new ContextManagementGetConfigHandler(),
        new ContextManagementUpdateConfigHandler(),
        new StatisticsGetHandler(),
        new StatisticsGetTodayHandler(),
        new LogsGetStatsHandler(),
        new RequestLogsGetStatsHandler(),
        new ManagementApiGetConfigHandler(),
    ];
}
