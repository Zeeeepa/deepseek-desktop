using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services;

public static partial class AgentAutomationPrompt
{
    private static readonly Regex TemplateRegex = TemplatePattern();

    public static string Render(string template, JsonElement? triggerPayload)
    {
        if (string.IsNullOrEmpty(template))
            return "";

        return TemplateRegex.Replace(template, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            if (string.IsNullOrEmpty(key))
                return match.Value;

            if (triggerPayload is null || triggerPayload.Value.ValueKind == JsonValueKind.Undefined)
                return "";

            if (TryReadPath(triggerPayload.Value, key, out var value))
                return value;

            return "";
        });
    }

    private static bool TryReadPath(JsonElement root, string path, out string value)
    {
        value = "";
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cur = root;
        foreach (var part in parts)
        {
            if (cur.ValueKind != JsonValueKind.Object ||
                !cur.TryGetProperty(part, out cur))
            {
                return false;
            }
        }

        value = cur.ValueKind switch
        {
            JsonValueKind.String => cur.GetString() ?? "",
            JsonValueKind.Number => cur.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => cur.GetRawText()
        };
        return true;
    }

    [GeneratedRegex(@"\{\{\s*trigger\.(?<key>[\w.\-]+)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex TemplatePattern();
}
