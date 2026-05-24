using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Desktop.Services;

public sealed class WinUiWebChatBridgeHostAdapter : IAgentWebChat
{
    private readonly AppHost _host;

    public WinUiWebChatBridgeHostAdapter(AppHost host) => _host = host;

    private WinUiWebChatBridgeHost Bridge =>
        _host.ChatBridge ?? throw new InvalidOperationException("Chat bridge WebView is not initialized.");

    public Task<WebChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        Bridge.WebChatAsync(
            messages, model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);

    public IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        Bridge.WebChatStreamAsync(
            messages, model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);
}
