using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessToolExecutor
{
    private readonly McpHub _mcp;
    private readonly BuiltinToolExecutor _builtin = new();
    private readonly ApprovalGate _approval;
    private readonly HarnessTrace _trace;

    public HarnessToolExecutor(McpHub mcp, ApprovalGate approval, HarnessTrace trace)
    {
        _mcp = mcp;
        _approval = approval;
        _trace = trace;
    }

    public async Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        AppConfig config,
        string workspaceRoot,
        HarnessPhase phase,
        CancellationToken ct,
        HarnessSandboxCoordinator? sandboxCoordinator = null)
    {
        if (HarnessPhasePolicy.IsReadonlyPhase(phase) && !_approval.IsReadonlyTool(toolName))
            return "ERROR: Phase " + HarnessPhasePolicy.TraceLabel(phase) + " 不允许调用工具: " + toolName;

        var detail = toolName + " " + TruncateArgs(argumentsJson);
        if (!await _approval.AllowToolAsync(toolName, detail, phase, ct))
            return "ERROR: 用户拒绝执行工具";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string result;
            if (BuiltinToolExecutor.IsBuiltin(toolName))
            {
                IHarnessSandbox? sandbox = null;
                if (sandboxCoordinator is not null)
                    sandbox = await sandboxCoordinator.EnsureInitializedAsync(ct);

                result = await _builtin.ExecuteAsync(
                    toolName, argumentsJson, workspaceRoot, config.AgentAllowShell, ct, sandbox);
            }
            else
            {
                var exposed = _mcp.ResolveExposedToolName(toolName);
                result = await _mcp.CallToolAsync(exposed, argumentsJson, ct);
            }

            _trace.Tool(toolName, sw.ElapsedMilliseconds, result.StartsWith("ERROR:", StringComparison.Ordinal));
            return result;
        }
        catch (Exception ex)
        {
            _trace.Tool(toolName, sw.ElapsedMilliseconds, true);
            return "ERROR: " + ex.Message;
        }
    }

    private static string TruncateArgs(string json) =>
        json.Length <= 200 ? json : json[..200] + "…";
}
