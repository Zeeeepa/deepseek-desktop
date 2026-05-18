using System.Net;
using System.Security.Cryptography;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

public static class LocalApiKeyService
{
    public static string GenerateKeyValue()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        var sb = new System.Text.StringBuilder("sk-", 51);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    public static string NewId() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") +
        RandomNumberGenerator.GetInt32(0, 0xFFFFFF).ToString("x");

    public static bool ShouldEnforceAuth(AppConfig config) =>
        config.EnableLocalApiKeyAuth && config.LocalApiKeys.Any(k => k.Enabled);

    public static bool IsLoopback(HttpListenerRequest request) =>
        request.RemoteEndPoint?.Address is { } addr &&
        (IPAddress.IsLoopback(addr) || addr.ToString() == "127.0.0.1");

    public static string? ExtractProvidedKey(HttpListenerRequest request)
    {
        var auth = request.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(auth) &&
            auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var header = request.Headers["X-API-Key"];
        if (!string.IsNullOrWhiteSpace(header))
            return header.Trim();

        var query = request.Url?.Query;
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("api_key", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }

        return null;
    }

    public static bool TryValidate(AppConfig config, HttpListenerRequest request, out LocalApiKey? matched)
    {
        matched = null;
        if (!ShouldEnforceAuth(config))
            return true;

        if (IsLoopback(request))
            return true;

        var provided = ExtractProvidedKey(request);
        if (string.IsNullOrWhiteSpace(provided))
            return false;

        matched = config.LocalApiKeys.FirstOrDefault(k => k.Enabled && k.Key == provided);
        return matched is not null;
    }

    public static void RecordUsage(AppConfig config, LocalApiKey key)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var k in config.LocalApiKeys)
        {
            if (k.Id != key.Id) continue;
            k.LastUsedAt = now;
            k.UsageCount++;
            break;
        }

        ConfigStore.Save(config);
    }

    public static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        if (key.Length <= 10) return key;
        return key[..7] + "****" + key[^4..];
    }
}
