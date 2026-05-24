using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness.Sandbox;

/// <summary>
/// DeerFlow 风格：Acquire → Inject state → Release（Middleware 生命周期）。
/// </summary>
public sealed class HarnessSandboxCoordinator : IAsyncDisposable
{
    private readonly IHarnessSandboxProvider _provider;
    private readonly HarnessTrace? _trace;
    private readonly bool _lazyInit;
    private readonly string _sessionId;
    private readonly string _workspaceRoot;
    private IHarnessSandbox? _sandbox;
    private bool _released;

    public HarnessSandboxKind Kind { get; }
    public string SessionId => _sessionId;

    private HarnessSandboxCoordinator(
        IHarnessSandboxProvider provider,
        HarnessSandboxKind kind,
        string sessionId,
        string workspaceRoot,
        bool lazyInit,
        HarnessTrace? trace,
        IHarnessSandbox? eagerSandbox)
    {
        _provider = provider;
        Kind = kind;
        _sessionId = sessionId;
        _workspaceRoot = workspaceRoot;
        _lazyInit = lazyInit;
        _trace = trace;
        _sandbox = eagerSandbox;
    }

    public static async Task<HarnessSandboxCoordinator> BeginRunAsync(
        HarnessRunState state,
        AppConfig config,
        string workspaceRoot,
        HarnessTrace? trace,
        Action<string>? onLog,
        CancellationToken ct)
    {
        _ = onLog;
        var (provider, kind) = await HarnessSandboxFactory.CreateProviderAsync(config, ct);

        var stableKey = HarnessSandboxSessionIds.ResolveStableKey(state);
        var sessionId = !string.IsNullOrWhiteSpace(stableKey)
            ? HarnessSandboxSessionIds.Deterministic(stableKey)
            : state.SandboxSessionId ?? HarnessSandboxSessionIds.Deterministic("ephemeral:" + Guid.NewGuid().ToString("N"));
        state.SandboxSessionId = sessionId;
        state.SandboxKind = kind;

        IHarnessSandbox? eager = null;
        if (!config.AgentSandboxLazyInit)
        {
            eager = await provider.AcquireAsync(sessionId, workspaceRoot, ct);
            trace?.Sandbox("acquire", $"{kind} {sessionId}");
        }
        else
        {
            trace?.Sandbox("acquire-deferred", $"{kind} {sessionId}");
        }

        return new HarnessSandboxCoordinator(
            provider, kind, sessionId, workspaceRoot, config.AgentSandboxLazyInit, trace, eager);
    }

    public async Task<IHarnessSandbox> EnsureInitializedAsync(CancellationToken ct)
    {
        if (_sandbox is not null)
            return _sandbox;

        _sandbox = await _provider.AcquireAsync(_sessionId, _workspaceRoot, ct);
        _trace?.Sandbox("acquire", $"{Kind} {_sessionId} (lazy)");
        return _sandbox;
    }

    public IHarnessSandbox? Current => _sandbox;

    public void Dispose() => Release();

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    public void Release()
    {
        if (_released) return;
        _released = true;
        _provider.Release(_sessionId);
        _sandbox = null;
        _trace?.Sandbox("release", _sessionId);
    }
}
