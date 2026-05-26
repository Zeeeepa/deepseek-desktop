using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services;
public sealed class DesktopWebChatAdapter : IAgentWebChat
{
    private readonly IDesktopWebHost _host;

    public DesktopWebChatAdapter(IDesktopWebHost host) => _host = host;

    public Task<WebChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        AgentChatOptions? options = null) =>
        _host.WebChatAsync(
            HarnessWebChatMessageAdapter.FlattenForWeb(messages),
            model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);

    public IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        AgentChatOptions? options = null) =>
        _host.WebChatStreamAsync(
            HarnessWebChatMessageAdapter.FlattenForWeb(messages),
            model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);
}
