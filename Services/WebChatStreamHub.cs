using System.Text.Json;
using System.Threading.Channels;

namespace DeepSeekBrowser.Services;

/// <summary>接收 bridge.js 的 bridge_stream 消息并暴露为异步枚举。</summary>
internal sealed class WebChatStreamHub : IDisposable
{
    private readonly string _fallbackModel;

    public WebChatStreamHub(string fallbackModel) => _fallbackModel = fallbackModel;
    private readonly Channel<WebChatStreamEvent> _channel =
        Channel.CreateUnbounded<WebChatStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly TaskCompletionSource _scriptDone =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string StreamId { get; } = Guid.NewGuid().ToString("N");

    public void SignalScriptCompleted() => _scriptDone.TrySetResult();

    public void SignalScriptFailed(Exception ex) => _scriptDone.TrySetException(ex);

    public void PushError(string message) =>
        _channel.Writer.TryWrite(new WebChatStreamError(message));

    public async IAsyncEnumerable<WebChatStreamEvent> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var ev in _channel.Reader.ReadAllAsync(ct))
        {
            yield return ev;
            if (ev is WebChatStreamDone or WebChatStreamError)
                break;
        }

        try
        {
            await _scriptDone.Task.WaitAsync(ct);
        }
        catch
        {
            // 错误已通过 WebChatStreamError 下发
        }
    }

    public bool TryHandleMessage(JsonElement root)
    {
        if (!root.TryGetProperty("channel", out var ch) ||
            !string.Equals(ch.GetString(), "bridge_stream", StringComparison.Ordinal))
            return false;

        if (!root.TryGetProperty("streamId", out var sidEl) ||
            !string.Equals(sidEl.GetString(), StreamId, StringComparison.Ordinal))
            return false;

        var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        switch (type)
        {
            case "delta":
            {
                var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : "content";
                var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(text))
                    _channel.Writer.TryWrite(new WebChatStreamDelta(kind ?? "content", text));
                return true;
            }
            case "done":
            {
                var result = ParseDonePayload(root, _fallbackModel);
                _channel.Writer.TryWrite(new WebChatStreamDone(result));
                return true;
            }
            case "error":
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "stream error";
                _channel.Writer.TryWrite(new WebChatStreamError(msg ?? "stream error"));
                return true;
            }
            default:
                return true;
        }
    }

    private WebChatResult ParseDonePayload(JsonElement root, string fallbackModel)
    {
        if (root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object)
            return WebChatBridgeHost.ParseWebChatResultFromJson(res, fallbackModel);

        return new WebChatResult { Content = "", Model = fallbackModel, FinishReason = "stop" };
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }
}
