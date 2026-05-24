using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services;

/// <summary>应用退出时的统一清理（MainWindow / App.OnExit 共用）。</summary>
internal static class ShutdownCoordinator
{
    private static DesktopAgentHost? _agentHost;
    private static LocalOpenAiServer? _localApi;
    private static int _cleanupDone;

    public static void Register(DesktopAgentHost agentHost, LocalOpenAiServer? localApi = null)
    {
        _agentHost = agentHost;
        _localApi = localApi;
    }

    public static void RunExitCleanup()
    {
        if (Interlocked.Exchange(ref _cleanupDone, 1) != 0)
            return;

        var host = _agentHost;
        _agentHost = null;
        if (host is not null)
        {
            try
            {
                host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // MCP 断开超时不阻止退出
            }
        }

        var api = _localApi;
        _localApi = null;
        try
        {
            api?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            HarnessSandboxProviderRegistry.Shutdown();
        }
        catch
        {
            // ignore
        }
    }
}
