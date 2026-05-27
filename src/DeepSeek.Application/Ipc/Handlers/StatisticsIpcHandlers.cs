using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc.Handlers;

public sealed class StatisticsGetHandler : IIpcHandler
{
    public string Channel => "statistics:get";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetStatistics());
}

public sealed class StatisticsGetTodayHandler : IIpcHandler
{
    public string Channel => "statistics:getToday";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetStatisticsToday());
}

public sealed class LogsGetStatsHandler : IIpcHandler
{
    public string Channel => "logs:getStats";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetAppLogStats());
}

public sealed class RequestLogsGetStatsHandler : IIpcHandler
{
    public string Channel => "requestLogs:getStats";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetRequestLogStats());
}

public sealed class ManagementApiGetConfigHandler : IIpcHandler
{
    public string Channel => "managementApi:getConfig";

    public Task<object?> HandleAsync(IDsdApiIpcHost host, JsonElement[] args, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(host.GetManagementApiConfig());
}
