namespace DeepSeekBrowser.Models;

public sealed class Chat2ApiSessionInfo
{
    public string ClientSessionId { get; init; } = "";
    public string WebSessionId { get; init; } = "";
    public long LastUsedAt { get; init; }
    public int MessageCount { get; init; }
}
