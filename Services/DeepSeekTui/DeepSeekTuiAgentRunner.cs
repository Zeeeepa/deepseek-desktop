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
        CancellationToken ct)
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

        onLog($"DeepSeek-TUI · {_host.BaseUrl}");
        onLog($"工作区: {workspace}");
        onLog($"模式: {mode} · 模型: {model}");
        onLog("Runtime API 鉴权: 已配置");

        var client = new DeepSeekTuiRuntimeClient(_host.BaseUrl, _host.RuntimeBearerToken);

        var threadId = string.IsNullOrWhiteSpace(existingThreadId)
            ? await CreateThreadWithAuthRetryAsync(
                client, config, workspace, mode, model, autoApprove, allowShell, onLog, ct).ConfigureAwait(false)
            : existingThreadId;
        onLog($"线程: {threadId}");

        var answer = new StringBuilder();
        var turnDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeTurn = new ActiveTurnRef();
        Exception? streamError = null;

        using var interruptOnCancel = ct.Register(() =>
        {
            var turnId = activeTurn.Id;
            if (string.IsNullOrWhiteSpace(turnId))
                return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.InterruptTurnAsync(threadId, turnId, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignore — stream cancellation will still unwind
                }
            });
        });

        var streamTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in client.StreamEventsAsync(threadId, 0, ct).ConfigureAwait(false))
                {
                    await HandleEventAsync(client, ev, answer, onLog, onAnswerDelta, turnDone, ct)
                        .ConfigureAwait(false);
                    if (ev.Name is "turn.completed" or "turn.lifecycle")
                    {
                        var status = GetPayloadString(ev.Payload, "status");
                        if (status is "completed" or "failed" or "interrupted" or "canceled")
                            turnDone.TrySetResult();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                streamError = ex;
                turnDone.TrySetResult();
            }
        }, ct);

        await Task.Delay(150, ct).ConfigureAwait(false);
        activeTurn.Id = await client.StartTurnAsync(threadId, prompt, ct).ConfigureAwait(false);
        onLog($"回合: {activeTurn.Id}");

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

        await streamTask.ConfigureAwait(false);

        var text = answer.ToString().Trim();
        return new DeepSeekTuiRunResult(threadId, string.IsNullOrWhiteSpace(text) ? "任务已结束" : text);
    }

    private sealed class ActiveTurnRef
    {
        public string? Id;
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

    private async Task HandleEventAsync(
        DeepSeekTuiRuntimeClient client,
        RuntimeSseEvent ev,
        StringBuilder answer,
        Action<string> onLog,
        Action<string>? onAnswerDelta,
        TaskCompletionSource turnDone,
        CancellationToken ct)
    {
        switch (ev.Name)
        {
            case "turn.started":
                onLog("正努力工作…");
                break;
            case "item.started":
            {
                var itemKind = GetPayloadString(ev.Payload, "kind");
                var tool = GetPayloadString(ev.Payload, "tool_name") ?? GetPayloadString(ev.Payload, "name");
                if (itemKind is "tool_call" or "command_execution" && !string.IsNullOrWhiteSpace(tool))
                    onLog($"工具: {tool}");
                else if (itemKind is "reasoning")
                    onLog("正努力工作…");
                else if (itemKind is "agent_message" or "message")
                    onLog("正在整理回复…");
                break;
            }
            case "item.delta":
            {
                var delta = GetPayloadString(ev.Payload, "delta");
                var kind = GetPayloadString(ev.Payload, "kind");
                if (!string.IsNullOrEmpty(delta) &&
                    (kind is null or "agent_message" or "message"))
                {
                    answer.Append(delta);
                    onAnswerDelta?.Invoke(delta);
                }

                break;
            }
            case "item.completed":
            {
                var kind = GetPayloadString(ev.Payload, "kind");
                if (kind is "tool_call" or "command_execution")
                {
                    var name = GetPayloadString(ev.Payload, "tool_name")
                               ?? GetPayloadString(ev.Payload, "name")
                               ?? kind;
                    onLog($"工具: {name}");
                }

                break;
            }
            case "approval.required":
            {
                var approvalId = GetPayloadString(ev.Payload, "approval_id")
                                 ?? GetPayloadString(ev.Payload, "id");
                var tool = GetPayloadString(ev.Payload, "tool_name") ?? "tool";
                var desc = GetPayloadString(ev.Payload, "description") ?? "";
                if (string.IsNullOrWhiteSpace(approvalId))
                    break;

                onLog($"待审批: {tool}");
                var allowed = await _requestApprovalAsync(tool, desc).ConfigureAwait(false);
                await client.DecideApprovalAsync(approvalId, allowed, ct).ConfigureAwait(false);
                onLog(allowed ? "已允许" : "已拒绝");
                break;
            }
            case "item.failed":
            case "turn.lifecycle":
            {
                var err = GetPayloadString(ev.Payload, "error")
                          ?? GetPayloadString(ev.Payload, "message");
                if (!string.IsNullOrWhiteSpace(err))
                    onLog("错误: " + err);
                break;
            }
        }
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
}

public sealed record DeepSeekTuiRunResult(string ThreadId, string Answer);
