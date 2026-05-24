using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness.Sandbox;

/// <summary>全局 lazy 单例 Provider（对齐 DeerFlow get_sandbox_provider）。</summary>
public static class HarnessSandboxProviderRegistry
{
    private static readonly object Gate = new();
    private static IHarnessSandboxProvider? _provider;

    public static IHarnessSandboxProvider Get(AppConfig? config = null)
    {
        _ = config;
        if (_provider is not null)
            return _provider;

        lock (Gate)
        {
            _provider ??= new LocalWorkspaceSandboxProvider();
            return _provider;
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _provider?.Shutdown();
            _provider = null;
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            _provider?.Shutdown();
            _provider = null;
        }
    }
}
