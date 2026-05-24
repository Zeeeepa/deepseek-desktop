namespace DeepSeekBrowser.Services.Harness.Sandbox;

public interface IHarnessSandboxProvider
{
    HarnessSandboxKind Kind { get; }
    Task<IHarnessSandbox> AcquireAsync(string sessionId, string workspaceRoot, CancellationToken ct);
    IHarnessSandbox? TryGet(string sessionId);
    void Release(string sessionId);
    void Shutdown();
}
