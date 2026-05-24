using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using System.Text.Json;

namespace DeepSeek.Core.Tests;

public sealed class AgentAutomationTests
{
    [Fact]
    public void Prompt_replaces_trigger_fields()
    {
        using var doc = JsonDocument.Parse("""{"body":{"title":"Fix login"}}""");
        var rendered = AgentAutomationPrompt.Render(
            "Review PR: {{ trigger.body.title }}",
            doc.RootElement);
        Assert.Contains("Fix login", rendered);
    }

    [Fact]
    public void Scheduler_daily_computes_future()
    {
        var auto = new AgentAutomation
        {
            Enabled = true,
            Trigger = new AgentAutomationTrigger
            {
                Type = "schedule",
                SchedulePreset = "daily",
                ScheduleTimeUtc = "09:00"
            }
        };
        var from = new DateTimeOffset(2026, 5, 24, 10, 0, 0, TimeSpan.Zero);
        var next = AgentAutomationScheduler.ComputeNextRunUtc(auto, from);
        Assert.NotNull(next);
        var nextDt = DateTimeOffset.FromUnixTimeMilliseconds(next!.Value);
        Assert.Equal(25, nextDt.Day);
        Assert.Equal(9, nextDt.Hour);
    }
}
