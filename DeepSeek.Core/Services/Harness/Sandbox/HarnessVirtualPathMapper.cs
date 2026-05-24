using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness.Sandbox;

/// <summary>DeerFlow 风格虚拟路径 ↔ 本地物理路径（最长前缀匹配）。</summary>
public sealed class HarnessVirtualPathMapper
{
    public const string WorkspaceVirtual = "/mnt/user-data/workspace";
    public const string OutputsVirtual = "/mnt/user-data/outputs";
    public const string UploadsVirtual = "/mnt/user-data/uploads";
    public const string SkillsVirtual = "/mnt/skills";

    private readonly string _workspaceRoot;
    private readonly string _outputsRoot;
    private readonly string _uploadsRoot;
    private readonly string _skillsRoot;
    private readonly (string VirtualPrefix, string PhysicalRoot, bool ReadOnly)[] _rules;

    public HarnessVirtualPathMapper(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _outputsRoot = Path.Combine(_workspaceRoot, "outputs");
        _uploadsRoot = Path.Combine(_workspaceRoot, "uploads");
        _skillsRoot = Path.Combine(AgentDesktopConfigSync.HomeDirectory, "skills");

        _rules = new[]
        {
            (NormalizeVirtual(OutputsVirtual) + "/", _outputsRoot, false),
            (NormalizeVirtual(UploadsVirtual) + "/", _uploadsRoot, true),
            (NormalizeVirtual(WorkspaceVirtual) + "/", _workspaceRoot, false),
            (NormalizeVirtual(SkillsVirtual) + "/", _skillsRoot, true),
        };
        Array.Sort(_rules, (a, b) => b.VirtualPrefix.Length.CompareTo(a.VirtualPrefix.Length));
    }

    public string WorkspaceRoot => _workspaceRoot;

    public static void EnsureLayoutDirectories(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "outputs"));
        Directory.CreateDirectory(Path.Combine(root, "uploads"));
    }

    public string ResolveToPhysical(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空");

        var trimmed = path.Trim().Trim('"');
        var normalized = NormalizeVirtual(trimmed);

        foreach (var (virtualPrefix, physicalRoot, _) in _rules)
        {
            if (normalized.Equals(virtualPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return physicalRoot;

            if (normalized.StartsWith(virtualPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = normalized[virtualPrefix.Length..].TrimStart('/');
                return string.IsNullOrEmpty(suffix)
                    ? physicalRoot
                    : Path.GetFullPath(Path.Combine(physicalRoot, suffix.Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        if (Path.IsPathRooted(trimmed))
            return WorkspacePathGuard.ResolveUnderWorkspace(_workspaceRoot, trimmed);

        return WorkspacePathGuard.ResolveUnderWorkspace(_workspaceRoot, trimmed);
    }

    public bool IsReadOnlyTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = NormalizeVirtual(path.Trim().Trim('"'));
        foreach (var (virtualPrefix, _, readOnly) in _rules)
        {
            if (!readOnly) continue;
            if (normalized.Equals(virtualPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalized.StartsWith(virtualPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public string ResolveToVirtual(string physicalPath)
    {
        var full = Path.GetFullPath(physicalPath);
        foreach (var (virtualPrefix, physicalRoot, _) in _rules)
        {
            var root = physicalRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                full.Equals(physicalRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(physicalRoot, full).Replace('\\', '/');
                if (rel is "." or "")
                    return virtualPrefix.TrimEnd('/');
                return virtualPrefix.TrimEnd('/') + "/" + rel;
            }
        }

        if (full.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(_workspaceRoot, full).Replace('\\', '/');
            return WorkspaceVirtual + (rel is "." or "" ? "" : "/" + rel);
        }

        return full.Replace('\\', '/');
    }

    public string VirtualizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;
        var replacements = new List<(string Physical, string Virtual)>
        {
            (_workspaceRoot, WorkspaceVirtual),
            (_outputsRoot, OutputsVirtual),
            (_uploadsRoot, UploadsVirtual),
            (_skillsRoot, SkillsVirtual),
        };

        foreach (var (physical, virtualPath) in replacements.OrderByDescending(r => r.Physical.Length))
        {
            if (string.IsNullOrWhiteSpace(physical) || !Directory.Exists(physical) && !File.Exists(physical))
            {
                // still replace path strings even if missing
            }

            result = result.Replace(physical, virtualPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    public static string NormalizeVirtual(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
