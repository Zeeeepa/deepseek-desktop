namespace DeepSeekBrowser.Services;

/// <summary>向 DSD API 管理台 WebView 推送 ipcEvent（oauth:progress、proxy:statusChanged 等）。</summary>
public static class DsdApiIpcEventHub
{
    public static event Action<string, object?[]>? Event;

    public static void Publish(string channel, params object?[] args) =>
        Event?.Invoke(channel, args);

    public static void PublishOAuthProgress(string status, string message) =>
        Publish("oauth:progress", new { status, message });
}
