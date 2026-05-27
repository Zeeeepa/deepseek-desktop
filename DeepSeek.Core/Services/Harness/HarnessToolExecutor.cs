using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Composio;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessToolExecutor
{
    private readonly McpHub _mcp;
    private readonly BuiltinToolExecutor _builtin = new();
    private readonly PermissionGate _permission;
    private readonly HarnessTrace _trace;
    private readonly IUserQuestionHandler? _userQuestions;
    private readonly HarnessFileHistory? _fileHistory;
    private readonly HarnessSubAgentService? _subAgents;
    private HarnessRunCallbacks? _runCallbacks;
    private string? _agentSessionId;
    public string? LastCheckpointHash { get; private set; }

    public HarnessToolExecutor(
        McpHub mcp,
        PermissionGate permission,
        HarnessTrace trace,
        IUserQuestionHandler? userQuestions = null,
        string? workspaceRootForHistory = null,
        HarnessSubAgentService? subAgents = null)
    {
        _mcp = mcp;
        _permission = permission;
        _trace = trace;
        _userQuestions = userQuestions;
        _subAgents = subAgents;
        if (!string.IsNullOrWhiteSpace(workspaceRootForHistory))
            _fileHistory = new HarnessFileHistory(workspaceRootForHistory);
    }

    public void SetAgentSessionId(string? sessionId) => _agentSessionId = sessionId;

    public void SetRunCallbacks(HarnessRunCallbacks? callbacks) => _runCallbacks = callbacks;

    public Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        AppConfig config,
        string workspaceRoot,
        HarnessPhase phase,
        CancellationToken ct,
        HarnessSandboxCoordinator? sandboxCoordinator = null,
        Action<string>? onShellOutput = null) =>
        ExecuteDetailedAsync(toolName, argumentsJson, config, workspaceRoot, phase, ct, sandboxCoordinator, onShellOutput)
            .ContinueWith(t => t.Result.Output, ct);

    public async Task<HarnessToolExecuteResult> ExecuteDetailedAsync(
        string toolName,
        string argumentsJson,
        AppConfig config,
        string workspaceRoot,
        HarnessPhase phase,
        CancellationToken ct,
        HarnessSandboxCoordinator? sandboxCoordinator = null,
        Action<string>? onShellOutput = null)
    {
        var normalized = BuiltinToolExecutor.NormalizeName(
            HarnessXmlToolCallParser.NormalizeToolName(toolName));
        if (HarnessPhasePolicy.IsReadonlyPhase(phase) && !IsReadonlyTool(normalized))
            return HarnessToolExecuteResult.FromOutput(
                "ERROR: Phase " + HarnessPhasePolicy.TraceLabel(phase) + " 不允许调用工具: " + toolName);

        var detail = toolName + " " + TruncateArgs(argumentsJson);
        if (!await _permission.AllowToolAsync(normalized, detail, argumentsJson, phase, ct))
            return HarnessToolExecuteResult.FromOutput("ERROR: 用户拒绝执行工具");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            HarnessToolExecuteResult result;
            if (IsSpecialTool(toolName))
            {
                var text = await ExecuteSpecialAsync(toolName, argumentsJson, config, workspaceRoot, ct);
                result = HarnessToolExecuteResult.FromOutput(text);
            }
            else if (ComposioToolBridge.IsComposioTool(toolName))
            {
                var text = await ComposioToolBridge.ExecuteAsync(config, toolName, argumentsJson, ct);
                result = HarnessToolExecuteResult.FromOutput(text);
            }
            else if (BuiltinToolExecutor.IsBuiltin(normalized))
            {
                var relPath = ExtractPath(argumentsJson);
                if (_fileHistory is not null && relPath is not null
                    && (normalized.Contains("write") || normalized.Contains("edit"))
                    && !string.IsNullOrWhiteSpace(_agentSessionId))
                {
                    LastCheckpointHash = _fileHistory.RecordCheckpoint(
                        _agentSessionId, [relPath], "tool " + normalized)
                        ?? LastCheckpointHash;
                }

                IHarnessSandbox? sandbox = null;
                if (sandboxCoordinator is not null)
                    sandbox = await sandboxCoordinator.EnsureInitializedAsync(ct);

                result = await _builtin.ExecuteDetailedAsync(
                    normalized, argumentsJson, workspaceRoot, config.AgentAllowShell, ct, sandbox, onShellOutput, config);

                if (result.PendingPatch is not null
                    && !string.IsNullOrWhiteSpace(_agentSessionId)
                    && !string.IsNullOrWhiteSpace(workspaceRoot))
                {
                    try
                    {
                        var store = new HarnessSessionStore(workspaceRoot);
                        store.SavePatchSnapshot(
                            _agentSessionId,
                            result.PendingPatch.Value.Path,
                            result.PendingPatch.Value.OriginalContent);
                    }
                    catch
                    {
                        // ignore session persist errors
                    }
                }
            }
            else
            {
                var exposed = _mcp.ResolveExposedToolName(toolName);
                var text = await _mcp.CallToolAsync(exposed, argumentsJson, ct);
                result = HarnessToolExecuteResult.FromOutput(text);
            }

            _trace.Tool(toolName, sw.ElapsedMilliseconds, result.Output.StartsWith("ERROR:", StringComparison.Ordinal));
            return result;
        }
        catch (Exception ex)
        {
            _trace.Tool(toolName, sw.ElapsedMilliseconds, true);
            return HarnessToolExecuteResult.FromOutput("ERROR: " + ex.Message);
        }
    }

    private static bool IsSpecialTool(string name) =>
        name.Equals("AskUserQuestion", StringComparison.OrdinalIgnoreCase)
        || name.Equals("UpdatePlan", StringComparison.OrdinalIgnoreCase)
        || name.Equals("WebSearch", StringComparison.OrdinalIgnoreCase)
        || name.Equals("web_search", StringComparison.OrdinalIgnoreCase)
        || name.Equals("DelegateAgent", StringComparison.OrdinalIgnoreCase)
        || name.Equals("delegate_agent", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ExecuteSpecialAsync(
        string toolName,
        string argumentsJson,
        AppConfig config,
        string workspaceRoot,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var root = doc.RootElement;

        if (toolName.Equals("AskUserQuestion", StringComparison.OrdinalIgnoreCase))
        {
            if (_userQuestions is null)
                return "ERROR: AskUserQuestion 需要 UI 支持";
            var question = root.TryGetProperty("question", out var q) ? q.GetString() ?? "" : "";
            var options = new List<UserQuestionOption>();
            if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in opts.EnumerateArray())
                {
                    options.Add(new UserQuestionOption
                    {
                        Label = opt.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                        Description = opt.TryGetProperty("description", out var d) ? d.GetString() : null
                    });
                }
            }

            if (options.Count == 0)
                options.Add(new UserQuestionOption { Label = "继续" });

            var answer = await _userQuestions.AskAsync(new UserQuestionRequest
            {
                Question = question,
                Options = options
            }, ct);
            return "User answered: " + answer;
        }

        if (toolName.Equals("UpdatePlan", StringComparison.OrdinalIgnoreCase))
        {
            var plan = root.TryGetProperty("plan", out var p) ? p.GetString() ?? "" : "";
            config.AgentSessionPlanMarkdown = plan;
            ConfigStore.Save(config);
            _runCallbacks?.OnPlanUpdated?.Invoke(plan);
            return "Plan updated.";
        }

        if (toolName.Equals("WebSearch", StringComparison.OrdinalIgnoreCase)
            || toolName.Equals("web_search", StringComparison.OrdinalIgnoreCase))
        {
            var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            return await HarnessWebSearchTool.RunAsync(config, query, ct);
        }

        if (toolName.Equals("DelegateAgent", StringComparison.OrdinalIgnoreCase)
            || toolName.Equals("delegate_agent", StringComparison.OrdinalIgnoreCase))
        {
            if (_subAgents is null)
                return "ERROR: delegate_agent 未配置";
            var req = HarnessSubAgentService.ParseDelegateArgs(root, config, _agentSessionId);
            if (string.IsNullOrWhiteSpace(req.Task))
                return "ERROR: delegate_agent 需要 task 或 prompt";
            var ctx = root.TryGetProperty("context", out var c) ? c.GetString() : null;
            req = new HarnessSubAgentRequest
            {
                Config = config,
                Role = req.Role,
                Task = req.Task,
                Context = ctx,
                ParentSessionId = _agentSessionId
            };
            var sub = await _subAgents.RunAsync(req, _runCallbacks, ct);
            return sub.Ok
                ? $"Sub-agent ({sub.Role}) result:\n{sub.Answer}"
                : "ERROR: " + (sub.Error ?? "sub-agent failed");
        }

        if (toolName.Equals("parallel_explore", StringComparison.OrdinalIgnoreCase))
        {
            if (_subAgents is null)
                return "ERROR: parallel_explore 未配置";
            if (!config.EnableParallelExplore)
                return "ERROR: parallel explore disabled in settings (EnableParallelExplore)";
            var task = root.TryGetProperty("task", out var t) ? t.GetString() ?? ""
                       : root.TryGetProperty("prompt", out var p) ? p.GetString() ?? ""
                       : "";
            if (string.IsNullOrWhiteSpace(task))
                return "ERROR: parallel_explore 需要 task 或 prompt";
            int? fanOverride = null;
            if (root.TryGetProperty("fan_out", out var fo) && fo.TryGetInt32(out var n))
                fanOverride = Math.Clamp(n, 1, Math.Clamp(config.MaxConcurrentSubAgents, 1, 10));

            var runReq = new HarnessRunRequest
            {
                Config = config,
                Prompt = task,
                Strategy = AgentStrategies.ParallelExplore,
                AgentSessionId = _agentSessionId,
                ParallelExploreFanOutOverride = fanOverride
            };
            var result = await new HarnessParallelExploreOrchestrator(_subAgents)
                .RunAsync(runReq, _runCallbacks ?? new HarnessRunCallbacks(), ct);
            return result.Answer;
        }

        return "ERROR: 未知特殊工具 " + toolName;
    }

    private static string? ExtractPath(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
            if (root.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String)
                return fp.GetString();
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string TruncateArgs(string json) =>
        json.Length <= 200 ? json : json[..200] + "…";

    private static bool IsReadonlyTool(string toolName)
    {
        var n = toolName.ToLowerInvariant();
        return n is "read_file" or "read" or "list_dir" or "grep" or "glob" or "image_analyze"
            or "delegate_agent" or "parallel_explore" or "askuserquestion" or "updateplan" or "websearch"
            || ComposioToolBridge.IsComposioTool(toolName);
    }
}
