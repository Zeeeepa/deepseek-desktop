using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>加载/校验前修复用户 config.json（编码损坏、Token 包装、空映射项）。</summary>
public static class ConfigFileRepair
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public enum RepairOutcome
    {
        NoFile,
        OkNoChanges,
        Repaired,
        StillInvalid
    }

    /// <summary>用 System.Text.Json 校验并写回修复后的 config.json（供 build 脚本调用）。</summary>
    public static RepairOutcome TryRepairUserConfigFile(string configDirectory)
    {
        var path = Path.Combine(configDirectory, "config.json");
        if (!File.Exists(path))
            return RepairOutcome.NoFile;

        var raw = File.ReadAllText(path, Encoding.UTF8);
        var prepared = PrepareJsonForDeserialize(raw, configDirectory);
        if (!IsValidJson(prepared))
            return RepairOutcome.StillInvalid;

        var cfg = JsonSerializer.Deserialize<AppConfig>(prepared, JsonOptions) ?? new AppConfig();
        var changed = !string.Equals(prepared.Trim(), raw.Trim(), StringComparison.Ordinal);
        if (MigrateAndRepair(cfg, configDirectory, out var migrated))
            changed = true;

        if (!changed)
            return RepairOutcome.OkNoChanges;

        File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return RepairOutcome.Repaired;
    }

    public static string PrepareJsonForDeserialize(string json, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "{}";

        json = json.TrimStart('\uFEFF');
        if (IsValidJson(json))
            return json;

        var repaired = TryRepairTruncatedStringProperty(json, "qwenCodeWorkspaceRoot");
        if (repaired is not null && IsValidJson(repaired))
            return repaired;

        return json;
    }

    public static bool MigrateAndRepair(AppConfig cfg, string configDirectory, out bool changed)
    {
        changed = false;

        var normalizedToken = WebUserTokenNormalizer.Normalize(cfg.WebUserToken);
        if (!string.Equals(normalizedToken, cfg.WebUserToken, StringComparison.Ordinal))
        {
            cfg.WebUserToken = normalizedToken;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(cfg.WebUserToken))
        {
            var fromAccounts = TryReadDeepSeekTokenFromAccounts(configDirectory);
            if (!string.IsNullOrWhiteSpace(fromAccounts))
            {
                cfg.WebUserToken = fromAccounts;
                changed = true;
            }
        }

        if (cfg.ModelMappings.Count > 0
            && cfg.ModelMappings.All(m =>
                string.IsNullOrWhiteSpace(m.RequestModel)
                && string.IsNullOrWhiteSpace(m.ActualModel)
                && string.IsNullOrWhiteSpace(m.PreferredProviderId)
                && string.IsNullOrWhiteSpace(m.PreferredAccountId)))
        {
            cfg.ModelMappings.Clear();
            changed = true;
        }

        if (cfg.LocalApiPort == 5111 && !cfg.EnableExternalOpenAiApi)
        {
            cfg.LocalApiPort = 0;
            changed = true;
        }

        if (cfg.ConfigSchemaVersion < 2)
        {
            cfg.AgentDeepThinking = true;
            cfg.AgentWebSearch = true;
            cfg.ConfigSchemaVersion = 2;
            changed = true;
        }

        return changed;
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>修复未闭合的字符串值（多因路径含中文被截断）。</summary>
    private static string? TryRepairTruncatedStringProperty(string json, string propertyName)
    {
        var pattern = $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"";
        var match = Regex.Match(json, pattern);
        if (!match.Success)
            return null;

        var valueStart = match.Index + match.Length;
        var nextQuote = json.IndexOf('"', valueStart);
        var nextComma = json.IndexOf(',', valueStart);
        var nextBrace = json.IndexOf('}', valueStart);
        var nextNewline = json.IndexOf('\n', valueStart);

        var looksBroken = nextQuote < 0
                          || (nextComma >= 0 && nextQuote > nextComma)
                          || (nextBrace >= 0 && nextQuote > nextBrace);
        if (!looksBroken)
            return null;

        var end = json.Length;
        foreach (var i in new[] { nextComma, nextBrace, nextNewline }.Where(x => x >= valueStart))
            end = Math.Min(end, i);

        var rawValue = json[valueStart..end].TrimEnd('\r', '\n', ' ', '\t');
        rawValue = rawValue.TrimEnd('\\');
        if (rawValue.EndsWith("\"", StringComparison.Ordinal))
            rawValue = rawValue[..^1];

        var sb = new StringBuilder();
        sb.Append(json.AsSpan(0, valueStart));
        sb.Append(EscapeJsonString(rawValue));
        sb.Append('"');
        if (end < json.Length)
            sb.Append(json.AsSpan(end));
        return sb.ToString();
    }

    private static string EscapeJsonString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string? TryReadDeepSeekTokenFromAccounts(string configDirectory)
    {
        var path = Path.Combine(configDirectory, "provider-accounts.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var acc in doc.RootElement.EnumerateArray())
            {
                if (!acc.TryGetProperty("providerId", out var pid)
                    || !string.Equals(pid.GetString(), "deepseek", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!acc.TryGetProperty("credentials", out var creds))
                    continue;

                if (creds.TryGetProperty("token", out var tok))
                {
                    var s = tok.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
