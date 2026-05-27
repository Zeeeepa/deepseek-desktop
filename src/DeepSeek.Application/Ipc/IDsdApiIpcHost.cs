using System.Text.Json;

namespace DeepSeekBrowser.AppLayer.Ipc;

/// <summary>
/// IPC host surface implemented by <see cref="Services.DsdApiIpcBridge"/> (Desktop shell).
/// Handlers depend on this port �?not on WPF or WebView2.
/// </summary>
public interface IDsdApiIpcHost
{
    object BuildConfig();
    Task<object?> UpdateConfigAsync(JsonElement[] args, CancellationToken cancellationToken);

    object GetProviders();
    object GetBuiltinProviders();
    object CheckAllProviderStatus();
    object GetAccounts(JsonElement[] args);

    object GetSessionConfig();
    Task<object> UpdateSessionConfigAsync(JsonElement[] args);

    object GetContextManagementConfig();
    Task<object> UpdateContextManagementConfigAsync(JsonElement[] args, CancellationToken cancellationToken);

    object GetStatistics();
    object GetStatisticsToday();
    object GetAppLogStats();
    object GetRequestLogStats();
    object GetManagementApiConfig();
}
