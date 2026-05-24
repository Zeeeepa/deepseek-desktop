namespace DeepSeekBrowser.Services.Harness;

public static class WorkspacePathGuard
{
    public static string ResolveUnderWorkspace(string workspaceRoot, string? relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
            throw new ArgumentException("路径不能为空");

        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var path = relativeOrAbsolute.Trim().Trim('"');
        var full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("路径超出工作区范围: " + relativeOrAbsolute);

        return full;
    }
}
