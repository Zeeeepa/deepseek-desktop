namespace DeepSeekBrowser.Services.Harness.Sandbox;

public sealed class LocalWorkspaceSandboxProvider : IHarnessSandboxProvider
{
    private readonly Dictionary<string, LocalWorkspaceSandbox> _boxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public HarnessSandboxKind Kind => HarnessSandboxKind.Local;

    public Task<IHarnessSandbox> AcquireAsync(string sessionId, string workspaceRoot, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_boxes.TryGetValue(sessionId, out var existing))
                return Task.FromResult<IHarnessSandbox>(existing);

            var box = new LocalWorkspaceSandbox(sessionId, workspaceRoot);
            _boxes[sessionId] = box;
            return Task.FromResult<IHarnessSandbox>(box);
        }
    }

    public IHarnessSandbox? TryGet(string sessionId)
    {
        lock (_gate)
            return _boxes.TryGetValue(sessionId, out var box) ? box : null;
    }

    public void Release(string sessionId)
    {
        lock (_gate)
        {
            if (_boxes.Remove(sessionId, out var box))
                box.Dispose();
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            foreach (var box in _boxes.Values)
                box.Dispose();
            _boxes.Clear();
        }
    }
}
