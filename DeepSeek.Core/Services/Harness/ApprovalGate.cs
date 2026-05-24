using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness;

public sealed class ApprovalGate
{
    private readonly AppConfig _config;
    private readonly Func<string, string, Task<bool>> _requestApproval;

    public ApprovalGate(AppConfig config, Func<string, string, Task<bool>> requestApproval)
    {
        _config = config;
        _requestApproval = requestApproval;
    }

    public async Task<bool> AllowToolAsync(string toolName, string detail, HarnessPhase phase, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (HarnessPhasePolicy.IsReadonlyPhase(phase))
            return IsReadonlyTool(toolName);

        if (IsShellTool(toolName) && !_config.AgentAllowShell)
            return false;

        var mode = (_config.AgentApprovalMode ?? "smart").Trim().ToLowerInvariant();
        if (mode is "never")
            return true;
        if (mode is "always" or "readonly" && IsWriteOrShell(toolName))
            return await _requestApproval(toolName, detail);
        if (mode is "smart")
        {
            if (IsWriteOrShell(toolName))
                return await _requestApproval(toolName, detail);
            return true;
        }

        return await _requestApproval(toolName, detail);
    }

    private static bool IsShellTool(string toolName)
    {
        var n = toolName.ToLowerInvariant();
        return n.Contains("shell") || n.Contains("run_shell") || n.Contains("exec");
    }

    private bool IsWriteOrShell(string toolName)
    {
        var n = toolName.ToLowerInvariant();
        if (IsShellTool(n)) return true;
        return n.Contains("write") || n.Contains("edit");
    }

    public bool IsReadonlyTool(string toolName)
    {
        var n = toolName.ToLowerInvariant();
        if (n.Contains("write") || n.Contains("edit") || n.Contains("shell") || n.Contains("run_shell"))
            return false;
        return true;
    }
}
