using System.Net.Http;
using System.Text.Json;

namespace DeepSeekBrowser.Services.OAuth;

/// <summary>
/// 直接请求 DeepSeek 网页 API 校验 userToken（对齐 Chat2API deepseek adapter），不依赖 dsDesktopBridge。
/// </summary>
public static class DeepSeekWebTokenValidator
{
    private const string ApiBase = "https://chat.deepseek.com/api";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public sealed record Result(bool Valid, string? Error, string? UserId, string? Email, string? Name);

    public static async Task<Result> ValidateAsync(string? userToken, CancellationToken ct = default)
    {
        var token = (userToken ?? "").Trim();
        if (string.IsNullOrEmpty(token))
            return new Result(false, "请填写 DeepSeek 用户 Token", null, null, null);

        if (!DsdOAuthTokenValidator.IsValidToken(token))
            return new Result(false, "Token 格式无效", null, null, null);

        using var req = new HttpRequestMessage(HttpMethod.Get, ApiBase + "/v0/users/current");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Origin", "https://chat.deepseek.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://chat.deepseek.com/");
        req.Headers.TryAddWithoutValidation("X-App-Version", "20241129.1");
        req.Headers.TryAddWithoutValidation("X-Client-Locale", "zh-CN");
        req.Headers.TryAddWithoutValidation("X-Client-Platform", "web");
        req.Headers.TryAddWithoutValidation("X-Client-Version", "1.8.0");

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new Result(false, "无法连接 DeepSeek：" + ex.Message, null, null, null);
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            return new Result(false, "Token 无效或已过期", null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeEl)
                && codeEl.ValueKind == JsonValueKind.Number
                && codeEl.GetInt32() != 0)
            {
                var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : null;
                return new Result(false, msg ?? "Token 无效", null, null, null);
            }

            if (!root.TryGetProperty("data", out var dataEl))
                return new Result(false, "Token 校验失败：响应格式异常", null, null, null);

            if (dataEl.TryGetProperty("biz_code", out var bizCodeEl)
                && bizCodeEl.ValueKind == JsonValueKind.Number
                && bizCodeEl.GetInt32() != 0)
            {
                var bizMsg = dataEl.TryGetProperty("biz_msg", out var bm) ? bm.GetString() : null;
                return new Result(false, bizMsg ?? "Token 无效", null, null, null);
            }

            if (!dataEl.TryGetProperty("biz_data", out var biz) || biz.ValueKind != JsonValueKind.Object)
                return new Result(false, "Token 无效或尚未完成登录", null, null, null);

            var userId = biz.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
            var email = biz.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
            var name = biz.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

            if (email?.Contains("@guest.com", StringComparison.OrdinalIgnoreCase) == true)
                return new Result(false, "访客账户不允许，请使用真实账户登录", null, null, null);

            return new Result(true, null, userId, email, name);
        }
        catch
        {
            return new Result(false, "Token 校验失败：无法解析响应", null, null, null);
        }
    }
}
