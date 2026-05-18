using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>Chat2API 多轮会话：client session_id → DeepSeek 网页 chat_session_id。</summary>
public sealed class Chat2ApiSessionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public string WebSessionId { get; set; } = "";
        public long LastUsedAt { get; set; }
        public int MessageCount { get; set; }
    }

    public string? ResolveWebSessionId(AppConfig config, string? clientSessionId)
    {
        if (!IsMultiTurn(config) || string.IsNullOrWhiteSpace(clientSessionId))
            return null;

        lock (_lock)
        {
            PurgeExpiredLocked(config);
            if (_sessions.TryGetValue(clientSessionId, out var entry))
            {
                entry.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return entry.WebSessionId;
            }
        }

        return null;
    }

    public void Bind(AppConfig config, string clientSessionId, string webSessionId)
    {
        if (!IsMultiTurn(config) || string.IsNullOrWhiteSpace(clientSessionId))
            return;

        lock (_lock)
        {
            PurgeExpiredLocked(config);
            if (_sessions.TryGetValue(clientSessionId, out var existing))
            {
                existing.WebSessionId = webSessionId;
                existing.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                existing.MessageCount++;
                TrimMessagesLocked(config, existing);
                return;
            }

            EnforceMaxSessionsLocked(config);
            _sessions[clientSessionId] = new Entry
            {
                WebSessionId = webSessionId,
                LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageCount = 1
            };
        }
    }

    public void Touch(AppConfig config, string clientSessionId)
    {
        if (!IsMultiTurn(config)) return;
        lock (_lock)
        {
            if (_sessions.TryGetValue(clientSessionId, out var e))
            {
                e.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                e.MessageCount++;
                TrimMessagesLocked(config, e);
            }
        }
    }

    public int Count
    {
        get { lock (_lock) return _sessions.Count; }
    }

    public IReadOnlyList<Chat2ApiSessionInfo> ListSessions(AppConfig config)
    {
        lock (_lock)
        {
            PurgeExpiredLocked(config);
            return _sessions.Select(kv => new Chat2ApiSessionInfo
            {
                ClientSessionId = kv.Key,
                WebSessionId = kv.Value.WebSessionId,
                LastUsedAt = kv.Value.LastUsedAt,
                MessageCount = kv.Value.MessageCount
            }).OrderByDescending(s => s.LastUsedAt).ToList();
        }
    }

    public bool Delete(string clientSessionId)
    {
        lock (_lock) return _sessions.Remove(clientSessionId);
    }

    public void ClearAll()
    {
        lock (_lock) _sessions.Clear();
    }

    private static bool IsMultiTurn(AppConfig config) =>
        string.Equals(config.Chat2ApiSessionMode, "multi", StringComparison.OrdinalIgnoreCase);

    private void PurgeExpiredLocked(AppConfig config)
    {
        var timeoutMs = Math.Max(1, config.Chat2ApiSessionTimeoutMinutes) * 60_000L;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var kv in _sessions.Where(kv => now - kv.Value.LastUsedAt > timeoutMs).ToList())
            _sessions.Remove(kv.Key);
    }

    private static void TrimMessagesLocked(AppConfig config, Entry entry)
    {
        var max = Math.Max(1, config.Chat2ApiMaxMessagesPerSession);
        if (entry.MessageCount > max)
            entry.MessageCount = max;
    }

    private void EnforceMaxSessionsLocked(AppConfig config)
    {
        const int maxSessions = 10;
        if (_sessions.Count < maxSessions) return;
        var oldest = _sessions.OrderBy(kv => kv.Value.LastUsedAt).First().Key;
        _sessions.Remove(oldest);
    }
}
