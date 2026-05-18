namespace DeepSeekBrowser.Services;

/// <summary>网页 Chat API 流式事件（bridge.js → WebView2 postMessage）。</summary>
public abstract record WebChatStreamEvent;

public sealed record WebChatStreamDelta(string Kind, string Text) : WebChatStreamEvent
{
    public const string Reasoning = "reasoning";
    public const string Content = "content";
}

public sealed record WebChatStreamDone(WebChatResult Result) : WebChatStreamEvent;

public sealed record WebChatStreamError(string Message) : WebChatStreamEvent;
