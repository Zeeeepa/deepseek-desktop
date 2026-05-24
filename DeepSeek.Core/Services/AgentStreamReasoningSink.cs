namespace DeepSeekBrowser.Services;

/// <summary>Agent 运行期间将网页推理流转发给 UI（可选订阅）。</summary>
public static class AgentStreamReasoningSink
{
    public static event Action<string, bool>? ReasoningPublished;

    public static void Publish(string text, bool append = true)
    {
        if (string.IsNullOrEmpty(text))
            return;
        ReasoningPublished?.Invoke(text, append);
    }
}
