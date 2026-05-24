using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services;

public sealed class AgentAutomationSupport
{
    private readonly AgentAutomationHost _host;
    private readonly Func<object, Task> _postToAgent;

    public AgentAutomationSupport(AgentAutomationHost host, Func<object, Task> postToAgent)
    {
        _host = host;
        _postToAgent = postToAgent;
    }

    public async Task HandleAsync(JsonElement msg, string type)
    {
        var reqId = msg.TryGetProperty("reqId", out var r) ? r.GetString() : null;

        try
        {
            switch (type)
            {
                case "agentAutomationsBootstrap":
                case "agentAutomationsList":
                    await ReplyAsync(reqId, new
                    {
                        ok = true,
                        automations = _host.Store.List(),
                        webhookBaseUrl = _host.WebhookBaseUrl
                    });
                    break;

                case "agentAutomationsRuns":
                    var autoId = msg.TryGetProperty("automationId", out var idEl) ? idEl.GetString() : null;
                    await ReplyAsync(reqId, new
                    {
                        ok = !string.IsNullOrWhiteSpace(autoId),
                        runs = string.IsNullOrWhiteSpace(autoId)
                            ? Array.Empty<AgentAutomationRun>()
                            : _host.Store.ListRuns(autoId!)
                    });
                    break;

                case "agentAutomationsSave":
                    if (!msg.TryGetProperty("automation", out var autoEl))
                    {
                        await ReplyAsync(reqId, new { ok = false, error = "automation 缺失" });
                        break;
                    }

                    var automation = JsonSerializer.Deserialize<AgentAutomation>(
                        autoEl.GetRawText(),
                        AgentSessionJson.Options);
                    if (automation is null || string.IsNullOrWhiteSpace(automation.Name))
                    {
                        await ReplyAsync(reqId, new { ok = false, error = "automation 无效" });
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(automation.Id))
                        automation.Id = "auto_" + Guid.NewGuid().ToString("N")[..12];

                    AgentAutomationScheduler.RefreshSchedule(automation);
                    _host.Store.Save(automation);
                    await ReplyAsync(reqId, new { ok = true, automation, webhookBaseUrl = _host.WebhookBaseUrl });
                    break;

                case "agentAutomationsDelete":
                    var delId = msg.TryGetProperty("id", out var delEl) ? delEl.GetString() : null;
                    var deleted = !string.IsNullOrWhiteSpace(delId) && _host.Store.Delete(delId!);
                    await ReplyAsync(reqId, new { ok = deleted });
                    break;

                case "agentAutomationsToggle":
                    var toggleId = msg.TryGetProperty("id", out var tEl) ? tEl.GetString() : null;
                    var enabled = msg.TryGetProperty("enabled", out var enEl) && enEl.ValueKind == JsonValueKind.True;
                    var item = string.IsNullOrWhiteSpace(toggleId) ? null : _host.Store.Get(toggleId!);
                    if (item is null)
                    {
                        await ReplyAsync(reqId, new { ok = false, error = "未找到" });
                        break;
                    }

                    item.Enabled = enabled;
                    AgentAutomationScheduler.RefreshSchedule(item);
                    _host.Store.Save(item);
                    await ReplyAsync(reqId, new { ok = true, automation = item });
                    break;

                case "agentAutomationsTest":
                    var testId = msg.TryGetProperty("id", out var testEl) ? testEl.GetString() : null;
                    var payload = msg.TryGetProperty("payload", out var pEl) ? pEl.GetRawText() : null;
                    if (string.IsNullOrWhiteSpace(testId))
                    {
                        await ReplyAsync(reqId, new { ok = false, error = "id 缺失" });
                        break;
                    }

                    var run = await _host.TriggerManualAsync(testId!, payload);
                    await ReplyAsync(reqId, new { ok = run is not null, run });
                    break;
            }
        }
        catch (Exception ex)
        {
            await ReplyAsync(reqId, new { ok = false, error = ex.Message });
        }
    }

    private Task ReplyAsync(string? reqId, object payload) =>
        _postToAgent(new { type = "agentAutomation", reqId, payload });
}
