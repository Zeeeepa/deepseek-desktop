using System.Text;
using System.Text.Json;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>MiniMax Agent 账户 API：积分查询与批量删会话（对齐 Chat2API MiniMaxAdapter）。</summary>
public static class MiniMaxAccountApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public sealed record CreditsResult(
        int TotalCredits,
        int UsedCredits,
        int RemainingCredits,
        long? ExpiresAt);

    public static async Task<CreditsResult?> GetCreditsAsync(
        ProviderAccountRecord account,
        CancellationToken ct = default)
    {
        var (token, userId) = ResolveToken(account);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
            return null;

        var headers = BuildSignedHeaders(userId, token,
            "/matrix/api/v1/commerce/get_membership_info", "{}");
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://agent.minimaxi.com/matrix/api/v1/commerce/get_membership_info")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        foreach (var (k, v) in headers)
            req.Headers.TryAddWithoutValidation(k, v);

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("base_resp", out var br)
                && br.TryGetProperty("status_code", out var sc)
                && sc.GetInt32() != 0)
                return null;

            var remaining = root.TryGetProperty("daily_login_gift_credit_remaining", out var r)
                ? r.GetInt32()
                : 0;
            long? expiresAt = null;
            if (root.TryGetProperty("credits", out var credits)
                && credits.TryGetProperty("4", out var tier)
                && tier.ValueKind == JsonValueKind.Array
                && tier.GetArrayLength() > 0
                && tier[0].TryGetProperty("expires_at", out var exp))
                expiresAt = exp.GetInt64();

            return new CreditsResult(0, 0, remaining, expiresAt);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> DeleteAllChatsAsync(
        ProviderAccountRecord account,
        CancellationToken ct = default)
    {
        var (token, userId) = ResolveToken(account);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
            return false;

        var chatIds = new List<string>();
        long? nextPage = null;
        while (true)
        {
            var body = JsonSerializer.Serialize(new
            {
                page_size = 100,
                workspace_storage_mode = 0,
                next_page_index_id = nextPage
            });
            var headers = BuildSignedHeaders(userId, token,
                "/matrix/api/v1/chat/list_chat", body);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://agent.minimaxi.com/matrix/api/v1/chat/list_chat")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            foreach (var (k, v) in headers)
                req.Headers.TryAddWithoutValidation(k, v);

            using var resp = await Http.SendAsync(req, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) break;

            try
            {
                using var doc = JsonDocument.Parse(respBody);
                var root = doc.RootElement;
                if (root.TryGetProperty("base_resp", out var br)
                    && br.TryGetProperty("status_code", out var sc)
                    && sc.GetInt32() != 0)
                    break;

                var list = root.TryGetProperty("chats", out var chats) ? chats
                    : root.TryGetProperty("chat_list", out var cl) ? cl : default;
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                    break;

                foreach (var chat in list.EnumerateArray())
                {
                    if (chat.TryGetProperty("chat_id", out var id))
                        chatIds.Add(id.ToString());
                }

                if (list.GetArrayLength() < 100) break;
                var last = list[list.GetArrayLength() - 1];
                if (last.TryGetProperty("chat_id", out var lastId) && lastId.TryGetInt64(out var np))
                    nextPage = np;
                else
                    break;
            }
            catch
            {
                break;
            }
        }

        if (chatIds.Count == 0) return true;

        var fail = 0;
        foreach (var chatId in chatIds)
        {
            var delBody = JsonSerializer.Serialize(new { chat_id = chatId });
            var headers = BuildSignedHeaders(userId, token,
                "/matrix/api/v1/chat/delete_chat", delBody);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://agent.minimaxi.com/matrix/api/v1/chat/delete_chat")
            {
                Content = new StringContent(delBody, Encoding.UTF8, "application/json")
            };
            foreach (var (k, v) in headers)
                req.Headers.TryAddWithoutValidation(k, v);

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) fail++;
        }

        return fail == 0;
    }

    private static (string? Token, string? UserId) ResolveToken(ProviderAccountRecord account)
    {
        var token = account.Credentials.TryGetValue("token", out var t) ? t.Trim() : "";
        var userId = account.Credentials.TryGetValue("realUserID", out var u) ? u.Trim() : "";
        if (token.Contains('+', StringComparison.Ordinal))
        {
            var parts = token.Split('+', 2);
            userId = parts[0];
            token = parts[1];
        }

        if (string.IsNullOrWhiteSpace(userId))
            userId = TryExtractJwtUserId(token);

        return (token, userId);
    }

    private static Dictionary<string, string> BuildSignedHeaders(
        string userId,
        string jwtToken,
        string relativePath,
        string bodyJson)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = GlmRequestSigner.Md5Hex($"{timestamp}{jwtToken}{bodyJson}");
        var query = $"device_platform=web&biz_id=3&app_id=3001&version_code=22201&uuid={Uri.EscapeDataString(userId)}&user_id={Uri.EscapeDataString(userId)}";
        var fullUri = relativePath.Contains('?', StringComparison.Ordinal)
            ? relativePath
            : $"{relativePath}?{query}";
        var yy = GlmRequestSigner.Md5Hex($"{Uri.EscapeDataString(fullUri)}_{bodyJson}{GlmRequestSigner.Md5Hex(timestamp.ToString())}ooui");

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["token"] = jwtToken,
            ["x-timestamp"] = timestamp.ToString(),
            ["x-signature"] = signature,
            ["yy"] = yy,
            ["Origin"] = "https://agent.minimaxi.com/",
            ["Referer"] = "https://agent.minimaxi.com/"
        };
    }

    private static string? TryExtractJwtUserId(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            var pad = payload.Length % 4;
            if (pad > 0) payload += new string('=', 4 - pad);
            payload = payload.Replace('-', '+').Replace('_', '/');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var uid))
                return uid.ToString();
            if (root.TryGetProperty("sub", out var sub)) return sub.GetString();
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
