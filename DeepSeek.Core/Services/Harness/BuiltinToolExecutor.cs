using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

public sealed class BuiltinToolExecutor
{
    private static readonly HashSet<string> BuiltinNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file", "read", "write_file", "write", "edit_file", "edit",
        "list_dir", "grep", "glob", "run_shell", "bash", "image_analyze"
    };

    public static bool IsBuiltin(string name) => BuiltinNames.Contains(name);

    public static string NormalizeName(string name) =>
        name.ToLowerInvariant() switch
        {
            "read" => "read_file",
            "write" => "write_file",
            "edit" => "edit_file",
            "bash" => "run_shell",
            _ => name
        };

    public Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        bool allowShell,
        CancellationToken ct,
        IHarnessSandbox? sandbox = null,
        Action<string>? onShellOutput = null,
        AppConfig? config = null) =>
        ExecuteDetailedAsync(toolName, argumentsJson, workspaceRoot, allowShell, ct, sandbox, onShellOutput, config)
            .ContinueWith(t => t.Result.Output, ct);

    public async Task<HarnessToolExecuteResult> ExecuteDetailedAsync(
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        bool allowShell,
        CancellationToken ct,
        IHarnessSandbox? sandbox = null,
        Action<string>? onShellOutput = null,
        AppConfig? config = null)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var root = doc.RootElement;
        var effectiveRoot = sandbox?.WorkspaceRoot ?? workspaceRoot;
        var paths = new SandboxPathResolver(effectiveRoot);
        var normalized = NormalizeName(toolName);

        return normalized switch
        {
            "read_file" or "read" => HarnessReadFileTool.Execute(root, paths),
            "write_file" or "write" => HarnessToolExecuteResult.FromOutput(WriteFile(root, paths)),
            "edit_file" or "edit" => HarnessToolExecuteResult.FromEdit(
                HarnessEditFileTool.Execute(root, paths, config)),
            "list_dir" => HarnessToolExecuteResult.FromOutput(ListDir(root, paths)),
            "grep" => HarnessToolExecuteResult.FromOutput(Grep(root, paths, ct)),
            "glob" => HarnessToolExecuteResult.FromOutput(Glob(root, paths)),
            "run_shell" or "bash" => HarnessToolExecuteResult.FromOutput(
                await RunShellAsync(root, paths, allowShell, sandbox, config, ct, onShellOutput)),
            "image_analyze" => config is null
                ? HarnessToolExecuteResult.FromOutput("ERROR: image_analyze 需要 AppConfig")
                : await HarnessImageAnalyzeTool.RunAsync(root, paths, config, ct),
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

    private static string WriteFile(JsonElement args, SandboxPathResolver paths)
    {
        var path = GetString(args, "file_path") ?? GetString(args, "path")
                   ?? throw new ArgumentException("write_file 需要 file_path");
        var content = GetString(args, "content") ?? "";
        var redacted = HarnessSecretScanner.RedactIfNeeded(content);
        if (redacted is not null)
            return redacted;
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
        AppConfig? config,
        CancellationToken ct,
        Action<string>? onShellOutput = null)
    {
        if (!allowShell)
            return "ERROR: Shell 已在设置中禁用";

        var command = GetString(args, "command") ?? throw new ArgumentException("run_shell 需要 command");
        var blocked = HarnessShellGuard.BlockReason(command);
        if (blocked is not null)
            return "ERROR: " + blocked;

        var timeoutMs = ResolveBashTimeoutMs(config);
        var shellOpts = new HarnessShellRunOptions { TimeoutMs = timeoutMs, OnOutput = onShellOutput };

        if (sandbox is not null)
            return await sandbox.ExecuteShellAsync(command, ct, shellOpts);

        return await HarnessShellRunner.RunAsync(
            command,
            paths.Mapper.WorkspaceRoot,
            paths,
            timeoutMs,
            ct,
            onShellOutput);
    }

    private static int ResolveBashTimeoutMs(AppConfig? config)
    {
        if (config is null)
            return 600_000;
        var min = Math.Clamp(config.AgentBashMinTimeoutMs, 1000, 3_600_000);
        return Math.Clamp(config.AgentBashTimeoutMs, min, 3_600_000);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
