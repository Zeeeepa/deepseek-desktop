using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessPlaybookRegistry
{
    private static readonly object Gate = new();
    private static Dictionary<string, (HarnessPlaybook Playbook, HarnessPlaybookSummary Summary)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static long _loadedStamp;

    public static IReadOnlyList<HarnessPlaybookSummary> List(string? workspaceRoot = null)
    {
        RefreshIfNeeded(workspaceRoot);
        return _cache.Values.Select(x => x.Summary).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool TryGet(string id, string? workspaceRoot, out HarnessPlaybook? playbook)
    {
        RefreshIfNeeded(workspaceRoot);
        if (_cache.TryGetValue(id, out var entry))
        {
            playbook = entry.Playbook;
            return true;
        }

        playbook = null;
        return false;
    }

    public static void InvalidateCache()
    {
        lock (Gate)
            _loadedStamp = 0;
    }

    private static void RefreshIfNeeded(string? workspaceRoot)
    {
        var stamp = ComputeStamp(workspaceRoot);
        lock (Gate)
        {
            if (stamp == _loadedStamp && _cache.Count > 0)
                return;

            _cache = LoadAll(workspaceRoot);
            _loadedStamp = stamp;
        }
    }

    private static long ComputeStamp(string? workspaceRoot)
    {
        long hash = 0;
        foreach (var dir in GetSearchDirectories(workspaceRoot))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not ".json" and not ".yaml" and not ".yml") continue;
                hash ^= File.GetLastWriteTimeUtc(file).Ticks;
            }
        }

        return hash;
    }

    private static Dictionary<string, (HarnessPlaybook, HarnessPlaybookSummary)> LoadAll(string? workspaceRoot)
    {
        var map = new Dictionary<string, (HarnessPlaybook, HarnessPlaybookSummary)>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in GetSearchDirectories(workspaceRoot).Reverse())
        {
            if (!Directory.Exists(dir)) continue;
            var source = dir.StartsWith(AgentDesktopConfigSync.HomeDirectory, StringComparison.OrdinalIgnoreCase)
                ? "user"
                : "workspace";

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not ".json" and not ".yaml" and not ".yml") continue;

                try
                {
                    var pb = HarnessPlaybookParser.ParseFile(file);
                    var summary = new HarnessPlaybookSummary
                    {
                        Id = pb.Id,
                        Name = pb.Name,
                        Description = pb.Description,
                        Strategy = pb.Strategy,
                        HasVerify = pb.Verify is not null,
                        VerifyStepCount = pb.Verify?.Steps.Count > 0
                            ? pb.Verify.Steps.Count
                            : (pb.Verify is not null && !string.IsNullOrWhiteSpace(pb.Verify.Command) ? 1 : 0),
                        Source = source
                    };
                    map[pb.Id] = (pb, summary);
                }
                catch
                {
                    // 跳过损坏的 playbook 文件
                }
            }
        }

        return map;
    }

    private static IEnumerable<string> GetSearchDirectories(string? workspaceRoot)
    {
        yield return Path.Combine(AgentDesktopConfigSync.HomeDirectory, "playbooks");
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            yield return Path.Combine(workspaceRoot, ".deepseek", "playbooks");
    }
}
