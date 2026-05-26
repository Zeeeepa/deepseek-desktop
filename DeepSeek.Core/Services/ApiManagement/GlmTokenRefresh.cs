using System.Text;
using System.Text.Json;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>GLM refresh_token → access_token（OAuth refresh IPC）。</summary>
public static class GlmTokenRefresh
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<Dictionary<string, string>?> RefreshCredentialsAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken ct = default)
    {
        var refresh = credentials.TryGetValue("refresh_token", out var rt) ? rt?.Trim() : null;
        if (string.IsNullOrEmpty(refresh))
            return null;

        var access = await RefreshAccessTokenAsync(refresh, ct);
        if (string.IsNullOrEmpty(access))
            return null;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["refresh_token"] = refresh,
            ["chatglm_refresh_token"] = refresh,
            ["access_token"] = access
        };
    }

    public static async Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        refreshToken = refreshToken.Trim();
        var sign = GlmRequestSigner.Create();
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://chatglm.cn/chatglm/user-api/user/refresh")
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

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            return null;

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
}
