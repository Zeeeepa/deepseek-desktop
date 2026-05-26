using System.Text.Json;

namespace DeepSeekBrowser.Services.OAuth;

/// <summary>对齐 Chat2API <c>mapOAuthCredentials</c>（AddProviderDialog）。</summary>
public static class DsdOAuthCredentialMapper
{
    public static Dictionary<string, string> Map(string providerId, IReadOnlyDictionary<string, string> raw)
    {
        if (raw.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var id = providerId.Trim().ToLowerInvariant();
        return id switch
        {
            "deepseek" => MapDeepSeek(raw),
            "glm" => MapSingle(raw, "chatglm_refresh_token", "refresh_token"),
            "qwen" => MapSingle(raw, "tongyi_sso_ticket", "ticket"),
            "qwen-ai" or "zai" or "kimi" => MapSingle(raw, "token", "token"),
            "perplexity" => MapPerplexity(raw),
            "mimo" => MapMimo(raw),
            "minimax" => MapMinimax(raw),
            _ => new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Dictionary<string, string> MapDeepSeek(IReadOnlyDictionary<string, string> raw)
    {
        if (!raw.TryGetValue("userToken", out var token) && !raw.TryGetValue("token", out token))
            return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);

        token = UnwrapJsonToken(token);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["token"] = token };
    }

    private static Dictionary<string, string> MapPerplexity(IReadOnlyDictionary<string, string> raw)
    {
        if (raw.TryGetValue("__Secure-next-auth.session-token", out var secure) && !string.IsNullOrWhiteSpace(secure))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["sessionToken"] = secure.Trim() };
        if (raw.TryGetValue("next-auth.session-token", out var plain) && !string.IsNullOrWhiteSpace(plain))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["sessionToken"] = plain.Trim() };
        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> MapMimo(IReadOnlyDictionary<string, string> raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Copy(raw, result, "serviceToken", "service_token");
        Copy(raw, result, "userId", "user_id");
        Copy(raw, result, "xiaomichatbot_ph", "ph_token");
        return result;
    }

    private static Dictionary<string, string> MapMinimax(IReadOnlyDictionary<string, string> raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token))
            result["token"] = token.Trim();
        else if (raw.TryGetValue("_token", out var underscored) && !string.IsNullOrWhiteSpace(underscored))
            result["token"] = UnwrapJsonToken(underscored);

        if (raw.TryGetValue("realUserID", out var rid) && !string.IsNullOrWhiteSpace(rid))
            result["realUserID"] = rid.Trim();
        else if (raw.TryGetValue("user_detail_agent", out var detail) && !string.IsNullOrWhiteSpace(detail))
        {
            try
            {
                using var doc = JsonDocument.Parse(detail);
                var root = doc.RootElement;
                if (root.TryGetProperty("realUserID", out var r) && r.ValueKind == JsonValueKind.String)
                    result["realUserID"] = r.GetString() ?? "";
                else if (root.TryGetProperty("id", out var idEl))
                    result["realUserID"] = idEl.ToString();
            }
            catch
            {
                // ignore
            }
        }

        return result;
    }

    private static Dictionary<string, string> MapSingle(
        IReadOnlyDictionary<string, string> raw,
        string sourceKey,
        string targetKey)
    {
        foreach (var kv in raw)
        {
            if (!string.Equals(kv.Key, sourceKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [targetKey] = kv.Value.Trim()
            };
        }

        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static void Copy(
        IReadOnlyDictionary<string, string> raw,
        Dictionary<string, string> target,
        string from,
        string to)
    {
        if (raw.TryGetValue(from, out var v) && !string.IsNullOrWhiteSpace(v))
            target[to] = v.Trim();
        else if (raw.TryGetValue(to, out var existing) && !string.IsNullOrWhiteSpace(existing))
            target[to] = existing.Trim();
    }

    private static string UnwrapJsonToken(string token)
    {
        token = token.Trim();
        if (!token.StartsWith('{') || !token.EndsWith('}'))
            return token;
        try
        {
            using var doc = JsonDocument.Parse(token);
            if (doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? token;
        }
        catch
        {
            // ignore
        }

        return token;
    }
}
