using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessMemoryLoader
{
    public static HarnessMemoryContext Load(string userPrompt, string? workspaceRoot)
    {
        var domain = HarnessDomainRouter.Route(userPrompt, workspaceRoot);
        var checkpoint = HarnessCheckpointStore.Load();

        return new HarnessMemoryContext
        {
            DomainId = domain.Id,
            DomainName = domain.Name,
            L0CoreExcerpt = LoadTextFile(ResolvePath("core.yaml", workspaceRoot, domain.Id)),
            L2Behavior = LoadYamlExcerpt(ResolvePath("memory/L2_behavior.yaml", workspaceRoot, domain.Id)),
            L1Context = LoadYamlExcerpt(GetDomainFile(workspaceRoot, domain.Id, "L1_context.yaml")),
            L3Cognitive = LoadYamlExcerpt(GetDomainFile(workspaceRoot, domain.Id, "L3_cognitive.yaml")),
            CheckpointSummary = string.IsNullOrWhiteSpace(checkpoint.Summary) ? null : checkpoint.Summary,
            PendingItems = checkpoint.PendingItems,
            Pitfalls = LoadPitfallsSection(ResolvePath("core.yaml", workspaceRoot, domain.Id))
        };
    }

    private static string? GetDomainFile(string? workspaceRoot, string domainId, string fileName)
    {
        var workspacePath = !string.IsNullOrWhiteSpace(workspaceRoot)
            ? Path.Combine(workspaceRoot, ".deepseek", "memory", "domains", domainId, fileName)
            : null;
        if (workspacePath is not null && File.Exists(workspacePath))
            return File.ReadAllText(workspacePath);

        var homePath = Path.Combine(
            AgentDesktopConfigSync.HomeDirectory, "memory", "domains", domainId, fileName);
        return File.Exists(homePath) ? File.ReadAllText(homePath) : null;
    }

    private static string ResolvePath(string relative, string? workspaceRoot, string domainId)
    {
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var ws = Path.Combine(workspaceRoot, ".deepseek", relative);
            if (File.Exists(ws)) return File.ReadAllText(ws);
            if (relative.StartsWith("memory/", StringComparison.Ordinal))
            {
                var alt = Path.Combine(workspaceRoot, ".deepseek", "memory", "domains", domainId,
                    relative["memory/".Length..]);
                if (File.Exists(alt)) return File.ReadAllText(alt);
            }
        }

        var home = Path.Combine(AgentDesktopConfigSync.HomeDirectory, relative);
        return File.Exists(home) ? File.ReadAllText(home) : "";
    }

    private static string? LoadTextFile(string content) =>
        string.IsNullOrWhiteSpace(content) ? null : content.Trim();

    private static string? LoadYamlExcerpt(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return null;
        try
        {
            var lines = yaml.Replace("\r\n", "\n").Split('\n')
                .Where(l => !l.TrimStart().StartsWith('#'))
                .Take(40);
            return string.Join("\n", lines).Trim();
        }
        catch
        {
            return yaml.Trim();
        }
    }

    private static string? LoadPitfallsSection(string coreYaml)
    {
        if (string.IsNullOrWhiteSpace(coreYaml)) return null;
        var idx = coreYaml.IndexOf("pitfalls", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var slice = coreYaml[idx..];
        return slice.Length > 800 ? slice[..800] : slice;
    }
}
