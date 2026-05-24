using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

public sealed class BuiltinToolExecutor
{
    private static readonly HashSet<string> BuiltinNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file", "write_file", "list_dir", "grep", "glob", "run_shell"
    };

    public static bool IsBuiltin(string name) => BuiltinNames.Contains(name);

    public async Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        bool allowShell,
        CancellationToken ct,
        IHarnessSandbox? sandbox = null)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var root = doc.RootElement;
        var effectiveRoot = sandbox?.WorkspaceRoot ?? workspaceRoot;
        var paths = new SandboxPathResolver(effectiveRoot);

        return toolName.ToLowerInvariant() switch
        {
            "read_file" => ReadFile(root, paths),
            "write_file" => WriteFile(root, paths),
            "list_dir" => ListDir(root, paths),
            "grep" => Grep(root, paths, ct),
            "glob" => Glob(root, paths),
            "run_shell" => await RunShellAsync(root, paths, allowShell, sandbox, ct),
            _ => throw new InvalidOperationException("未知内置工具: " + toolName)
        };
    }

    public static async Task<string> RunShellOnHostAsync(
        string command,
        string workspaceRoot,
        CancellationToken ct,
        SandboxPathResolver? paths = null)
    {
        paths ??= new SandboxPathResolver(workspaceRoot);
        var cwd = paths.Mapper.WorkspaceRoot;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 shell");
        await proc.WaitForExitAsync(ct);
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("exit=" + proc.ExitCode);
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.AppendLine("stderr: " + stderr.TrimEnd());
        var text = paths.VirtualizeText(sb.ToString().Trim());
        return text.Length > 80_000 ? text[..80_000] + "\n…(已截断)" : text;
    }

    private static string ReadFile(JsonElement args, SandboxPathResolver paths)
    {
        var path = GetString(args, "path") ?? GetString(args, "file_path")
                   ?? throw new ArgumentException("read_file 需要 path");
        string full;
        try
        {
            full = paths.ResolveRead(path);
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }

        if (!File.Exists(full))
            return "ERROR: 文件不存在: " + path;
        var text = File.ReadAllText(full, Encoding.UTF8);
        if (text.Length > 120_000)
            text = text[..120_000] + "\n…(已截断)";
        return text;
    }

    private static string WriteFile(JsonElement args, SandboxPathResolver paths)
    {
        var path = GetString(args, "path") ?? GetString(args, "file_path")
                   ?? throw new ArgumentException("write_file 需要 path");
        var content = GetString(args, "content") ?? "";
        string full;
        try
        {
            full = paths.ResolveWrite(path);
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, Encoding.UTF8);
        return "已写入 " + paths.ToVirtual(full) + " (" + content.Length + " 字符)";
    }

    private static string ListDir(JsonElement args, SandboxPathResolver paths)
    {
        var path = GetString(args, "path") ?? ".";
        string full;
        try
        {
            full = paths.ResolveRead(path);
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }

        if (!Directory.Exists(full))
            return "ERROR: 目录不存在: " + path;
        var lines = Directory.EnumerateFileSystemEntries(full)
            .Take(200)
            .Select(e =>
            {
                var label = Directory.Exists(e) ? "[dir] " : "[file] ";
                return label + paths.ToVirtual(e);
            });
        return string.Join("\n", lines);
    }

    private static string Grep(JsonElement args, SandboxPathResolver paths, CancellationToken ct)
    {
        var pattern = GetString(args, "pattern") ?? GetString(args, "query")
                      ?? throw new ArgumentException("grep 需要 pattern");
        var path = GetString(args, "path") ?? ".";
        string full;
        try
        {
            full = paths.ResolveRead(path);
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }

        var re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(2));
        var hits = new List<string>();
        IEnumerable<string> files;
        if (File.Exists(full))
            files = [full];
        else if (Directory.Exists(full))
            files = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Take(500);
        else
            return "ERROR: 路径不存在: " + path;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fi = new FileInfo(file);
                if (fi.Length > 2_000_000) continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!re.IsMatch(lines[i])) continue;
                    var virt = paths.ToVirtual(file);
                    hits.Add($"{virt}:{i + 1}: {lines[i].Trim()}");
                    if (hits.Count >= 40) break;
                }
            }
            catch
            {
                // skip binary/unreadable
            }

            if (hits.Count >= 40) break;
        }

        return hits.Count == 0 ? "(无匹配)" : string.Join("\n", hits);
    }

    private static string Glob(JsonElement args, SandboxPathResolver paths)
    {
        var pattern = GetString(args, "pattern") ?? GetString(args, "glob_pattern") ?? "*";
        var path = GetString(args, "path") ?? ".";
        string full;
        try
        {
            full = paths.ResolveRead(path);
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }

        if (!Directory.Exists(full))
            return "ERROR: 目录不存在: " + path;
        var normalized = pattern.Replace('\\', '/');
        var files = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(f => paths.ToVirtual(f))
            .Where(f => MatchGlob(normalized, f))
            .Take(100);
        return string.Join("\n", files);
    }

    private static bool MatchGlob(string pattern, string path)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(path, regex, RegexOptions.IgnoreCase);
    }

    private static async Task<string> RunShellAsync(
        JsonElement args,
        SandboxPathResolver paths,
        bool allowShell,
        IHarnessSandbox? sandbox,
        CancellationToken ct)
    {
        if (!allowShell)
            return "ERROR: Shell 已在设置中禁用";

        var command = GetString(args, "command") ?? throw new ArgumentException("run_shell 需要 command");
        var blocked = HarnessShellGuard.BlockReason(command);
        if (blocked is not null)
            return "ERROR: " + blocked;

        if (sandbox is not null)
            return await sandbox.ExecuteShellAsync(command, ct);

        return await RunShellOnHostAsync(command, paths.Mapper.WorkspaceRoot, ct, paths);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
