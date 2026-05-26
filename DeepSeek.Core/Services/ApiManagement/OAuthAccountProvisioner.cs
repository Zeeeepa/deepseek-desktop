using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>
/// OAuth 成功后自动创建/确保供应商与账户（对齐 Chat2API「添加内置供应商 + 凭证」流程）。
/// </summary>
public static class OAuthAccountProvisioner
{
    public sealed record ProvisionResult(
        bool Success,
        ProviderAccountRecord? Account,
        string? Error,
        bool ProviderCreated);

    public static async Task<ProvisionResult> ProvisionAsync(
        string providerId,
        IReadOnlyDictionary<string, string> rawCredentials,
        AppConfig config,
        string? accountName = null,
        string? email = null,
        bool credentialsAlreadyMapped = false,
        CancellationToken ct = default)
    {
        providerId = (providerId ?? "").Trim();
        if (string.IsNullOrEmpty(providerId))
            return new ProvisionResult(false, null, "供应商 ID 无效", false);

        var credentials = credentialsAlreadyMapped
            ? rawCredentials.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : MapCredentials(providerId, rawCredentials);
        if (credentials.Count == 0)
            return new ProvisionResult(false, null, "未获取到有效凭证", false);

        var validation = await ProviderCredentialValidator.ValidateProviderCredentialsAsync(
            providerId, credentials, ct);
        if (!validation.Valid)
            return new ProvisionResult(false, null, validation.Error ?? "凭证校验失败", false);

        var providerCreated = ProviderAvailabilitySync.EnsureProviderRegistryEntry(config, providerId);
        var meta = BuiltinProviderCatalog.Find(providerId);
        var name = string.IsNullOrWhiteSpace(accountName)
            ? validation.Name ?? $"{meta?.Name ?? providerId} 账户"
            : accountName.Trim();
        var accountEmail = email ?? validation.Email ?? "";

        var rec = ProviderAccountStore.AddOrUpdate(
            providerId,
            name,
            credentials,
            string.IsNullOrWhiteSpace(accountEmail) ? null : accountEmail);

        SyncDeepSeekWebToken(config, credentials);

        return new ProvisionResult(true, rec, null, providerCreated);
    }

    public static void SyncDeepSeekWebToken(AppConfig config, IReadOnlyDictionary<string, string> credentials)
    {
        if (!credentials.TryGetValue("token", out var dsToken)
            || string.IsNullOrWhiteSpace(dsToken))
            return;

        config.WebUserToken = dsToken.Trim();
    }

    private static Dictionary<string, string> MapCredentials(
        string providerId,
        IReadOnlyDictionary<string, string> raw)
    {
        var id = providerId.ToLowerInvariant();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in raw)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
                result[kv.Key] = kv.Value.Trim();
        }

        switch (id)
        {
            case "deepseek":
                if (result.TryGetValue("userToken", out var ut))
                {
                    result["token"] = UnwrapJson(ut);
                    result.Remove("userToken");
                }
                else if (result.TryGetValue("token", out var t))
                    result["token"] = UnwrapJson(t);
                break;
            case "glm":
                if (result.TryGetValue("chatglm_refresh_token", out var rt))
                    result["refresh_token"] = rt;
                break;
            case "qwen":
                if (result.TryGetValue("tongyi_sso_ticket", out var ticket))
                    result["ticket"] = ticket;
                break;
            case "qwen-ai":
            case "zai":
            case "kimi":
                if (result.TryGetValue("tongyi_sso_ticket", out var qTicket))
                    result["token"] = qTicket;
                break;
            case "perplexity":
                if (result.TryGetValue("__Secure-next-auth.session-token", out var sec))
                    result["sessionToken"] = sec;
                else if (result.TryGetValue("next-auth.session-token", out var sess))
                    result["sessionToken"] = sess;
                break;
            case "mimo":
                CopyKey(result, "serviceToken", "service_token");
                CopyKey(result, "userId", "user_id");
                CopyKey(result, "xiaomichatbot_ph", "ph_token");
                break;
            case "minimax":
                if (result.TryGetValue("_token", out var mt))
                    result["token"] = UnwrapJson(mt);
                break;
        }

        return result;
    }

    private static void CopyKey(Dictionary<string, string> dict, string from, string to)
    {
        if (dict.TryGetValue(from, out var v) && !string.IsNullOrWhiteSpace(v))
            dict[to] = v;
    }

    private static string UnwrapJson(string token)
    {
        token = token.Trim();
        if (!token.StartsWith('{') || !token.EndsWith('}'))
            return token;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(token);
            if (doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                return v.GetString() ?? token;
        }
        catch
        {
            // ignore
        }

        return token;
    }
}
