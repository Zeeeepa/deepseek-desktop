using System.Text.Json;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessDomainRouter
{
    private static readonly (string Id, string Name, string[] Keywords)[] BuiltIn =
    [
        ("coding", "编程开发", ["代码", "bug", "debug", "编译", "dotnet", "csharp", "react", "api", "重构", "测试"]),
        ("harness", "Agent 系统", ["harness", "agent", "playbook", "blueprint", "mcp", "工具"]),
        ("work", "工作文档", ["报告", "邮件", "文档", "ppt", "方案", "汇报"]),
    ];

    public static HarnessDomainMatch Route(string prompt, string? workspaceRoot)
    {
        var text = prompt ?? "";
        var fromRegistry = TryRouteFromJsonRegistry(text, workspaceRoot);
        if (fromRegistry is not null)
            return fromRegistry;

        foreach (var (id, name, keywords) in BuiltIn)
        {
            if (keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return new HarnessDomainMatch { Id = id, Name = name };
        }

        return new HarnessDomainMatch { Id = "general", Name = "通用" };
    }

    private static HarnessDomainMatch? TryRouteFromJsonRegistry(string prompt, string? workspaceRoot)
    {
        foreach (var path in GetRegistryPaths(workspaceRoot))
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("domains", out var domains)
                    || domains.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in domains.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : id;
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    if (!item.TryGetProperty("keywords", out var kwEl) || kwEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var kw in kwEl.EnumerateArray())
                    {
                        var k = kw.GetString();
                        if (!string.IsNullOrWhiteSpace(k)
                            && prompt.Contains(k, StringComparison.OrdinalIgnoreCase))
                            return new HarnessDomainMatch { Id = id, Name = name ?? id };
                    }
                }
            }
            catch
            {
                // skip
            }
        }

        return null;
    }

    private static IEnumerable<string> GetRegistryPaths(string? workspaceRoot)
    {
        yield return Path.Combine(AgentDesktopConfigSync.HomeDirectory, "memory", "domains", "registry.json");
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            yield return Path.Combine(workspaceRoot, ".deepseek", "memory", "domains", "registry.json");
    }
}

public sealed class HarnessDomainMatch
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}
