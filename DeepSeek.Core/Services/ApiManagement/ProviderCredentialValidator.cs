using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>内置供应商账户凭证校验（对齐 Chat2API ProviderChecker / AccountManager.validate）。</summary>
public static class ProviderCredentialValidator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record ValidationOutcome(
        bool Valid,
        string? Error,
        string? Name,
        string? Email);

    public static async Task<ValidationOutcome> ValidateProviderCredentialsAsync(
        string providerId,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken ct = default)
    {
        providerId = (providerId ?? "").Trim().ToLowerInvariant();
        return providerId switch
        {
            "deepseek" => await ValidateDeepSeekAsync(Get(credentials, "token"), ct),
            "glm" => await ValidateGlmAsync(Get(credentials, "refresh_token"), ct),
            "kimi" => await ValidateKimiAsync(Get(credentials, "token"), ct),
            "minimax" => await ValidateMiniMaxAsync(
                Get(credentials, "realUserID"),
                Get(credentials, "token"),
                ct),
            "qwen" => await ValidateQwenAsync(Get(credentials, "ticket"), ct),
            "qwen-ai" => await ValidateQwenAiAsync(Get(credentials, "token"), ct),
            "perplexity" => ValidatePerplexity(Get(credentials, "sessionToken", "token")),
            "mimo" => ValidateMimo(
                Get(credentials, "service_token"),
                Get(credentials, "user_id"),
                Get(credentials, "ph_token")),
            "zai" => await ValidateZaiAsync(Get(credentials, "token"), ct),
            _ => ValidateCustomApiKey(providerId, credentials)
        };
    }

    public static async Task<ValidationOutcome> ValidateAccountAsync(
        ProviderAccountRecord account,
        AppConfig config,
        CancellationToken ct = default)
    {
        if (string.Equals(account.ProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
        {
            var token = AccountCredentials.ResolveWebUserToken(account, config);
            return await ValidateDeepSeekAsync(token, ct);
        }

        return await ValidateProviderCredentialsAsync(account.ProviderId, account.Credentials, ct);
    }

    private static async Task<ValidationOutcome> ValidateDeepSeekAsync(string? token, CancellationToken ct)
    {
        token = (token ?? "").Trim();
        if (string.IsNullOrEmpty(token))
            return new ValidationOutcome(false, "请填写 DeepSeek 用户 Token", null, null);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chat.deepseek.com/api/v0/users/current");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        AddBrowserHeaders(req, "https://chat.deepseek.com/");

        var (ok, body, status) = await SendAsync(req, ct);
        if (!ok || string.IsNullOrWhiteSpace(body))
            return new ValidationOutcome(false, "Token 无效或已过期", null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
                return new ValidationOutcome(false, ReadMsg(root), null, null);

            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("biz_data", out var biz))
                return new ValidationOutcome(false, "Token 无效或尚未完成登录", null, null);

            if (data.TryGetProperty("biz_code", out var bizCode) && bizCode.GetInt32() != 0)
                return new ValidationOutcome(false, ReadBizMsg(data), null, null);

            var email = biz.TryGetProperty("email", out var em) ? em.GetString() : null;
            var name = biz.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            if (email?.Contains("@guest.com", StringComparison.OrdinalIgnoreCase) == true)
                return new ValidationOutcome(false, "访客账户不允许", null, null);

            return new ValidationOutcome(true, null, name ?? "DeepSeek", email);
        }
        catch
        {
            return new ValidationOutcome(false, "Token 校验失败", null, null);
        }
    }

    private static async Task<ValidationOutcome> ValidateGlmAsync(string? refreshToken, CancellationToken ct)
    {
        refreshToken = (refreshToken ?? "").Trim();
        if (string.IsNullOrEmpty(refreshToken))
            return new ValidationOutcome(false, "请填写 Refresh Token", null, null);

        var sign = GlmRequestSigner.Create();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://chatglm.cn/chatglm/user-api/user/refresh")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + refreshToken);
        req.Headers.TryAddWithoutValidation("App-Name", "chatglm");
        req.Headers.TryAddWithoutValidation("X-App-Platform", "pc");
        req.Headers.TryAddWithoutValidation("X-Device-Id", Guid.NewGuid().ToString("N"));
        req.Headers.TryAddWithoutValidation("X-Nonce", sign.Nonce);
        req.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString("N"));
        req.Headers.TryAddWithoutValidation("X-Sign", sign.Sign);
        req.Headers.TryAddWithoutValidation("X-Timestamp", sign.Timestamp);
        req.Headers.TryAddWithoutValidation("Origin", "https://chatglm.cn");
        req.Headers.TryAddWithoutValidation("Referer", "https://chatglm.cn/");

        var (ok, body, _) = await SendAsync(req, ct);
        if (!ok || string.IsNullOrWhiteSpace(body))
            return new ValidationOutcome(false, "Refresh Token 无效或已过期", null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("access_token", out _))
            {
                var name = result.TryGetProperty("user", out var user)
                           && user.TryGetProperty("name", out var n)
                    ? n.GetString()
                    : "GLM";
                return new ValidationOutcome(true, null, name, null);
            }

            return new ValidationOutcome(false, "Refresh Token 校验失败", null, null);
        }
        catch
        {
            return new ValidationOutcome(false, "Refresh Token 校验失败", null, null);
        }
    }

    private static async Task<ValidationOutcome> ValidateKimiAsync(string? token, CancellationToken ct)
    {
        token = (token ?? "").Trim();
        if (string.IsNullOrEmpty(token))
            return new ValidationOutcome(false, "请填写 Access Token", null, null);

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://www.kimi.com/apiv2/kimi.gateway.order.v1.SubscriptionService/GetSubscription")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        req.Headers.TryAddWithoutValidation("Origin", "https://www.kimi.com/");

        var (ok, body, _) = await SendAsync(req, ct);
        if (!ok || string.IsNullOrWhiteSpace(body))
            return new ValidationOutcome(false, "Token 无效或已过期", null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("subscription", out var sub))
            {
                var name = sub.TryGetProperty("userName", out var u) ? u.GetString() : "Kimi";
                return new ValidationOutcome(true, null, name, null);
            }

            return new ValidationOutcome(false, "Token 无效或已过期", null, null);
        }
        catch
        {
            return new ValidationOutcome(false, "Token 校验失败", null, null);
        }
    }

    private static async Task<ValidationOutcome> ValidateMiniMaxAsync(
        string? realUserId,
        string? token,
        CancellationToken ct)
    {
        token = (token ?? "").Trim();
        if (string.IsNullOrEmpty(token))
            return new ValidationOutcome(false, "请填写 Token", null, null);

        if (string.IsNullOrWhiteSpace(realUserId) && token.Contains('+', StringComparison.Ordinal))
        {
            var parts = token.Split('+', 2);
            realUserId = parts[0];
            token = parts[1];
        }

        if (string.IsNullOrWhiteSpace(realUserId))
            realUserId = TryExtractJwtUserId(token);

        if (string.IsNullOrWhiteSpace(realUserId))
            return new ValidationOutcome(false, "无法从 Token 解析用户 ID，请填写 realUserID", null, null);

        var uuid = realUserId;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dataJson = JsonSerializer.Serialize(new { uuid });
        var signature = GlmRequestSigner.Md5Hex($"{timestamp}{token}{dataJson}");
        var query = $"device_platform=web&biz_id=3&app_id=3001&version_code=22201&uuid={Uri.EscapeDataString(uuid)}&user_id={Uri.EscapeDataString(realUserId)}";
        var fullUri = $"/v1/api/user/device/register?{query}";
        var yy = GlmRequestSigner.Md5Hex($"{Uri.EscapeDataString(fullUri)}_{dataJson}{GlmRequestSigner.Md5Hex(timestamp.ToString())}ooui");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://agent.minimaxi.com" + fullUri)
        {
            Content = new StringContent(dataJson, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("token", token);
        req.Headers.TryAddWithoutValidation("x-timestamp", timestamp.ToString());
        req.Headers.TryAddWithoutValidation("x-signature", signature);
        req.Headers.TryAddWithoutValidation("yy", yy);
        req.Headers.TryAddWithoutValidation("Origin", "https://agent.minimaxi.com/");

        var (ok, body, _) = await SendAsync(req, ct);
        if (!ok || string.IsNullOrWhiteSpace(body))
            return new ValidationOutcome(false, "Token 无效或已过期", null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("deviceIDStr", out _))
            {
                string? name = null;
                string? email = null;
                if (data.TryGetProperty("userInfo", out var user))
                {
                    name = user.TryGetProperty("name", out var n) ? n.GetString()
                        : user.TryGetProperty("nickname", out var nn) ? nn.GetString() : null;
                    email = user.TryGetProperty("email", out var e) ? e.GetString() : null;
                }

                return new ValidationOutcome(true, null, name ?? "MiniMax", email);
            }

            return new ValidationOutcome(false, "Token 校验失败", null, null);
        }
        catch
        {
            return new ValidationOutcome(false, "Token 校验失败", null, null);
        }
    }

    private static async Task<ValidationOutcome> ValidateQwenAsync(string? ticket, CancellationToken ct)
    {
        ticket = (ticket ?? "").Trim();
        if (string.IsNullOrEmpty(ticket))
            return new ValidationOutcome(false, "请填写 SSO Ticket", null, null);

        var url = "https://chat2-api.qianwen.com/api/v2/session/page/list" +
                  "?biz_id=ai_qwen&chat_client=h5&device=pc&fr=pc&pr=qwen";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Cookie", "tongyi_sso_ticket=" + ticket);
        req.Headers.TryAddWithoutValidation("Origin", "https://www.qianwen.com/");

        var (ok, body, _) = await SendAsync(req, ct);
        if (!ok || string.IsNullOrWhiteSpace(body))
            return new ValidationOutcome(false, "SSO Ticket 无效或已过期", null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean())
                return new ValidationOutcome(true, null, "通义千问", null);
            return new ValidationOutcome(false, "SSO Ticket 无效或已过期", null, null);
        }
        catch
        {
            return new ValidationOutcome(false, "SSO Ticket 校验失败", null, null);
        }
    }

    private static async Task<ValidationOutcome> ValidateQwenAiAsync(string? token, CancellationToken ct)
    {
        token = (token ?? "").Trim();
        if (string.IsNullOrEmpty(token))
            return new ValidationOutcome(false, "请填写 Token", null, null);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chat.qwen.ai/api/v2/user");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("source", "web");

        var (ok, body, status) = await SendAsync(req, ct);
        if (status == 401)
            return new ValidationOutcome(false, "Token 无效或已过期", null, null);
        if (!ok || string.IsNullOrWhiteSpace(body))
            return new ValidationOutcome(false, "Token 校验失败", null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var name = data.TryGetProperty("name", out var n) ? n.GetString()
                    : data.TryGetProperty("email", out var e) ? e.GetString() : "Qwen AI";
                var email = data.TryGetProperty("email", out var em) ? em.GetString() : null;
                return new ValidationOutcome(true, null, name, email);
            }

            return new ValidationOutcome(false, "Token 无效", null, null);
        }
        catch
        {
            return new ValidationOutcome(false, "Token 校验失败", null, null);
        }
    }

    private static ValidationOutcome ValidatePerplexity(string? sessionToken)
    {
        sessionToken = (sessionToken ?? "").Trim();
        if (string.IsNullOrEmpty(sessionToken))
            return new ValidationOutcome(false, "请填写 Session Token", null, null);
        if (sessionToken.Length < 100)
            return new ValidationOutcome(false, "Session Token 格式似乎无效（过短）", null, null);
        return new ValidationOutcome(true, null, "Perplexity", null);
    }

    private static ValidationOutcome ValidateMimo(string? serviceToken, string? userId, string? phToken)
    {
        if (string.IsNullOrWhiteSpace(serviceToken)
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(phToken))
            return new ValidationOutcome(false, "请填写 service_token、user_id、ph_token", null, null);
        return new ValidationOutcome(true, null, "Mimo", null);
    }

    private static async Task<ValidationOutcome> ValidateZaiAsync(string? token, CancellationToken ct)
    {
        token = (token ?? "").Trim();
        if (string.IsNullOrEmpty(token))
            return new ValidationOutcome(false, "请填写 JWT Token", null, null);

        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://chat.z.ai/api/api/v1/users/user/settings");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Origin", "https://chat.z.ai");
        req.Headers.TryAddWithoutValidation("Referer", "https://chat.z.ai/");

        var (ok, _, status) = await SendAsync(req, ct);
        if (status is >= 200 and < 300)
            return new ValidationOutcome(true, null, "Z.ai", null);
        if (status == 401)
            return new ValidationOutcome(false, "Token 无效或已过期", null, null);
        return new ValidationOutcome(false, $"Token 校验失败：HTTP {status}", null, null);
    }

    private static ValidationOutcome ValidateCustomApiKey(
        string providerId,
        IReadOnlyDictionary<string, string> credentials)
    {
        var meta = BuiltinProviderCatalog.Find(providerId);
        if (meta is not null)
        {
            foreach (var field in meta.CredentialFields.Where(f => f.Required))
            {
                if (string.IsNullOrWhiteSpace(Get(credentials, field.Name)))
                    return new ValidationOutcome(false, $"请填写 {field.Label}", null, null);
            }

            return new ValidationOutcome(true, null, meta.Name, null);
        }

        var apiKey = Get(credentials, "apiKey", "api_key", "token");
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ValidationOutcome(false, "请填写 API Key 或 Token", null, null);
        return new ValidationOutcome(true, null, providerId, null);
    }

    private static async Task<(bool Ok, string Body, int Status)> SendAsync(
        HttpRequestMessage req,
        CancellationToken ct)
    {
        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }

    private static void AddBrowserHeaders(HttpRequestMessage req, string referer)
    {
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Origin", referer.TrimEnd('/'));
        req.Headers.TryAddWithoutValidation("Referer", referer);
    }

    private static string? Get(IReadOnlyDictionary<string, string> creds, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (creds.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
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
            if (root.TryGetProperty("id", out var id)) return id.ToString();
            if (root.TryGetProperty("sub", out var sub)) return sub.GetString();
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? ReadMsg(JsonElement root) =>
        root.TryGetProperty("msg", out var m) ? m.GetString() : null;

    private static string? ReadBizMsg(JsonElement data) =>
        data.TryGetProperty("biz_msg", out var m) ? m.GetString() : null;
}
