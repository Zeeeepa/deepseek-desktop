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

    public Task<bool> AllowToolAsync(string toolName, string detail, HarnessPhase phase, CancellationToken ct) =>
        AllowToolAsync(toolName, detail, phase, ct, suppressPrompt: false);

    public async Task<bool> AllowToolAsync(
        string toolName,
        string detail,
        HarnessPhase phase,
        CancellationToken ct,
        bool suppressPrompt)
    {
        ct.ThrowIfCancellationRequested();
        if (HarnessPhasePolicy.IsReadonlyPhase(phase))
            return IsReadonlyTool(toolName);

        if (IsShellTool(toolName) && !_config.AgentAllowShell)
            return false;

        if (suppressPrompt)
            return true;

        var workspace = _config.AgentWorkspaceRoot;
        if (!string.IsNullOrWhiteSpace(workspace)
            && HarnessApprovalRulesStore.IsPreApproved(workspace, toolName, detail))
            return true;

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
        var n = NormalizeToolName(toolName);
        if (n is "read" or "read_file" or "list_dir" or "grep" or "glob"
            or "image_analyze" or "delegate_agent" or "askuserquestion" or "updateplan" or "websearch")
            return true;
        if (n.Contains("write") || n.Contains("edit") || n is "bash" or "run_shell")
            return false;
        if (n.StartsWith("mcp_", StringComparison.Ordinal))
            return true;
        return !IsWriteOrShell(n);
    }

    private static string NormalizeToolName(string toolName) =>
        toolName.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
}
