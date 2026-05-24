using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness.Interop;

/// <summary>
/// 合并桌面配置与市场主流 MCP 配置文件（Cursor / Claude Desktop 同款 mcpServers JSON）。
/// </summary>
public static class McpConfigInterop
{
    public static IReadOnlyList<McpServerConfig> MergeEnabledServers(AppConfig config)
    {
        var merged = new List<McpServerConfig>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in config.McpServers.Where(x => x.Enabled))
        {
            merged.Add(s);
            keys.Add(ServerKey(s));
        }

        if (!config.AgentImportMarketMcp)
            return merged;

        foreach (var discovered in DiscoverExternalServers())
        {
            var key = ServerKey(discovered);
            if (keys.Contains(key)) continue;
            merged.Add(discovered);
            keys.Add(key);
        }

        return merged;
    }

    public static IReadOnlyList<McpServerConfig> DiscoverExternalServers()
    {
        var list = new List<McpServerConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in HarnessInteropPaths.McpConfigPaths())
        {
            if (!File.Exists(path)) continue;
            foreach (var s in ParseMcpFile(path))
            {
                var key = ServerKey(s);
                if (seen.Add(key))
                    list.Add(s);
            }
        }

        return list;
    }

    private static IEnumerable<McpServerConfig> ParseMcpFile(string path)
    {
        var list = new List<McpServerConfig>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("mcpServers", out var servers)
                || servers.ValueKind != JsonValueKind.Object)
                return list;

            foreach (var prop in servers.EnumerateObject())
            {
                var cfg = ParseServer(prop.Name, prop.Value);
                if (cfg is not null)
                    list.Add(cfg);
            }
        }
        catch
        {
            // ignore invalid mcp json
        }

        return list;
    }

    private static McpServerConfig? ParseServer(string id, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        var url = GetString(el, "url") ?? GetString(el, "serverUrl");
        var isRemote = !string.IsNullOrWhiteSpace(url)
                       || GetString(el, "transport") is "http" or "sse";

        var args = new List<string>();
        if (el.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in argsEl.EnumerateArray())
            {
                var s = a.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    args.Add(s);
            }
        }

        return new McpServerConfig
        {
            Id = "ext-" + SanitizeId(id),
            Name = id,
            Enabled = true,
            TransportType = isRemote ? "remote" : "stdio",
            Url = url,
            Command = GetString(el, "command") ?? "npx",
            Arguments = args,
            WorkingDirectory = GetString(el, "cwd") ?? GetString(el, "workingDirectory")
        };
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string ServerKey(McpServerConfig s)
    {
        if (s.IsRemote && !string.IsNullOrWhiteSpace(s.Url))
            return "url:" + s.Url.Trim().ToLowerInvariant();
        return "cmd:" + s.Command + "|" + string.Join(" ", s.Arguments);
    }

    private static string SanitizeId(string id) =>
        id.Replace(' ', '-').ToLowerInvariant();
}
