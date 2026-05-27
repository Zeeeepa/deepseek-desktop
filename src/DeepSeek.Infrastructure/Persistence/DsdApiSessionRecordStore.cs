using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>API 管理台会话记录（对齐 Chat2API storeManager sessions / sessionManager）。</summary>
public static class DsdApiSessionRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object Gate = new();

    public sealed class ChatMessageRecord
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public long Timestamp { get; set; }
    }

    public sealed class SessionRecord
    {
        public string Id { get; set; } = "";
        public string ProviderId { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string SessionType { get; set; } = "chat";
        public List<ChatMessageRecord> Messages { get; set; } = new();
        public long CreatedAt { get; set; }
        public long LastActiveAt { get; set; }
        public string Status { get; set; } = "active";
        public string? Model { get; set; }
        public string? ProviderSessionId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private static string FilePath => Path.Combine(ConfigStore.ConfigDirectory, "api-sessions.json");

    public static IReadOnlyList<SessionRecord> GetAll()
    {
        lock (Gate) return Load();
    }

    public static IReadOnlyList<SessionRecord> GetActive()
    {
        var cfg = DsdSessionConfigStore.Get();
        var timeoutMs = Math.Max(1, cfg.SessionTimeout) * 60_000L;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return GetAll()
            .Where(s => string.Equals(s.Status, "active", StringComparison.OrdinalIgnoreCase)
                        && now - s.LastActiveAt < timeoutMs)
            .ToList();
    }

    public static SessionRecord? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetAll().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<SessionRecord> GetByAccountId(string accountId) =>
        GetAll().Where(s => string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<SessionRecord> GetByProviderId(string providerId) =>
        GetAll().Where(s => string.Equals(s.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToList();

    public static SessionRecord Add(SessionRecord session)
    {
        lock (Gate)
        {
            var list = Load();
            list.Add(session);
            Save(list);
            return session;
        }
    }

    public static SessionRecord? UpsertWebBridgeSession(
        string clientSessionId,
        string webSessionId,
        string providerId,
        string? accountId,
        long lastActiveAt,
        int messageCount)
    {
        lock (Gate)
        {
            var list = Load();
            var existing = list.FirstOrDefault(s =>
                string.Equals(s.Id, clientSessionId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.ProviderSessionId = webSessionId;
                existing.LastActiveAt = lastActiveAt;
                existing.Status = "active";
                Save(list);
                return existing;
            }

            var rec = new SessionRecord
            {
                Id = clientSessionId,
                ProviderId = providerId,
                AccountId = accountId ?? "",
                SessionType = "chat",
                Messages = new List<ChatMessageRecord>(),
                CreatedAt = lastActiveAt,
                LastActiveAt = lastActiveAt,
                Status = "active",
                ProviderSessionId = webSessionId
            };
            list.Add(rec);
            Save(list);
            return rec;
        }
    }

    public static void Touch(string id, string? model)
    {
        lock (Gate)
        {
            var list = Load();
            var rec = list.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (rec is null) return;
            rec.LastActiveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rec.Status = "active";
            if (!string.IsNullOrWhiteSpace(model))
                rec.Model = model;
            Save(list);
        }
    }

    public static bool Delete(string id)
    {
        lock (Gate)
        {
            var list = Load();
            var removed = list.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            Save(list);
            return true;
        }
    }

    public static void ClearAll()
    {
        lock (Gate) Save(new List<SessionRecord>());
    }

    public static int CleanExpired()
    {
        var cfg = DsdSessionConfigStore.Get();
        var timeoutMs = Math.Max(1, cfg.SessionTimeout) * 60_000L;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var removedCount = 0;

        lock (Gate)
        {
            var sessions = Load();
            var remaining = new List<SessionRecord>();

            foreach (var s in sessions)
            {
                if (string.Equals(s.Status, "expired", StringComparison.OrdinalIgnoreCase))
                {
                    removedCount++;
                    continue;
                }

                var timedOut = string.Equals(s.Status, "active", StringComparison.OrdinalIgnoreCase)
                               && now - s.LastActiveAt >= timeoutMs;
                if (!timedOut)
                {
                    remaining.Add(s);
                    continue;
                }

                removedCount++;
                if (!cfg.DeleteAfterTimeout)
                {
                    s.Status = "expired";
                    remaining.Add(s);
                }
            }

            Save(remaining);
            return removedCount;
        }
    }

    public static object ToUi(SessionRecord s) => new
    {
        id = s.Id,
        providerId = s.ProviderId,
        accountId = s.AccountId,
        providerSessionId = s.ProviderSessionId,
        sessionType = s.SessionType,
        messages = s.Messages.Select(m => new { role = m.Role, content = m.Content, timestamp = m.Timestamp }).ToArray(),
        createdAt = s.CreatedAt,
        lastActiveAt = s.LastActiveAt,
        status = s.Status,
        model = s.Model,
        metadata = s.Metadata
    };

    private static List<SessionRecord> Load()
    {
        if (!File.Exists(FilePath))
            return new List<SessionRecord>();
        try
        {
            return JsonSerializer.Deserialize<List<SessionRecord>>(File.ReadAllText(FilePath), JsonOptions)
                   ?? new List<SessionRecord>();
        }
        catch
        {
            return new List<SessionRecord>();
        }
    }

    private static void Save(List<SessionRecord> list)
    {
        Directory.CreateDirectory(ConfigStore.ConfigDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
    }
}
