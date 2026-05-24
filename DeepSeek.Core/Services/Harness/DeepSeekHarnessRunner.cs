namespace DeepSeekBrowser.Services.Harness;

public sealed class DeepSeekHarnessRunner : IHarnessRunner
{
    private readonly IAgentWebChat _chat;
    private readonly McpHub _mcp;
    private readonly Func<string, string, Task<bool>> _requestApproval;

    public DeepSeekHarnessRunner(
        IAgentWebChat chat,
        McpHub mcp,
        Func<string, string, Task<bool>> requestApproval)
    {
        _chat = chat;
        _mcp = mcp;
        _requestApproval = requestApproval;
    }

    public Task<HarnessRunResult> RunAsync(
        HarnessRunRequest request,
        HarnessRunCallbacks callbacks,
        CancellationToken ct)
    {
        var approval = new ApprovalGate(request.Config, _requestApproval);
        var orchestrator = new HarnessOrchestrator(_chat, _mcp, approval);
        return orchestrator.RunAsync(request, callbacks, ct);
    }
}
