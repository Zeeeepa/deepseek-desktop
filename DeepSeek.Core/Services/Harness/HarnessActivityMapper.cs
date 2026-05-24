namespace DeepSeekBrowser.Services.Harness;

public static class HarnessActivityMapper
{
    public static AgentUiActivity MapToolCall(string toolName, string argumentsJson, string workspace)
    {
        string? path = null;
        string? pattern = null;
        string? command = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("path", out var p)) path = p.GetString();
            if (root.TryGetProperty("file_path", out var fp)) path ??= fp.GetString();
            if (root.TryGetProperty("pattern", out var pat)) pattern = pat.GetString();
            if (root.TryGetProperty("query", out var q)) pattern ??= q.GetString();
            if (root.TryGetProperty("command", out var c)) command = c.GetString();
        }
        catch
        {
            // ignore parse errors
        }

        return Format(toolName, path, pattern, command, workspace);
    }

    private static AgentUiActivity Format(
        string tool,
        string? path,
        string? pattern,
        string? command,
        string workspace)
    {
        var name = tool.Trim();
        var rel = Relativize(path, workspace);

        if (name.Contains("read", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Read", Wrap(rel ?? path ?? name), null);

        if (name.Contains("write", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("edit", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Edited", Wrap(rel ?? path ?? name), null);

        if (name.Contains("grep", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("search", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Grepped", pattern is not null ? $"`{pattern}`" : rel ?? name, null);

        if (name.Contains("list", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Listed", Wrap(rel ?? path ?? name), null);

        if (name.Contains("glob", StringComparison.OrdinalIgnoreCase))
            return new AgentUiActivity("Searched", pattern ?? rel ?? name, null);

        if (name.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("run_shell", StringComparison.OrdinalIgnoreCase))
        {
            var preview = command is not null ? Truncate(command, 72) : name;
            return new AgentUiActivity("Ran", preview, "terminal");
        }

        return new AgentUiActivity("Ran", name, rel);
    }

    private static string Wrap(string s) => "`" + s + "`";

    private static string? Relativize(string? path, string workspace)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetFullPath(workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var rel = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(rel) ? Path.GetFileName(full) : rel.Replace('\\', '/');
            }

            return full.Replace('\\', '/');
        }
        catch
        {
            return path;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
