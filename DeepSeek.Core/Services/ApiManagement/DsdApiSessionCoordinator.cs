using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>代理转发与会话记录表挂接（对齐 Chat2API sessionManager.getOrCreateSession）。</summary>
public static class DsdApiSessionCoordinator
{
    public static string EnsureClientSession(
        string? clientSessionId,
        string providerId,
        string? accountId,
        string? model)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        accountId ??= "";
        if (string.IsNullOrWhiteSpace(clientSessionId))
        {
            clientSessionId = $"session-{now}-{Random.Shared.Next(100000, 999999)}";
            DsdApiSessionRecordStore.Add(new DsdApiSessionRecordStore.SessionRecord
            {
                Id = clientSessionId,
                ProviderId = providerId,
                AccountId = accountId,
                SessionType = "chat",
                CreatedAt = now,
                LastActiveAt = now,
                Status = "active",
                Model = model
            });
            EnforceMaxSessionsPerAccount(accountId);
            return clientSessionId;
        }

        var existing = DsdApiSessionRecordStore.GetById(clientSessionId);
        if (existing is null)
        {
            DsdApiSessionRecordStore.Add(new DsdApiSessionRecordStore.SessionRecord
            {
                Id = clientSessionId,
                ProviderId = providerId,
                AccountId = accountId,
                SessionType = "chat",
                CreatedAt = now,
                LastActiveAt = now,
                Status = "active",
                Model = model
            });
            EnforceMaxSessionsPerAccount(accountId);
        }
        else
        {
            DsdApiSessionRecordStore.Touch(clientSessionId, model);
        }

        return clientSessionId;
    }

    public static void BindProviderSession(
        string clientSessionId,
        string providerSessionId,
        string providerId,
        string? accountId)
    {
        if (string.IsNullOrWhiteSpace(clientSessionId)) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DsdApiSessionRecordStore.UpsertWebBridgeSession(
            clientSessionId,
            providerSessionId,
            providerId,
            accountId ?? "",
            now,
            0);
    }

    public static void Touch(string clientSessionId) =>
        DsdApiSessionRecordStore.Touch(clientSessionId, null);

    private static void EnforceMaxSessionsPerAccount(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return;
        var cfg = DsdSessionConfigStore.Get();
        var max = Math.Max(1, cfg.MaxSessionsPerAccount);
        var active = DsdApiSessionRecordStore.GetByAccountId(accountId)
            .Where(s => string.Equals(s.Status, "active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.LastActiveAt)
            .ToList();
        while (active.Count > max)
        {
            DsdApiSessionRecordStore.Delete(active[0].Id);
            active.RemoveAt(0);
        }
    }
}
