using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness.Workers;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessSubAgentService
{
    private readonly Func<IAgentWebChat> _chatFactory;
    private readonly McpHub _mcp;
    private readonly Func<string, string, Task<bool>> _requestApproval;
    private readonly Func<string, string, IReadOnlyList<string>, Task<bool>>? _scopeApproval;
    private readonly IUserQuestionHandler? _userQuestions;
    private readonly SemaphoreSlim _concurrency;
    private readonly HarnessWorkerProcessPool? _workerPool;

    public HarnessSubAgentService(
        Func<IAgentWebChat> chatFactory,
        McpHub mcp,
        Func<string, string, Task<bool>> requestApproval,
        IUserQuestionHandler? userQuestions = null,
        Func<string, string, IReadOnlyList<string>, Task<bool>>? scopeApproval = null,
        int maxConcurrent = 3,
        HarnessWorkerProcessPool? workerPool = null)
    {
        _chatFactory = chatFactory;
        _mcp = mcp;
        _requestApproval = requestApproval;
        _userQuestions = userQuestions;
        _scopeApproval = scopeApproval;
        _workerPool = workerPool;
        _concurrency = new SemaphoreSlim(Math.Clamp(maxConcurrent, 1, 10));
    }

    public async Task<IReadOnlyList<HarnessSubAgentResult>> RunParallelAsync(
        IReadOnlyList<HarnessSubAgentRequest> requests,
        HarnessRunCallbacks? parentCallbacks,
        CancellationToken ct)
    {
        if (requests.Count == 0)
            return Array.Empty<HarnessSubAgentResult>();

        if (!requests[0].Config.EnableSubAgents)
        {
            return requests.Select(_ =>
                HarnessSubAgentResult.Fail("Sub-agents are disabled in settings (EnableSubAgents).")).ToList();
        }

        parentCallbacks?.OnLog?.Invoke($"[Parallel] fan-out ×{requests.Count}");
        var tasks = requests.Select(r => RunAsync(r, parentCallbacks, ct)).ToArray();
        return await Task.WhenAll(tasks);
    }

    public async Task<HarnessSubAgentResult> RunAsync(
        HarnessSubAgentRequest request,
        HarnessRunCallbacks? parentCallbacks,
        CancellationToken ct)
    {
        if (!request.Config.EnableSubAgents)
            return HarnessSubAgentResult.Fail("Sub-agents are disabled in settings (EnableSubAgents).");

        var role = HarnessAgentRoleRegistry.Resolve(request.Role);
        var audit = new HarnessTeamAuditLog(request.ParentSessionId);
        audit.Append("subagent_start", new { role = role.Id, task = Truncate(request.Task, 500) });

        parentCallbacks?.OnLog?.Invoke($"[SubAgent:{role.DisplayName}] starting…");
        parentCallbacks?.OnActivity?.Invoke(new AgentUiActivity
        {
            Verb = "Delegate",
            Target = role.DisplayName,
            Detail = Truncate(request.Task, 120)
        });

        await _concurrency.WaitAsync(ct);
        try
        {
            if (ShouldUseWorker(request) && _workerPool is not null)
            {
                using var lease = await _workerPool.AcquireAsync(ct);
                var workerResult = await lease.RunSubAgentAsync(request, ct);
                audit.Append(workerResult.Ok ? "subagent_done" : "subagent_error",
                    new { role = role.Id, ok = workerResult.Ok, worker = true });
                return workerResult;
            }

            var chat = _chatFactory();
            var approval = new ApprovalGate(request.Config, _requestApproval);
            var permission = PermissionGate.ForSubAgentRole(request.Config, approval, role, _scopeApproval);
            var workspace = AgentWorkspace.ResolveRoot(request.Config);
            var orchestrator = new HarnessOrchestrator(chat, _mcp, permission, _userQuestions, workspace);

            var subCallbacks = new HarnessRunCallbacks
            {
                OnLog = line => parentCallbacks?.OnLog?.Invoke($"[{role.Id}] {line}"),
                OnThinking = parentCallbacks?.OnThinking,
                OnAnswerDelta = parentCallbacks?.OnAnswerDelta,
                OnActivity = parentCallbacks?.OnActivity,
                OnFilePreview = parentCallbacks?.OnFilePreview,
                OnPendingPatch = parentCallbacks?.OnPendingPatch,
                OnShellOutput = parentCallbacks?.OnShellOutput,
                OnPhaseChanged = parentCallbacks?.OnPhaseChanged
            };

            var prompt = BuildSubAgentPrompt(role, request);
            var runRequest = new HarnessRunRequest
            {
                Config = request.Config,
                Prompt = prompt,
                Strategy = AgentStrategies.Execute,
                AgentSessionId = request.ParentSessionId,
                SubAgentRole = role.Id,
                MaxStepsOverride = Math.Clamp(request.Config.MaxSubAgentSteps, 1, 50),
                RefFileIds = request.RefFileIds
            };

            var result = await orchestrator.RunAsync(runRequest, subCallbacks, ct);
            audit.Append("subagent_done", new { role = role.Id, ok = true, answerLen = result.Answer.Length });

            return new HarnessSubAgentResult
            {
                Ok = true,
                Role = role.Id,
                Answer = HarnessSubAgentResultCompressor.Compress(result.Answer, request.Config),
                AuditLogPath = audit.FilePath
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            audit.Append("subagent_error", new { role = role.Id, error = ex.Message });
            return HarnessSubAgentResult.Fail(ex.Message, role.Id);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public static HarnessSubAgentRequest ParseDelegateArgs(JsonElement root, AppConfig config, string? sessionId)
    {
        var role = root.TryGetProperty("role", out var r) ? r.GetString()
                   : root.TryGetProperty("agent_type", out var at) ? at.GetString()
                   : "general";
        var task = root.TryGetProperty("task", out var t) ? t.GetString() ?? ""
                   : root.TryGetProperty("prompt", out var p) ? p.GetString() ?? ""
                   : "";
        return new HarnessSubAgentRequest
        {
            Config = config,
            Role = role ?? "general",
            Task = task,
            ParentSessionId = sessionId
        };
    }

    private static string BuildSubAgentPrompt(HarnessAgentRoleProfile role, HarnessSubAgentRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Sub-agent assignment");
        sb.AppendLine(role.SystemPrompt);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine("## Context from lead agent");
            sb.AppendLine(request.Context);
            sb.AppendLine();
        }

        sb.AppendLine("## Task");
        sb.AppendLine(request.Task);
        return sb.ToString();
    }

    private static bool ShouldUseWorker(HarnessSubAgentRequest request) =>
        request.Config.AgentWorkerEnabled
        && (request.UseWorkerProcess
            || request.Task.Length > 1500
            || string.Equals(request.Role, "engineer", StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

public sealed class HarnessSubAgentRequest
{
    public required AppConfig Config { get; init; }
    public string Role { get; init; } = "general";
    public string Task { get; init; } = "";
    public string? Context { get; init; }
    public string? ParentSessionId { get; init; }
    public IReadOnlyList<string> RefFileIds { get; init; } = Array.Empty<string>();
    public bool UseWorkerProcess { get; init; }
}

public sealed class HarnessSubAgentResult
{
    public bool Ok { get; init; }
    public string Role { get; init; } = "";
    public string Answer { get; init; } = "";
    public string? Error { get; init; }
    public string? AuditLogPath { get; init; }

    public static HarnessSubAgentResult Fail(string error, string role = "") =>
        new() { Ok = false, Error = error, Role = role };
}
