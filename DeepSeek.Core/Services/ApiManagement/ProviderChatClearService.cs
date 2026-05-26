using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>清除供应商网站上的对话记录（对齐 Chat2API clearChatsHandlers）。</summary>
public static class ProviderChatClearService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "deepseek", "glm", "zai", "qwen-ai", "perplexity", "minimax", "mimo"
    };

    public static bool IsSupported(string providerId) =>
        Supported.Contains(providerId);

    public static async Task<(bool Success, string? Error)> ClearAllChatsAsync(
        ProviderAccountRecord account,
        AppConfig config,
        CancellationToken ct = default)
    {
        var providerId = account.ProviderId;
        if (!IsSupported(providerId))
            return (false, "此供应商不支持清除对话记录");

        try
        {
            var ok = providerId.ToLowerInvariant() switch
            {
                "deepseek" => await ClearDeepSeekAsync(account, config, ct),
                "glm" => await ClearGlmAsync(account, ct),
                "zai" => await ClearZaiAsync(account, ct),
                "qwen-ai" => await ClearQwenAiAsync(account, ct),
                "perplexity" => await ClearPerplexityAsync(account, ct),
                "minimax" => await ClearMiniMaxAsync(account, ct),
                "mimo" => await ClearMimoAsync(account, ct),
                _ => false
            };
            return ok ? (true, null) : (false, "清除对话失败，请检查凭证是否有效");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<bool> ClearDeepSeekAsync(
        ProviderAccountRecord account,
        AppConfig config,
        CancellationToken ct)
    {
        var token = AccountCredentials.ResolveWebUserToken(account, config);
        if (string.IsNullOrWhiteSpace(token)) return false;

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://chat.deepseek.com/api/v0/chat_session/delete_all")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Origin", "https://chat.deepseek.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://chat.deepseek.com/");

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("code", out var c) && c.GetInt32() == 0;
        }
        catch
        {
            return resp.IsSuccessStatusCode;
        }
    }

    private static async Task<bool> ClearZaiAsync(ProviderAccountRecord account, CancellationToken ct)
    {
        var token = GetCred(account, "token");
        if (string.IsNullOrWhiteSpace(token)) return false;

        using var req = new HttpRequestMessage(HttpMethod.Delete, "https://chat.z.ai/api/api/v1/chats/");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Origin", "https://chat.z.ai");
        req.Headers.TryAddWithoutValidation("Referer", "https://chat.z.ai/");

        using var resp = await Http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) return true;
        var body = await resp.Content.ReadAsStringAsync(ct);
        return body.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ClearQwenAiAsync(ProviderAccountRecord account, CancellationToken ct)
    {
        var token = GetCred(account, "token");
        if (string.IsNullOrWhiteSpace(token)) return false;

        using var req = new HttpRequestMessage(HttpMethod.Delete, "https://chat.qwen.ai/api/v2/chats/");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("source", "web");

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
        }
        catch
        {
            return true;
        }
    }

    private static async Task<bool> ClearPerplexityAsync(ProviderAccountRecord account, CancellationToken ct)
    {
        var session = GetCred(account, "sessionToken", "token");
        if (string.IsNullOrWhiteSpace(session)) return false;

        var url = "https://www.perplexity.ai/rest/thread/delete_all_threads?version=2.18&source=default";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Cookie", "pplx-session=" + session);
        req.Headers.TryAddWithoutValidation("Origin", "https://www.perplexity.ai");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.perplexity.ai/library");
        req.Headers.TryAddWithoutValidation("x-perplexity-request-endpoint", url);

        using var resp = await Http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    private static async Task<bool> ClearGlmAsync(ProviderAccountRecord account, CancellationToken ct)
    {
        var refresh = GetCred(account, "refresh_token");
        if (string.IsNullOrWhiteSpace(refresh)) return false;

        var access = await GlmRefreshAccessTokenAsync(refresh, ct);
        if (string.IsNullOrWhiteSpace(access)) return false;

        var ids = new List<string>();
        var page = 1;
        while (true)
        {
            var sign = GlmRequestSigner.Create();
            using var listReq = new HttpRequestMessage(HttpMethod.Post,
                "https://chatglm.cn/mainchat-api/conversation/recent_list")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { page, page_size = 100 }),
                    Encoding.UTF8,
                    "application/json")
            };
            AddGlmHeaders(listReq, access, sign);
            using var listResp = await Http.SendAsync(listReq, ct);
            var listBody = await listResp.Content.ReadAsStringAsync(ct);
            if (!listResp.IsSuccessStatusCode) break;

            try
            {
                using var doc = JsonDocument.Parse(listBody);
                if (!doc.RootElement.TryGetProperty("result", out var result)
                    || !result.TryGetProperty("conversation_list", out var list)
                    || list.ValueKind != JsonValueKind.Array)
                    break;

                var count = 0;
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("conversation_id", out var cid))
                        ids.Add(cid.GetString() ?? "");
                    count++;
                }

                if (count < 100) break;
                page++;
            }
            catch
            {
                break;
            }
        }

        if (ids.Count == 0) return true;

        var ok = true;
        foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var sign = GlmRequestSigner.Create();
            using var delReq = new HttpRequestMessage(HttpMethod.Post,
                "https://chatglm.cn/mainchat-api/conversation/delete")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        assistant_id = "default",
                        conversation_id = id
                    }),
                    Encoding.UTF8,
                    "application/json")
            };
            AddGlmHeaders(delReq, access, sign);
            using var delResp = await Http.SendAsync(delReq, ct);
            if (!delResp.IsSuccessStatusCode) ok = false;
        }

        return ok;
    }

    private static async Task<string?> GlmRefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        var sign = GlmRequestSigner.Create();
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://chatglm.cn/chatglm/user-api/user/refresh")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + refreshToken);
        req.Headers.TryAddWithoutValidation("X-Nonce", sign.Nonce);
        req.Headers.TryAddWithoutValidation("X-Sign", sign.Sign);
        req.Headers.TryAddWithoutValidation("X-Timestamp", sign.Timestamp);
        req.Headers.TryAddWithoutValidation("X-Device-Id", Guid.NewGuid().ToString("N"));
        req.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString("N"));

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("result", out var r)
                   && r.TryGetProperty("access_token", out var t)
                ? t.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AddGlmHeaders(HttpRequestMessage req, string accessToken, GlmRequestSigner.SignResult sign)
    {
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        req.Headers.TryAddWithoutValidation("Referer", "https://chatglm.cn/main/alltoolsdetail");
        req.Headers.TryAddWithoutValidation("X-Device-Id", Guid.NewGuid().ToString("N"));
        req.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString("N"));
        req.Headers.TryAddWithoutValidation("X-Sign", sign.Sign);
        req.Headers.TryAddWithoutValidation("X-Timestamp", sign.Timestamp);
        req.Headers.TryAddWithoutValidation("X-Nonce", sign.Nonce);
    }

    private static async Task<bool> ClearMiniMaxAsync(ProviderAccountRecord account, CancellationToken ct) =>
        await MiniMaxAccountApi.DeleteAllChatsAsync(account, ct);

    private static async Task<bool> ClearMimoAsync(ProviderAccountRecord account, CancellationToken ct)
    {
        var serviceToken = GetCred(account, "service_token");
        var userId = GetCred(account, "user_id");
        var phToken = GetCred(account, "ph_token");
        if (string.IsNullOrWhiteSpace(serviceToken) || string.IsNullOrWhiteSpace(userId))
            return false;

        var ids = new List<string>();
        for (var page = 1; page <= 50; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://aistudio.xiaomimimo.com/api/v1/conversation/list")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { page_num = page, page_size = 100 }),
                    Encoding.UTF8,
                    "application/json")
            };
            req.Headers.TryAddWithoutValidation("Cookie",
                $"serviceToken={serviceToken}; userId={userId}" + (string.IsNullOrWhiteSpace(phToken) ? "" : $"; ph_token={phToken}"));

            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) break;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("conversation_list", out var list))
                    break;
                var added = 0;
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("conversation_id", out var cid))
                    {
                        ids.Add(cid.GetString() ?? "");
                        added++;
                    }
                }

                if (added < 100) break;
            }
            catch
            {
                break;
            }
        }

        if (ids.Count == 0) return true;

        using var delReq = new HttpRequestMessage(HttpMethod.Post,
            "https://aistudio.xiaomimimo.com/api/v1/conversation/delete")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { conversation_ids = ids }),
                Encoding.UTF8,
                "application/json")
        };
        delReq.Headers.TryAddWithoutValidation("Cookie",
            $"serviceToken={serviceToken}; userId={userId}");

        using var delResp = await Http.SendAsync(delReq, ct);
        var delBody = await delResp.Content.ReadAsStringAsync(ct);
        if (!delResp.IsSuccessStatusCode) return false;
        try
        {
            using var doc = JsonDocument.Parse(delBody);
            return doc.RootElement.TryGetProperty("code", out var c) && c.GetInt32() == 0;
        }
        catch
        {
            return true;
        }
    }

    private static string? GetCred(ProviderAccountRecord account, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (account.Credentials.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }
}
