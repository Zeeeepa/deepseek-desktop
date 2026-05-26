using System.Text;
using System.Text.Json;

namespace DeepSeekBrowser.Services.OAuth;

/// <summary>对齐 Chat2API <c>inAppLogin.ts</c> 的 <c>isValidToken</c>。</summary>
public static class DsdOAuthTokenValidator
{
    public static bool IsValidToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 5)
            return false;

        value = value.Trim();
        if (!value.StartsWith("eyJ", StringComparison.Ordinal))
        {
            if (value.Length >= 20)
                return true;
            if (value.Length >= 10 && IsBase64ish(value))
                return true;
            return value.Length >= 8;
        }

        var parts = value.Split('.');
        if (parts.Length == 5 && value.Length >= 100)
            return true;

        if (parts.Length != 3)
            return value.Length >= 32;

        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("email", out var email)
                && email.GetString()?.Contains("@guest.com", StringComparison.OrdinalIgnoreCase) == true)
                return false;

            if (root.TryGetProperty("app_id", out _)
                || root.TryGetProperty("sub", out _)
                || root.TryGetProperty("exp", out _)
                || root.TryGetProperty("id", out _)
                || root.TryGetProperty("user_id", out _)
                || root.TryGetProperty("uid", out _)
                || root.TryGetProperty("email", out _))
                return true;
        }
        catch
        {
            return value.Length >= 32;
        }

        return value.Length >= 32;
    }

    private static bool IsBase64ish(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '+' or '/' or '=' or '-' or '_')
                continue;
            return false;
        }

        return true;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
