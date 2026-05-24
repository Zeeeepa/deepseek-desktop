namespace DeepSeekBrowser.Models;

public class AgentSessionMeta
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "新对话";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public bool Pinned { get; set; }
}

public sealed class AgentSessionThinkRecord
{
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public string? Verb { get; set; }
    public string? Target { get; set; }
    public string? Detail { get; set; }
    public bool Error { get; set; }
}

public sealed class AgentSessionThink
{
    public List<AgentSessionThinkRecord> Records { get; set; } = [];
    public int DurationSec { get; set; } = 1;
}

public sealed class AgentSessionMessage
{
    public string Role { get; set; } = "user";
    public string? Text { get; set; }
    public string? Answer { get; set; }
    public AgentSessionThink? Think { get; set; }
}

public sealed class AgentSessionData : AgentSessionMeta
{
    public List<AgentSessionMessage> Messages { get; set; } = [];
    public string? TuiThreadId { get; set; }
    public string? WebChatSessionId { get; set; }
}
