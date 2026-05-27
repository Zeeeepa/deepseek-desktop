using System.Text.Json;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>工具写入/编辑文件后推送到 Agent UI 的 Monaco 预览载荷。</summary>
public readonly record struct AgentFilePreview(
    string Path,
    string Language,
    string Content,
    bool Complete = true);

public static class HarnessFilePreview
{
    private const int MaxPreviewChars = 120_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "htm", "css", "js", "mjs", "cjs", "ts", "tsx", "jsx", "json", "md", "markdown",
        "xml", "svg", "txt", "py", "cs", "java", "go", "rs", "cpp", "c", "h", "hpp", "sql",
        "yaml", "yml", "sh", "bash", "ps1", "vue", "svelte", "php", "rb", "kt", "swift",
        "scss", "less", "ini", "toml", "env", "bat", "cmd"
    };

    public static bool TryBuildAfterTool(
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        bool success,
        out AgentFilePreview preview)
    {
        preview = default;
        if (!success) return false;

        var norm = HarnessXmlToolCallParser.NormalizeToolName(toolName);
        if (norm is "write_file" or "write")
        {
            var args = HarnessXmlToolCallParser.NormalizeWriteFileArguments(toolName, argumentsJson);
            return TryFromWriteArgs(args, workspaceRoot, out preview);
        }
        if (norm is "edit_file" or "edit")
            return TryFromEditedFile(argumentsJson, workspaceRoot, out preview);
        return false;
    }

    private static bool TryFromWriteArgs(string argumentsJson, string workspaceRoot, out AgentFilePreview preview)
    {
        preview = default;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;
            var path = GetPath(root);
            if (string.IsNullOrWhiteSpace(path)) return false;
            var content = root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(content))
            {
                var full = ResolveFullPath(path, workspaceRoot);
                if (full is not null && File.Exists(full))
                    content = File.ReadAllText(full);
            }
            if (!IsLikelyTextFile(path)) return false;
            preview = Build(path, content, workspaceRoot);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFromEditedFile(string argumentsJson, string workspaceRoot, out AgentFilePreview preview)
    {
        preview = default;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var path = GetPath(doc.RootElement);
            if (string.IsNullOrWhiteSpace(path) || !IsLikelyTextFile(path)) return false;

            var full = ResolveFullPath(path, workspaceRoot);
            if (full is null || !File.Exists(full)) return false;

            var content = File.ReadAllText(full);
            preview = Build(path, content, workspaceRoot);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AgentFilePreview Build(string path, string content, string workspaceRoot)
    {
        var rel = Relativize(path, workspaceRoot);
        if (content.Length > MaxPreviewChars)
            content = content[..MaxPreviewChars] + "\n…（预览已截断）";
        return new AgentFilePreview(rel, LanguageFromPath(rel), content, Complete: true);
    }

    private static string? GetPath(JsonElement root)
    {
        if (root.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String)
            return fp.GetString();
        if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString();
        return null;
    }

    private static bool IsLikelyTextFile(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        if (string.IsNullOrEmpty(ext)) return true;
        return TextExtensions.Contains(ext);
    }

    public static string LanguageFromExtension(string path) => LanguageFromPath(path);

    private static string LanguageFromPath(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "js" or "mjs" or "cjs" => "javascript",
            "ts" or "tsx" => "typescript",
            "jsx" => "javascript",
            "py" => "python",
            "cs" => "csharp",
            "md" or "markdown" => "markdown",
            "yml" => "yaml",
            "sh" or "bash" => "shell",
            "htm" => "html",
            "" => "plaintext",
            _ => ext
        };
    }

    private static string? ResolveFullPath(string path, string workspaceRoot)
    {
        try
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);
            return Path.GetFullPath(Path.Combine(workspaceRoot, path));
        }
        catch
        {
            return null;
        }
    }

    private static string Relativize(string path, string workspace)
    {
        try
        {
            var full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspace, path));
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
            return path.Replace('\\', '/');
        }
    }
}
