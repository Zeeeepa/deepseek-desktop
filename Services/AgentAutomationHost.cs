using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services;

/// <summary>后台调度与 Webhook 触发器（对标 Cursor Automations 的 schedule / webhook / GitHub / Slack）。</summary>
public sealed class AgentAutomationHost : IDisposable
{
    private readonly AgentAutomationStore _store = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _runningIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<AgentAutomation, string, string?, CancellationToken, Task<AgentAutomationRun>> _execute;
    private readonly Action<AgentAutomationRun>? _onRunUpdated;
    private readonly int _webhookPort;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Timer? _scheduleTimer;

    public AgentAutomationHost(
        AppConfig config,
        Func<AgentAutomation, string, string?, CancellationToken, Task<AgentAutomationRun>> execute,
        Action<AgentAutomationRun>? onRunUpdated = null)
    {
        _webhookPort = config.AgentAutomationsWebhookPort > 0
            ? config.AgentAutomationsWebhookPort
            : 17426;
        _execute = execute;
        _onRunUpdated = onRunUpdated;
    }

    public string WebhookBaseUrl => $"http://127.0.0.1:{_webhookPort}";

    public AgentAutomationStore Store => _store;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        StartWebhookListener();
        _scheduleTimer = new Timer(_ => _ = CheckSchedulesAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _scheduleTimer?.Dispose();
        _scheduleTimer = null;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener?.Close();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public async Task<AgentAutomationRun?> TriggerManualAsync(string automationId, string? testPayloadJson = null)
    {
        var automation = _store.Get(automationId);
        if (automation is null)
            return null;

        return await EnqueueRunAsync(automation, "manual", testPayloadJson, CancellationToken.None);
    }

    public async Task<AgentAutomationRun?> EnqueueRunAsync(
        AgentAutomation automation,
        string triggerType,
        string? payloadJson,
        CancellationToken ct)
    {
        if (_runningIds.ContainsKey(automation.Id))
        {
            var skipped = new AgentAutomationRun
            {
                Id = "run_" + Guid.NewGuid().ToString("N")[..10],
                AutomationId = automation.Id,
                AutomationName = automation.Name,
                TriggerType = triggerType,
                StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Status = "skipped",
                Summary = "该自动化已在运行中",
                TriggerPayloadJson = payloadJson
            };
            _store.RecordRun(skipped);
            Notify(skipped);
            return skipped;
        }

        await _runGate.WaitAsync(ct);
        try
        {
            if (!_runningIds.TryAdd(automation.Id, 0))
                return null;

            var run = await _execute(automation, triggerType, payloadJson, ct);
            _store.RecordRun(run);
            _store.UpdateAutomationAfterRun(automation, run.FinishedAt ?? run.StartedAt);
            Notify(run);
            return run;
        }
        finally
        {
            _runningIds.TryRemove(automation.Id, out _);
            _runGate.Release();
        }
    }

    private async Task CheckSchedulesAsync()
    {
        if (_cts?.IsCancellationRequested == true)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var automation in _store.List())
        {
            if (!AgentAutomationScheduler.IsDue(automation, now))
                continue;

            _ = EnqueueRunAsync(automation, "schedule", BuildSchedulePayload(now), CancellationToken.None);
        }
    }

    private static string BuildSchedulePayload(DateTimeOffset now) =>
        JsonSerializer.Serialize(new
        {
            type = "schedule",
            firedAt = now.ToString("o"),
            utc = now.ToUnixTimeMilliseconds()
        });

