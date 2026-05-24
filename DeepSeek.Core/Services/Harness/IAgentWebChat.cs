namespace DeepSeekBrowser.Services.Harness;

/// <summary>Agent Harness 使用的网页 Chat 模型通道（由桌面 WebView 桥实现）。</summary>
public interface IAgentWebChat
{
    Task<WebChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null);

    IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null);
}
