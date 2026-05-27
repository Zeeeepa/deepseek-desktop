namespace DeepSeekBrowser.Models;

public sealed class DsdApiSessionInfo
{
    public string ClientSessionId { get; init; } = "";
    public string WebSessionId { get; init; } = "";
    public long LastUsedAt { get; init; }
    public int MessageCount { get; init; }
}
