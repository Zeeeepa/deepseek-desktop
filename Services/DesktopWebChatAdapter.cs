using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services;

public sealed class DesktopWebChatAdapter : IAgentWebChat
{
    private readonly DesktopWebHost _host;

    public DesktopWebChatAdapter(DesktopWebHost host) => _host = host;

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
        _host.WebChatAsync(
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
        _host.WebChatStreamAsync(
            messages, model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);
}
