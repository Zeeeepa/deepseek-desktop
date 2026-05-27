using System.Text.Json;

namespace DeepSeekBrowser.Services;

/// <summary>规范化 config.WebUserToken（部分 UI 会写入 {"value":...,"__version":"0"} 包装）。</summary>
public static class WebUserTokenNormalizer
{
    public static string Normalize(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0)
            return "";

        if (t.StartsWith('{') && t.Contains("__version", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                if (doc.RootElement.TryGetProperty("value", out var value))
                {
                    return value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString()?.Trim() ?? "",
                        JsonValueKind.Null => "",
                        _ => t
                    };
                }
            }
            catch
            {
                // fall through
            }
        }

        return t;
    }
}
