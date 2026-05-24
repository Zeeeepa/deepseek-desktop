namespace DeepSeekBrowser.Models;

/// <summary>Always-on Agent 自动化（对标 Cursor Automations 的本地桌面实现）。</summary>
public sealed class AgentAutomation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public AgentAutomationTrigger Trigger { get; set; } = new();
    public List<AgentAutomationAction> Actions { get; set; } = [new() { Type = "agent" }];
    /// <summary>Agent 指令；支持 <c>{{ trigger.field }}</c> 模板。</summary>
    public string Instructions { get; set; } = "";
    public string Strategy { get; set; } = "execute";
    public string? PlaybookId { get; set; }
    public string? SkillId { get; set; }
    public string? WorkspaceRoot { get; set; }
    public long? LastRunAt { get; set; }
    public long? NextRunAt { get; set; }
    public int RunCount { get; set; }
    /// <summary>Webhook 可选密钥（请求头 X-Automation-Secret）。</summary>
    public string? WebhookSecret { get; set; }
}

public sealed class AgentAutomationTrigger
{
    /// <summary>schedule | webhook | github | slack | manual</summary>
    public string Type { get; set; } = "schedule";

    /// <summary>hourly | daily | weekly</summary>
    public string SchedulePreset { get; set; } = "daily";

    /// <summary>UTC HH:mm，用于 daily / weekly。</summary>
    public string ScheduleTimeUtc { get; set; } = "09:00";

    /// <summary>weekly：0=周日 … 6=周六</summary>
    public int ScheduleDayOfWeek { get; set; } = 1;

    /// <summary>GitHub 事件过滤（如 pull_request.opened）。</summary>
    public string? GithubEvent { get; set; }

    /// <summary>Slack 事件过滤（如 message.posted）。</summary>
    public string? SlackEvent { get; set; }
}

public sealed class AgentAutomationAction
{
    /// <summary>agent | notify</summary>
    public string Type { get; set; } = "agent";
    public string? NotifyChannel { get; set; }
}

public sealed class AgentAutomationRun
{
    public string Id { get; set; } = "";
    public string AutomationId { get; set; } = "";
    public string AutomationName { get; set; } = "";
    public string TriggerType { get; set; } = "";
    public long StartedAt { get; set; }
    public long? FinishedAt { get; set; }
    public string Status { get; set; } = "running";
    public string? Summary { get; set; }
    public string? Answer { get; set; }
    public string? Error { get; set; }
    public string? TriggerPayloadJson { get; set; }
}

public sealed class AgentAutomationListFile
{
    public List<AgentAutomation> Automations { get; set; } = [];
}
