namespace DeepSeekBrowser.Services;

/// <summary>经 WebView 注入的网页对话 API。</summary>
public interface IWebChatBridge
{
    Task SyncChatSessionAsync(string? sessionId);

    Task<WebChatResult> WebChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null);

    IAsyncEnumerable<WebChatStreamEvent> WebChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null);

    void CancelActiveWebChatStreams();
}
