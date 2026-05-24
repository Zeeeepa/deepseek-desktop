using System.IO;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.DeepSeekTui;

/// <summary>通过 DeepSeek-TUI 运行时 HTTP API 执行 Agent 回合。</summary>
public sealed class DeepSeekTuiAgentRunner
{
    private readonly DeepSeekTuiHost _host;
    private readonly Func<string, string, Task<bool>> _requestApprovalAsync;

    public DeepSeekTuiAgentRunner(
        DeepSeekTuiHost host,
        Func<string, string, Task<bool>> requestApprovalAsync)
    {
        _host = host;
        _requestApprovalAsync = requestApprovalAsync;
    }

    public async Task<DeepSeekTuiRunResult> RunAsync(
        AppConfig config,
        string prompt,
        string strategy,
        string? existingThreadId,
        Action<string> onLog,
        Action<string>? onAnswerDelta,
        CancellationToken ct,
        Action<AgentUiActivity>? onActivity = null,
        Action<string, bool>? onThinking = null,
        ulong eventSinceSeq = 0)
    {
        DeepSeekTuiConfigSync.Apply(config);
        await _host.EnsureRunningAsync(config, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(_host.RuntimeBearerToken))
            throw new InvalidOperationException(
                "DeepSeek-TUI Runtime 鉴权 Token 未就绪。请完全退出并重启 DeepSeek 桌面端。");

        var workspace = AgentWorkspace.ResolveRoot(config);
        var mode = string.Equals(strategy, AgentStrategies.Plan, StringComparison.OrdinalIgnoreCase)
            ? "plan"
            : "agent";
        var model = "deepseek-v4-pro";
        var autoApprove = string.Equals(config.AgentApprovalMode, "never", StringComparison.OrdinalIgnoreCase);
        var allowShell = config.AgentAllowShell;

        var client = new DeepSeekTuiRuntimeClient(_host.BaseUrl, _host.RuntimeBearerToken);

        var threadId = string.IsNullOrWhiteSpace(existingThreadId)
            ? await CreateThreadWithAuthRetryAsync(
                client, config, workspace, mode, model, autoApprove, allowShell, onLog, ct).ConfigureAwait(false)
            : existingThreadId;

        AgentDebugLogger.Current?.Write("TUI", $"StartTurn thread={threadId} since_seq={eventSinceSeq}");
        var startResult = await StartTurnWithRecoveryAsync(
            client, threadId, config, workspace, mode, model, autoApprove, allowShell, prompt, onLog, ct)
            .ConfigureAwait(false);
        threadId = startResult.ThreadId;
        var activeTurnId = startResult.ActiveTurnId;
        AgentDebugLogger.Current?.Write("TUI", $"Turn started id={activeTurnId}");

        var answer = new StringBuilder();
        var eventState = new TurnEventState();
        var turnDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportedTools = new HashSet<string>(StringComparer.Ordinal);
        var maxEventSeq = eventSinceSeq;
        Exception? streamError = null;
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var interruptOnCancel = ct.Register(() =>
        {
            if (string.IsNullOrWhiteSpace(activeTurnId))
                return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.InterruptTurnAsync(threadId, activeTurnId, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            });
        });

