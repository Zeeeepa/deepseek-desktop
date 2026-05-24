namespace DeepSeekBrowser.Services.Harness.Sandbox;

public sealed class LocalWorkspaceSandbox : IHarnessSandbox
{
    private readonly SandboxPathResolver _paths;

    public LocalWorkspaceSandbox(string sessionId, string workspaceRoot)
    {
        SessionId = sessionId;
        WorkspaceRoot = workspaceRoot;
        HarnessVirtualPathMapper.EnsureLayoutDirectories(workspaceRoot);
        _paths = new SandboxPathResolver(workspaceRoot);
    }

    public string SessionId { get; }
    public HarnessSandboxKind Kind => HarnessSandboxKind.Local;
    public string WorkspaceRoot { get; }
    public bool IsAlive => true;

    public async Task<string> ExecuteShellAsync(string command, CancellationToken ct) =>
        await BuiltinToolExecutor.RunShellOnHostAsync(command, WorkspaceRoot, ct, _paths);

    public void Dispose()
    {
        // 本地沙盒无容器生命周期
    }
}
