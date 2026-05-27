using System.Diagnostics;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services;

public sealed class DesktopWebChatAdapter : IAgentWebChat
{
    private readonly IDesktopWebHost _host;

    public DesktopWebChatAdapter(IDesktopWebHost host) => _host = host;

    public async Task<WebChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        AgentChatOptions? options = null)
    {
        var config = ConfigStore.Load();
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _host.WebChatAsync(
                HarnessWebChatMessageAdapter.FlattenForWeb(messages),
                model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);
            AgentRequestTelemetry.TryPublish(
                config, model, thinking, search, result, sw.ElapsedMilliseconds, stream: false, "embedded-web");
            return result;
        }
        catch (Exception ex)
        {
            AgentRequestTelemetry.TryPublish(
                config, model, thinking, search, null, sw.ElapsedMilliseconds, stream: false, "embedded-web", ex);
            throw;
        }
    }

    public async IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        AgentChatOptions? options = null)
    {
        var config = ConfigStore.Load();
        var sw = Stopwatch.StartNew();
        WebChatResult? final = null;
        Exception? error = null;

        await foreach (var ev in _host.WebChatStreamAsync(
                           HarnessWebChatMessageAdapter.FlattenForWeb(messages),
                           model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId))
        {
            switch (ev)
            {
                case WebChatStreamDone done:
                    final = done.Result;
                    break;
                case WebChatStreamError err:
                    error = new InvalidOperationException(err.Message);
                    break;
            }

            yield return ev;
        }

        AgentRequestTelemetry.TryPublish(
            config, model, thinking, search, final, sw.ElapsedMilliseconds, stream: true, "embedded-web", error);
    }
}
