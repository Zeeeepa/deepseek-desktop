namespace DeepSeekBrowser.Models;

public sealed class AgentSessionMeta
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "新对话";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class AgentSessionFile
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "新对话";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public List<AgentSessionMessage> Messages { get; set; } = new();

    /// <summary>DeepSeek-TUI 运行时线程 ID，用于多轮对话续接。</summary>
    public string? TuiThreadId { get; set; }
}

public sealed class AgentSessionMessage
{
    public string Role { get; set; } = "";
    public string? Text { get; set; }
    public string? Answer { get; set; }
    public AgentThinkSnapshot? Think { get; set; }
}

public sealed class AgentThinkSnapshot
{
    public List<AgentThinkRecord> Records { get; set; } = new();
    public int DurationSec { get; set; }
}

public sealed class AgentThinkRecord
{
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public string? Label { get; set; }
    public string? Verb { get; set; }
    public string? Target { get; set; }
    public bool Error { get; set; }
}
