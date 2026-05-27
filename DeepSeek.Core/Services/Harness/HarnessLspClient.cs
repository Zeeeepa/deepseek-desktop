using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

public sealed class LspDiagnostic
{
    public required string File { get; init; }
    public int Line { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
}

/// <summary>轻量诊断：优先编译器输出（C#/TS），可扩展 stdio LSP。</summary>
public static class HarnessLspClient
{
    public static async Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(
        string workspaceRoot,
        string? filePath = null,
        CancellationToken ct = default)
    {
        var list = new List<LspDiagnostic>();
        if (Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories).Length > 0)
            list.AddRange(await RunDotnetBuildDiagnosticsAsync(workspaceRoot, ct));
        else if (File.Exists(Path.Combine(workspaceRoot, "tsconfig.json")))
            list.AddRange(await RunTscDiagnosticsAsync(workspaceRoot, ct));

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var norm = filePath.Replace('\\', '/');
            list = list.Where(d => d.File.Replace('\\', '/').EndsWith(norm, StringComparison.OrdinalIgnoreCase)
                                   || norm.EndsWith(d.File.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return list;
    }

    public static string FormatForContext(IReadOnlyList<LspDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return "";
        var errors = diagnostics.Where(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)).Take(20);
        var sb = new StringBuilder("## LSP / 编译诊断\n");
        foreach (var d in errors)
            sb.AppendLine($"- {d.File}:{d.Line} [{d.Severity}] {d.Message}");
        return sb.ToString().Trim();
    }

    private static async Task<IReadOnlyList<LspDiagnostic>> RunDotnetBuildDiagnosticsAsync(
        string workspaceRoot,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build --no-restore -v q",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return Array.Empty<LspDiagnostic>();
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return ParseMsBuild(stderr);
        }
        catch
        {
            return Array.Empty<LspDiagnostic>();
        }
    }

    private static async Task<IReadOnlyList<LspDiagnostic>> RunTscDiagnosticsAsync(
        string workspaceRoot,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c npx --yes tsc --noEmit -p tsconfig.json",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return Array.Empty<LspDiagnostic>();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            var err = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return ParseTsc(output + "\n" + err);
        }
        catch
        {
            return Array.Empty<LspDiagnostic>();
        }
    }

    private static List<LspDiagnostic> ParseMsBuild(string text)
    {
        var rx = new Regex(@"(?<file>[^(\s]+)\((?<line>\d+),\d+\): error (?<msg>.+)$", RegexOptions.Multiline);
        return rx.Matches(text).Select(m => new LspDiagnostic
        {
            File = m.Groups["file"].Value,
            Line = int.Parse(m.Groups["line"].Value),
            Severity = "error",
            Message = m.Groups["msg"].Value.Trim()
        }).ToList();
    }

    private static List<LspDiagnostic> ParseTsc(string text)
    {
        var rx = new Regex(@"(?<file>[^\s(]+)\((?<line>\d+),\d+\): error TS\d+: (?<msg>.+)$", RegexOptions.Multiline);
        return rx.Matches(text).Select(m => new LspDiagnostic
        {
            File = m.Groups["file"].Value,
            Line = int.Parse(m.Groups["line"].Value),
            Severity = "error",
            Message = m.Groups["msg"].Value.Trim()
        }).ToList();
    }
}
