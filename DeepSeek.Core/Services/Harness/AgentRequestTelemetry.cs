using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>Harness / 网页桥 LLM 调用遥测；由桌面宿主订阅并写入 requestLogs。</summary>
public static class AgentRequestTelemetry
{
    public static Action<AgentRequestTelemetryEvent>? OnCompleted { get; set; }

    public static void Publish(AgentRequestTelemetryEvent evt) => OnCompleted?.Invoke(evt);

    public static void TryPublish(
        AppConfig config,
        string model,
        bool thinking,
        bool search,
        WebChatResult? result,
        long latencyMs,
        bool stream,
        string channel,
        Exception? error = null)
    {
        if (OnCompleted is null)
            return;

        try
        {
            OnCompleted(new AgentRequestTelemetryEvent
            {
                Config = config,
                Model = model,
                Thinking = thinking,
                WebSearch = search,
                Result = result,
                LatencyMs = latencyMs,
                IsStream = stream,
                Channel = channel,
                Error = error
            });
        }
        catch
        {
            // diagnostics only
        }
    }
}

public sealed class AgentRequestTelemetryEvent
{
    public required AppConfig Config { get; init; }
    public required string Model { get; init; }
    public bool Thinking { get; init; }
    public bool WebSearch { get; init; }
    public WebChatResult? Result { get; init; }
    public long LatencyMs { get; init; }
    public bool IsStream { get; init; }
    public required string Channel { get; init; }
    public Exception? Error { get; init; }
}