        var streamTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in client.StreamEventsAsync(threadId, eventSinceSeq, streamCts.Token)
                                   .ConfigureAwait(false))
                {
                    var seq = DeepSeekTuiTurnEventFilter.GetEventSeq(ev.Payload, ev.Seq);
                    if (seq > maxEventSeq)
                        maxEventSeq = seq;

                    if (!DeepSeekTuiTurnEventFilter.ShouldProcess(ev.Name, ev.Payload, activeTurnId, ev.TurnId))
                        continue;

                    await HandleEventAsync(
                            client, ev, workspace, answer, eventState, reportedTools, onLog, onAnswerDelta,
                            onActivity, onThinking, activeTurnId, ev.TurnId,
                            streamCts.Token)
                        .ConfigureAwait(false);

                    if (DeepSeekTuiTurnEventFilter.IsTurnTerminal(ev.Name, ev.Payload, activeTurnId, ev.TurnId))
                        turnDone.TrySetResult();
                }
            }
            catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
            {
                // turn finished; stop reading long-lived thread stream
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                streamError = ex;
                turnDone.TrySetResult();
            }
        }, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            await turnDone.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            onLog("已停止。");
            throw;
        }
        catch (OperationCanceledException)
        {
            onLog("回合超时。");
        }

        if (streamError is not null)
            throw streamError;

        streamCts.Cancel();
        try
        {
            await streamTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected after turn completion
        }

        var text = answer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(eventState.TurnFailure))
            text = eventState.TurnFailure.Trim();
        return new DeepSeekTuiRunResult(
            threadId,
            string.IsNullOrWhiteSpace(text) ? "未能生成回复，请重试。" : text,
            maxEventSeq);
    }

    private sealed record StartTurnResult(string ThreadId, string ActiveTurnId);

    private async Task<StartTurnResult> StartTurnWithRecoveryAsync(
        DeepSeekTuiRuntimeClient client,
        string threadId,
        AppConfig config,
        string workspace,
        string mode,
        string model,
        bool autoApprove,
        bool allowShell,
        string prompt,
        Action<string> onLog,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var activeTurnId = await client.StartTurnAsync(threadId, prompt, ct).ConfigureAwait(false);
                return new StartTurnResult(threadId, activeTurnId);
            }
            catch (InvalidOperationException ex) when (DeepSeekTuiTurnEventFilter.IsActiveTurnConflict(ex))
            {
                if (attempt == 0)
                {
                    onLog("正在结束上一轮未完成回合…");
                    if (await TryInterruptLatestTurnAsync(client, threadId, ct).ConfigureAwait(false))
                    {
                        await Task.Delay(500, ct).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }

                throw;
            }
        }

        throw new InvalidOperationException("无法在当前 DeepSeek-TUI 线程上开始新回合。");
    }

    private static async Task<bool> TryInterruptLatestTurnAsync(
        DeepSeekTuiRuntimeClient client,
        string threadId,
        CancellationToken ct)
    {
        try
        {
            var thread = await client.GetThreadAsync(threadId, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(thread?.LatestTurnId))
                return false;

            await client.InterruptTurnAsync(threadId, thread.LatestTurnId, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> CreateThreadWithAuthRetryAsync(
        DeepSeekTuiRuntimeClient client,
        AppConfig config,
        string workspace,
        string mode,
        string model,
        bool autoApprove,
        bool allowShell,
        Action<string> onLog,
        CancellationToken ct)
    {
        try
        {
            return await client.CreateThreadAsync(workspace, mode, model, autoApprove, allowShell, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsRuntimeAuthFailure(ex))
        {
            onLog("Runtime 鉴权失败，正在重启 DeepSeek-TUI 并同步 Token…");
            await _host.EnsureRunningAsync(config, ct).ConfigureAwait(false);
            var retry = new DeepSeekTuiRuntimeClient(_host.BaseUrl, _host.RuntimeBearerToken);
            return await retry.CreateThreadAsync(workspace, mode, model, autoApprove, allowShell, ct)
                .ConfigureAwait(false);
        }
    }

    private static bool IsRuntimeAuthFailure(Exception ex) =>
        ex.Message.Contains("401", StringComparison.Ordinal) &&
        ex.Message.Contains("bearer", StringComparison.OrdinalIgnoreCase);

    private sealed class TurnEventState
    {
        public string? TurnFailure { get; set; }
    }

    private async Task HandleEventAsync(
        DeepSeekTuiRuntimeClient client,
        RuntimeSseEvent ev,
        string workspace,
        StringBuilder answer,
        TurnEventState eventState,
        HashSet<string> reportedTools,
        Action<string> onLog,
        Action<string>? onAnswerDelta,
        Action<AgentUiActivity>? onActivity,
        Action<string, bool>? onThinking,
        string activeTurnId,
        string? envelopeTurnId,
        CancellationToken ct)
    {
        var dbg = AgentDebugLogger.Current;
        switch (ev.Name)
        {
            case "item.started":
            case "item.completed":
            {
                var itemKind = GetPayloadString(ev.Payload, "kind");
                dbg?.LogSseEvent(ev.Name, itemKind, GetPayloadString(ev.Payload, "status"));
                if (itemKind is "tool_call" or "command_execution" or "file_change")
                    TryEmitToolActivity(ev.Payload, workspace, reportedTools, onActivity);
                break;
            }
            case "item.delta":
            {
                var delta = GetPayloadString(ev.Payload, "delta");
                var kind = GetPayloadString(ev.Payload, "kind");
                if (string.IsNullOrEmpty(delta))
                    break;

                if (DeepSeekTuiItemDeltaKind.IsReasoning(kind))
                {
                    onThinking?.Invoke(delta, true);
                    break;
                }

                if (DeepSeekTuiItemDeltaKind.IsAnswer(kind))
                {
                    if (delta.Length > 0 && answer.Length == 0)
                        dbg?.Write("TUI", "首条 answer delta 到达");
                    answer.Append(delta);
                    onAnswerDelta?.Invoke(delta);
                }
                else
                {
                    dbg?.LogSseEvent("item.delta", kind, $"+{delta.Length} chars");
                }

                break;
            }
            case "turn.completed":
                dbg?.LogSseEvent(
                    ev.Name,
                    GetPayloadString(ev.Payload, "status"),
                    GetPayloadString(ev.Payload, "phase"));
                break;
            case "approval.required":
            {
                var approvalId = GetPayloadString(ev.Payload, "approval_id")
                                 ?? GetPayloadString(ev.Payload, "id");
                var tool = GetPayloadString(ev.Payload, "tool_name") ?? "tool";
                var desc = GetPayloadString(ev.Payload, "description") ?? "";
                if (string.IsNullOrWhiteSpace(approvalId))
                    break;

                onActivity?.Invoke(new AgentUiActivity("Awaiting approval", tool, desc));
                var allowed = await _requestApprovalAsync(tool, desc).ConfigureAwait(false);
                await client.DecideApprovalAsync(approvalId, allowed, ct).ConfigureAwait(false);
                onActivity?.Invoke(new AgentUiActivity(allowed ? "Approved" : "Denied", tool, null));
                break;
            }
            case "item.failed":
            {
                if (!DeepSeekTuiTurnEventFilter.ShouldProcess(ev.Name, ev.Payload, activeTurnId, envelopeTurnId))
                    break;

                var failKind = GetPayloadString(ev.Payload, "kind");
                dbg?.LogSseEvent(
                    ev.Name,
                    failKind,
                    GetPayloadString(ev.Payload, "status"));
                var err = ResolveFailureMessage(ev.Payload);
                dbg?.Write(
                    "TUI-ERR",
                    $"kind={failKind ?? "?"} answerLen={answer.Length} err={err ?? "(empty)"}");
                if (!string.IsNullOrWhiteSpace(err))
                {
                    onLog("错误: " + err);
                    eventState.TurnFailure = err;
                    if (answer.Length == 0)
                        onAnswerDelta?.Invoke(err);
                }
                else
                {
                    eventState.TurnFailure = "模型生成失败（无正文回复）";
                }

                break;
            }
            case "turn.lifecycle":
            {
                dbg?.LogSseEvent(
                    ev.Name,
                    GetPayloadString(ev.Payload, "status"),
                    GetPayloadString(ev.Payload, "phase"));
                var err = ResolveFailureMessage(ev.Payload);
                if (!string.IsNullOrWhiteSpace(err))
                {
                    dbg?.Write("TUI-ERR", err);
                    onLog("错误: " + err);
                }

                break;
            }
            default:
                dbg?.LogSseEvent(ev.Name, GetPayloadString(ev.Payload, "kind"));
                break;
        }
    }

    private static string? ResolveFailureMessage(JsonElement payload)
    {
        var err = GetPayloadString(payload, "error")
                  ?? GetPayloadString(payload, "message")
                  ?? GetPayloadString(payload, "error_summary")
                  ?? GetNestedString(payload, "error_message", "detail", "reason");
        return string.IsNullOrWhiteSpace(err) ? null : err.Trim();
    }

    private static void TryEmitToolActivity(
        JsonElement payload,
        string workspace,
        HashSet<string> reportedTools,
        Action<AgentUiActivity>? onActivity)
    {
        if (onActivity is null)
            return;

        var tool = GetNestedString(payload, "tool_name", "name", "tool");
        if (string.IsNullOrWhiteSpace(tool))
            return;

        var path = GetNestedString(payload, "path", "file", "file_path", "target", "directory");
        var pattern = GetNestedString(payload, "pattern", "query", "regex", "glob_pattern");
        var command = GetNestedString(payload, "command", "cmd", "script");
        var startLine = GetNestedString(payload, "start_line", "line", "offset");
        var endLine = GetNestedString(payload, "end_line", "limit");

        var key = tool + "|" + (path ?? pattern ?? command ?? "");
        if (!reportedTools.Add(key))
            return;

        var activity = FormatToolActivity(tool, path, pattern, command, startLine, endLine, workspace);
        onActivity(activity);
    }

    internal static AgentUiActivity FormatToolActivity(
        string tool,
        string? path,
        string? pattern,
        string? command,
        string? startLine,
        string? endLine,
        string workspace)
    {
        var name = tool.Trim();
        var rel = RelativizePath(path, workspace);
        var detail = FormatLineRange(startLine, endLine);

        if (name.Contains("read", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "read_file", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Read", WrapCode(rel ?? path ?? name), detail);

        if (name.Contains("write", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("edit", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("patch", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Edited", WrapCode(rel ?? path ?? name), detail);

        if (name.Contains("grep", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rg", StringComparison.OrdinalIgnoreCase))
        {
            var target = pattern is not null
                ? $"`{pattern}`" + (rel is not null ? $" in `{rel}`" : "")
                : rel ?? name;
            return new AgentUiActivity("Grepped", target, null);
        }

        if (name.Contains("list", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "list_dir", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Listed", WrapCode(rel ?? path ?? name), null);

        if (name.Contains("glob", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Searched", pattern ?? rel ?? name, null);

        if (name.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("exec", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bash", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            var preview = command is not null ? Truncate(command, 72) : name;
            return new AgentUiActivity("Ran", preview, "terminal");
        }

        if (name.Contains("fetch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("http", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Fetched", path ?? pattern ?? name, null);

        return new AgentUiActivity("Ran", name, rel);
    }

    private static string? RelativizePath(string? path, string workspace)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetFullPath(workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var rel = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(rel) ? Path.GetFileName(full) : rel.Replace('\\', '/');
            }

            return full.Replace('\\', '/');
        }
        catch
        {
            return path.Trim().Replace('\\', '/');
        }
    }

    private static string? FormatLineRange(string? start, string? end)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;
        if (!string.IsNullOrWhiteSpace(end) && end != start)
            return $"L{start}-{end}";
        return $"L{start}";
    }

    private static string? GetNestedString(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            var v = GetPayloadString(payload, name);
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        if (payload.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                var v = GetPayloadString(args, name);
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
        }

        if (payload.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                var v = GetPayloadString(input, name);
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
        }

        if (payload.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                var v = GetPayloadString(meta, name);
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
        }

        return null;
    }

    private static string? GetPayloadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;
        if (!payload.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.GetRawText()
        };
    }

    private static string WrapCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var v = value.Trim();
        return v.StartsWith('`') ? v : "`" + v + "`";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

public sealed record DeepSeekTuiRunResult(string ThreadId, string Answer, ulong LastEventSeq = 0);
