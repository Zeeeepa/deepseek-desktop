namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>按供应商主凭证识别同一账户，避免 OAuth 与手动添加重复写入。</summary>
public static class AccountCredentialIdentity
{
    public static string? GetPrimaryValue(string providerId, IReadOnlyDictionary<string, string> credentials)
    {
        if (credentials.Count == 0)
            return null;

        var id = (providerId ?? "").Trim().ToLowerInvariant();
        return id switch
        {
            "deepseek" => First(credentials, "token", "userToken"),
            "glm" => First(credentials, "refresh_token", "chatglm_refresh_token"),
            "kimi" => First(credentials, "token"),
            "minimax" => First(credentials, "token"),
            "qwen" => First(credentials, "ticket", "tongyi_sso_ticket"),
            "qwen-ai" or "zai" => First(credentials, "token", "tongyi_sso_ticket"),
            "mimo" => First(credentials, "service_token", "serviceToken"),
            "perplexity" => First(credentials, "sessionToken", "session_token"),
            _ => credentials.Values.FirstOrDefault(IsUsableCredential)
        };
    }

    public static ProviderAccountRecord? FindExisting(string providerId, IReadOnlyDictionary<string, string> credentials)
    {
        var primary = GetPrimaryValue(providerId, credentials);
        if (string.IsNullOrWhiteSpace(primary))
            return null;

        foreach (var account in ProviderAccountStore.ByProvider(providerId))
        {
            var existing = GetPrimaryValue(providerId, account.Credentials);
            if (!string.IsNullOrWhiteSpace(existing)
                && string.Equals(existing, primary, StringComparison.Ordinal))
                return account;
        }

        return null;
    }

    private static string? First(IReadOnlyDictionary<string, string> credentials, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!credentials.TryGetValue(key, out var value))
                continue;
            if (IsUsableCredential(value))
                return value.Trim();
        }

        return null;
    }

    private static bool IsUsableCredential(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith("••••", StringComparison.Ordinal);
}