    private void StartWebhookListener()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_webhookPort}/");
        _listener.Start();
        _ = Task.Run(() => WebhookLoopAsync(_cts!.Token));
    }

    private async Task WebhookLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleWebhookAsync(ctx), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task HandleWebhookAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!path.StartsWith("/hooks/", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx, 404, new { error = "not_found" });
                return;
            }

            var automationId = Uri.UnescapeDataString(path["/hooks/".Length..].Trim('/'));
            var automation = _store.Get(automationId);
            if (automation is null || !automation.Enabled)
            {
                await WriteJsonAsync(ctx, 404, new { error = "automation_not_found" });
                return;
            }

            if (!string.IsNullOrWhiteSpace(automation.WebhookSecret))
            {
                var secret = ctx.Request.Headers["X-Automation-Secret"];
                if (!string.Equals(secret, automation.WebhookSecret, StringComparison.Ordinal))
                {
                    await WriteJsonAsync(ctx, 401, new { error = "invalid_secret" });
                    return;
                }
            }

            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var triggerType = ResolveWebhookTriggerType(ctx.Request, automation);
            if (!MatchesExternalTrigger(automation, triggerType, ctx.Request, body))
            {
                await WriteJsonAsync(ctx, 202, new { ok = true, skipped = true, reason = "filter_no_match" });
                return;
            }

            var payload = WrapWebhookPayload(triggerType, body, ctx.Request);
            _ = EnqueueRunAsync(automation, triggerType, payload, CancellationToken.None);
            await WriteJsonAsync(ctx, 202, new { ok = true, automationId, triggerType });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 500, new { error = ex.Message });
        }
    }

    private static string ResolveWebhookTriggerType(HttpListenerRequest req, AgentAutomation automation)
    {
        var gh = req.Headers["X-GitHub-Event"];
        if (!string.IsNullOrWhiteSpace(gh))
            return "github";

        var slack = req.Headers["X-Slack-Request-Timestamp"];
        if (!string.IsNullOrWhiteSpace(slack))
            return "slack";

        return automation.Trigger.Type.Equals("github", StringComparison.OrdinalIgnoreCase) ? "github"
            : automation.Trigger.Type.Equals("slack", StringComparison.OrdinalIgnoreCase) ? "slack"
            : "webhook";
    }

    private static bool MatchesExternalTrigger(
        AgentAutomation automation,
        string triggerType,
        HttpListenerRequest req,
        string body)
    {
        if (triggerType == "github" && !string.IsNullOrWhiteSpace(automation.Trigger.GithubEvent))
        {
            var ev = req.Headers["X-GitHub-Event"] ?? "";
            var action = TryReadJsonString(body, "action");
            var combined = string.IsNullOrWhiteSpace(action) ? ev : $"{ev}.{action}";
            return combined.Contains(automation.Trigger.GithubEvent, StringComparison.OrdinalIgnoreCase)
                   || ev.Contains(automation.Trigger.GithubEvent, StringComparison.OrdinalIgnoreCase);
        }

        if (triggerType == "slack" && !string.IsNullOrWhiteSpace(automation.Trigger.SlackEvent))
        {
            var t = TryReadJsonString(body, "type") ?? TryReadJsonString(body, "event.type") ?? "";
            return t.Contains(automation.Trigger.SlackEvent, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string WrapWebhookPayload(string triggerType, string body, HttpListenerRequest req)
    {
        var headers = new Dictionary<string, string>();
        foreach (var key in req.Headers.AllKeys)
        {
            if (key is null) continue;
            if (key.StartsWith("X-GitHub", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("X-Slack", StringComparison.OrdinalIgnoreCase))
                headers[key] = req.Headers[key] ?? "";
        }

        object? bodyObj = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                bodyObj = JsonSerializer.Deserialize<object>(body);
            }
            catch
            {
                bodyObj = body;
            }
        }

        return JsonSerializer.Serialize(new
        {
            type = triggerType,
            path = req.Url?.AbsolutePath,
            headers,
            body = bodyObj
        });
    }

    private static string? TryReadJsonString(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el))
                    return null;
            }

            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
        }
        catch
        {
            return null;
        }
    }

    private void Notify(AgentAutomationRun run) => _onRunUpdated?.Invoke(run);

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose() => Stop();
}
