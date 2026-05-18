namespace DeepSeekBrowser.Services;

public sealed class WebChatResult
{
    public string? Content { get; init; }
    public string? ReasoningContent { get; init; }
    public List<WebToolCall>? ToolCalls { get; init; }
    public string Model { get; init; } = "deepseek-chat";
    /// <summary>OpenAI 风格：stop / length / tool_calls 等。</summary>
    public string? FinishReason { get; init; }
    public bool IsLikelyTruncated { get; init; }

    /// <summary>DeepSeek 网页 chat_session_id（多轮 session_id 绑定用）。</summary>
    public string? ChatSessionId { get; init; }
}

public sealed class WebToolCall
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Arguments { get; init; } = "{}";
}
