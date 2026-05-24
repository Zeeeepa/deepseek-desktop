namespace DeepSeekBrowser.Services.Harness.Sandbox;

public interface IHarnessSandbox : IDisposable
{
    string SessionId { get; }
    HarnessSandboxKind Kind { get; }
    string WorkspaceRoot { get; }
    bool IsAlive { get; }
    Task<string> ExecuteShellAsync(string command, CancellationToken ct);
}
